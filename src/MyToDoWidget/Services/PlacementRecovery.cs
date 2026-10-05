using Microsoft.Win32;
using System.Windows.Threading;

namespace MyToDoWidget.Services;

// Keep the user's placement while Windows temporarily removes sleeping/locked displays.
public sealed class PlacementRecovery : IDisposable
{
    private readonly Window _window;
    private readonly Func<bool> _isInteracting;
    private readonly Action _restoreSize;
    private readonly DispatcherTimer _timer;
    private bool _disposed, _suspended, _locked, _restoring;
    private int _ticks, _stableTicks;
    private string? _lastLayout;
    public DesktopPosition? Preferred { get; private set; }

    public PlacementRecovery(Window window, DesktopPosition? preferred,
        Func<bool> isInteracting, Action restoreSize)
    {
        _window = window; Preferred = preferred;
        _isInteracting = isInteracting; _restoreSize = restoreSize;
        _timer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher)
        { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += Recover;
        SystemEvents.DisplaySettingsChanged += DisplayChanged;
        SystemEvents.PowerModeChanged += PowerChanged;
        SystemEvents.SessionSwitch += SessionChanged;
        _window.LocationChanged += LocationChanged;
    }

    public bool RememberUserPosition()
    {
        if (_disposed || _locked || _suspended) return false;
        var position = DesktopAppearance.CapturePosition(_window);
        if (position is null) return false;
        Preferred = position;
        _timer.Stop(); // A deliberate move takes precedence over a pending automatic restore.
        return true;
    }

    public void Schedule()
    {
        if (_disposed || Preferred is null) return;
        _timer.Stop();
        _ticks = _stableTicks = 0;
        _lastLayout = null;
        if (!_locked && !_suspended) _timer.Start();
    }

    public void BringToPrimary()
    {
        _restoring = true;
        try { DesktopAppearance.EnsureVisible(_window, true); }
        finally { _restoring = false; }
        RememberUserPosition();
    }

    private void Recover(object? sender, EventArgs e)
    {
        if (_disposed || Preferred is null || _locked || _suspended) { _timer.Stop(); return; }
        if (_isInteracting()) return;
        _ticks++;
        string layout = DesktopAppearance.DisplayLayout();
        _stableTicks = layout == _lastLayout ? _stableTicks + 1 : 0;
        _lastLayout = layout;
        if (_stableTicks >= 1) // Two matching samples before touching the window.
        {
            _restoring = true;
            try
            {
                _restoreSize();
                if (!DesktopAppearance.RestorePosition(_window, Preferred) && _ticks >= 5)
                    DesktopAppearance.EnsureVisible(_window); // Temporary fallback after 10 seconds.
            }
            finally { _restoring = false; }
        }
        // Keep checking for slow display wake-up for 30 seconds; a later display event restarts it.
        if (_ticks >= 15) _timer.Stop();
    }

    private void LocationChanged(object? sender, EventArgs e)
    {
        if (!_restoring && !_isInteracting()) Schedule();
    }

    private void Dispatch(Action action)
    {
        if (_disposed || _window.Dispatcher.HasShutdownStarted) return;
        _ = _window.Dispatcher.BeginInvoke(new Action(() => { if (!_disposed) action(); }));
    }

    private void DisplayChanged(object? sender, EventArgs e) => Dispatch(Schedule);

    private void PowerChanged(object sender, PowerModeChangedEventArgs e) => Dispatch(() =>
    {
        if (e.Mode == PowerModes.Suspend) { _suspended = true; _timer.Stop(); }
        else if (e.Mode == PowerModes.Resume) { _suspended = false; Schedule(); }
    });

    private void SessionChanged(object sender, SessionSwitchEventArgs e) => Dispatch(() =>
    {
        if (e.Reason == SessionSwitchReason.SessionLock) { _locked = true; _timer.Stop(); }
        else if (e.Reason == SessionSwitchReason.SessionUnlock) { _locked = false; Schedule(); }
        else if (e.Reason is SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect) Schedule();
    });

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= Recover;
        _window.LocationChanged -= LocationChanged;
        SystemEvents.DisplaySettingsChanged -= DisplayChanged;
        SystemEvents.PowerModeChanged -= PowerChanged;
        SystemEvents.SessionSwitch -= SessionChanged;
    }
}
