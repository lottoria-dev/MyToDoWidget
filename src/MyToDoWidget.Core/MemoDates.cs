using System.Globalization;

namespace MyToDoWidget.Core;

public static class MemoDates
{
    /// <summary>Retain seconds/offset when a minute-resolution editor has not changed the visible input.</summary>
    public static DateTimeOffset AtLocalDate(DateTime date, string text, DateTimeOffset? original, TimeZoneInfo zone)
    {
        text = text.Trim();
        if (original is { } value)
        {
            var shown = TimeZoneInfo.ConvertTime(value, zone);
            if (date.Date == shown.Date && text == shown.ToString("HH:mm", CultureInfo.InvariantCulture)) return value;
        }
        if (!TimeOnly.TryParseExact(text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            throw new MemoException("시각은 HH:mm 형식으로 입력해 주세요. 예: 09:00, 15:30");
        var local = DateTime.SpecifyKind(date.Date + time.ToTimeSpan(), DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local)) throw new MemoException("시간대 변경으로 존재하지 않는 시각입니다. 다른 시각을 선택해 주세요.");
        if (zone.IsAmbiguousTime(local)) throw new MemoException("시간대 변경으로 두 번 존재하는 시각입니다. 다른 시각을 선택해 주세요.");
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }
}
