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
        bool freshProfile = !store.HasStoredState;
        StateLoadResult loaded;
        try { loaded = store.Load(); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            MessageBox.Show("저장 파일을 읽거나 원본 보관본을 만들지 못했습니다. 데이터 폴더의 권한과 여유 공간을 확인해 주세요.",
                "MyToDo2", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown(); return;
        }
        // Initialize local state without changing an existing Windows startup choice.
        if (freshProfile)
        {
            try { store.Save(loaded.State); }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                MessageBox.Show("처음 사용할 환경을 초기화하지 못했습니다. " + (ex is MemoException ? ex.Message
                    : "데이터 폴더의 권한과 여유 공간을 확인해 주세요."), "MyToDo2", MessageBoxButton.OK, MessageBoxImage.Warning);
                Shutdown(); return;
            }
        }
        string? startupNotice = freshProfile ? null : WindowsStartup.RefreshExistingPath();
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
