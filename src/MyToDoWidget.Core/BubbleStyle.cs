namespace MyToDoWidget.Core;

public enum BubbleTail { Left, Right, None }
public enum BubbleShape { Rounded, Square, Droplet, Capsule, CutCorner }

public static class MemoStyle
{
    public const string DefaultBackground = "#DDF2E7";
    public const string DefaultText = "#263B33";
    public const string DefaultFont = "Malgun Gothic";

    public static bool TryHexColor(string? value, out string color)
    {
        color = (value ?? "").Trim();
        if (color.Length == 6) color = "#" + color;
        if (color.Length != 7 || color[0] != '#' || !color.AsSpan(1).ToString().All(Uri.IsHexDigit)) return false;
        color = color.ToUpperInvariant();
        return true;
    }
}
