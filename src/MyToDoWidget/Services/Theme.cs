namespace MyToDoWidget.Services;

public static class Theme
{
    public static void Apply(bool light) => ApplyTo(Application.Current.Resources, light);

    // Window-local resources keep settings readable during widget previews.
    public static void ApplySettings(ResourceDictionary resources) => ApplyTo(resources, light: true);

    private static void ApplyTo(ResourceDictionary resources, bool light)
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
            ["AccentBrush"] = "#00A0D7",
            ["AccentTextBrush"] = "#FFFFFF",
            ["TabBrush"] = light ? "#D7F0FA" : "#21485D",
            ["TabTextBrush"] = light ? "#18546D" : "#C6ECFC",
            ["ScrollThumbBrush"] = light ? "#96A8B4" : "#7C91A2"
        };
        foreach (var pair in colors)
            resources[pair.Key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(pair.Value));
    }
}
