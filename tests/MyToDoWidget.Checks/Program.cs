using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using MyToDoWidget.Core;

int checks = 0;
void Assert(bool condition, string name)
{
    if (!condition) throw new Exception("FAILED: " + name);
    checks++;
    Console.WriteLine("PASS " + name);
}
void Equal(string[] expected, string input, string name) => Assert(MemoCsv.ReadFirstColumn(input).SequenceEqual(expected), name);
void Throws(Action action, string name)
{
    try { action(); } catch (MemoException) { Assert(true, name); return; }
    throw new Exception("FAILED: " + name);
}
const string published = "https://docs.google.com/spreadsheets/d/e/2PACX-TEST12345678/pub?gid=723&single=true&output=csv";

Equal(["첫 메모", "둘째 메모"], "\uFEFF첫 메모\r\n둘째 메모\r\n", "BOM, Korean, CRLF");
Equal(["오늘\n수업 준비", "쉼표, 따옴표 \"그대로\""], "\"오늘\n수업 준비\"\r\n\"쉼표, 따옴표 \"\"그대로\"\"\"", "quoted multiline and quotes");
Equal(["A1", "A4"], "A1,B1,C1\r\n,B2,C2\r\n  ,B3\r\nA4,\"B4\nnext\"", "only A column and blank rows");
Equal(["  들여쓰기  ", "9/25", "todo", "done", "=1+1", "📌 메모"], "  들여쓰기  \n9/25\ntodo\ndone\n=1+1\n📌 메모", "literal dates, status, formula text and emoji");
Equal([], "", "empty CSV is valid");
Equal([], "\"\"\n\n,ignored", "empty A cells");
Equal(["메모"], "메모", "no trailing newline");
Equal(["메모"], "메모,", "trailing comma");
Equal(["x\ny\nz"], "\"x\ry\r\nz\"", "line endings normalized inside cell");
Throws(() => MemoCsv.ReadFirstColumn("\"unclosed"), "reject broken quote");
Throws(() => MemoCsv.ReadFirstColumn("\"closed\"junk"), "reject bytes after closing quote");
Throws(() => MemoCsv.ReadFirstColumn("raw\"quote"), "reject invalid unquoted quote");
Throws(() => MemoCsv.ReadFirstColumn(new string('x', 50001)), "cell size bound");
Throws(() => MemoCsv.ReadFirstColumn(string.Join('\n', Enumerable.Repeat("x", 5001))), "note count bound");
Assert(SheetSource.Normalize(published).Query == "?output=csv&gid=723&single=true", "preserve numeric tab id");
Assert(SheetSource.Normalize(published.Replace("/pub?", "/pubhtml?")).AbsolutePath.EndsWith("/pub"), "published HTML link normalization");
Assert(SheetSource.Normalize("https://docs.google.com/spreadsheets/d/e/2PACX-TEST12345678/pub").Query == "?output=csv", "no invented tab id");
Throws(() => SheetSource.Normalize("https://docs.google.com/spreadsheets/d/PRIVATE_ID/edit"), "reject normal sharing URL");
Throws(() => SheetSource.Normalize(published.Replace("https:", "http:")), "HTTPS required");
Throws(() => SheetSource.Normalize(published.Replace("docs.google.com", "docs.google.com.evil.example")), "reject lookalike host");
Throws(() => SheetSource.Normalize(published.Replace("docs.google.com", "user@docs.google.com")), "reject embedded credentials");
Throws(() => SheetSource.Normalize(published + "&gid=7"), "reject conflicting tab ids");
Throws(() => SheetSource.Normalize(published.Replace("gid=723", "gid=ABC")), "reject malformed tab id");
Assert(!SheetSource.IsAllowedRedirect(new Uri("https://accounts.google.com/login")), "no login redirect");
Assert(!SheetSource.IsAllowedRedirect(new Uri("https://evilgoogleusercontent.com/data")), "redirect suffix boundary");
Assert(SheetSource.IsAllowedRedirect(new Uri("https://doc-xyz-sheets.googleusercontent.com/data")), "Google CSV download redirect");

using (var client = new SheetClient(new StubHandler((_, _) => Csv("\"9/25\n모임\",ignored"))))
    Assert((await client.FetchAsync(published)).SequenceEqual(["9/25\n모임"]), "HTTP fetch to parser");
