using System.Reflection;

namespace MyToDoWidget.Services;

internal static class BuildIdentity
{
    public static bool IsTest => false;
    public const string StartupValueName = "MyToDoWidget.V2";
    public static readonly string Version = PreviewProfile.VersionName(
        typeof(BuildIdentity).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? throw new InvalidOperationException("프로그램 버전 정보가 없습니다."));
    // Stable across regular updates; independent from 1.x and developer preview data.
    public const string StateFolder = "MyToDoWidget.V2";
    public const string InstancePrefix = @"Local\MyToDoWidget.V2";
    public const string DisplayName = "MyToDo2";
}
