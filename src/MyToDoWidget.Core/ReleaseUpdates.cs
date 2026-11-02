using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace MyToDoWidget.Core;

public sealed record ReleaseUpdate(string Tag, Uri Page, int Comparison);

// Manual, public release lookup. This client never receives memo or connection data.
public sealed class ReleaseUpdateClient : IDisposable
{
    public const string ReleasesPage = "https://github.com/lottoria-dev/MyToDoWidget/releases";
    public const string LatestApi = "https://api.github.com/repos/lottoria-dev/MyToDoWidget/releases/latest";
    private const int MaxBytes = 512 * 1024;
    private readonly HttpClient _http;

    public ReleaseUpdateClient() : this(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { }
    public ReleaseUpdateClient(HttpMessageHandler handler)
    {
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MyToDo2", "2"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<ReleaseUpdate> CheckAsync(string currentVersion, CancellationToken cancellation = default)
    {
        Version current = ParseVersion(currentVersion.Split('+', 2)[0]);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            using var response = await _http.GetAsync(LatestApi, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new MemoException("공개된 정식 릴리즈를 찾지 못했습니다. 릴리즈 페이지에서 확인하세요.");
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                throw new MemoException("GitHub 요청이 제한되었습니다. 잠시 뒤 다시 확인하거나 릴리즈 페이지를 여세요.");
            if (!response.IsSuccessStatusCode)
                throw new MemoException("업데이트 정보를 가져오지 못했습니다. 잠시 뒤 다시 확인하세요.");
            if (response.Content.Headers.ContentLength > MaxBytes) throw InvalidResponse();
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var body = new MemoryStream();
            byte[] buffer = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(buffer, timeout.Token)) != 0)
            {
                if (body.Length + read > MaxBytes) throw InvalidResponse();
                body.Write(buffer, 0, read);
            }
            using var json = JsonDocument.Parse(body.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("draft", out var draft) || draft.ValueKind != JsonValueKind.False
                || !root.TryGetProperty("prerelease", out var preview) || preview.ValueKind != JsonValueKind.False
                || !root.TryGetProperty("tag_name", out var name) || name.ValueKind != JsonValueKind.String)
                throw InvalidResponse();
            string tag = name.GetString()!;
            Version latest = ParseVersion(tag);
            // Do not follow a response-supplied download URL or install any file.
            var page = new Uri(ReleasesPage + "/tag/" + Uri.EscapeDataString(tag));
            return new ReleaseUpdate(tag, page, latest.CompareTo(current));
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        { throw new MemoException("업데이트 확인 시간이 초과되었습니다. 네트워크를 확인하고 다시 시도하세요."); }
        catch (HttpRequestException)
        { throw new MemoException("GitHub에 연결하지 못했습니다. 네트워크를 확인하거나 릴리즈 페이지를 여세요."); }
        catch (JsonException) { throw InvalidResponse(); }
        catch (IOException) { throw new MemoException("업데이트 정보를 읽지 못했습니다. 다시 시도하세요."); }
    }

    private static Version ParseVersion(string text)
    {
        if (text.StartsWith('v')) text = text[1..];
        if (text.Length is < 5 or > 48 || !text.All(c => char.IsAsciiDigit(c) || c == '.')
            || text.Split('.').Length is < 3 or > 4 || !Version.TryParse(text, out var version))
            throw InvalidResponse();
        return new Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
    }

    private static MemoException InvalidResponse() => new("정식 버전 정보를 확인할 수 없습니다. 릴리즈 페이지에서 확인하세요.");
    public void Dispose() => _http.Dispose();
}
