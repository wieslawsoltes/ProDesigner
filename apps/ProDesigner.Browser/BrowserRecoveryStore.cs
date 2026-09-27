using System.Runtime.InteropServices.JavaScript;
using ProDesigner.Persistence;

namespace ProDesigner.Browser;

internal sealed partial class BrowserRecoveryStore : IRecoveryStore
{
    [JSImport("read", "prodesigner-storage")] private static partial string? Read(string key);
    [JSImport("write", "prodesigner-storage")] private static partial void Write(string key, string value);
    [JSImport("remove", "prodesigner-storage")] private static partial void Remove(string key);
    public Task<string?> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var key in new[] { "prodesigner.recovery", "prodesigner.previous" })
        {
            var json = Read(key); if (json is null) continue;
            try { WorkspaceCodec.Deserialize(json); return Task.FromResult<string?>(json); }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException) { }
        }
        return Task.FromResult<string?>(null);
    }
    public async Task WriteAsync(string json, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); WorkspaceCodec.Deserialize(json);
        var previous = await ReadAsync(cancellationToken);
        if (previous is not null) Write("prodesigner.previous", previous);
        Write("prodesigner.recovery", json); // Quota errors propagate to the workbench status; no false save acknowledgement.
    }
    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); Remove("prodesigner.recovery"); Remove("prodesigner.previous"); return Task.CompletedTask;
    }
}
