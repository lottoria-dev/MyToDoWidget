using System.ComponentModel;
using MyToDoWidget.Core;

namespace MyToDoWidget;

public sealed class MemoCard : INotifyPropertyChanged
{
    public MemoItem Item { get; private set; }
    public int ViewLevel { get; private set; }
    public MemoCard(MemoItem item, int viewLevel) { Item = item; ViewLevel = Math.Clamp(viewLevel, 1, 3); _metadata = TaggedText.Parse(item.Body); }
    private TextMetadata _metadata;
    public string Id => Item.Id;
    public bool IsSchedule => Item.Kind == MemoKind.Schedule;
    public string Body => _metadata.DisplayBody;
    private bool FreeMemo => !IsSchedule && string.IsNullOrWhiteSpace(Item.Title);
    // Untitled memos retain their entire text in this block: stage two can show two
    // wrapped visual lines even when the original text contains no newline.
    public string HeaderTitle => FreeMemo ? (Body.Length > 0 ? Body : "메모") : Item.DisplayTitle;
    public string BodyContent => FreeMemo ? "" : Body;
    public bool ShowPreview => ViewLevel == 2 && BodyContent.Length > 0;
    public bool ShowDetails => ViewLevel == 3;
    public bool WrapHeader => ViewLevel == 3 || (ViewLevel == 2 && FreeMemo);
    public int HeaderLines => ViewLevel == 3 ? 0 : FreeMemo && ViewLevel == 2 ? 2 : 1;
    public bool CanExpand => true;
    public IReadOnlyList<string> Tags => _metadata.Tags;
    public bool HasTags => Tags.Count > 0 && ShowDetails;
    public string ExpandPath => ViewLevel == 3 ? "M2,8 L6,4 L10,8" : "M2,4 L6,8 L10,4";
    public int NextViewLevel => ViewLevel % 3 + 1;
    public string Preview => MemoPresentation.Preview(Item);
    public string Title => MemoPresentation.Title(Item);
    public bool IsCompleted => Item.IsCompleted;
    public string CompleteHint => IsCompleted ? "완료 취소" : "완료로 표시";
    public string CompleteGlyph => IsCompleted ? "↶" : "✓";
    public string CompletePath => IsCompleted
        ? "M3,6 L7,2 M3,6 L7,10 M3,6 L10,6 C16,6 16,14 10,14"
        : "M3,8 L7,12 L14,4";
    public bool HasTitle => !string.IsNullOrWhiteSpace(Title);
    public bool HasSource => Source.Length > 0;
    public string Source => _metadata.Source ?? Item.Source ?? "";
    public string? Link => _metadata.Link ?? Item.OriginalUrl;
    public bool HasLink => !string.IsNullOrWhiteSpace(Link);
    public string DateLine => MemoPresentation.DateLine(Item, TimeZoneInfo.Local);
    public string ReminderLine => Item.ReminderAt is { } time ? $"알림 · {time.ToLocalTime():yyyy.MM.dd HH:mm}" : "";
    public bool HasReminder => ReminderLine.Length > 0;
    public int TextLines => 1;
    public string Status => ScheduleText.Parse(Item.Body).Error is not null ? "날짜 표기 확인 · 편집에서 확인하세요."
        : Item.ConflictOf is not null ? "충돌 보관본 · 편집 후 저장하면 알림을 사용합니다." : Item.IsCompleted ? "완료" : "";
    public bool HasStatus => Status.Length > 0 && ShowDetails;
    public string ExpandHint => $"{ViewLevel}단계 · 다음: {(NextViewLevel == 3 ? "전체 내용" : NextViewLevel + "줄")} (1줄 → 2줄 → 전체)";
    public double ContentOpacity => Item.IsCompleted ? 0.65 : 1;
    public string DetailHint => "더블클릭: 편집 · 끌어서 순서 변경(시트에 동기화) · 화살표: 1줄 → 2줄 → 전체 · 휴지통: 시트에 보관";
    public string BackgroundColor { get; private set; } = MemoStyle.DefaultBackground;
    public void ApplyColors(string fallback, bool urgentEnabled, int days, string urgentColor, DateTimeOffset now, TimeZoneInfo zone)
    {
        string color = CardColors.Resolve(Item, fallback, urgentEnabled, days, urgentColor, now, zone);
        if (BackgroundColor == color) return;
        BackgroundColor = color; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BackgroundColor)));
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Update(MemoItem item, int viewLevel)
    {
        Item = item; ViewLevel = Math.Clamp(viewLevel, 1, 3); _metadata = TaggedText.Parse(item.Body);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
}
