using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using ProDesigner.Core;

namespace ProDesigner.PreviewProtocol;

/// <summary>Supervises a trusted out-of-process preview. Process isolation contains crashes, not OS permissions.</summary>
public sealed class PreviewProcessClient : IAsyncDisposable
{
    private readonly NamedPipeServerStream _pipe;
    private readonly Process _process;
    private readonly SemaphoreSlim _requests = new(1, 1);
    private long _revision;
    private int _disposed;
    public int ProcessId => _process.Id;
    public bool IsAlive => Volatile.Read(ref _disposed) == 0 && !_process.HasExited;
    public TimeSpan ResponseTimeout { get; set; } = TimeSpan.FromSeconds(20);
    private PreviewProcessClient(NamedPipeServerStream pipe, Process process) { _pipe = pipe; _process = process; }
    public static async Task<PreviewProcessClient> StartAsync(string executable, bool headless = false, CancellationToken cancellationToken = default)
    {
        var name = "pd-" + Guid.NewGuid().ToString("N");
        // Absolute Unix pipe paths avoid the runtime's CoreFxPipe_ prefix and macOS TMPDIR length limit.
        var pipeName = OperatingSystem.IsWindows() ? name : Path.Combine(Path.GetTempPath(), name);
        if (!OperatingSystem.IsWindows() && System.Text.Encoding.UTF8.GetByteCount(pipeName) >= 100)
            pipeName = Path.Combine("/tmp", name);
        var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var start = new ProcessStartInfo { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        if (executable.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            start.FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
            start.ArgumentList.Add(Path.GetFullPath(executable));
        }
        else start.FileName = executable;
        start.ArgumentList.Add("--preview-worker"); start.ArgumentList.Add(pipeName);
        if (headless) start.ArgumentList.Add("--preview-headless");
        Process? process = null;
        try
        {
            process = Process.Start(start) ?? throw new IOException("Preview worker could not start.");
            // Drain arbitrary application output without retaining an unbounded log or corrupting IPC.
            _ = DrainAsync(process.StandardOutput); _ = DrainAsync(process.StandardError);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(20));
            await pipe.WaitForConnectionAsync(timeout.Token).ConfigureAwait(false);
            return new(pipe, process);
        }
        catch { pipe.Dispose(); if (process is not null) { Kill(process); process.Dispose(); } throw; }
    }
    private static async Task DrainAsync(StreamReader reader)
    {
        try { var buffer = new char[4096]; while (await reader.ReadAsync(buffer).ConfigureAwait(false) != 0) { } }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
    }
    public Task<PreviewResponse> UpdateAsync(TrustedPreviewRequest document, string? assemblyPath = null, CancellationToken cancellationToken = default)
    {
        if (!document.Trusted) throw new UnauthorizedAccessException("Preview execution requires explicit trust.");
        return SendAsync("update", document, assemblyPath, cancellationToken);
    }
    public Task<PreviewResponse> PingAsync(CancellationToken cancellationToken = default) => SendAsync("ping", null, null, cancellationToken);
    public async Task ResetInputAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendAsync("reset-input", null, null, cancellationToken).ConfigureAwait(false);
        if (!response.Success) throw new InvalidOperationException(response.Error);
    }
    public async Task<RenderedPreviewFrame> RenderAsync(PreviewViewport viewport, string expectedSourceHash,
        IReadOnlyList<PreviewInput>? input = null, CancellationToken cancellationToken = default)
    {
        viewport.Validate();
        if (string.IsNullOrWhiteSpace(expectedSourceHash) || expectedSourceHash.Length != 64 || input?.Count > 128)
            throw new ArgumentException("A source hash and bounded input batch are required.");
        var inputs = input?.ToArray() ?? [];
        foreach (var item in inputs) item.Validate();
        var response = await SendAsync("render", null, null, cancellationToken, viewport, expectedSourceHash, inputs).ConfigureAwait(false);
        if (!response.Success || response.Frame is null) throw new InvalidOperationException(response.Error ?? "No rendered frame was returned.");
        response.Frame.Validate();
        if (response.Frame.SourceHash != expectedSourceHash) throw new InvalidDataException("Rendered frame targets an outdated source revision.");
        return response.Frame;
    }
    private async Task<PreviewResponse> SendAsync(string operation, TrustedPreviewRequest? document, string? assemblyPath, CancellationToken cancellationToken, PreviewViewport? viewport = null, string? sourceHash = null, PreviewInput[]? input = null)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(ResponseTimeout);
        await _requests.WaitAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            var revision = Interlocked.Increment(ref _revision);
            await PreviewWire.WriteAsync(_pipe, JsonSerializer.SerializeToUtf8Bytes(new PreviewRequest(revision, operation, document, assemblyPath, viewport, sourceHash, input), PreviewJsonContext.Default.PreviewRequest), timeout.Token).ConfigureAwait(false);
            var payload = await PreviewWire.ReadAsync(_pipe, timeout.Token).ConfigureAwait(false);
            var response = JsonSerializer.Deserialize(payload, PreviewJsonContext.Default.PreviewResponse) ?? throw new InvalidDataException("Empty preview response.");
            if (response.ProtocolVersion != 2) throw new InvalidDataException("Incompatible preview protocol version.");
            if (response.Revision != revision) throw new InvalidDataException("Preview revision mismatch.");
            return response;
        }
        catch { await DisposeAsync().ConfigureAwait(false); throw; }
        finally { _requests.Release(); }
    }
    private static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await _pipe.DisposeAsync().ConfigureAwait(false); Kill(_process);
        try { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)); await _process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        _process.Dispose();
    }
}
