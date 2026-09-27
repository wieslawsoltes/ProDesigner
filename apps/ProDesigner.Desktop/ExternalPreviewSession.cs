using ProDesigner.Core;
using ProDesigner.PreviewProtocol;
using ProDesigner.Workspaces;

namespace ProDesigner.Desktop;

internal sealed class ExternalPreviewSession(PreviewProcessClient client, TrustedPreviewRequest request, string? assemblyPath) : IRenderedExternalPreview
{
    public bool IsAlive => client.IsAlive;
    public async Task UpdateAsync(string source, CancellationToken cancellationToken = default)
    {
        var response = await client.UpdateAsync(request with { Source = source }, assemblyPath, cancellationToken);
        if (!response.Success) throw new InvalidOperationException(response.Error);
    }
    public Task ResetInputAsync(CancellationToken cancellationToken = default) => client.ResetInputAsync(cancellationToken);
    public Task<RenderedPreviewFrame> RenderAsync(PreviewViewport viewport, string expectedSourceHash,
        IReadOnlyList<PreviewInput>? input = null, CancellationToken cancellationToken = default) =>
        client.RenderAsync(viewport, expectedSourceHash, input, cancellationToken);
    public ValueTask DisposeAsync() => client.DisposeAsync();
    public static async Task<IExternalPreview> StartAsync(TrustedPreviewRequest request, CancellationToken cancellationToken)
    {
        if (!request.Trusted) throw new UnauthorizedAccessException("A project preview requires explicit trust.");
        string? assembly = null;
        if (request.ProjectPath is not null)
        {
            var result = await new DotNetProjectBuilder().BuildAsync(new(request.ProjectPath, true, TargetFramework: request.TargetFramework), cancellationToken);
            if (!result.Success) throw new InvalidOperationException(result.Log + "\n" + string.Join("\n", result.Diagnostics.Select(d => d.Message)));
            assembly = result.AssemblyPath;
        }
        // Reuse the shipped desktop executable as a worker; no optional helper executable to forget when packaging.
        var host = Environment.ProcessPath!;
        if (Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) host = typeof(Program).Assembly.Location;
        var client = await PreviewProcessClient.StartAsync(host, headless: true, cancellationToken: cancellationToken);
        var session = new ExternalPreviewSession(client, request, assembly);
        try { await session.UpdateAsync(request.Source, cancellationToken); return session; }
        catch { await session.DisposeAsync(); throw; }
    }
}
