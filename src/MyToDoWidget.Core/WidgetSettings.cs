namespace MyToDoWidget.Core;

// Only portable widget preferences belong here. Connection credentials, queues,
// item contents, desktop coordinates and Windows startup registration are excluded.
public sealed class WidgetSettings
{
    public double FontSize { get; set; } = 16;
    public bool ScaleTextWithWindow { get; set; } = true;
    public double TextReferenceWidth { get; set; }
    public double TextReferenceHeight { get; set; }
    public int BackgroundPercent { get; set; } = 60;
    public int HeaderBackgroundPercent { get; set; } = 92;
    public int OuterBorderPercent { get; set; } = 90;
    public string? GlassColor { get; set; }
    public BubbleTail TailSide { get; set; } = BubbleTail.Left;
    public BubbleShape BubbleShape { get; set; } = BubbleShape.Rounded;
    public int BubbleGap { get; set; } = 10;
    public string BubbleColor { get; set; } = MemoStyle.DefaultBackground;
    public int BubbleBackgroundPercent { get; set; } = 100;
    public string TagTextColor { get; set; } = CardColors.DefaultTag;
    public bool UrgentHighlight { get; set; } = true;
    public int UrgentDays { get; set; } = 3;
    public string UrgentColor { get; set; } = CardColors.DefaultUrgent;
    public string MemoTextColor { get; set; } = MemoStyle.DefaultText;
    public string MemoFontFamily { get; set; } = MemoStyle.DefaultFont;
    public bool MemoBold { get; set; }
    public bool MemoItalic { get; set; }
    public bool LightTheme { get; set; }
    public bool AlwaysOnTop { get; set; }
    public bool LockPosition { get; set; }
    public bool GlassEffect { get; set; } = true;
    public bool RefreshOnStart { get; set; } = true;
    public NoteApp NoteApp { get; set; } = NoteApp.GoogleKeep;
    public int PreviewLines { get; set; } = 2;
    public bool ShowCompleted { get; set; } = true;
    public bool NotificationsEnabled { get; set; } = true;
    public int SyncIntervalSeconds { get; set; } = SyncPolicy.DefaultIntervalSeconds;

    public static WidgetSettings FromState(AppState state) => new()
    {
        FontSize = state.FontSize,
        ScaleTextWithWindow = state.ScaleTextWithWindow,
        TextReferenceWidth = state.TextReferenceWidth,
        TextReferenceHeight = state.TextReferenceHeight,
        BackgroundPercent = state.BackgroundPercent,
        HeaderBackgroundPercent = state.HeaderBackgroundPercent,
        OuterBorderPercent = state.OuterBorderPercent,
        GlassColor = state.GlassColor,
        TailSide = state.TailSide,
        BubbleShape = state.BubbleShape,
        BubbleGap = state.BubbleGap,
        BubbleColor = state.BubbleColor,
        BubbleBackgroundPercent = state.BubbleBackgroundPercent,
        TagTextColor = state.TagTextColor,
        UrgentHighlight = state.UrgentHighlight,
        UrgentDays = state.UrgentDays,
        UrgentColor = state.UrgentColor,
        MemoTextColor = state.MemoTextColor,
        MemoFontFamily = state.MemoFontFamily,
        MemoBold = state.MemoBold,
        MemoItalic = state.MemoItalic,
        LightTheme = state.LightTheme,
        AlwaysOnTop = state.AlwaysOnTop,
        LockPosition = state.LockPosition,
        GlassEffect = state.GlassEffect,
        RefreshOnStart = state.RefreshOnStart,
        NoteApp = state.NoteApp,
        PreviewLines = state.PreviewLines,
        ShowCompleted = state.ShowCompleted,
        NotificationsEnabled = state.NotificationsEnabled,
        SyncIntervalSeconds = state.SyncIntervalSeconds,
    };

    public void ApplyTo(AppState state)
    {
        state.FontSize = FontSize;
        state.ScaleTextWithWindow = ScaleTextWithWindow;
        state.TextReferenceWidth = TextReferenceWidth;
        state.TextReferenceHeight = TextReferenceHeight;
        state.BackgroundPercent = BackgroundPercent;
        state.HeaderBackgroundPercent = HeaderBackgroundPercent;
        state.OuterBorderPercent = OuterBorderPercent;
        state.GlassColor = GlassColor;
        state.TailSide = TailSide;
        state.BubbleShape = BubbleShape;
        state.BubbleGap = BubbleGap;
        state.BubbleColor = BubbleColor;
        state.BubbleBackgroundPercent = BubbleBackgroundPercent;
        state.TagTextColor = TagTextColor;
        state.UrgentHighlight = UrgentHighlight;
        state.UrgentDays = UrgentDays;
        state.UrgentColor = UrgentColor;
        state.MemoTextColor = MemoTextColor;
        state.MemoFontFamily = MemoFontFamily;
        state.MemoBold = MemoBold;
        state.MemoItalic = MemoItalic;
        state.LightTheme = LightTheme;
        state.AlwaysOnTop = AlwaysOnTop;
        state.LockPosition = LockPosition;
        state.GlassEffect = GlassEffect;
        state.RefreshOnStart = RefreshOnStart;
        state.NoteApp = NoteApp;
        state.PreviewLines = PreviewLines;
        state.ShowCompleted = ShowCompleted;
        state.NotificationsEnabled = NotificationsEnabled;
        state.SyncIntervalSeconds = SyncIntervalSeconds;
    }

    public WidgetSettings Normalized()
    {
        var state = new AppState();
        ApplyTo(state);
        state.Normalize();
        return FromState(state);
    }
}
