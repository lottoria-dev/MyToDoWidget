using System.Text.Json;

namespace MyToDoWidget.Core;

public interface IStateCipher
{
    byte[] Protect(byte[] bytes);
    byte[] Unprotect(byte[] bytes);
}

public sealed record StateLoadResult(AppState State, string? Notice);

public sealed class StateStore(string directory, IStateCipher cipher)
{
    private readonly string _path = Path.Combine(directory, "state.dat");
    private const int MaxStateBytes = 16 * 1024 * 1024;

    public StateLoadResult Load()
    {
        foreach (string path in new[] { _path, _path + ".bak" })
        {
            if (!File.Exists(path)) continue;
            try
            {
                if (new FileInfo(path).Length > MaxStateBytes) continue;
                var state = JsonSerializer.Deserialize<AppState>(cipher.Unprotect(File.ReadAllBytes(path)));
                if (state is null || state.SchemaVersion != 1) continue;
                if (state.Source is not null) state.Source = SheetSource.Normalize(state.Source).AbsoluteUri;
                state.Normalize();
                return new(state, path == _path ? null : "백업에서 설정과 메모를 복구했습니다.");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) { /* No raw errors: they may contain a source URL. */ }
        }
        bool found = File.Exists(_path) || File.Exists(_path + ".bak");
        return new(new AppState(), found ? "저장 정보를 읽을 수 없습니다. 이 Windows 사용자 계정에서 주소를 다시 등록해 주세요." : null);
    }

    public void Save(AppState state, bool forgetOldSource = false)
    {
        Directory.CreateDirectory(directory);
        var bytes = cipher.Protect(JsonSerializer.SerializeToUtf8Bytes(state));
        string temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { file.Write(bytes); file.Flush(true); }
            // Remove an obsolete connection's backup before committing its replacement.
            // If that deletion fails the original state is still intact.
            if (forgetOldSource && File.Exists(_path + ".bak")) File.Delete(_path + ".bak");
            if (File.Exists(_path)) File.Replace(temp, _path, forgetOldSource ? null : _path + ".bak");
            else File.Move(temp, _path);
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes);
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
