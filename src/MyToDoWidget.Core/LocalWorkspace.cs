namespace MyToDoWidget.Core;

/// <summary>All local changes are committed atomically before the UI reports success.</summary>
public sealed class LocalWorkspace(StateStore store, AppState state)
{
    private readonly object _gate = new();
    private AppState _state = state.Copy();
    public event Action? Changed;
    public AppState Snapshot() { lock (_gate) return _state.Copy(); }
    public void Update(Action<AppState> edit)
    {
        lock (_gate)
        {
            var next = _state.Copy(); edit(next); next.Normalize(); store.Save(next); _state = next;
        }
        Changed?.Invoke();
    }
    public MemoItem SaveItem(MemoItem draft, long? expectedLocalVersion = null)
    {
        MemoItem? saved = null;
        Update(next =>
        {
            var existing = next.Items.FirstOrDefault(i => i.Id == draft.Id);
            if (expectedLocalVersion is { } version && (existing?.Version ?? 0) != version)
            {
                saved = draft.Copy(); saved.ConflictOf = draft.Id;
                saved.ConflictReason = "편집하는 동안 다른 기기의 변경이 도착했습니다.";
                saved.Id = Guid.NewGuid().ToString("N"); saved.Version = 1;
                saved.CreatedAt = saved.UpdatedAt = DateTimeOffset.UtcNow; saved.Validate();
                Queue(next, saved, 0); return;
            }
            saved = draft.Copy(); saved.CreatedAt = existing?.CreatedAt ?? DateTimeOffset.UtcNow;
            saved.UpdatedAt = DateTimeOffset.UtcNow; saved.Version = (existing?.Version ?? 0) + 1; saved.Validate();
            Queue(next, saved, existing?.Version ?? 0);
        });
        return saved!;
    }
    private static void Queue(AppState state, MemoItem item, long baseVersion)
    {
        state.Items.RemoveAll(i => i.Id == item.Id); state.Items.Add(item.Copy());
        state.Outbox.Add(new PendingChange { BaseVersion = baseVersion, Item = item.Copy() });
    }
    public void SetCompleted(string id, bool completed)
    {
        Update(next =>
        {
            var original = next.Items.FirstOrDefault(i => i.Id == id && !i.IsDeleted)
                ?? throw new MemoException("메모를 찾지 못했습니다.");
            if (original.IsCompleted == completed) return;
            var item = original.Copy(); item.IsCompleted = completed;
            item.CompletedAt = completed ? DateTimeOffset.UtcNow : null;
            if (!completed && item.ReminderAt is not null) item.ReminderId = Guid.NewGuid().ToString("N");
            item.Version++; item.UpdatedAt = DateTimeOffset.UtcNow; Queue(next, item, original.Version);
        });
    }
    public void Delete(string id)
    {
        Update(next =>
        {
            var original = next.Items.FirstOrDefault(i => i.Id == id && !i.IsDeleted);
            if (original is null) return;
            var item = original.Copy(); item.IsDeleted = true; item.DeletedAt = item.UpdatedAt = DateTimeOffset.UtcNow;
            item.Version++; Queue(next, item, original.Version);
        });
    }
    public void Snooze(string id, DateTimeOffset until)
    {
        if (until <= DateTimeOffset.UtcNow) throw new MemoException("다시 알림 시간은 현재 이후로 지정해 주세요.");
        Update(next =>
        {
            var original = next.Items.FirstOrDefault(i => i.Id == id && !i.IsDeleted && !i.IsCompleted)
                ?? throw new MemoException("완료되었거나 삭제된 메모입니다.");
            var item = original.Copy(); item.ReminderAt = until; item.ReminderId = Guid.NewGuid().ToString("N");
            item.Version++; item.UpdatedAt = DateTimeOffset.UtcNow; Queue(next, item, original.Version);
        });
    }
    public void Acknowledge(PendingChange change, MutationResult result)
    {
        Update(next =>
        {
            if (!next.Outbox.Any(c => c.OperationId == change.OperationId)) return;
            if (result.Status == "applied")
            {
                next.Outbox.RemoveAll(c => c.OperationId == change.OperationId);
                if (!next.Outbox.Any(c => c.Item.Id == change.Item.Id))
                {
                    next.Items.RemoveAll(i => i.Id == change.Item.Id); next.Items.Add(result.Item!.Copy());
                }
                return;
            }
            // Include any edits made locally while this request was in flight.
            var local = next.Items.FirstOrDefault(i => i.Id == change.Item.Id)?.Copy() ?? change.Item.Copy();
            next.Outbox.RemoveAll(c => c.Item.Id == change.Item.Id); next.Items.RemoveAll(i => i.Id == change.Item.Id);
            if (result.Item is not null) next.Items.Add(result.Item.Copy());
            local.ConflictOf = change.Item.Id;
            local.ConflictReason = local.IsDeleted ? "다른 기기의 변경과 삭제 요청이 겹쳤습니다." : "다른 기기에서도 수정한 내용입니다.";
            local.Id = Guid.NewGuid().ToString("N"); local.Version = 1; local.IsDeleted = false; local.DeletedAt = null;
            local.CreatedAt = local.UpdatedAt = DateTimeOffset.UtcNow;
            Queue(next, local, 0);
        });
    }
    public void MoveItem(string sourceId, string targetId, bool after)
    {
        Update(next =>
        {
            var source = next.Items.FirstOrDefault(i => i.Id == sourceId && !i.IsDeleted)
                ?? throw new MemoException("옮길 항목을 찾지 못했습니다.");
            var target = next.Items.FirstOrDefault(i => i.Id == targetId && !i.IsDeleted && i.Kind == source.Kind)
                ?? throw new MemoException("같은 탭의 항목 사이에서 순서를 바꿔 주세요.");
            if (source.Id == target.Id) return;
            var change = new PendingOrderChange { Kind = source.Kind, SourceId = source.Id, TargetId = target.Id, After = after };
            change.Validate();
            next.OrderOutbox.Add(change);
            ApplyOrderChange(next, change);
        });
    }
    public void ResetOrder(MemoKind kind) => Update(next =>
    {
        var change = new PendingOrderChange { Kind = kind, Reset = true };
        change.Validate(); next.OrderOutbox.Add(change); ApplyOrderChange(next, change);
    });
    private static void ApplyOrderChange(AppState state, PendingOrderChange change)
    {
        if (change.Reset) state.ItemOrders.Remove(change.Kind);
        else state.ItemOrders[change.Kind] = MemoOrdering.Move(state.Items, change.Kind,
            state.ItemOrders.GetValueOrDefault(change.Kind), change.SourceId!, change.TargetId!, change.After);
    }
    private static void AcceptOrders(AppState state, IReadOnlyList<SharedOrder> orders)
    {
        foreach (var order in orders) order.Validate();
        if (orders.Count != 2 || orders.Select(o => o.Kind).Distinct().Count() != 2)
            throw new MemoException("시트의 표시 순서를 확인하지 못했습니다. 기존 메모는 유지합니다.");
        state.ItemOrders = orders.Where(o => o.Ids.Count > 0).ToDictionary(o => o.Kind, o => new List<string>(o.Ids));
        foreach (var change in state.OrderOutbox) ApplyOrderChange(state, change);
    }
    public void AcknowledgeOrder(PendingOrderChange change, OrderMutationResult result) => Update(next =>
    {
        if (result.OperationId != change.OperationId) throw new MemoException("순서 저장 응답이 일치하지 않습니다.");
        next.OrderOutbox.RemoveAll(c => c.OperationId == change.OperationId);
        AcceptOrders(next, result.Orders);
    });
    public void MergeSnapshot(IReadOnlyList<MemoItem> serverItems, DateTimeOffset serverTime, IReadOnlyList<SharedOrder>? orders = null)
    {
        Update(next =>
        {
            var ids = next.Outbox.Select(c => c.Item.Id).ToHashSet();
            var pending = next.Items.Where(i => ids.Contains(i.Id)).Select(i => i.Copy()).ToList();
            next.Items = serverItems.Where(i => !ids.Contains(i.Id)).Select(i => i.Copy()).ToList();
            next.Items.AddRange(pending); next.LastSynced = serverTime;
            if (orders is not null) AcceptOrders(next, orders);
        });
    }
    public void Bind(string endpoint, string key, string datasetId, bool copyToDifferentDataset)
    {
        Update(next =>
        {
            if (next.DatasetId is not null && next.DatasetId != datasetId)
            {
                if (!copyToDifferentDataset && next.Items.Any(i => !i.IsDeleted))
                    throw new MemoException("다른 저장소입니다. 현재 메모를 새 저장소에 복사할지 선택해 주세요.");
                next.Outbox.Clear(); next.OrderOutbox.Clear(); next.ItemOrders.Clear(); next.Items.RemoveAll(i => i.IsDeleted);
                foreach (var item in next.Items)
                {
                    item.Version = 1; item.UpdatedAt = DateTimeOffset.UtcNow;
                    next.Outbox.Add(new PendingChange { BaseVersion = 0, Item = item.Copy() });
                }
                next.LastSynced = null;
            }
            next.Endpoint = endpoint; next.ConnectionKey = key; next.DatasetId = datasetId;
        });
    }
    public void Disconnect() => Update(next => { next.Endpoint = null; next.ConnectionKey = null; });
    public IReadOnlyList<MemoItem> DueReminders(DateTimeOffset now)
    {
        lock (_gate) return _state.NotificationsEnabled
            ? _state.Items.Where(i => !i.IsDeleted && !i.IsCompleted && i.ConflictOf is null && i.ReminderAt <= now
                && (!_state.DeliveredReminders.TryGetValue(i.Id, out var token) || token != i.ReminderToken))
                .OrderBy(i => i.ReminderAt).Select(i => i.Copy()).ToList()
            : [];
    }
    public void MarkRemindersDelivered(IEnumerable<MemoItem> items) => Update(next =>
    {
        foreach (var item in items) next.DeliveredReminders[item.Id] = item.ReminderToken;
    });
    public int Import(IEnumerable<MemoItem> items, WidgetSettings? settings = null)
    {
        int added = 0;
        Update(next =>
        {
            foreach (var original in items)
            {
                var item = original.Copy(); item.IsDeleted = false; item.DeletedAt = null; item.Validate();
                if (next.Items.Any(i => !i.IsDeleted && Equivalent(i, item))) continue;
                if (next.Items.Any(i => i.Id == item.Id)) item.Id = Guid.NewGuid().ToString("N");
                item.Version = 1; item.UpdatedAt = DateTimeOffset.UtcNow; Queue(next, item, 0); added++;
            }
            settings?.ApplyTo(next);
        });
        return added;
    }
    private static bool Equivalent(MemoItem a, MemoItem b) => a.Kind == b.Kind && a.Body == b.Body
        && a.CardColor == b.CardColor && a.Title == b.Title && a.Source == b.Source && a.OriginalUrl == b.OriginalUrl && a.DateRole == b.DateRole
        && a.WhenDate == b.WhenDate && a.WhenAt == b.WhenAt && a.ReminderAt == b.ReminderAt && a.IsCompleted == b.IsCompleted;
}
