using System.Text;

namespace ProDesigner.Persistence;

public sealed class FileConflictException(string path) : IOException($"'{path}' changed outside ProDesigner. Reload or save a copy instead of overwriting it.");
public sealed record FileSnapshot(string Text, string Sha256);
/// <summary>Atomic same-directory replacement, serialized designer writers and optimistic external-change detection.</summary>
public static class AtomicFileStore
{
    public static async Task<FileSnapshot> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);
        if (info.Length > WorkspaceCodec.MaximumBytes) throw new InvalidDataException("File exceeds the read limit.");
        var text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return new(text, WorkspaceCodec.Hash(text));
    }
    public static async Task<string> WriteAsync(string path, string text, string? expectedHash, CancellationToken cancellationToken = default)
    {
        path = Path.GetFullPath(path); var directory = Path.GetDirectoryName(path)!;
        if (Encoding.UTF8.GetByteCount(text) > WorkspaceCodec.MaximumBytes) throw new InvalidDataException("File exceeds the write limit.");
        Directory.CreateDirectory(directory);
        // Keep the lock-file inode stable. Deleting a lock file can allow a third writer to bypass an existing lock.
        await using var gate = new FileStream(path + ".prodesigner-lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (expectedHash is not null)
        {
            if (!File.Exists(path) || (await ReadAsync(path, cancellationToken).ConfigureAwait(false)).Sha256 != expectedHash) throw new FileConflictException(path);
        }
        var temporary = Path.Combine(directory, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.WriteThrough))
            { await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false); await stream.FlushAsync(cancellationToken).ConfigureAwait(false); stream.Flush(true); }
            cancellationToken.ThrowIfCancellationRequested(); File.Move(temporary, path, overwrite: true);
            return WorkspaceCodec.Hash(text);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
public interface IRecoveryStore
{
    Task<string?> ReadAsync(CancellationToken cancellationToken = default);
    Task WriteAsync(string json, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}
/// <summary>Two-generation recovery journal with corruption fallback. Reading never evaluates a project.</summary>
public sealed class FileRecoveryStore(string path) : IRecoveryStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public async Task<string?> ReadAsync(CancellationToken cancellationToken = default)
    {
        foreach (var candidate in new[] { path, path + ".previous" })
        {
            if (!File.Exists(candidate)) continue;
            try { var state = await AtomicFileStore.ReadAsync(candidate, cancellationToken).ConfigureAwait(false); WorkspaceCodec.Deserialize(state.Text); return state.Text; }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException) { }
        }
        return null;
    }
    public async Task WriteAsync(string json, CancellationToken cancellationToken = default)
    {
        WorkspaceCodec.Deserialize(json);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var previous = await ReadAsync(cancellationToken).ConfigureAwait(false);
            if (previous is not null) await AtomicFileStore.WriteAsync(path + ".previous", previous, null, cancellationToken).ConfigureAwait(false);
            await AtomicFileStore.WriteAsync(path, json, null, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }
    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var candidate in new[] { path, path + ".previous" }) if (File.Exists(candidate)) File.Delete(candidate);
        return Task.CompletedTask;
    }
}
