using System.Text;
using System.Text.Json;

namespace MyToDoWidget.Core;

// A connection code contains credentials. Never use it in status text or logs.
public sealed class TemplateConnection(string endpoint, string key, string datasetId)
{
    public string Endpoint { get; } = endpoint;
    public string Key { get; } = key;
    public string DatasetId { get; } = datasetId;
    public override string ToString() => "MyToDo2 연결 정보 [비공개]";
}

public static class TemplateConnectionCode
{
    public const string Prefix = "MYTODO2:";
    private static MemoException Invalid() => new("시트의 MyToDo2 → 위젯 연결에서 복사한 연결 정보를 붙여넣어 주세요. 주소·키는 표시하지 않습니다.");

    public static TemplateConnection Parse(string value)
    {
        value = value.Trim();
        if (value.Length > 4096 || !value.StartsWith(Prefix, StringComparison.Ordinal)) throw Invalid();
        var payload = value[Prefix.Length..];
        if (payload.Length == 0 || payload.Length % 4 == 1
            || !payload.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-')) throw Invalid();
        try
        {
            string base64 = payload.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight((base64.Length + 3) / 4 * 4, '=');
            var bytes = Convert.FromBase64String(base64);
            // Reject malformed UTF-8 instead of silently replacing characters.
            string json = new UTF8Encoding(false, true).GetString(bytes);
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw Invalid();
            var fields = root.EnumerateObject().Select(p => p.Name).ToArray();
            if (fields.Length != 4 || fields.Distinct(StringComparer.Ordinal).Count() != 4
                || fields.Any(p => p is not ("version" or "endpoint" or "key" or "datasetId"))) throw Invalid();
            if (!root.GetProperty("version").TryGetInt32(out int version) || version != 1) throw Invalid();
            string endpoint = AppsScriptConnection.NormalizeEndpoint(root.GetProperty("endpoint").GetString() ?? "").AbsoluteUri;
            string key = root.GetProperty("key").GetString() ?? "";
            AppsScriptConnection.ValidateKey(key);
            string datasetId = root.GetProperty("datasetId").GetString() ?? "";
            if (!MemoItem.IsId(datasetId)) throw Invalid();
            return new(endpoint, key, datasetId);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException
            or KeyNotFoundException or DecoderFallbackException or MemoException) { throw Invalid(); }
    }

    public static TemplateConnection ReplaceEndpoint(string value, string endpoint)
    {
        var connection = Parse(value);
        return new(AppsScriptConnection.NormalizeEndpoint(endpoint).AbsoluteUri, connection.Key, connection.DatasetId);
    }

    public static string Create(string endpoint, string key, string datasetId)
    {
        endpoint = AppsScriptConnection.NormalizeEndpoint(endpoint).AbsoluteUri;
        AppsScriptConnection.ValidateKey(key);
        if (!MemoItem.IsId(datasetId)) throw Invalid();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { version = 1, endpoint, key, datasetId });
        return Prefix + Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}

public static class SheetTemplate
{
    public static string? SelectCopyUrl(string? packagedUrl, string? savedUrl)
    {
        string? selected = string.IsNullOrWhiteSpace(packagedUrl) ? savedUrl : packagedUrl;
        return string.IsNullOrWhiteSpace(selected) ? null : NormalizeCopyUrl(selected).AbsoluteUri;
    }

    public static Uri NormalizeCopyUrl(string value)
    {
        value = value.Trim();
        if (value.Length > 4096 || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != "https" || uri.Host != "docs.google.com" || !uri.IsDefaultPort || uri.UserInfo.Length != 0)
            throw Invalid();
        var parts = uri.AbsolutePath.TrimEnd('/').Split('/');
        if (parts.Length is not (4 or 5) || parts[1] != "spreadsheets" || parts[2] != "d"
            || parts[3].Length is < 16 or > 160 || !parts[3].All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-')
            || (parts.Length == 5 && parts[4] is not ("edit" or "copy" or "preview"))) throw Invalid();
        return new Uri($"https://docs.google.com/spreadsheets/d/{parts[3]}/copy");
    }
    private static MemoException Invalid() => new("준비된 Google 시트 템플릿의 주소를 입력해 주세요. XLSX·CSV 파일에는 Apps Script가 포함되지 않습니다.");
}
