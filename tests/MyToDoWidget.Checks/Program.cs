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
// Startup registration uses an in-memory store here; no Windows registration is changed.
string startupExe = Path.Combine(Path.GetTempPath(), "Math Time", "한글", "MyToDoWidget.exe");
string startupCommand = "\"" + startupExe + "\"";
Assert(StartupRegistration.BuildCommand(startupExe) == startupCommand, "quote startup path with spaces and Korean");
Throws(() => StartupRegistration.BuildCommand("MyToDoWidget.exe"), "reject relative startup path");
Throws(() => StartupRegistration.BuildCommand(Path.Combine(Path.GetTempPath(), "dotnet.exe")), "reject dotnet host as startup app");
Throws(() => StartupRegistration.BuildCommand(startupExe + "\""), "reject quotes in startup path");
Throws(() => StartupRegistration.BuildCommand(Path.Combine(Path.GetTempPath(), new string('x', 260) + ".exe")), "enforce Windows Run command length");
var startupStore = new MemoryStartupStore();
var startup = new StartupRegistration(startupStore);
Assert(!startup.IsRegistered, "startup off for a fresh installation");
startup.RefreshExistingPath(startupExe);
Assert(startupStore.Writes == 0, "launch does not create a startup registration");
bool optionsSaved = false;
startup.Save(true, startupExe, () => optionsSaved = true);
Assert(optionsSaved && startupStore.Command == startupCommand, "save registers the exact executable");
startupStore.Writes = 0;
startup.RefreshExistingPath(startupExe);
Assert(startupStore.Writes == 0, "unchanged startup path is not rewritten at login");
string movedExe = Path.Combine(Path.GetTempPath(), "Moved", "MyToDoWidget.exe");
startup.RefreshExistingPath(movedExe);
Assert(startupStore.Command == StartupRegistration.BuildCommand(movedExe), "manual launch repairs an existing moved path");
startup.Save(false, null, () => { });
Assert(!startup.IsRegistered, "disable removes registration without needing an executable path");
startup.RefreshExistingPath(startupExe);
Assert(!startup.IsRegistered, "removed startup registration stays removed after launch");
startup.Save(null, null, () => optionsSaved = true);
Assert(!startup.IsRegistered, "unknown registration leaves startup alone while saving other settings");
startupStore.Command = startupCommand;
try { startup.Save(false, null, () => throw new IOException("state save failure")); throw new Exception("Expected save failure"); }
catch (IOException) { Assert(startupStore.Command == startupCommand, "failed settings save restores prior startup command"); }
startupStore.Command = null;
startupStore.FailWrites = true;
optionsSaved = false;
Throws(() => startup.Save(true, startupExe, () => optionsSaved = true), "report registration write failure");
Assert(!optionsSaved && startupStore.Command is null, "registration failure does not save other settings");
startupStore.FailWrites = false;
startupStore.Command = startupCommand;
Throws(() => startup.Save(false, null, () => { startupStore.FailWrites = true; throw new IOException(); }),
    "report rollback failure instead of claiming restoration");

const string shared = "https://docs.google.com/spreadsheets/d/SHAREDSHEET_TEST1234/edit?usp=sharing#gid=723";
const string legacyPublished = "https://docs.google.com/spreadsheets/d/e/2PACX-TEST12345678/pub?gid=723&single=true&output=csv";

var savedPosition = new DesktopPosition("DISPLAY2", 1920, 1040, 125, 70);
Assert(savedPosition.Resolve(-1920, 0, 1920, 1040, 390, 530) == (-1795, 70),
    "restore exact offset on a monitor left of primary");
Assert(savedPosition.Resolve(1920, 120, 1920, 1040, 390, 530) == (2045, 190),
    "monitor rearrangement preserves monitor-relative position");
Assert(savedPosition.Resolve(0, 0, 960, 520, 390, 530) == (62, 35),
    "resolution change scales the saved position");
var lowPosition = savedPosition with { OffsetY = 1010 };
Assert(lowPosition.Resolve(0, 0, 1920, 1040, 180, 30) == (125, 1010),
    "rolled-up widget restores without moving upward");
