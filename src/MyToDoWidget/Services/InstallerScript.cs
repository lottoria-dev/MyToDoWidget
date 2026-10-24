using System.IO;
using System.Text;

namespace MyToDoWidget.Services;

internal static class InstallerScript
{
    public const string NewSheetUrl = "https://sheets.google.com/create";
    private static readonly Lazy<string> Source = new(() =>
    {
        using Stream stream = typeof(InstallerScript).Assembly.GetManifestResourceStream("MyToDoWidget.Installer.gs")
            ?? throw new InvalidOperationException("The single installer resource is missing.");
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        return reader.ReadToEnd();
    });
    public static string Read() => Source.Value;
}
