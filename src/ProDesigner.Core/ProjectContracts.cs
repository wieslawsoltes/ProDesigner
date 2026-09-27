namespace ProDesigner.Core;

public sealed record ProjectBuildRequest(string ProjectPath, bool Trusted, string Configuration = "Debug", string? TargetFramework = null);
public sealed record ProjectBuildResult(bool Success, string? AssemblyPath, IReadOnlyList<DesignDiagnostic> Diagnostics, string Log);
public interface IProjectBuildService
{
    Task<ProjectBuildResult> BuildAsync(ProjectBuildRequest request, CancellationToken cancellationToken = default);
}
public sealed record TrustedPreviewRequest(string Source, string? DocumentPath = null, string? ProjectPath = null, bool Trusted = false, string? TargetFramework = null);

/// <summary>An explicitly trusted preview running outside the designer's process.</summary>
public interface IExternalPreview : IAsyncDisposable
{
    bool IsAlive { get; }
    Task UpdateAsync(string source, CancellationToken cancellationToken = default);
}
