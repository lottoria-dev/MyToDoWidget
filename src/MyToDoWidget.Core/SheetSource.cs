using System.Text.RegularExpressions;

namespace MyToDoWidget.Core;

public sealed class MemoException(string message) : Exception(message);

public static partial class SheetSource
{
    public const string SheetsHome = "https://docs.google.com/spreadsheets/";

    public const string PrivacyWarning = "민감한 메모는 입력하지 않는 것을 권장합니다. ‘링크가 있는 모든 사용자 · 뷰어’는 수정을 막지만, 링크를 아는 사람은 로그인 없이 내용을 읽을 수 있습니다.";
    public const string LegacyNotice = "이전 웹 게시 연결을 해제했습니다. ‘링크가 있는 모든 사용자 · 뷰어’로 설정한 일반 공유 주소를 다시 등록해 주세요. 기존 게시본을 없애려면 Google Sheets에서 ‘웹에 게시’를 별도로 중지해야 합니다.";

    // Rebuild the download URL; do not forward arbitrary query parameters.
    // CSV export preserves mixed-type cells without query type inference.
    public static Uri Normalize(string input)
    {
        if (input.Length > 8192 || input.Any(char.IsControl)
            || !Uri.TryCreate(input.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != "https" || uri.Host != "docs.google.com"
            || !uri.IsDefaultPort || uri.UserInfo.Length != 0)
            throw InvalidAddress();

        var match = SharedPath().Match(uri.AbsolutePath);
        if (!match.Success)
            throw new MemoException("Google Sheets의 일반 공유 주소를 입력해 주세요. ‘웹에 게시’ 주소는 사용할 수 없습니다. 공유 → 일반 액세스를 ‘링크가 있는 모든 사용자 · 뷰어’로 설정한 뒤 링크를 복사하세요.");

        string? gid = null, resourceKey = null;
        ReadParameters(uri.Query.TrimStart('?'), ref gid, ref resourceKey);
        ReadParameters(uri.Fragment.TrimStart('#'), ref gid, ref resourceKey);
        return new Uri($"https://docs.google.com/spreadsheets/d/{match.Groups[1].Value}/export?format=csv"
            + (gid is null ? "" : $"&gid={gid}")
            + (resourceKey is null ? "" : $"&resourcekey={resourceKey}"));
    }

    private static void ReadParameters(string parameters, ref string? gid, ref string? resourceKey)
    {
        bool hasGid = false, hasKey = false;
        foreach (string part in parameters.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            string key = Uri.UnescapeDataString(pair[0]);
            if (key is not ("gid" or "resourcekey")) continue;
            string value = pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : "";
            if (key == "gid")
            {
                if (hasGid || !SheetId().IsMatch(value) || (gid is not null && gid != value))
                    throw new MemoException("시트 탭 번호가 올바르지 않습니다. 메모 탭을 연 뒤 브라우저 주소를 다시 복사해 주세요.");
                hasGid = true; gid = value;
            }
            else
            {
                if (hasKey || !ResourceKey().IsMatch(value) || (resourceKey is not null && resourceKey != value))
                    throw InvalidAddress();
                hasKey = true; resourceKey = value;
            }
        }
    }

    public static bool IsLegacyPublished(string source) => Uri.TryCreate(source, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && uri.Host == "docs.google.com" && uri.IsDefaultPort
        && uri.UserInfo.Length == 0 && PublishedPath().IsMatch(uri.AbsolutePath);

    public static bool IsAllowedRedirect(Uri uri) => uri.Scheme == "https" && uri.IsDefaultPort
        && uri.UserInfo.Length == 0 && (uri.Host == "docs.google.com"
            || uri.Host.EndsWith(".googleusercontent.com", StringComparison.OrdinalIgnoreCase));

    private static MemoException InvalidAddress() => new("Google Sheets의 HTTPS 공유 주소를 입력해 주세요.");

    [GeneratedRegex(@"^/spreadsheets/(?:u/[0-9]+/)?d/([A-Za-z0-9_-]{8,})(?:/(?:edit|view|export))?/?$", RegexOptions.CultureInvariant)]
    private static partial Regex SharedPath();
    [GeneratedRegex(@"^/spreadsheets/d/e/([A-Za-z0-9_-]{8,})/(?:pub|pubhtml)/?$", RegexOptions.CultureInvariant)]
    private static partial Regex PublishedPath();
    [GeneratedRegex(@"^[0-9]{1,20}$", RegexOptions.CultureInvariant)]
    private static partial Regex SheetId();
    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,256}$", RegexOptions.CultureInvariant)]
    private static partial Regex ResourceKey();
}
