using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace MyToDoWidget.Core;

public sealed class SheetClient : IDisposable
{
    private const int MaxBytes = 2 * 1024 * 1024;
    private readonly HttpClient _client;
    public SheetClient(HttpMessageHandler? handler = null)
    {
        _client = new HttpClient(handler ?? new HttpClientHandler
        {
            AllowAutoRedirect = false, UseCookies = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        }) { Timeout = Timeout.InfiniteTimeSpan };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("MyToDoWidget/1.1.2");
    }

    public async Task<List<string>> FetchAsync(string source, CancellationToken cancellation = default)
    {
        var uri = SheetSource.Normalize(source);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        try
        {
            for (int redirects = 0; redirects <= 5; redirects++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
                using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                int status = (int)response.StatusCode;
                if (status is 301 or 302 or 303 or 307 or 308)
                {
                    var location = response.Headers.Location;
                    if (location is null) throw Unavailable();
                    var next = location.IsAbsoluteUri ? location : new Uri(uri, location);
                    if (!SheetSource.IsAllowedRedirect(next)) throw Unavailable();
                    uri = next;
                    continue;
                }
                if (status is 401 or 403) throw SharingRequired();
                if (!response.IsSuccessStatusCode) throw Unavailable();
                string media = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? "";
                if (media is not ("text/csv" or "application/csv" or "text/comma-separated-values" or "application/octet-stream"))
                    throw SharingRequired();
                if (response.Content.Headers.ContentLength > MaxBytes) throw TooLarge();
                await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
                using var bytes = new MemoryStream();
                byte[] buffer = new byte[8192];
                int read;
                while ((read = await stream.ReadAsync(buffer, timeout.Token)) != 0)
                {
                    if (bytes.Length + read > MaxBytes) throw TooLarge();
                    bytes.Write(buffer, 0, read);
                }
                string csv = new UTF8Encoding(false, true).GetString(bytes.ToArray());
                return MemoCsv.ReadFirstColumn(csv);
            }
            throw Unavailable();
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        { throw new MemoException("응답 시간이 초과되었습니다. 잠시 후 다시 불러와 주세요."); }
        catch (HttpRequestException) { throw new MemoException("시트에 연결하지 못했습니다. 인터넷 연결과 공유 설정을 확인해 주세요."); }
        catch (DecoderFallbackException) { throw new MemoException("CSV 문자 인코딩을 읽을 수 없습니다. Google Sheets의 일반 공유 주소를 다시 등록해 주세요."); }
        // Never forward exception messages, response bodies or URLs to the UI/log.
    }

    private static MemoException SharingRequired() => new("시트를 읽을 수 없습니다. ‘링크가 있는 모든 사용자 · 뷰어’ 설정과 메모 탭 주소를 확인해 주세요. 다운로드 제한이나 조직 계정 정책도 영향을 줄 수 있습니다.");
    private static MemoException Unavailable() => new("시트를 읽을 수 없습니다. 인터넷 연결, 공유 설정과 메모 탭 주소를 확인해 주세요.");
    private static MemoException TooLarge() => new("시트 데이터가 2MB를 초과했습니다. 작은 메모 전용 시트를 사용해 주세요.");
    public void Dispose() => _client.Dispose();
}
