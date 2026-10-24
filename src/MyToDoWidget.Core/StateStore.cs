using System.Security.Cryptography;
using System.Text.Json;

namespace MyToDoWidget.Core;

public interface IStateCipher
{
    byte[] Protect(byte[] bytes);
    byte[] Unprotect(byte[] bytes);
}
public sealed record StateLoadResult(AppState State, string? Notice);
public static class MemoJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    { PropertyNameCaseInsensitive = false, WriteIndented = false };
}

public sealed class StateStore(string directory, IStateCipher cipher)
{
    private readonly string _path = Path.Combine(directory, "state-v2.dat");
    private const int MaxBytes = 64 * 1024 * 1024;
    public bool HasStoredState => File.Exists(_path) || File.Exists(_path + ".bak");
    public StateLoadResult Load()
    {
        foreach (string path in new[] { _path, _path + ".bak" })
        {
            if (!File.Exists(path)) continue;
            byte[]? plain = null;
            try
            {
                if (new FileInfo(path).Length > MaxBytes) continue;
                plain = cipher.Unprotect(File.ReadAllBytes(path));
                var state = JsonSerializer.Deserialize<AppState>(plain, MemoJson.Options);
                if (state is null || state.SchemaVersion != 2) continue;
                state.Normalize();
                foreach (var item in state.Items) item.Validate();
                if (state.Items.Select(i => i.Id).Distinct().Count() != state.Items.Count)
                    throw new MemoException("중복된 메모 정보입니다.");
                foreach (var change in state.Outbox)
                {
                    change.Item.Validate();
                    if (!MemoItem.IsId(change.OperationId) || change.BaseVersion < 0
                        || change.Item.Version != change.BaseVersion + 1)
                        throw new MemoException("저장 대기 정보가 올바르지 않습니다.");
                }
                foreach (var change in state.OrderOutbox) change.Validate();
                if (state.OrderOutbox.Select(change => change.OperationId).Distinct().Count() != state.OrderOutbox.Count)
                    throw new MemoException("중복된 표시 순서 변경 정보입니다.");
                if (state.HasConnection)
                {
                    state.Endpoint = AppsScriptConnection.NormalizeEndpoint(state.Endpoint!).AbsoluteUri;
                    AppsScriptConnection.ValidateKey(state.ConnectionKey!);
                }
                return new(state, path == _path ? null : "백업에서 메모와 미전송 변경을 복구했습니다.");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) { /* Never expose addresses or keys. */ }
            finally { if (plain is not null) CryptographicOperations.ZeroMemory(plain); }
        }
        bool found = File.Exists(_path) || File.Exists(_path + ".bak");
        if (found)
        {
            // Preserve unreadable encrypted files before a new workspace can replace them.
            string archive = Path.Combine(directory, "unreadable-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(archive);
            foreach (string path in new[] { _path, _path + ".bak" })
                if (File.Exists(path)) File.Copy(path, Path.Combine(archive, Path.GetFileName(path)));
        }
        return new(new AppState(), found ? "저장 파일을 읽지 못했습니다. 암호화된 원본은 데이터 폴더의 unreadable 폴더에 보관했습니다. 다른 Windows 계정에서는 열 수 없습니다. JSON 백업을 확인해 주세요." : null);
    }

    public void Save(AppState state, bool forgetOldSource = false)
    {
        Directory.CreateDirectory(directory);
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(state, MemoJson.Options);
        byte[]? bytes = null;
        string temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            if (plain.Length > MaxBytes) throw new MemoException("저장 공간이 너무 커졌습니다. 메모를 백업한 후 정리해 주세요.");
            bytes = cipher.Protect(plain);
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { file.Write(bytes); file.Flush(true); }
            if (forgetOldSource && File.Exists(_path + ".bak")) File.Delete(_path + ".bak");
            if (File.Exists(_path)) File.Replace(temp, _path, forgetOldSource ? null : _path + ".bak");
            else File.Move(temp, _path);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
            if (bytes is not null) CryptographicOperations.ZeroMemory(bytes);
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
