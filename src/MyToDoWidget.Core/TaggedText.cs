using System.Text.RegularExpressions;

namespace MyToDoWidget.Core;

public sealed record TextMetadata(string DisplayBody, IReadOnlyList<string> Tags, string? Source, string? Link)
{
    public bool HasTag(string tag) => Tags.Contains(tag.TrimStart('#'), StringComparer.OrdinalIgnoreCase);
}

/// <summary>Markers are explicit. Raw bodies, ordinary email and URL fragments stay intact.</summary>
public static partial class TaggedText
{
    [GeneratedRegex(@"(?<!\S)#([\p{L}\p{M}\p{N}_-]+)(?=$|\s|[,.;!?])", RegexOptions.CultureInvariant)]
    private static partial Regex TagPattern();

    public static TextMetadata Parse(string body)
    {
        var tags = TagPattern().Matches(body).Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        string? source = null, link = null;
        var display = new List<string>();
        foreach (string line in body.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            string value = line.Trim();
            if (value.StartsWith('@') && value.Length > 1)
            { source = value[1..].Trim(); continue; }
            if (value.StartsWith('>') && MemoItem.SafeWebUrl(value[1..].Trim()))
            { link = value[1..].Trim(); continue; }
            if (ScheduleText.IsMarkerLine(value)) continue;
            // Only lines consisting entirely of hashtag tokens are metadata-only.
            if (value.Length > 0 && TagPattern().Matches(value).Count > 0
                && string.IsNullOrWhiteSpace(TagPattern().Replace(value, ""))) continue;
            display.Add(line);
        }
        return new(string.Join("\n", display), tags, source, link);
    }

    public static string Insert(string body, string kind, string input)
    {
        input = input.Trim();
        if (input.Length == 0) throw new MemoException("넣을 내용을 입력해 주세요.");
        string line;
        if (kind is "Date" or "Time") return ScheduleText.Insert(body, kind, input);
        if (kind == "Tag")
        {
            string[] parts = input.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.TrimStart('#')).ToArray();
            if (parts.Any(s => s.Length > 40 || !TagPattern().IsMatch("#" + s)
                || TagPattern().Match("#" + s).Length != s.Length + 1))
                throw new MemoException("태그는 한글·영문·숫자·밑줄·하이픈으로 입력합니다. 여러 태그는 띄어 쓰세요.");
            var existing = Parse(body).Tags;
            line = string.Join(" ", parts.Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(s => !existing.Contains(s, StringComparer.OrdinalIgnoreCase)).Select(s => "#" + s));
            if (line.Length == 0) return body;
        }
        else if (kind == "Source")
        {
            input = input.TrimStart('@').Trim();
            if (input.Length is 0 or > 500 || input.Contains('\n') || input.Contains('\r'))
                throw new MemoException("출처는 한 줄로 500자까지 입력합니다.");
            line = "@" + input;
        }
        else if (kind == "Link")
        {
            input = input.TrimStart('>').Trim();
            if (input.Length > 2048 || !MemoItem.SafeWebUrl(input))
                throw new MemoException("링크는 http 또는 https 주소로 입력합니다.");
            line = ">" + input;
        }
        else throw new MemoException("입력 종류를 선택해 주세요.");
        if (kind is "Source" or "Link")
        {
            // Replacing an explicit marker leaves ordinary text and emails alone.
            body = string.Join("\n", body.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
                .Where(s => kind == "Source" ? !(s.Trim().StartsWith('@') && s.Trim().Length > 1)
                    : !(s.Trim().StartsWith('>') && MemoItem.SafeWebUrl(s.Trim()[1..].Trim()))));
        }
        return body.Length == 0 ? line : body + (body.EndsWith('\n') || body.EndsWith('\r') ? "" : "\n") + line;
    }

    // These spans are shared by the real bubble renderer and checks.
    public static IReadOnlyList<(int Start, int Length)> TagSpans(string text) =>
        TagPattern().Matches(text).Select(m => (m.Index, m.Length)).ToArray();

    public static bool Matches(MemoItem item, string query)
    {
        var metadata = Parse(item.Body);
        return query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).All(term =>
            term.StartsWith('#') ? metadata.HasTag(term)
            : term.StartsWith('@') && term.Length > 1 ? (metadata.Source ?? item.Source)?.Contains(term[1..], StringComparison.OrdinalIgnoreCase) == true
            : new[] { item.Body, item.Title, metadata.Source ?? item.Source }.Any(v => v?.Contains(term, StringComparison.OrdinalIgnoreCase) == true));
    }
}