var migrated = System.Text.Json.JsonSerializer.Deserialize<AppState>("{\"Width\":190,\"Left\":120,\"Top\":80}")!;
migrated.Normalize();
Assert(migrated.Width == 190 && migrated.HeaderBackgroundPercent == 92 && migrated.OuterBorderPercent == 90,
    "old settings gain independent opacity defaults and retain narrow width");
var invalidPosition = new AppState { PreferredDesktopPosition = savedPosition with { WorkAreaWidth = 0 } };
invalidPosition.Normalize();
Assert(invalidPosition.PreferredDesktopPosition is null, "invalid stored monitor geometry is discarded");

Assert(MemoSizing.FontSize(16, true, 390, 530, 390, 530) == 16, "reference window keeps font size");
Assert(MemoSizing.FontSize(16, true, 780, 1060, 390, 530) == 32, "doubling both window dimensions doubles text");
Assert(MemoSizing.FontSize(16, true, 780, 530, 390, 530) == 22.6, "one-axis resize uses a gentle geometric scale");
Assert(MemoSizing.FontSize(16, false, 780, 1060, 390, 530) == 16, "fixed text option ignores window size");
Assert(MemoSizing.FontSize(11, true, 310, 280, 390, 530) == 11, "minimum text remains readable");
Assert(MemoSizing.FontSize(36, true, 2000, 2000, 390, 530) == 72, "large text is capped");
var legacySize = new AppState { Width = 600, Height = 800 };
legacySize.Normalize();
Assert(legacySize.TextReferenceWidth == 600 && legacySize.TextReferenceHeight == 800,
    "older settings anchor at their existing window dimensions");
legacySize.Width = 750; legacySize.Height = 1000; legacySize.Normalize();
Assert(legacySize.TextReferenceWidth == 600 && legacySize.TextReferenceHeight == 800,
    "saving a resized window does not reset the text scale anchor");

var legacyLayout = System.Text.Json.JsonSerializer.Deserialize<AppState>("{\"TailSide\":1}")!;
legacyLayout.Normalize();
Assert(legacyLayout.BubbleAlignment == BubbleAlign.Right && legacyLayout.BubbleGap == 10
    && legacyLayout.BubbleWidth == BubbleWidthMode.Content, "old right-tail layout keeps its former alignment");
