namespace MyToDoWidget.Core;

/// <summary>UI-thread requests after durable edits; never driven by appearance or incoming snapshots.</summary>
public sealed class EditSyncQueue
{
    public bool HasPending { get; private set; }
    public void Request() => HasPending = true;
    public bool TryTake(bool canStart)
    {
        if (!canStart || !HasPending) return false;
        HasPending = false;
        return true;
    }
}
