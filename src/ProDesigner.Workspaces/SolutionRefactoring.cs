using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using ProDesigner.Core;
using ProDesigner.Roslyn;

namespace ProDesigner.Workspaces;

public sealed partial class SolutionWorkspace : IProjectRefactoringService
{
    private Solution? _refactoringSnapshot;
    public async Task<RefactoringPlan> PrepareRenameAsync(ProjectRenameRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.OpenFiles is null || request.OpenFiles.Length > 10000) throw new ArgumentException("Invalid open-file set.", nameof(request));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var solution = Solution ?? throw new InvalidOperationException("Open and trust a solution before preparing a project rename.");
            var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var project = solution.Projects.FirstOrDefault(p => p.FilePath is not null && comparer.Equals(Path.GetFullPath(p.FilePath), Path.GetFullPath(request.ProjectPath)))
                ?? throw new InvalidOperationException("The selected project is not loaded.");
            var overlays = request.OpenFiles.ToDictionary(f => Path.GetFullPath(f.Path), f => f.Source, comparer);
            var disk = new Dictionary<string, string>(comparer); long total = 0;
            async Task<string> Read(string path)
            {
                path = Path.GetFullPath(path);
                if (!disk.TryGetValue(path, out var source))
                {
                    var info = new FileInfo(path);
                    if (!info.Exists || info.Length > 16 * 1024 * 1024) throw new IOException("A refactoring source file is missing or oversized: " + path);
                    source = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false); disk.Add(path, source);
                    total += source.Length;
                    if (total > 32 * 1024 * 1024) throw new InvalidOperationException("The refactoring source set exceeds its 64 MiB text budget.");
                }
                var result = overlays.GetValueOrDefault(path, source);
                if (result is null || result.Length > ProDesigner.Xaml.XamlSyntaxTree.MaximumLength) throw new InvalidOperationException("An open source document is oversized.");
                return result;
            }
            foreach (var sourceProject in solution.Projects.ToArray())
            {
                foreach (var document in sourceProject.Documents)
                {
                    if (document.FilePath is null || !File.Exists(document.FilePath)) continue;
                    var source = await Read(document.FilePath).ConfigureAwait(false);
                    solution = solution.WithDocumentText(document.Id, SourceText.From(source, Encoding.UTF8), PreservationMode.PreserveIdentity);
                }
            }
            var owners = new Dictionary<string, Project>(comparer);
            foreach (var sourceProject in solution.Projects.Where(p => p.FilePath is not null).OrderBy(p => p.FilePath!.Length))
                foreach (var file in InspectProject(sourceProject.FilePath!).Files.Where(f => f.Kind is ".axaml" or ".xaml")) owners[Path.GetFullPath(file.Path)] = sourceProject;
            var xaml = new List<XamlRefactoringDocument>();
            foreach (var pair in owners) xaml.Add(new(pair.Value.Id, pair.Key, await Read(pair.Key).ConfigureAwait(false)));
            var plan = await new CrossLanguageRenameService().PrepareAsync(solution, project.Id, request.TypeMetadataName,
                request.MemberName, request.NewName, xaml, cancellationToken).ConfigureAwait(false);
            return plan with { Files = plan.Files.Select(f => f with { DiskBefore = disk[Path.GetFullPath(f.Path)] }).ToArray() };
        }
        finally { _gate.Release(); }
    }
    public async Task RefreshFilesAsync(IReadOnlyList<OpenSourceFile> files, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var solution = Solution; if (solution is null) return;
            var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var sources = files.ToDictionary(f => Path.GetFullPath(f.Path), f => f.Source, comparer);
            foreach (var document in solution.Projects.SelectMany(p => p.Documents).ToArray())
                if (document.FilePath is not null && sources.TryGetValue(Path.GetFullPath(document.FilePath), out var source))
                    solution = solution.WithDocumentText(document.Id, SourceText.From(source, Encoding.UTF8));
            _refactoringSnapshot = solution;
        }
        finally { _gate.Release(); }
    }
}