using (var client = new SheetClient(new StubHandler((_, _) => Csv(""))))
    Assert((await client.FetchAsync(published)).Count == 0, "empty fetched sheet succeeds");
using (var client = new SheetClient(new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>login</html>", Encoding.UTF8, "text/html") })))
    await FetchFails(client, "HTML response rejected");
using (var client = new SheetClient(new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Forbidden))))
    await FetchFails(client, "403 handled");
using (var client = new SheetClient(new StubHandler((_, i) => i == 1 ? Redirect("https://doc-test-sheets.googleusercontent.com/export") : Csv("redirected"))))
    Assert((await client.FetchAsync(published))[0] == "redirected", "follow Google download redirect");
using (var client = new SheetClient(new StubHandler((_, _) => Redirect("https://evil.example/?secret=PRIVATE"))))
    await FetchFails(client, "untrusted redirect rejected");
using (var client = new SheetClient(new StubHandler((_, _) => Redirect(published))))
    await FetchFails(client, "redirect loop bounded");
using (var client = new SheetClient(new StubHandler((_, _) => Csv(new string('x', 2 * 1024 * 1024 + 1)))))
    await FetchFails(client, "HTTP response size bound");

string folder = Path.Combine(Path.GetTempPath(), "mytodo-check-" + Guid.NewGuid().ToString("N"));
try
{
    var cipher = new TestCipher();
    var store = new StateStore(folder, cipher);
    var state = new AppState { Source = SheetSource.Normalize(published).AbsoluteUri, Notes = ["원본"], HasSnapshot = true, LastFetched = DateTimeOffset.UtcNow };
    Assert(store.Load().State.Source is null, "fresh installation");
    store.Save(state);
    Assert(store.Load().State.Notes[0] == "원본", "state round trip");
    Assert(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(folder, "state.dat"))).Contains("docs.google.com"), "store uses cipher before disk write");
    state.Notes = ["변경"];
    store.Save(state);
    File.WriteAllText(Path.Combine(folder, "state.dat"), "damaged");
    var restored = store.Load();
    Assert(restored.State.Notes[0] == "원본" && restored.Notice is not null, "recover corrupt state from backup");
    state.Source = null; state.Notes = []; state.HasSnapshot = false; state.LastFetched = null;
    store.Save(state, true);
    File.WriteAllText(Path.Combine(folder, "state.dat"), "damaged");
    Assert(store.Load().State.Source is null, "forgotten source cannot return through backup");
    Assert(!Directory.EnumerateFiles(folder, "*.tmp").Any(), "atomic temporary files cleaned");
}
finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }

if (args.Length > 0)
{
    var template = MemoCsv.ReadFirstColumn(File.ReadAllText(args[0], Encoding.UTF8));
    Assert(template.Count == 4 && template[0].Contains('\n') && template[3].Contains('"'), "shipped CSV preserves freeform sample cells");
}
Console.WriteLine($"All {checks} checks passed.");

async Task FetchFails(SheetClient client, string name)
{
    try { await client.FetchAsync(published); }
    catch (MemoException ex)
    {
        Assert(!ex.Message.Contains("2PACX") && !ex.Message.Contains("PRIVATE") && !ex.Message.Contains("https://"), name);
        return;
    }
    throw new Exception("FAILED: " + name);
}
static HttpResponseMessage Csv(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "text/csv") };
static HttpResponseMessage Redirect(string uri)
{
    var response = new HttpResponseMessage(HttpStatusCode.Found);
    response.Headers.Location = new Uri(uri);
    return response;
}

sealed class StubHandler(Func<HttpRequestMessage, int, HttpResponseMessage> response) : HttpMessageHandler
{
    private int _calls;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(response(request, ++_calls)); }
}

// Cross-platform test cipher; production always uses Windows DPAPI, never this type.
sealed class TestCipher : IStateCipher
{
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    public byte[] Protect(byte[] input)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16]; var encrypted = new byte[input.Length];
        using var aes = new AesGcm(_key, 16);
        aes.Encrypt(nonce, input, encrypted, tag);
        return nonce.Concat(tag).Concat(encrypted).ToArray();
    }
    public byte[] Unprotect(byte[] input)
    {
        if (input.Length < 28) throw new CryptographicException();
        var plain = new byte[input.Length - 28];
        using var aes = new AesGcm(_key, 16);
        aes.Decrypt(input.AsSpan(0, 12), input.AsSpan(28), input.AsSpan(12, 16), plain);
        return plain;
    }
}
