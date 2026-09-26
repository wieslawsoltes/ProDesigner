using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using ProDesigner.Core;
using Severity = ProDesigner.Core.DiagnosticSeverity;

namespace ProDesigner.Workspaces;

public static class WorkspaceBootstrap
{
    public static void Initialize()
    {
        if (!MSBuildLocator.IsRegistered) MSBuildLocator.RegisterDefaults();
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IWorkspaceService Create() => new SolutionWorkspace();
}
public sealed class SolutionWorkspace : IWorkspaceService
{
    private MSBuildWorkspace? _workspace;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentQueue<DesignDiagnostic> _diagnostics = new();
    public Solution? Solution => _workspace?.CurrentSolution;
    public async Task<IReadOnlyList<ProjectSummary>> OpenAsync(string path, bool trusted, CancellationToken cancellationToken = default)
    {
        if (!trusted) throw new UnauthorizedAccessException("MSBuild evaluates project code. Explicitly trust this workspace before loading it.");
        path = Path.GetFullPath(path);
        if (!File.Exists(path)) throw new FileNotFoundException("The project or solution was not found.", path);
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not ".sln" and not ".slnx" and not ".csproj") throw new ArgumentException("Choose a .sln, .slnx, or .csproj file.", nameof(path));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _workspace?.Dispose(); _diagnostics.Clear();
            _workspace = MSBuildWorkspace.Create(new Dictionary<string, string> { ["Configuration"] = "Debug" });
            _workspace.WorkspaceFailed += (_, args) => _diagnostics.Enqueue(new("MSBUILD001", args.Diagnostic.Message, Severity.Warning, File: path));
            _workspace.LoadMetadataForReferencedProjects = false;
            if (extension == ".csproj") await _workspace.OpenProjectAsync(path, cancellationToken: cancellationToken).ConfigureAwait(false);
            else await _workspace.OpenSolutionAsync(path, cancellationToken: cancellationToken).ConfigureAwait(false);
            return _workspace.CurrentSolution.Projects.Select(p => InspectProject(p.FilePath!, p.Name)).ToArray();
        }
        finally { _gate.Release(); }
    }
    public async Task<IReadOnlyList<DesignDiagnostic>> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = _diagnostics.ToList();
            if (_workspace is null) return result;
            foreach (var project in _workspace.CurrentSolution.Projects)
            {
                var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
                if (compilation is null) continue;
                result.AddRange(compilation.GetDiagnostics(cancellationToken).Where(d => d.Severity is Microsoft.CodeAnalysis.DiagnosticSeverity.Error or Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
                    .Take(500).Select(d => new DesignDiagnostic(d.Id, d.GetMessage(), d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error ? Severity.Error : Severity.Warning,
                        d.Location.SourceSpan.Start, d.Location.SourceSpan.Length, d.Location.SourceTree?.FilePath)));
            }
            return result;
        }
        finally { _gate.Release(); }
    }
    public static ProjectSummary InspectProject(string path, string? name = null)
    {
        using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4 * 1024 * 1024 });
        var xml = XDocument.Load(reader);
        IEnumerable<XElement> Elements(string localName) => xml.Descendants().Where(e => e.Name.LocalName == localName);
        var frameworks = Elements("TargetFramework").Concat(Elements("TargetFrameworks")).SelectMany(e => e.Value.Split(';', StringSplitOptions.RemoveEmptyEntries)).Distinct().ToArray();
        var references = Elements("ProjectReference").Select(e => (string?)e.Attribute("Include") ?? "").ToArray();
        var packages = Elements("PackageReference").Select(e => $"{e.Attribute("Include")?.Value} {e.Attribute("Version")?.Value ?? e.Elements().FirstOrDefault(c => c.Name.LocalName == "Version")?.Value ?? "(central/evaluated)"}").ToArray();
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        var files = Directory.EnumerateFiles(directory, "*", options)
            .Where(f => !Path.GetRelativePath(directory, f).Split(Path.DirectorySeparatorChar).Any(s => s is "obj" or "bin" or ".git" or "node_modules"))
            .Where(f => Path.GetExtension(f) is ".axaml" or ".xaml" or ".cs")
            .Take(10000).Select(f => new WorkspaceFile(f, Path.GetRelativePath(directory, f), Path.GetExtension(f))).ToArray();
        return new(name ?? Path.GetFileNameWithoutExtension(path), path, frameworks, references, packages, files);
    }
    public void Dispose() { _workspace?.Dispose(); _gate.Dispose(); }
}
