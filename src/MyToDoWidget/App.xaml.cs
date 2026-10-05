using MyToDoWidget.Services;

namespace MyToDoWidget;

public partial class App : Application
{
    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showWait;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _mutex = new Mutex(true, BuildIdentity.InstancePrefix + ".Instance", out _ownsMutex);
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, BuildIdentity.InstancePrefix + ".Show");
        if (!_ownsMutex) { _showEvent.Set(); Shutdown(); return; }

        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), BuildIdentity.StateFolder);
        var store = new StateStore(directory, new WindowsDataCipher());
        var loaded = store.Load();
        string? startupNotice = WindowsStartup.RefreshExistingPath();
        string? notice = loaded.Notice is null ? startupNotice : startupNotice is null
            ? loaded.Notice : loaded.Notice + "\n" + startupNotice;
        var window = new MainWindow(store, loaded.State, notice);
        MainWindow = window;
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent,
            (_, _) => Dispatcher.BeginInvoke(new Action(window.ShowWidget)), null, Timeout.Infinite, false);
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showWait?.Unregister(null);
        _showEvent?.Dispose();
        if (_ownsMutex) _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
