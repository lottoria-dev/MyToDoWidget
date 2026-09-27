using System.Text.RegularExpressions;

namespace MyToDoWidget.Core;

public sealed class MemoException(string message) : Exception(message);

public static partial class SheetSource
{
    public const string SheetsHome = "https://docs.google.com/spreadsheets/";

    // Only published Sheet URLs are accepted. Normal share/edit URLs have a
    // different authorization and export contract and must not be guessed.
    public static Uri Normalize(string input)
    {
        if (input.Length > 8192 || !Uri.TryCreate(input.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != "https" || uri.Host != "docs.google.com"
            || !uri.IsDefaultPort || uri.UserInfo.Length != 0)
            throw new MemoException("Google Sheets의 ‘웹에 게시’ 주소를 입력해 주세요.");

        var match = PublishedPath().Match(uri.AbsolutePath);
        if (!match.Success)
            throw new MemoException("일반 공유 주소는 사용할 수 없습니다. 시트의 ‘파일 → 공유 → 웹에 게시’에서 메모 탭을 선택하고 CSV 주소를 복사해 주세요.");

        string? gid = null;
        foreach (string part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (Uri.UnescapeDataString(pair[0]) != "gid") continue;
            var value = pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : "";
            if (gid is not null || !SheetId().IsMatch(value))
                throw new MemoException("게시 주소의 시트 번호가 올바르지 않습니다. CSV 게시 주소를 다시 복사해 주세요.");
            gid = value;
        }
        return new Uri($"https://docs.google.com/spreadsheets/d/e/{match.Groups[1].Value}/pub?output=csv"
            + (gid is null ? "" : $"&gid={gid}&single=true"));
    }

    public static bool IsAllowedRedirect(Uri uri) => uri.Scheme == "https" && uri.IsDefaultPort
        && uri.UserInfo.Length == 0 && (uri.Host == "docs.google.com"
            || uri.Host.EndsWith(".googleusercontent.com", StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex(@"^/spreadsheets/d/e/([A-Za-z0-9_-]{8,})/(?:pub|pubhtml)/?$", RegexOptions.CultureInvariant)]
    private static partial Regex PublishedPath();
    [GeneratedRegex(@"^[0-9]{1,20}$", RegexOptions.CultureInvariant)]
    private static partial Regex SheetId();
}
