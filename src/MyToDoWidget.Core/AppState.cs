using System.Text.Json.Serialization;

namespace MyToDoWidget.Core;

public sealed class AppState
{
    [JsonRequired] public int SchemaVersion { get; set; } = 2;
    public string? TemplateCopyUrl { get; set; }
    public string? Endpoint { get; set; }
    public string? ConnectionKey { get; set; }
    public string? DatasetId { get; set; }
    public List<MemoItem> Items { get; set; } = [];
    public List<PendingChange> Outbox { get; set; } = [];
    public List<PendingOrderChange> OrderOutbox { get; set; } = [];
    [JsonIgnore] public int PendingCount => Outbox.Count + OrderOutbox.Count;
    public DateTimeOffset? LastSynced { get; set; }
    public MemoKind ActiveTab { get; set; }
    public int PreviewLines { get; set; } = 2;
    public bool ShowCompleted { get; set; } = true;
    public string SearchText { get; set; } = "";
    public Dictionary<string, int> ItemViewLevels { get; set; } = [];
    public Dictionary<MemoKind, List<string>> ItemOrders { get; set; } = [];
    public Dictionary<MemoKind, double> ScrollOffsets { get; set; } = [];
    public Dictionary<string, string> DeliveredReminders { get; set; } = [];
    public bool NotificationsEnabled { get; set; } = true;
    public int SyncIntervalSeconds { get; set; } = SyncPolicy.DefaultIntervalSeconds;
    public bool WelcomeShown { get; set; }
    public int SetupCompletedSteps { get; set; }
    [JsonIgnore] public bool HasConnection => Endpoint is not null && ConnectionKey is not null;
    public double Width { get; set; } = 390;
    public double Height { get; set; } = 530;
    public double? Left { get; set; }
    public double? Top { get; set; }
    public DesktopPosition? PreferredDesktopPosition { get; set; }
    public double FontSize { get; set; } = 16;
    public bool ScaleTextWithWindow { get; set; } = true;
    public double TextReferenceWidth { get; set; }
    public double TextReferenceHeight { get; set; }
    // 0 = transparent, 100 = opaque.
    public int BackgroundPercent { get; set; } = 60;
    public int HeaderBackgroundPercent { get; set; } = 92;
    public int OuterBorderPercent { get; set; } = 90;
    // Null uses the widget default.
    public string? GlassColor { get; set; }
    public BubbleTail TailSide { get; set; } = BubbleTail.Left;
    public BubbleShape BubbleShape { get; set; } = BubbleShape.Rounded;
    // Alignment is independent of the tail direction.
    public BubbleAlign? BubbleAlignment { get; set; }
    public BubbleWidthMode BubbleWidth { get; set; } = BubbleWidthMode.Widget;
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
    public bool IsRolledUp { get; set; }
    public bool LightTheme { get; set; }
    public bool AlwaysOnTop { get; set; }
    public bool LockPosition { get; set; }
    public bool GlassEffect { get; set; } = true;
    // Retained for old backup/state files; startup synchronization always runs when connected.
    public bool RefreshOnStart { get; set; } = true;
    public NoteApp NoteApp { get; set; } = NoteApp.GoogleKeep;

    public AppState Copy()
    {
        var copy = (AppState)MemberwiseClone();
        copy.Items = Items.Select(i => i.Copy()).ToList();
        copy.Outbox = Outbox.Select(c => c.Copy()).ToList();
        copy.OrderOutbox = OrderOutbox.Select(c => c.Copy()).ToList();
        copy.ItemViewLevels = new(ItemViewLevels);
        copy.ItemOrders = ItemOrders.ToDictionary(p => p.Key, p => new List<string>(p.Value));
        copy.ScrollOffsets = new(ScrollOffsets);
        copy.DeliveredReminders = new(DeliveredReminders);
        return copy;
    }

