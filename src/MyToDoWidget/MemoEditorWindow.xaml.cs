using System.ComponentModel;
using MyToDoWidget.Services;

namespace MyToDoWidget;

public partial class MemoEditorWindow : Window
{
    private readonly MemoItem _original;
    private readonly Func<MemoItem, string?> _save;
    private bool _saved, _ready, _updatingColor;
    private sealed record ColorChoice(string Value, string Label);
    private readonly bool _isNew;
    private readonly string _initialFingerprint;
    public MemoEditorWindow(MemoItem item, Func<MemoItem, string?> save, bool isNew, string tagColor)
    {
        _original = item.Copy(); _save = save; _isNew = isNew;
        InitializeComponent(); Theme.ApplySettings(Resources);
        Resources["EditorTagBrush"] = MemoAppearance.Brush(tagColor);
        Title = isNew ? "새 메모 / 일정" : "메모 / 일정 편집";
        KindBox.SelectedIndex = (int)item.Kind;
        string body = item.Body;
        var oldMetadata = TaggedText.Parse(body);
        if (oldMetadata.Source is null && !string.IsNullOrWhiteSpace(item.Source)) body = TaggedText.Insert(body, "Source", item.Source);
        if (oldMetadata.Link is null && !string.IsNullOrWhiteSpace(item.OriginalUrl)) body = TaggedText.Insert(body, "Link", item.OriginalUrl);
        if (item.WhenDate is not null && item.DateRole == DateRole.Deadline && !oldMetadata.HasTag("마감")) body = TaggedText.Insert(body, "Tag", "마감");
        // Convert existing server dates once into explicit body markers when edited.
        // The source remains visible and editable in the body thereafter.
        if (!body.Replace("\r", "").Split('\n').Any(ScheduleText.IsMarkerLine) && item.WhenDate is { } date)
        {
            var zone = ScheduleText.ResolveZone(item.TimeZoneId, TimeZoneInfo.Local);
            body = TaggedText.Insert(body, "Date", date.ToString("yyyy-MM-dd"));
            if (item.WhenAt is { } time) body = TaggedText.Insert(body, "Time", TimeZoneInfo.ConvertTime(time, zone).ToString("HH:mm"));
        }
        BodyBox.Text = body; TitleBox.Text = item.Title ?? "";
        CardColorCombo.ItemsSource = new[] { new ColorChoice("", "공통색 사용") }.Concat(MemoAppearance.Presets.Select(t => new ColorChoice(t.Background, t.Name))).Append(new ColorChoice("custom", "사용자 색상")).ToList();
        CardColorHex.Text = item.CardColor ?? "";
        CardColorCombo.SelectedValue = item.CardColor is null ? "" : MemoAppearance.Presets.Any(t => t.Background == item.CardColor) ? item.CardColor : "custom";
        ReminderCheck.IsChecked = item.ReminderAt is not null;
        ReminderDatePicker.SelectedDate = item.ReminderAt?.ToLocalTime().Date ?? DateTime.Today;
        ReminderTimeBox.Text = item.ReminderAt?.ToLocalTime().ToString("HH:mm") ?? "09:00";
        CompletedCheck.IsChecked = item.IsCompleted;
        TimeZoneHint.Text = $"일정 기준 시간대: {ScheduleText.ResolveZone(item.TimeZoneId, TimeZoneInfo.Local).DisplayName}\n다른 PC에서는 현지 시각으로 표시합니다.\n알림 변경은 일정 기한에 영향을 주지 않습니다.";
        if (item.ConflictOf is not null) ErrorText.Text = "다른 기기와의 충돌로 보관한 내용입니다. 저장하면 일반 항목으로 확정합니다.";
        UpdateKindLayout();
        _ready = true; UpdateMetadataHint();
        _initialFingerprint = Fingerprint();
        Loaded += (_, _) => {
            var screen = System.Windows.Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle);
            double available = Math.Max(300, screen.WorkingArea.Height / VisualTreeHelper.GetDpi(this).DpiScaleY - 24);
            MinHeight = Math.Min(MinHeight, available); MaxHeight = available; Height = Math.Min(Height, available);
            if (isNew && item.Kind == MemoKind.Schedule) TitleBox.Focus(); else { BodyBox.Focus(); BodyBox.CaretIndex = BodyBox.Text.Length; } };
    }
    private void Kind_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => UpdateKindLayout();
    private void UpdateKindLayout()
    {
        if (DatePanel is null || EditorHint is null) return;
        bool schedule = KindBox.SelectedIndex == 1;
        DatePanel.Visibility = schedule || ScheduleText.Parse(BodyBox.Text).Date is not null ? Visibility.Visible : Visibility.Collapsed;
        EditorHint.Text = schedule ? "날짜·시각은 본문 표기를 읽어 자동 반영" : "제목을 비우면 자유 본문을 자유롭게 표시";
    }
    private void Body_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e) { if (_ready) UpdateMetadataHint(); }
    private void UpdateMetadataHint()
    {
        var metadata = TaggedText.Parse(BodyBox.Text);
        RecognizedTags.ItemsSource = metadata.Tags;
        RecognizedTags.ToolTip = string.Join(" ", metadata.Tags.Select(t => "#" + t));
        var schedule = ScheduleText.Parse(BodyBox.Text);
        RecognizedDate.Text = schedule.Error ?? schedule.Date?.ToString("yyyy.MM.dd") ?? "날짜 미지정";
        RecognizedTime.Text = schedule.Error is null ? schedule.Time?.ToString("HH:mm") ?? "종일" : "";
        UpdateKindLayout();
        MetadataHint.Text = string.Join(" · ", new[] {
            metadata.Source is not null ? "출처: " + metadata.Source : null,
            metadata.Link is not null ? "링크 입력됨" : null }.Where(s => s is not null));
    }
    private bool InsertPendingToken()
    {
        if (string.IsNullOrWhiteSpace(TokenInputBox.Text)) { TokenInputBox.Clear(); return true; }
        try
        {
            string body = TaggedText.Insert(BodyBox.Text, TokenKindBox.SelectedValue as string ?? "Tag", TokenInputBox.Text);
            if (body.Length > 40000) throw new MemoException("메모는 40,000자까지 저장합니다.");
            BodyBox.SelectAll(); BodyBox.SelectedText = body;
            TokenInputBox.Clear(); ErrorText.Text = ""; return true;
        }
        catch (MemoException error) { ErrorText.Text = error.Message; return false; }
    }
    private void InsertToken_Click(object sender, RoutedEventArgs e) => InsertPendingToken();
    private void TokenInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        e.Handled = true; InsertPendingToken();
    }
    private void Paste_Click(object sender, RoutedEventArgs e)
    {
        try { BodyBox.SelectedText = Clipboard.GetText(); BodyBox.Focus(); }
        catch (System.Runtime.InteropServices.ExternalException) { ErrorText.Text = "클립보드를 읽지 못했습니다. 다시 시도해 주세요."; }
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!InsertPendingToken()) return;
            if (!_isNew && _original.ConflictOf is null && _initialFingerprint == Fingerprint())
            { _saved = true; DialogResult = true; return; }
            var item = _original.Copy(); item.Kind = KindBox.SelectedIndex == 1 ? MemoKind.Schedule : MemoKind.Memo;
            item.Body = BodyBox.Text; item.Title = Optional(TitleBox.Text);
            var metadata = TaggedText.Parse(item.Body); item.Source = metadata.Source; item.OriginalUrl = metadata.Link;
            ScheduleText.Apply(item, _original, TimeZoneInfo.Local);
            if (!string.IsNullOrWhiteSpace(CardColorHex.Text) && !MemoStyle.TryHexColor(CardColorHex.Text, out _)) throw new MemoException("항목 배경색을 #RRGGBB로 입력하거나 비워 주세요.");
            item.CardColor = MemoStyle.TryHexColor(CardColorHex.Text, out var cardColor) ? cardColor : null;
            item.ReminderAt = ReminderCheck.IsChecked == true
                ? MemoDates.AtLocalDate(ReminderDatePicker.SelectedDate ?? throw new MemoException("알림 날짜를 선택해 주세요."), ReminderTimeBox.Text, _original.ReminderAt, TimeZoneInfo.Local) : null;
            item.ReminderId = item.ReminderAt is null ? null : item.ReminderAt != _original.ReminderAt || _original.ReminderId is null
                ? Guid.NewGuid().ToString("N") : _original.ReminderId;
            item.IsCompleted = CompletedCheck.IsChecked == true;
            item.CompletedAt = item.IsCompleted ? _original.CompletedAt ?? DateTimeOffset.UtcNow : null;
            if (_original.IsCompleted && !item.IsCompleted && item.ReminderAt is not null) item.ReminderId = Guid.NewGuid().ToString("N");
            item.ConflictOf = item.ConflictReason = null;
            item.Validate(); string? error = _save(item);
            if (error is not null) { ErrorText.Text = error; return; }
            _saved = true; DialogResult = true;
        }
        catch (MemoException ex) { ErrorText.Text = ex.Message; }
    }
    private void CardColor_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _updatingColor || CardColorCombo.SelectedValue is not string value || value == "custom") return;
        _updatingColor = true; CardColorHex.Text = value; _updatingColor = false;
    }
    private void CardColorText_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_ready || _updatingColor) return;
        _updatingColor = true;
        CardColorCombo.SelectedValue = string.IsNullOrWhiteSpace(CardColorHex.Text) ? "" : MemoStyle.TryHexColor(CardColorHex.Text, out var color) && MemoAppearance.Presets.Any(t => t.Background == color) ? color : "custom";
        _updatingColor = false;
    }
    private sealed class DialogOwner(IntPtr handle) : System.Windows.Forms.IWin32Window { public IntPtr Handle => handle; }
    private void CardColor_Click(object sender, RoutedEventArgs e)
    {
        string hex = MemoStyle.TryHexColor(CardColorHex.Text, out var current) ? current : MemoStyle.DefaultBackground;
        var color = (Color)ColorConverter.ConvertFromString(hex);
        using var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true, AnyColor = true, Color = System.Drawing.Color.FromArgb(color.R, color.G, color.B) };
        if (dialog.ShowDialog(new DialogOwner(new System.Windows.Interop.WindowInteropHelper(this).Handle)) == System.Windows.Forms.DialogResult.OK)
            CardColorHex.Text = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
    }
    private static string? Optional(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_saved || _initialFingerprint == Fingerprint()) return;
        if (MessageBox.Show(this, "저장하지 않은 내용을 닫을까요?", "MyToDo2", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) e.Cancel = true;
    }
    private string Fingerprint() => System.Text.Json.JsonSerializer.Serialize(new
    {
        BodyBox.Text, title = TitleBox.Text, token = TokenInputBox.Text, kind = KindBox.SelectedIndex,
        color = CardColorHex.Text,
        reminder = ReminderCheck.IsChecked, reminderDate = ReminderDatePicker.SelectedDate, reminderTime = ReminderTimeBox.Text,
        completed = CompletedCheck.IsChecked
    });
}
