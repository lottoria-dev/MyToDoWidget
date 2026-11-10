using System.ComponentModel;
using MyToDoWidget.Core;

namespace MyToDoWidget;

public sealed class MemoCard : INotifyPropertyChanged
{
    public MemoItem Item { get; private set; }
    public int ViewLevel { get; private set; }
    public MemoCard(MemoItem item, int viewLevel) { Item = item; ViewLevel = MemoView.Normalize(viewLevel); _metadata = TaggedText.Parse(item.Body); }
    private TextMetadata _metadata;
    public string Id => Item.Id;
    public bool IsSchedule => Item.Kind == MemoKind.Schedule;
    public string Body => _metadata.DisplayBody;
    private bool FreeMemo => !IsSchedule && string.IsNullOrWhiteSpace(Item.Title);
    // Untitled memos wrap the same text block when opened, without duplicating the body.
    public string HeaderTitle => FreeMemo ? (Body.Length > 0 ? Body : HasLink ? "참고 링크" : "메모") : Item.DisplayTitle;
    public string BodyContent => FreeMemo ? "" : Body;
    public bool HasBodyContent => BodyContent.Length > 0;
    public bool ShowDetails => ViewLevel == MemoView.Expanded;
    public bool WrapHeader => ShowDetails;
    public int HeaderLines => ShowDetails ? 0 : 1;
    public IReadOnlyList<string> Tags => _metadata.Tags;
    public bool HasTags => Tags.Count > 0 && ShowDetails;
    public string ExpandPath => ShowDetails ? "M2,8 L6,4 L10,8" : "M2,4 L6,8 L10,4";
    public int NextViewLevel => ShowDetails ? MemoView.Collapsed : MemoView.Expanded;
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
    public string? Link => _metadata.Link ?? Item.OriginalUrl
        ?? (Body.Length is > 0 and <= 2048 && !Body.Any(char.IsWhiteSpace) && MemoItem.SafeWebUrl(Body) ? Body : null);
    public bool HasLink => !string.IsNullOrWhiteSpace(Link);
    public string DateLine => MemoPresentation.DateLine(Item, TimeZoneInfo.Local);
    public string ReminderLine => Item.ReminderAt is { } time ? $"알림 · {time.ToLocalTime():yyyy.MM.dd HH:mm}" : "";
    public bool HasReminder => ReminderLine.Length > 0;
    public string Status => ScheduleText.Parse(Item.Body).Error is not null ? "날짜 표기 확인 · 편집에서 확인하세요."
        : Item.ConflictOf is not null ? "충돌 보관본 · 편집 후 저장하면 알림을 사용합니다." : Item.IsCompleted ? "완료" : "";
    public bool HasStatus => Status.Length > 0 && ShowDetails;
    public string ExpandHint => ShowDetails ? "한 줄로 접기" : "전체 내용 펼치기";
    public double ContentOpacity => Item.IsCompleted ? 0.65 : 1;
    public string DetailHint => "더블클릭: 편집 · 끌어서 순서 변경(시트에 동기화) · 화살표: 펼치기 / 접기 · 휴지통: 시트에 보관";
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
        Item = item; ViewLevel = MemoView.Normalize(viewLevel); _metadata = TaggedText.Parse(item.Body);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
}
