using System.Text.Json.Serialization;

namespace MyToDoWidget.Core;

public sealed class MemoException(string message) : Exception(message);
public enum MemoKind { Memo, Schedule }
public enum DateRole { None, Deadline, Event }

public sealed class MemoItem
{
    [JsonRequired] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public MemoKind Kind { get; set; }
    [JsonRequired] public string Body { get; set; } = "";
    public string? Title { get; set; }
    public string? CardColor { get; set; }
    public string? Source { get; set; }
    public string? OriginalUrl { get; set; }
    public DateRole DateRole { get; set; }
    public DateOnly? WhenDate { get; set; }
    public DateTimeOffset? WhenAt { get; set; }
    public string? TimeZoneId { get; set; }
    public DateTimeOffset? ReminderAt { get; set; }
    public string? ReminderId { get; set; }
    public bool IsCompleted { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public long Version { get; set; }
    public string? ConflictOf { get; set; }
    public string? ConflictReason { get; set; }

    [JsonIgnore] public string DisplayTitle => string.IsNullOrWhiteSpace(Title)
        ? TaggedText.Parse(Body).DisplayBody.Split('\n').FirstOrDefault(s => !string.IsNullOrWhiteSpace(s))?.Trim()
            ?? (Kind == MemoKind.Schedule ? "일정" : "메모")
        : Title;
    [JsonIgnore] public string ReminderToken => $"{Id}:{ReminderId}:{ReminderAt?.ToUniversalTime():O}";
    public MemoItem Copy() => (MemoItem)MemberwiseClone();

    public void Validate()
    {
        if (!IsId(Id) || !Enum.IsDefined(Kind) || !Enum.IsDefined(DateRole) || Version < 0)
            throw new MemoException("메모 형식이 올바르지 않습니다.");
        if (CardColor is not null && (!MemoStyle.TryHexColor(CardColor, out var color) || color != CardColor))
            throw new MemoException("항목 배경색은 #RRGGBB 형식으로 입력해 주세요.");
        if (string.IsNullOrWhiteSpace(Body)) throw new MemoException("메모 내용을 입력해 주세요.");
        if (Body.Length > 40000) throw new MemoException("메모는 40,000자까지 저장할 수 있습니다.");
        if ((TimeZoneId?.Length ?? 0) > 128 || (ConflictReason?.Length ?? 0) > 500) throw new MemoException("추가 정보가 너무 깁니다.");
        if ((Title?.Length ?? 0) > 200 || (Source?.Length ?? 0) > 500 || (OriginalUrl?.Length ?? 0) > 2048)
            throw new MemoException("제목·출처·링크가 너무 깁니다.");
        if (OriginalUrl is { Length: > 0 } && !SafeWebUrl(OriginalUrl))
            throw new MemoException("원문 링크는 https 또는 http 주소로 입력해 주세요.");
        if (DateRole == DateRole.None && (WhenDate is not null || WhenAt is not null))
            throw new MemoException("날짜의 용도를 선택해 주세요.");
        if (WhenAt is not null && WhenDate is null)
            throw new MemoException("일정의 날짜를 확인해 주세요.");
        if (ReminderAt is not null && !IsId(ReminderId))
            throw new MemoException("알림 정보가 올바르지 않습니다.");
        if (ConflictOf is not null && !IsId(ConflictOf)) throw new MemoException("보관본 형식이 올바르지 않습니다.");
    }

    public static bool IsId(string? value) => value is { Length: 32 }
        && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    public static bool SafeWebUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == "https" || uri.Scheme == "http") && string.IsNullOrEmpty(uri.UserInfo);
}

public sealed class PendingChange
{
    public string OperationId { get; set; } = Guid.NewGuid().ToString("N");
    public long BaseVersion { get; set; }
    public MemoItem Item { get; set; } = new();
    public PendingChange Copy() => new() { OperationId = OperationId, BaseVersion = BaseVersion, Item = Item.Copy() };
}