legacyLayout.TailSide = BubbleTail.Left;
legacyLayout.Normalize();
Assert(legacyLayout.BubbleAlignment == BubbleAlign.Right, "tail changes no longer override alignment after migration");
var layoutState = new AppState { BubbleAlignment = BubbleAlign.Center, BubbleWidth = BubbleWidthMode.Widget, BubbleGap = 24 };
var layoutCopy = System.Text.Json.JsonSerializer.Deserialize<AppState>(System.Text.Json.JsonSerializer.Serialize(layoutState))!;
layoutCopy.Normalize();
Assert(layoutCopy.BubbleAlignment == BubbleAlign.Center && layoutCopy.BubbleWidth == BubbleWidthMode.Widget
    && layoutCopy.BubbleGap == 24, "alignment, width and gap survive a settings round trip");

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
Assert(SheetSource.Normalize(shared).Query == "?format=csv&gid=723", "fragment tab id retained in export URL");
Assert(SheetSource.Normalize(shared).AbsolutePath == "/spreadsheets/d/SHAREDSHEET_TEST1234/export", "ordinary sharing link converted to CSV export");
Assert(SheetSource.Normalize(shared.Replace("/edit?usp=sharing#", "/view?")).Query == "?format=csv&gid=723", "view link and query tab id");
Assert(SheetSource.Normalize("https://docs.google.com/spreadsheets/d/SHAREDSHEET_TEST1234").Query == "?format=csv", "missing tab id uses first tab without inventing zero");
Assert(SheetSource.Normalize(shared.Replace("/spreadsheets/d/", "/spreadsheets/u/1/d/")).AbsolutePath == "/spreadsheets/d/SHAREDSHEET_TEST1234/export", "remove browser account index");
Assert(SheetSource.Normalize(shared.Replace("usp=sharing", "gid=723")).Query == "?format=csv&gid=723", "matching query and fragment tab ids accepted");
Assert(SheetSource.Normalize(SheetSource.Normalize(shared).AbsoluteUri) == SheetSource.Normalize(shared), "normalization is idempotent for stored URL");
Assert(SheetSource.Normalize(shared.Replace("usp=sharing", "resourcekey=0-key_123&usp=sharing")).Query == "?format=csv&gid=723&resourcekey=0-key_123", "resource key retained");
Assert(!SheetSource.Normalize(shared.Replace("usp=sharing", "access_token=DO_NOT_FORWARD&tq=select%20B&range=B:B")).Query.Contains("DO_NOT_FORWARD"), "drop tokens and arbitrary export parameters");
Throws(() => SheetSource.Normalize(legacyPublished), "reject legacy web publication URL for new connections");
Assert(SheetSource.IsLegacyPublished(legacyPublished), "recognize old published URL during migration");
Assert(!SheetSource.IsLegacyPublished(shared), "ordinary sharing link is not legacy publication");
Throws(() => SheetSource.Normalize(shared.Replace("https:", "http:")), "HTTPS required");
Throws(() => SheetSource.Normalize(shared.Replace("docs.google.com", "docs.google.com.evil.example")), "reject lookalike host");
Throws(() => SheetSource.Normalize(shared.Replace("docs.google.com", "user@docs.google.com")), "reject embedded credentials");
Throws(() => SheetSource.Normalize(shared.Replace("usp=sharing", "gid=723&gid=723")), "reject duplicate tab parameters");
Throws(() => SheetSource.Normalize(shared.Replace("usp=sharing", "gid=7")), "reject conflicting query and fragment tab ids");
Throws(() => SheetSource.Normalize(shared.Replace("gid=723", "gid=ABC")), "reject malformed tab id");
Throws(() => SheetSource.Normalize(shared.Replace("usp=sharing", "resourcekey=bad%26value")), "reject injected resource key");
Throws(() => SheetSource.Normalize(shared.Replace("usp=sharing", "resourcekey=key1&resourcekey=key2")), "reject duplicate resource keys");
Throws(() => SheetSource.Normalize(shared.Replace("SHAREDSHEET_TEST1234", "ID%2Fbadpath")), "reject path injection");
Throws(() => SheetSource.Normalize(shared.Replace("docs.google.com", "docs.google.com:444")), "reject non-default HTTPS port");
Throws(() => SheetSource.Normalize(shared + "\n"), "reject control characters");
Assert(!SheetSource.IsAllowedRedirect(new Uri("https://accounts.google.com/login")), "no login redirect");
Assert(!SheetSource.IsAllowedRedirect(new Uri("https://evilgoogleusercontent.com/data")), "redirect suffix boundary");
Assert(SheetSource.IsAllowedRedirect(new Uri("https://doc-xyz-sheets.googleusercontent.com/data")), "Google CSV download redirect");

using (var client = new SheetClient(new StubHandler((request, _) =>
{
    Assert(request.Method == HttpMethod.Get && request.Content is null, "fetch uses GET only");
    Assert(request.Headers.Authorization is null && !request.Headers.Contains("Cookie"), "no OAuth token or browser cookies forwarded");
    Assert(request.RequestUri == SheetSource.Normalize(shared), "download only canonical Google export URL");
    return Csv("123\n9/25\ntodo\ndone\n=1+1\n\"한글\n다음 줄\"");
})))
    Assert((await client.FetchAsync(shared)).SequenceEqual(["123", "9/25", "todo", "done", "=1+1", "한글\n다음 줄"]), "freeform export does not infer a column data type");
using (var client = new SheetClient(new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized))))
    await FetchFails(client, "401 permissions failure is sanitized");
using (var client = new SheetClient(new StubHandler((_, _) => Redirect("https://accounts.google.com/login"))))
    await FetchFails(client, "Google login redirect rejected without opening browser");
using (var client = new SheetClient(new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
    { Content = new StringContent("google.visualization.Query.setResponse({error:'denied'});", Encoding.UTF8, "application/javascript") })))
    await FetchFails(client, "query or script response rejected");
