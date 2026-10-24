namespace MyToDoWidget.Core;

public static class SyncPolicy
{
    public const int DefaultIntervalSeconds = 30 * 60;
    public static IReadOnlyList<int> PresetSeconds { get; } = Array.AsReadOnly(new[] { 0, 300, 600, 900, 1800, 3600, 7200 });

    // Older preview intervals in seconds fall back to the new 30-minute default.
    public static int NormalizeInterval(int seconds) => PresetSeconds.Contains(seconds) ? seconds : DefaultIntervalSeconds;

    public static bool ShouldRefreshOnStart(bool connected, int seconds, bool refreshOnStart)
        // Legacy interval/startup preferences never suppress the first request.
        => connected;

    public static bool IsDue(DateTimeOffset now, bool connected, int seconds, DateTimeOffset nextAttempt)
        => connected && NormalizeInterval(seconds) > 0 && now >= nextAttempt;

    public static DateTimeOffset NextAttempt(DateTimeOffset now, bool connected, int seconds, int failures = 0)
    {
        int interval = NormalizeInterval(seconds);
        if (!connected || interval == 0) return DateTimeOffset.MaxValue;
        // Failed automatic attempts wait at least the selected interval, up to four hours.
        int delay = Math.Min(4 * 60 * 60, interval * (1 << Math.Clamp(failures, 0, 3)));
        return now.AddSeconds(delay);
    }
}
