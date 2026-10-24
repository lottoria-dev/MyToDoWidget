using System.Globalization;

namespace MyToDoWidget.Core;

public static class MemoPresentation
{
    public static string Preview(MemoItem item) => item.Kind == MemoKind.Schedule
        ? item.DisplayTitle : item.Body;

    public static string Title(MemoItem item) => item.Kind == MemoKind.Schedule ? item.DisplayTitle : item.Title ?? "";

    public static string DateLine(MemoItem item, TimeZoneInfo zone)
    {
        if (item.WhenDate is not { } date) return item.Kind == MemoKind.Schedule ? "날짜 미지정" : "";
        string value = item.WhenAt is { } instant
            ? TimeZoneInfo.ConvertTime(instant, zone).ToString("yyyy.MM.dd HH:mm", CultureInfo.InvariantCulture)
            : date.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture);
        return value;
    }
}