using (var cancellation = new CancellationTokenSource())
using (var client = new SheetClient(new StubHandler((_, _) => Csv("unused"))))
{
    cancellation.Cancel();
    try { await client.FetchAsync(shared, cancellation.Token); throw new Exception("Expected cancellation"); }
    catch (OperationCanceledException) { Assert(true, "caller cancellation propagated"); }
}

using (var client = new SheetClient(new StubHandler((_, _) => Csv("\"9/25\n모임\",ignored"))))
    Assert((await client.FetchAsync(shared)).SequenceEqual(["9/25\n모임"]), "HTTP fetch to parser");
using (var client = new SheetClient(new StubHandler((_, _) => Csv(""))))
    Assert((await client.FetchAsync(shared)).Count == 0, "empty fetched sheet succeeds");
using (var client = new SheetClient(new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>login</html>", Encoding.UTF8, "text/html") })))
    await FetchFails(client, "HTML response rejected");
using (var client = new SheetClient(new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Forbidden))))
    await FetchFails(client, "403 handled");
using (var client = new SheetClient(new StubHandler((_, i) => i == 1 ? Redirect("https://doc-test-sheets.googleusercontent.com/export") : Csv("redirected"))))
    Assert((await client.FetchAsync(shared))[0] == "redirected", "follow Google download redirect");
using (var client = new SheetClient(new StubHandler((_, _) => Redirect("https://evil.example/?secret=PRIVATE"))))
    await FetchFails(client, "untrusted redirect rejected");
using (var client = new SheetClient(new StubHandler((_, _) => Redirect(shared))))
    await FetchFails(client, "redirect loop bounded");
using (var client = new SheetClient(new StubHandler((_, _) => Csv(new string('x', 2 * 1024 * 1024 + 1)))))
    await FetchFails(client, "HTTP response size bound");

string folder = Path.Combine(Path.GetTempPath(), "mytodo-check-" + Guid.NewGuid().ToString("N"));
try
{
    var cipher = new TestCipher();
    var store = new StateStore(folder, cipher);
    var state = new AppState { Source = SheetSource.Normalize(shared).AbsoluteUri, Notes = ["원본"], HasSnapshot = true, LastFetched = DateTimeOffset.UtcNow };
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
    var legacy = new AppState { Source = legacyPublished, Notes = ["legacy memo"], HasSnapshot = true,
        LastFetched = DateTimeOffset.UtcNow, BubbleGap = 22, BubbleColor = "#ABCDEF", Width = 230 };
    store.Save(legacy, true);
    var migratedSource = store.Load();
    Assert(migratedSource.State.Source is null && migratedSource.State.Notes.Count == 0 && !migratedSource.State.HasSnapshot,
        "legacy publication connection and memo cache cleared");
    Assert(migratedSource.State.SharedLinkSetupRequired && migratedSource.Notice == SheetSource.LegacyNotice,
        "legacy user told to register shared link and stop publishing separately");
    Assert(migratedSource.State.BubbleGap == 22 && migratedSource.State.BubbleColor == "#ABCDEF" && migratedSource.State.Width == 230,
        "legacy migration preserves appearance and dimensions");
    store.Save(migratedSource.State);
    Assert(store.Load().Notice == SheetSource.LegacyNotice, "migration notice remains until shared link configured");
    migratedSource.State.Source = SheetSource.Normalize(shared).AbsoluteUri;
    migratedSource.State.SharedLinkSetupRequired = false;
    migratedSource.State.Notes = ["shared memo"]; migratedSource.State.HasSnapshot = true;
    store.Save(migratedSource.State, true);
    Assert(store.Load().Notice is null && store.Load().State.Notes.SequenceEqual(["shared memo"]), "shared connection clears migration notice");
    File.WriteAllText(Path.Combine(folder, "state.dat"), "damaged");
    Assert(store.Load().State.Source is null, "obsolete publication backup cannot return after new connection saved");
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
    try { await client.FetchAsync(shared); }
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

sealed class MemoryStartupStore : IStartupRegistrationStore
{
    public string? Command { get; set; }
    public int Writes { get; set; }
    public bool FailWrites { get; set; }
    public string? ReadCommand() => Command;
    public void WriteCommand(string? command)
    {
        if (FailWrites) throw new UnauthorizedAccessException();
        Command = command;
        Writes++;
    }
}
