namespace MyToDoWidget.Core;

public sealed record SyncOutcome(int Sent, int Conflicts);
public sealed class SyncService(LocalWorkspace workspace, IAppsScriptClient client)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public async Task<ServerProbe> ConnectAsync(string endpoint, string key, bool copyToDifferentDataset, CancellationToken cancellation, string? expectedDatasetId = null, Func<int, bool>? confirmCopyToDifferentDataset = null)
    {
        endpoint = AppsScriptConnection.NormalizeEndpoint(endpoint).AbsoluteUri;
        key = key.Trim(); AppsScriptConnection.ValidateKey(key);
        await _gate.WaitAsync(cancellation);
        try
        {
            var probe = await client.ProbeAsync(endpoint, key, cancellation);
            if (expectedDatasetId is not null && expectedDatasetId != probe.DatasetId)
                throw new MemoException("연결 정보와 실제 저장소가 다릅니다. 시트에서 연결 정보를 다시 복사하세요. 기존 메모와 연결은 유지됩니다.");
            var current = workspace.Snapshot();
            int activeCount = current.Items.Count(item => !item.IsDeleted);
            if (!copyToDifferentDataset && current.DatasetId is not null && current.DatasetId != probe.DatasetId
                && activeCount > 0 && confirmCopyToDifferentDataset is not null)
            {
                if (!confirmCopyToDifferentDataset(activeCount)) throw new MemoException("연결 변경을 취소했습니다. 기존 연결과 메모를 유지합니다.");
                copyToDifferentDataset = true;
            }
            workspace.Bind(endpoint, key, probe.DatasetId, copyToDifferentDataset);
            return probe;
        }
        finally { _gate.Release(); }
    }
    public async Task DisconnectAsync(CancellationToken cancellation)
    {
        await _gate.WaitAsync(cancellation);
        try { workspace.Disconnect(); } finally { _gate.Release(); }
    }
    public async Task<SyncOutcome> SyncAsync(CancellationToken cancellation)
    {
        await _gate.WaitAsync(cancellation);
        try
        {
            var connection = workspace.Snapshot();
            if (!connection.HasConnection || connection.DatasetId is null) return new(0, 0);
            int sent = 0, conflicts = 0;
            // A bounded round avoids starving the UI when many offline edits are queued.
            for (int round = 0; round < 100; round++)
            {
                var change = workspace.Snapshot().Outbox.FirstOrDefault();
                if (change is null) break;
                var result = await client.MutateAsync(connection.Endpoint!, connection.ConnectionKey!, connection.DatasetId, change, cancellation);
                workspace.Acknowledge(change, result); sent++;
                if (result.Status == "conflict") conflicts++;
            }
            // New local items must reach the sheet before their relative order changes.
            if (workspace.Snapshot().Outbox.Count == 0)
            {
                for (int round = 0; round < 100; round++)
                {
                    var change = workspace.Snapshot().OrderOutbox.FirstOrDefault();
                    if (change is null) break;
                    var result = await client.ReorderAsync(connection.Endpoint!, connection.ConnectionKey!, connection.DatasetId, change, cancellation);
                    workspace.AcknowledgeOrder(change, result); sent++;
                }
            }
            var snapshot = await client.ReadAsync(connection.Endpoint!, connection.ConnectionKey!, connection.DatasetId, cancellation);
            workspace.MergeSnapshot(snapshot.Items, snapshot.ServerTime, snapshot.Orders);
            return new(sent, conflicts);
        }
        finally { _gate.Release(); }
    }
}
