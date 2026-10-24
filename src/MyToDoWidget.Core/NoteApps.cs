namespace MyToDoWidget.Core;

// Keep the former numeric values so a removed choice cannot become another service.
public enum NoteApp { GoogleKeep = 0, OneNote = 2, AppleNotes = 3, Notion = 4 }

public sealed record NoteAppOption(NoteApp Value, string Label);

public static class NoteApps
{
    public static IReadOnlyList<NoteAppOption> Choices { get; } = Array.AsReadOnly(new[]
    {
        new NoteAppOption(NoteApp.GoogleKeep, "Google Keep"),
        new NoteAppOption(NoteApp.OneNote, "Microsoft OneNote"),
        new NoteAppOption(NoteApp.AppleNotes, "Apple 메모 (iCloud)"),
        new NoteAppOption(NoteApp.Notion, "Notion")
    });

    public static string Label(NoteApp app) => Choices.FirstOrDefault(c => c.Value == app)?.Label ?? "Google Keep";

    // Fixed HTTPS destinations; memo text and Google sheet credentials are never appended.
    public static string LaunchTarget(NoteApp app) => app switch
    {
        NoteApp.GoogleKeep => "https://keep.google.com/",
        NoteApp.OneNote => "https://www.onenote.com/notebooks",
        NoteApp.AppleNotes => "https://www.icloud.com/notes/",
        NoteApp.Notion => "https://www.notion.so/login",
        _ => throw new MemoException("설정 → 동작에서 메모 웹 서비스를 다시 선택해 주세요.")
    };
}
