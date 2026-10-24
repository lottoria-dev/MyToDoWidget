namespace MyToDoWidget.Core;

// ItemOrders is a local cache of the order shared through the user's sheet.
public static class MemoOrdering
{
    public static List<MemoItem> ForTab(IEnumerable<MemoItem> items, MemoKind kind, IReadOnlyList<string>? order)
    {
        var visible = items.Where(i => !i.IsDeleted && i.Kind == kind);
        var defaults = kind == MemoKind.Schedule
            ? visible.OrderBy(i => i.WhenDate is null)
                .ThenBy(i => i.WhenDate?.ToDateTime(TimeOnly.MinValue) ?? DateTime.MaxValue)
                .ThenBy(i => i.CreatedAt).ThenBy(i => i.Id, StringComparer.Ordinal)
            : visible.OrderByDescending(i => i.CreatedAt).ThenBy(i => i.Id, StringComparer.Ordinal);
        if (order is null || order.Count == 0) return defaults.ToList();
        var ranks = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string id in order) ranks.TryAdd(id, ranks.Count);
        return defaults.OrderBy(i => ranks.GetValueOrDefault(i.Id, int.MaxValue)).ToList();
    }

    // Reorder the whole tab, including items hidden by a search or completion filter.
    public static List<string> Move(IEnumerable<MemoItem> items, MemoKind kind, IReadOnlyList<string>? order,
        string sourceId, string targetId, bool after)
    {
        var ids = ForTab(items, kind, order).Select(i => i.Id).ToList();
        if (sourceId == targetId || !ids.Contains(sourceId) || !ids.Contains(targetId)) return ids;
        ids.Remove(sourceId);
        ids.Insert(ids.IndexOf(targetId) + (after ? 1 : 0), sourceId);
        return ids;
    }
}
