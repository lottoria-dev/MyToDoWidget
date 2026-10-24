using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MyToDoWidget.Core;

public static class AppsScriptConnection
{
    public const string SheetsHome = "https://docs.google.com/spreadsheets/";
    public static Uri NormalizeEndpoint(string value)
    {
        value = value.Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https"
            || uri.Host != "script.google.com" || !uri.IsDefaultPort || uri.UserInfo.Length != 0
            || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new MemoException("Apps Script 배포 주소를 입력해 주세요. /exec로 끝나는 https 주소가 필요합니다.");
        var parts = uri.AbsolutePath.Split('/');
        if (parts.Length != 5 || parts[1] != "macros" || parts[2] != "s" || parts[4] != "exec"
            || parts[3].Length is < 16 or > 200 || !parts[3].All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            throw new MemoException("테스트용 /dev 주소 대신 배포된 /exec 주소를 입력해 주세요.");
        return uri;
    }
    public static void ValidateKey(string key)
    {
        if (key.Length is < 43 or > 128 || !key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            throw new MemoException("연결 키는 시트의 연결 창에서 생성한 긴 키여야 합니다. 숫자 PIN은 사용할 수 없습니다.");
    }
    public static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
public sealed record ServerProbe(string DatasetId, string ServerVersion);
public sealed record ServerSnapshot(IReadOnlyList<MemoItem> Items, DateTimeOffset ServerTime, IReadOnlyList<SharedOrder>? Orders = null);
public sealed record MutationResult(string Status, MemoItem? Item);
public interface IAppsScriptClient : IDisposable
{
    Task<ServerProbe> ProbeAsync(string endpoint, string key, CancellationToken cancellation);
    Task<ServerSnapshot> ReadAsync(string endpoint, string key, string datasetId, CancellationToken cancellation);
    Task<MutationResult> MutateAsync(string endpoint, string key, string datasetId, PendingChange change, CancellationToken cancellation);
    Task<OrderMutationResult> ReorderAsync(string endpoint, string key, string datasetId, PendingOrderChange change, CancellationToken cancellation);
}

public sealed class AppsScriptClient : IAppsScriptClient
{
    public const int Protocol = 3;
    private readonly HttpClient _http;
    private const int MaxResponseBytes = 32 * 1024 * 1024;
    public AppsScriptClient() : this(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { }
    public AppsScriptClient(HttpMessageHandler handler) => _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(35) };

    public async Task<ServerProbe> ProbeAsync(string endpoint, string key, CancellationToken cancellation)
    {
        using var json = await SendAsync(endpoint, key, "probe", null, null, cancellation);
        var root = json.RootElement;
        if (!root.TryGetProperty("capabilities", out var capabilities) || capabilities.ValueKind != JsonValueKind.Object
            || !capabilities.TryGetProperty("read", out var read) || read.ValueKind != JsonValueKind.True
            || !capabilities.TryGetProperty("write", out var write) || write.ValueKind != JsonValueKind.True)
            throw new MemoException("읽기·쓰기 연결을 확인하지 못했습니다.");
        return new(root.GetProperty("datasetId").GetString()!, root.GetProperty("serverVersion").GetString()!);
    }
    public async Task<ServerSnapshot> ReadAsync(string endpoint, string key, string datasetId, CancellationToken cancellation)
    {
        using var json = await SendAsync(endpoint, key, "snapshot", datasetId, null, cancellation);
        var root = json.RootElement;
        if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > 10000)
            throw InvalidResponse();
        var result = items.EnumerateArray().Select(ReadItem).ToList();
        if (result.Select(i => i.Id).Distinct().Count() != result.Count) throw InvalidResponse();
        return new(result, root.GetProperty("serverTime").GetDateTimeOffset(), ReadOrders(root));
    }
    public async Task<MutationResult> MutateAsync(string endpoint, string key, string datasetId, PendingChange change, CancellationToken cancellation)
    {
        using var json = await SendAsync(endpoint, key, "mutate", datasetId, change, cancellation);
        var root = json.RootElement;
        if (!root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object
            || !result.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.String
            || status.GetString() is not ("applied" or "conflict") || !result.TryGetProperty("item", out var raw)) throw InvalidResponse();
        var item = raw.ValueKind == JsonValueKind.Null ? null : ReadItem(raw);
        if (item is not null && item.Id != change.Item.Id) throw InvalidResponse();
        if (status.GetString() == "applied" && (item is null || item.Version != change.BaseVersion + 1)) throw InvalidResponse();
        return new(status.GetString()!, item);
    }
    public async Task<OrderMutationResult> ReorderAsync(string endpoint, string key, string datasetId, PendingOrderChange change, CancellationToken cancellation)
    {
        change.Validate();
        using var json = await SendAsync(endpoint, key, "reorder", datasetId, change, cancellation);
        var root = json.RootElement;
        if (!root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object
            || !result.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.String || status.GetString() != "applied"
            || !result.TryGetProperty("operationId", out var id) || id.ValueKind != JsonValueKind.String || id.GetString() != change.OperationId) throw InvalidResponse();
        return new(change.OperationId, ReadOrders(result));
    }
    private static IReadOnlyList<SharedOrder> ReadOrders(JsonElement root)
    {
        try
        {
            if (!root.TryGetProperty("orders", out var raw) || raw.ValueKind != JsonValueKind.Array || raw.GetArrayLength() != 2)
                throw InvalidResponse();
            foreach (var value in raw.EnumerateArray())
                if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("kind", out var kind)
                    || !kind.TryGetInt32(out _) || !value.TryGetProperty("ids", out var ids)
                    || ids.ValueKind != JsonValueKind.Array || ids.GetArrayLength() > 10000) throw InvalidResponse();
            var orders = raw.Deserialize<List<SharedOrder>>(MemoJson.Options) ?? throw InvalidResponse();
            foreach (var order in orders)
            {
                if (order is null) throw InvalidResponse();
                order.Validate();
            }
            if (orders.Select(o => o.Kind).Distinct().Count() != 2) throw InvalidResponse();
            return orders;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { throw InvalidResponse(); }
    }
    private async Task<JsonDocument> SendAsync(string endpoint, string key, string action, string? datasetId,
        object? change, CancellationToken cancellation)
    {
        var uri = AppsScriptConnection.NormalizeEndpoint(endpoint); AppsScriptConnection.ValidateKey(key);
        string body = JsonSerializer.Serialize(new { protocol = Protocol, action, key, datasetId, change }, MemoJson.Options);
        var method = HttpMethod.Post;
        try
        {
            for (int hop = 0; hop < 5; hop++)
            {
                using var request = new HttpRequestMessage(method, uri);
                if (method == HttpMethod.Post) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
                int code = (int)response.StatusCode;
                if (code is >= 300 and < 400)
                {
                    if (code is not (301 or 302 or 303) || response.Headers.Location is null) throw InvalidResponse();
                    var next = response.Headers.Location.IsAbsoluteUri ? response.Headers.Location : new Uri(uri, response.Headers.Location);
                    if (next.Scheme != "https" || next.Host != "script.googleusercontent.com" || !next.IsDefaultPort
                        || next.UserInfo.Length != 0 || next.OriginalString.Contains(key, StringComparison.Ordinal))
                        throw new MemoException("로그인 없이 접근 가능한 웹앱인지 확인해 주세요. 배포의 실행 사용자와 접근 허용 설정을 확인해야 합니다.");
                    uri = next; method = HttpMethod.Get; // The secret is NEVER replayed across a redirect.
                    continue;
                }
                if (!response.IsSuccessStatusCode) throw HttpFailure(response.StatusCode, method);
                if (response.Content.Headers.ContentType?.MediaType != "application/json") throw InvalidResponse();
                if (response.Content.Headers.ContentLength > MaxResponseBytes) throw InvalidResponse();
                await using var stream = await response.Content.ReadAsStreamAsync(cancellation);
                using var buffer = new MemoryStream(); byte[] chunk = new byte[8192]; int count;
                while ((count = await stream.ReadAsync(chunk, cancellation)) > 0)
                {
                    if (buffer.Length + count > MaxResponseBytes) throw InvalidResponse();
                    buffer.Write(chunk, 0, count);
                }
                var json = JsonDocument.Parse(buffer.ToArray());
                try { ValidateEnvelope(json.RootElement, datasetId); return json; }
                catch { json.Dispose(); throw; }
            }
            throw InvalidResponse();
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        { throw new MemoException("연결 시간이 초과되었습니다. 변경 내용은 기기에 보관하고 다시 시도합니다."); }
        catch (HttpRequestException) { throw new MemoException("인터넷 연결을 확인해 주세요. 변경 내용은 기기에 보관합니다."); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or KeyNotFoundException)
        { throw InvalidResponse(); }
    }
    private static void ValidateEnvelope(JsonElement root, string? expectedDataset)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("ok", out var ok)) throw InvalidResponse();
        if (ok.ValueKind == JsonValueKind.False)
        {
            string code = root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String ? error.GetString()! : "SERVER_ERROR";
            throw new MemoException(code switch
            {
                "AUTH" => "연결 키가 일치하지 않습니다. 키를 변경했다면 이 PC에도 다시 입력해 주세요.",
                "PROTOCOL" => "프로그램과 서버 버전이 호환되지 않습니다. Apps Script 코드를 업데이트해 주세요.",
                "NOT_CONFIGURED" => "사본 시트에서 MyToDo2 → 위젯 연결 → 연결 정보 만들기를 완료해 주세요.",
                "DATASET" => "연결된 저장소가 변경되었습니다. 설정에서 연결을 다시 확인해 주세요.",
                "BUSY" => "다른 기기의 저장을 처리하고 있습니다. 잠시 뒤 다시 시도합니다.",
                "INVALID" => "메모 또는 요청 형식이 올바르지 않습니다. 내용은 기기에 보관되어 있습니다.",
                "OPERATION_REUSED" => "변경 요청이 충돌했습니다. 미전송 메모를 백업한 뒤 연결을 확인해 주세요.",
                _ => "서버에서 처리하지 못했습니다. 시트 권한·배포·실행 제한을 확인해 주세요."
            });
        }
        if (ok.ValueKind != JsonValueKind.True || !root.TryGetProperty("protocol", out var protocol)
            || !protocol.TryGetInt32(out var p) || p != Protocol || !root.TryGetProperty("datasetId", out var id)
            || id.ValueKind != JsonValueKind.String || !MemoItem.IsId(id.GetString())
            || !root.TryGetProperty("serverVersion", out var version) || version.ValueKind != JsonValueKind.String
            || !root.TryGetProperty("serverTime", out var time) || !time.TryGetDateTimeOffset(out _)) throw InvalidResponse();
        if (expectedDataset is not null && expectedDataset != id.GetString())
            throw new MemoException("다른 저장소의 응답입니다. 기존 메모를 유지합니다. 연결을 다시 확인해 주세요.");
    }
    private static MemoItem ReadItem(JsonElement raw)
    {
        try
        {
            if (raw.ValueKind != JsonValueKind.Object) throw InvalidResponse();
            foreach (string field in new[] { "id", "body", "kind", "version", "createdAt", "updatedAt", "isDeleted", "isCompleted" })
                if (!raw.TryGetProperty(field, out _)) throw InvalidResponse();
            var item = raw.Deserialize<MemoItem>(MemoJson.Options) ?? throw InvalidResponse();
            item.Validate(); if (item.Version <= 0) throw InvalidResponse(); return item;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        { throw InvalidResponse(); }
    }
    private static MemoException HttpFailure(HttpStatusCode status, HttpMethod method)
    {
        int code = (int)status;
        string stage = method == HttpMethod.Post ? "웹앱 요청(POST)" : "응답 가져오기(GET)";
        string guidance = code switch
        {
            401 or 403 => "접근이 거부되었습니다. 웹앱 접근 설정과 PC의 네트워크 제한을 확인해 주세요.",
            404 or 410 => "배포 주소와 현재 배포의 활성 상태를 확인해 주세요.",
            405 => "요청 방식이 거부되었습니다. 아래 오류 번호와 단계를 개발자에게 알려 주세요.",
            429 => "요청이 많아 연결이 잠시 제한되었습니다. 잠시 뒤 다시 시도해 주세요.",
            >= 500 => "서버에서 오류 응답을 반환했습니다. 잠시 뒤 다시 시도해 주세요.",
            _ => "배포 상태와 네트워크를 확인해 주세요. 반복되면 아래 오류 번호와 단계를 개발자에게 알려 주세요."
        };
        // Never include the response body, reason phrase, endpoint, key or redirect URL.
        return new MemoException($"연결에 실패했습니다. {guidance}\n진단: HTTP {code} / {stage}");
    }
    private static MemoException InvalidResponse() => new("정상적인 MyToDo2 응답을 받지 못했습니다. 배포 주소·접근 설정·서버 버전을 확인해 주세요. 기존 메모는 유지합니다.");
    public void Dispose() => _http.Dispose();
}
