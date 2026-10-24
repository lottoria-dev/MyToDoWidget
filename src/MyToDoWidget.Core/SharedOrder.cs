namespace MyToDoWidget.Core;

public sealed class SharedOrder
{
    public MemoKind Kind { get; set; }
    public List<string> Ids { get; set; } = [];
    public SharedOrder Copy() => new() { Kind = Kind, Ids = new(Ids) };
    public void Validate()
    {
        if (!Enum.IsDefined(Kind) || Ids is null || Ids.Count > 10000 || Ids.Any(id => !MemoItem.IsId(id))
            || Ids.Distinct(StringComparer.Ordinal).Count() != Ids.Count)
            throw new MemoException("시트의 표시 순서 정보가 올바르지 않습니다. 기존 메모는 유지합니다.");
    }
}

// A relative move is replayed against the server's current order, never a stale whole-list replacement.
public sealed class PendingOrderChange
{
    public string OperationId { get; set; } = Guid.NewGuid().ToString("N");
    public MemoKind Kind { get; set; }
    public string? SourceId { get; set; }
    public string? TargetId { get; set; }
    public bool After { get; set; }
    public bool Reset { get; set; }
    public PendingOrderChange Copy() => (PendingOrderChange)MemberwiseClone();
    public void Validate()
    {
        if (!MemoItem.IsId(OperationId) || !Enum.IsDefined(Kind)
            || (Reset ? SourceId is not null || TargetId is not null || After
                : !MemoItem.IsId(SourceId) || !MemoItem.IsId(TargetId) || SourceId == TargetId))
            throw new MemoException("표시 순서 변경 정보가 올바르지 않습니다.");
    }
}

public sealed record OrderMutationResult(string OperationId, IReadOnlyList<SharedOrder> Orders);
