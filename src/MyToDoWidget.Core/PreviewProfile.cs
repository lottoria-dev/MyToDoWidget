namespace MyToDoWidget.Core;

public static class PreviewProfile
{
    public const string RootFolder = "MyToDoWidget.V2.Preview";

    public static string VersionName(string version)
    {
        // SDK informational versions may append a source revision after '+'.
        string name = version.Split('+', 2)[0];
        if (name.Length is < 1 or > 80 || name is "." or ".."
            || !char.IsAsciiLetterOrDigit(name[0])
            || !name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_'))
            throw new MemoException("개발 버전 정보가 올바르지 않습니다. Directory.Build.props의 Version을 확인해 주세요.");
        return name;
    }

    public static string StateFolder(string version) => Path.Combine(RootFolder, VersionName(version));
}
