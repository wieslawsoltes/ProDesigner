namespace ProDesigner.Core;

public sealed record ProjectBuildRequest(string ProjectPath, bool Trusted, string Configuration = "Debug", string? TargetFramework = null);
public sealed record ProjectBuildResult(bool Success, string? AssemblyPath, IReadOnlyList<DesignDiagnostic> Diagnostics, string Log);
public interface IProjectBuildService
{
    Task<ProjectBuildResult> BuildAsync(ProjectBuildRequest request, CancellationToken cancellationToken = default);
}
public sealed record TrustedPreviewRequest(string Source, string? DocumentPath = null, string? ProjectPath = null, bool Trusted = false);
