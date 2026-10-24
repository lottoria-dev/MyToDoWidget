using System.Globalization;
using System.Text.RegularExpressions;

namespace MyToDoWidget.Core;

public sealed record ParsedSchedule(DateOnly? Date, TimeOnly? Time, string? Error);

/// <summary>Explicit body markers are the only editable source of schedule dates.</summary>
public static partial class ScheduleText
{
    [GeneratedRegex(@"^(\d{4})(?:[-./]|년\s*)(\d{1,2})(?:[-./]|월\s*)(\d{1,2})일?$", RegexOptions.CultureInvariant)]
    private static partial Regex DatePattern();
    public static bool IsMarkerLine(string text) => Marker(text) is not null;
    private static string? Marker(string text)
    {
        text = text.Trim();
        foreach (string name in new[] { "!날짜", "!시간" })
            if (text == name || text.StartsWith(name, StringComparison.Ordinal) && text.Length > name.Length && char.IsWhiteSpace(text[name.Length])) return name;
        return null;
    }
    public static ParsedSchedule Parse(string body)
    {
        DateOnly? date = null; TimeOnly? time = null; bool seenDate = false, seenTime = false;
        foreach (string raw in body.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            string line = raw.Trim(); string? marker = Marker(line); if (marker is null) continue;
            string value = line[marker.Length..].Trim();
            if (marker == "!날짜")
            {
                if (seenDate) return new(null, null, "!날짜는 한 줄만 사용하세요.");
                seenDate = true; var match = DatePattern().Match(value);
                if (!match.Success) return new(null, null, "날짜 예: !날짜 2026-11-02");
                try { date = new DateOnly(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture)); }
                catch (ArgumentOutOfRangeException) { return new(null, null, "실제로 존재하는 날짜를 입력하세요."); }
            }
            else
            {
                if (seenTime) return new(null, null, "!시간은 한 줄만 사용하세요.");
                seenTime = true;
                if (!TimeOnly.TryParseExact(value, new[] { "HH:mm", "H:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                    return new(null, null, "시각 예: !시간 15:30 (24시간제)");
                time = parsed;
            }
        }
        return time is not null && date is null ? new(null, null, "!시간을 쓰려면 !날짜도 입력하세요.") : new(date, time, null);
    }
    public static void Apply(MemoItem draft, MemoItem original, TimeZoneInfo localZone)
    {
        var parsed = Parse(draft.Body); if (parsed.Error is not null) throw new MemoException(parsed.Error);
        var zone = ResolveZone(original.TimeZoneId, localZone);
        draft.WhenDate = parsed.Date;
        draft.DateRole = parsed.Date is null ? DateRole.None : TaggedText.Parse(draft.Body).HasTag("마감") ? DateRole.Deadline : DateRole.Event;
        draft.WhenAt = parsed.Date is { } date && parsed.Time is { } time
            ? MemoDates.AtLocalDate(date.ToDateTime(TimeOnly.MinValue), time.ToString("HH:mm", CultureInfo.InvariantCulture), original.WhenAt, zone) : null;
        draft.TimeZoneId = draft.WhenAt is null ? null : IanaId(zone);
    }
    public static TimeZoneInfo ResolveZone(string? id, TimeZoneInfo fallback)
    {
        try { return string.IsNullOrWhiteSpace(id) ? fallback : TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { return fallback; }
        catch (InvalidTimeZoneException) { return fallback; }
    }
    public static string IanaId(TimeZoneInfo zone) => TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var id) ? id : zone.Id;
    public static string Insert(string body, string kind, string input)
    {
        string marker = kind == "Date" ? "!날짜" : "!시간";
        input = input.Trim(); if (input.StartsWith(marker)) input = input[marker.Length..].Trim();
        string candidate = marker + " " + input;
        // Time can be inserted before a date, but save will require the date too.
        var parsed = Parse(kind == "Time" ? "!날짜 2000-01-01\n" + candidate : candidate);
        if (parsed.Error is not null) throw new MemoException(parsed.Error);
        candidate = kind == "Date" ? marker + " " + parsed.Date!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : marker + " " + parsed.Time!.Value.ToString("HH:mm", CultureInfo.InvariantCulture);
        body = string.Join("\n", body.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Where(s => Marker(s) != marker));
        return body.Length == 0 ? candidate : body + (body.EndsWith('\n') ? "" : "\n") + candidate;
    }
}
