namespace ProDesigner.Core;

public sealed record OpenSourceFile(string Path, string Source);
public sealed record ProjectRenameRequest(string ProjectPath, string TypeMetadataName, string? MemberName,
    string NewName, OpenSourceFile[] OpenFiles);
public sealed record RefactoringFile(string Path, string Before, string After, string DiskBefore);
public sealed record RefactoringPlan(string Id, string Label, RefactoringFile[] Files, DesignDiagnostic[] Diagnostics)
{
    public bool CanApply => Files.Length > 0 && Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);
}
public interface IProjectRefactoringService
{
    Task<RefactoringPlan> PrepareRenameAsync(ProjectRenameRequest request, CancellationToken cancellationToken = default);
    Task RefreshFilesAsync(IReadOnlyList<OpenSourceFile> files, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
