namespace MyToDoWidget.Core;

public static class CardColors
{
    public const string DefaultTag = "#176B98";
    public const string DefaultUrgent = "#FFE0D2";
    // Calendar days in the viewer's time zone. Includes today's unfinished schedules.
    public static bool IsApproaching(MemoItem item, int days, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (item.Kind != MemoKind.Schedule || item.IsDeleted || item.IsCompleted || item.WhenDate is null) return false;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).Date);
        var date = item.WhenAt is { } time ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time, zone).Date) : item.WhenDate.Value;
        int remaining = date.DayNumber - today.DayNumber;
        return remaining >= 0 && remaining <= Math.Clamp(days, 0, 30);
    }
    public static string Resolve(MemoItem item, string fallback, bool urgentEnabled, int days, string urgentColor, DateTimeOffset now, TimeZoneInfo zone)
        => urgentEnabled && IsApproaching(item, days, now, zone) ? urgentColor : item.CardColor ?? fallback;
}
