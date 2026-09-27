namespace ProDesigner.Core;

public enum DiagnosticSeverity { Info, Warning, Error }
public sealed record DesignDiagnostic(string Code, string Message, DiagnosticSeverity Severity, int Offset = 0, int Length = 0, string? File = null);
public readonly record struct SourceSpan(int Start, int Length)
{
    public int End => checked(Start + Length);
    public bool Contains(int offset) => offset >= Start && offset < End;
}
public sealed record TextEdit(SourceSpan Span, string NewText, string? ExpectedText = null);
public sealed record DocumentChange(long Version, string Label, string Source);
public sealed record PreviewProfile(string Name, double Width, double Height, bool Dark = false, double Scale = 1)
{
    public static IReadOnlyList<PreviewProfile> Defaults { get; } =
    [new("Desktop", 960, 620), new("Phone", 390, 844), new("Tablet", 768, 1024), new("Dark desktop", 960, 620, true)];
}
public interface IXamlSemanticService
{
    IReadOnlyList<DesignDiagnostic> Validate(string source);
}
public sealed record WorkspaceFile(string Path, string Name, string Kind);
public sealed record ProjectSummary(string Name, string Path, IReadOnlyList<string> Frameworks, IReadOnlyList<string> ProjectReferences, IReadOnlyList<string> Packages, IReadOnlyList<WorkspaceFile> Files);
public interface IWorkspaceService : IDisposable
{
    Task<IReadOnlyList<ProjectSummary>> OpenAsync(string path, bool trusted, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DesignDiagnostic>> GetDiagnosticsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CompletionItem>> GetXamlCompletionsAsync(string projectPath, string source, int offset, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CompletionItem>>([]);
    Task<IReadOnlyList<DesignDiagnostic>> AnalyzeXamlAsync(string projectPath, string source, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DesignDiagnostic>>([]);
}
public sealed record CompletionItem(string Label, string Kind, string? Detail = null);
public interface ICodeService
{
    IReadOnlyList<DesignDiagnostic> Validate(string source);
    string EnsureEventHandler(string source, string className, string handlerName);
}
public static class EditApplication
{
    public static string Apply(string source, IEnumerable<TextEdit> edits)
    {
        var ordered = edits.OrderBy(e => e.Span.Start).ThenBy(e => e.Span.Length).ToArray();
        var previousEnd = 0;
        foreach (var edit in ordered)
        {
            if (edit.Span.Start < previousEnd || edit.Span.Start < 0 || edit.Span.Length < 0 || edit.Span.End > source.Length)
                throw new InvalidOperationException("Edits overlap or are outside the current document.");
            if (edit.ExpectedText is { } expected && source.Substring(edit.Span.Start, edit.Span.Length) != expected)
                throw new InvalidOperationException("The document changed since this edit was prepared.");
            previousEnd = edit.Span.End;
        }
        var result = new System.Text.StringBuilder(source.Length);
        var cursor = 0;
        foreach (var edit in ordered)
        {
            result.Append(source, cursor, edit.Span.Start - cursor).Append(edit.NewText);
            cursor = edit.Span.End;
        }
        return result.Append(source, cursor, source.Length - cursor).ToString();
    }
}
