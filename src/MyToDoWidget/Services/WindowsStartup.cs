using Microsoft.Win32;

namespace MyToDoWidget.Services;

internal sealed class WindowsStartup : IStartupRegistrationStore
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = BuildIdentity.StartupValueName;

    internal static StartupRegistration Registration { get; } = new(new WindowsStartup());

    public string? ReadCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        object? value = key?.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (value is null) return null;
        if (value is string command && key!.GetValueKind(ValueName) == RegistryValueKind.String) return command;
        throw new IOException("Unexpected startup registration format.");
    }

    public void WriteCommand(string? command)
    {
        if (command is null)
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }
        using var target = Registry.CurrentUser.CreateSubKey(RunKey, writable: true)
            ?? throw new IOException("Startup registration is unavailable.");
        target.SetValue(ValueName, command, RegistryValueKind.String);
    }

    internal static string? RefreshExistingPath()
    {
        try { Registration.RefreshExistingPath(Environment.ProcessPath); return null; }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { return "자동 실행 경로를 확인하거나 갱신하지 못했습니다. 설정 → 동작에서 확인해 주세요."; }
    }
}