    public void Normalize()
    {
        SetupCompletedSteps = Math.Clamp(SetupCompletedSteps, 0, 3);
        if (TemplateCopyUrl is not null)
        {
            try { TemplateCopyUrl = SheetTemplate.NormalizeCopyUrl(TemplateCopyUrl).AbsoluteUri; }
            catch (MemoException) { TemplateCopyUrl = null; }
        }
        Width = double.IsFinite(Width) ? Math.Clamp(Width, 180, 2000) : 390;
        Height = double.IsFinite(Height) ? Math.Clamp(Height, 280, 2000) : 530;
        // Keep the text scale reference separate from the current window size.
        TextReferenceWidth = double.IsFinite(TextReferenceWidth) && TextReferenceWidth > 0
            ? Math.Clamp(TextReferenceWidth, 180, 2000) : Width;
        TextReferenceHeight = double.IsFinite(TextReferenceHeight) && TextReferenceHeight > 0
            ? Math.Clamp(TextReferenceHeight, 280, 2000) : Height;
        FontSize = double.IsFinite(FontSize) ? Math.Clamp(Math.Round(FontSize), 11, 36) : 16;
        BackgroundPercent = Math.Clamp(BackgroundPercent, 0, 100);
        HeaderBackgroundPercent = Math.Clamp(HeaderBackgroundPercent, 0, 100);
        OuterBorderPercent = Math.Clamp(OuterBorderPercent, 0, 100);
        if (PreferredDesktopPosition is { IsValid: false }) PreferredDesktopPosition = null;
        GlassColor = MemoStyle.TryHexColor(GlassColor, out var glass) ? glass : null;
        BubbleBackgroundPercent = Math.Clamp(BubbleBackgroundPercent, 0, 100);
        if (!Enum.IsDefined(TailSide)) TailSide = BubbleTail.Left;
        if (!Enum.IsDefined(BubbleShape)) BubbleShape = BubbleShape.Rounded;
        if (BubbleAlignment is not { } alignment || !Enum.IsDefined(alignment))
            BubbleAlignment = TailSide == BubbleTail.Right ? BubbleAlign.Right : BubbleAlign.Left;
        if (!Enum.IsDefined(BubbleWidth)) BubbleWidth = BubbleWidthMode.Widget;
        BubbleWidth = BubbleWidthMode.Widget;
        TagTextColor = MemoStyle.TryHexColor(TagTextColor, out var tagColor) ? tagColor : CardColors.DefaultTag;
        UrgentColor = MemoStyle.TryHexColor(UrgentColor, out var urgentColor) ? urgentColor : CardColors.DefaultUrgent;
        UrgentDays = Math.Clamp(UrgentDays, 0, 30);
        BubbleGap = Math.Clamp(BubbleGap, 0, 40);
        BubbleColor = MemoStyle.TryHexColor(BubbleColor, out var background) ? background : MemoStyle.DefaultBackground;
        MemoTextColor = MemoStyle.TryHexColor(MemoTextColor, out var text) ? text : MemoStyle.DefaultText;
        MemoFontFamily = string.IsNullOrWhiteSpace(MemoFontFamily) || MemoFontFamily.Length > 128
            ? MemoStyle.DefaultFont : MemoFontFamily.Trim();
        if (Left.HasValue && !double.IsFinite(Left.Value)) Left = null;
        if (Top.HasValue && !double.IsFinite(Top.Value)) Top = null;
        if (!Enum.IsDefined(NoteApp)) NoteApp = NoteApp.GoogleKeep;
        Items ??= []; Outbox ??= []; OrderOutbox ??= [];
        ItemViewLevels ??= [];
        ItemViewLevels = ItemViewLevels.Where(p => MemoItem.IsId(p.Key)).ToDictionary(p => p.Key, p => Math.Clamp(p.Value, 1, 3));
        ItemOrders ??= [];
        ItemOrders = ItemOrders.Where(p => Enum.IsDefined(p.Key) && p.Value is not null)
            .ToDictionary(p => p.Key, p => p.Value.Where(MemoItem.IsId).Distinct(StringComparer.Ordinal).ToList());
        ScrollOffsets ??= []; DeliveredReminders ??= [];
        PreviewLines = Math.Clamp(PreviewLines, 1, 3);
        SyncIntervalSeconds = SyncPolicy.NormalizeInterval(SyncIntervalSeconds);
        if (!Enum.IsDefined(ActiveTab)) ActiveTab = MemoKind.Memo;
        SearchText ??= "";
    }
}
