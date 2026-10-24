namespace MyToDoWidget.Core;

public interface IStartupRegistrationStore
{
    string? ReadCommand();
    void WriteCommand(string? command);
}

// The OS registration is the source of truth; a restored memo backup must not
// re-enable startup after the user has removed the registration in Windows.
public sealed class StartupRegistration(IStartupRegistrationStore store)
{
    public bool IsRegistered => !string.IsNullOrWhiteSpace(store.ReadCommand());

    public static string BuildCommand(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !Path.IsPathFullyQualified(executablePath)
            || executablePath.IndexOfAny(['"', '\r', '\n', '\0']) >= 0
            || !string.Equals(Path.GetExtension(executablePath), ".exe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFileName(executablePath), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
            throw new MemoException("자동 실행은 MyToDo2.exe로 실행한 뒤 설정해 주세요.");

        string command = "\"" + executablePath + "\"";
        if (command.Length > 260)
            throw new MemoException("자동 실행에 등록할 경로가 너무 깁니다. 프로그램을 더 짧은 경로로 옮겨 주세요.");
        return command;
    }

    public void Save(bool? enabled, string? executablePath, Action saveSettings)
    {
        // A read failure disables only this setting, leaving other options usable.
        if (enabled is null) { saveSettings(); return; }
        string? before;
        try { before = store.ReadCommand(); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { throw new MemoException("자동 실행 등록 상태를 읽지 못했습니다. 설정을 다시 열어 확인해 주세요."); }

        string? after = enabled.Value ? BuildCommand(executablePath) : null;
        if (string.Equals(before, after, StringComparison.OrdinalIgnoreCase)) { saveSettings(); return; }
        try { store.WriteCommand(after); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { throw new MemoException("Windows 자동 실행 설정을 변경하지 못했습니다. 계정 권한이나 시작 프로그램 제한을 확인해 주세요."); }

        try { saveSettings(); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            try { store.WriteCommand(before); }
            catch (Exception restoreError) when (restoreError is not OutOfMemoryException)
            { throw new MemoException("설정 저장에 실패했고 자동 실행도 이전 상태로 되돌리지 못했습니다. 설정창을 다시 열어 자동 실행 상태를 확인해 주세요."); }
            throw;
        }
    }

    public void RefreshExistingPath(string? executablePath)
    {
        string? before = store.ReadCommand();
        if (string.IsNullOrWhiteSpace(before)) return;
        string after = BuildCommand(executablePath);
        if (!string.Equals(before, after, StringComparison.OrdinalIgnoreCase)) store.WriteCommand(after);
    }
}
