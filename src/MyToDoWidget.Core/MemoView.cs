namespace MyToDoWidget.Core;

public static class MemoView
{
    public const int Collapsed = 1;
    public const int Expanded = 3;

    // Keep the saved value for full view. The old two-line preview becomes collapsed.
    public static int Normalize(int value) => value == Expanded ? Expanded : Collapsed;
}
