namespace MyToDoWidget.Services;

public static class Theme
{
    public static void Apply(bool light)
    {
        var colors = new Dictionary<string, string>
        {
            ["TextBrush"] = light ? "#1C2A36" : "#F1F5F9",
            ["MutedBrush"] = light ? "#506373" : "#ADBACB",
            ["CardBrush"] = light ? "#A8FFFFFF" : "#18FFFFFF",
            ["BubbleBrush"] = light ? "#F8FCFA" : "#30443E",
            ["BubbleEdgeBrush"] = light ? "#40668E7B" : "#5076A28F",
            ["GlassEdgeBrush"] = light ? "#90FFFFFF" : "#50FFFFFF",
            ["ChromeBrush"] = light ? "#E6F1F5F7" : "#E6202937",
            ["LineBrush"] = light ? "#203A5266" : "#25FFFFFF",
            ["PanelBrush"] = light ? "#F1F5F7" : "#202937",
            ["FieldBrush"] = light ? "#FFFFFF" : "#14202D",
            ["AccentBrush"] = light ? "#245F4F" : "#BFE3D7",
            ["AccentTextBrush"] = light ? "#FFFFFF" : "#153E32"
        };
        foreach (var pair in colors)
            Application.Current.Resources[pair.Key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(pair.Value));
    }
}
