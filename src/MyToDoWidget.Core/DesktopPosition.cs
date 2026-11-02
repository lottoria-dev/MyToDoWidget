namespace MyToDoWidget.Core;

// A user-selected position relative to its monitor, independent of temporary OS relocation.
public sealed record DesktopPosition(string DisplayName, int WorkAreaWidth, int WorkAreaHeight,
    int OffsetX, int OffsetY)
{
    // Device interface path, unlike DISPLAY1/2, identifies the monitor across enumeration changes.
    public string? MonitorId { get; init; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsValid => !string.IsNullOrWhiteSpace(DisplayName) && DisplayName.Length <= 128
        && (MonitorId is null || !string.IsNullOrWhiteSpace(MonitorId) && MonitorId.Length <= 1024)
        && WorkAreaWidth is > 0 and <= 100000 && WorkAreaHeight is > 0 and <= 100000
        && Math.Abs((long)OffsetX) <= 100000 && Math.Abs((long)OffsetY) <= 100000;

    public (int X, int Y) Resolve(int areaLeft, int areaTop, int areaWidth, int areaHeight,
        int windowWidth, int windowHeight)
    {
        // Preserve the exact offset on an unchanged screen; scale it if resolution changes.
        int x = areaLeft + (int)Math.Round(OffsetX * (double)areaWidth / WorkAreaWidth);
        int y = areaTop + (int)Math.Round(OffsetY * (double)areaHeight / WorkAreaHeight);
        int visibleWidth = Math.Min(100, Math.Min(windowWidth, areaWidth));
        int visibleHeight = Math.Min(40, Math.Min(windowHeight, areaHeight));
        return (Math.Clamp(x, areaLeft - Math.Max(0, windowWidth - visibleWidth), areaLeft + areaWidth - visibleWidth),
            Math.Clamp(y, areaTop, areaTop + areaHeight - visibleHeight));
    }
}

public sealed record DesktopMonitor(string DisplayName, string? MonitorId,
    int Left, int Top, int Width, int Height);

public static class DesktopPlacement
{
    public static DesktopMonitor? FindMonitor(DesktopPosition preferred, IEnumerable<DesktopMonitor> monitors)
    {
        if (!preferred.IsValid) return null;
        // Never fall back to a recycled DISPLAY number when a physical identity was saved.
        var matches = monitors.Where(m => preferred.MonitorId is not null
            ? string.Equals(m.MonitorId, preferred.MonitorId, StringComparison.OrdinalIgnoreCase)
            : string.Equals(m.DisplayName, preferred.DisplayName, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
}
