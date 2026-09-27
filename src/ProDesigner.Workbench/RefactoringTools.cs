using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using ProDesigner.Core;
using ProDesigner.Persistence;
using ProDesigner.Xaml;
using static ProDesigner.Workbench.StudioControls;

namespace ProDesigner.Workbench;

public sealed partial class DesignerWorkbench
{
    private RefactoringPlan? _preparedRename;
    private bool _refactoringBusy;
    private readonly List<(RefactoringPlan Plan, FileTransactionReceipt Receipt)> _renameHistory = [];
    private JournaledFileTransaction? _renameStore;
    private JournaledFileTransaction RenameStore => _renameStore ??= new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProDesigner", "Refactoring"));
    private static bool SamePath(string? a, string? b) => a is not null && b is not null && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private OpenSourceFile[] CaptureOpenSources()
    {
        var files = new List<OpenSourceFile>();
        foreach (var document in _documents.Where(d => d.Path is not null))
        {
            files.Add(new(document.Path!, document.Session.Source));
            if (!string.IsNullOrEmpty(document.CodeBehind)) files.Add(new(document.Path! + ".cs", document.CodeBehind));
        }
        return files.ToArray();
    }
    public async Task<RefactoringPlan> PrepareProjectRenameAsync(string typeName, string? member, string newName, CancellationToken cancellationToken = default)
    {
        _editor.Flush();
        if (_workspace is not IProjectRefactoringService service || _active.ProjectPath is null) throw new InvalidOperationException("Open a view through a trusted desktop solution before requesting project refactoring.");
        var request = new ProjectRenameRequest(_active.ProjectPath, typeName, string.IsNullOrWhiteSpace(member) ? null : member, newName, CaptureOpenSources());
        var plan = await service.PrepareRenameAsync(request, cancellationToken);
        if (_designSystem.Components.Length > 0) plan = plan with { Diagnostics = [.. plan.Diagnostics, new("RENAME102", "Workspace component templates are not project source files. Review their type references after this project rename.", DiagnosticSeverity.Warning)] };
        _preparedRename = plan; return plan;
    }
    public async Task ApplyProjectRenameAsync(RefactoringPlan plan, CancellationToken cancellationToken = default)
    {
        if (_refactoringBusy) throw new InvalidOperationException("Another file transaction is in progress.");
        if (!ReferenceEquals(plan, _preparedRename) || !plan.CanApply) throw new InvalidOperationException("Prepare and review a valid rename plan before applying it.");
        _editor.Flush(); ValidateOpenBuffers(plan, undo: false);
        _refactoringBusy = true; _root.IsEnabled = false; _overlay.IsEnabled = false;
        try
        {
            await StopExternalPreviewAsync();
            var receipt = await RenameStore.ApplyAsync(plan.Files.Select(f => new FileTextChange(f.Path, f.DiskBefore, f.After)).ToArray(), cancellationToken);
            // Once disk commit succeeds, complete buffer synchronization even if the caller cancels.
            SynchronizeRefactoredBuffers(plan, undo: false);
            _renameHistory.Add((plan, receipt));
            while (_renameHistory.Count > 1 && (_renameHistory.Count > 20 || _renameHistory.Sum(h => h.Plan.Files.Sum(f => ((long)f.Before.Length + f.After.Length + f.DiskBefore.Length) * 2)) > 64 * 1024 * 1024)) _renameHistory.RemoveAt(0);
            _preparedRename = null;
            if (_workspace is IProjectRefactoringService service) await service.RefreshFilesAsync(plan.Files.Select(f => new OpenSourceFile(f.Path, f.After)).ToArray());
            RefreshDocument(); ScheduleRecovery(); SetStatus($"Renamed across {plan.Files.Length} files. The before/after journal is available for Undo project rename.");
        }
        finally { _refactoringBusy = false; _root.IsEnabled = true; _overlay.IsEnabled = true; }
    }
    public async Task UndoProjectRenameAsync(CancellationToken cancellationToken = default)
    {
        if (_refactoringBusy || _renameHistory.Count == 0) return;
        _editor.Flush(); var item = _renameHistory[^1]; ValidateOpenBuffers(item.Plan, undo: true);
        _refactoringBusy = true; _root.IsEnabled = false; _overlay.IsEnabled = false;
        try
        {
            await StopExternalPreviewAsync();
            await RenameStore.RevertAsync(item.Receipt, item.Plan.Files.Select(f => f.Path).ToArray(), cancellationToken);
            SynchronizeRefactoredBuffers(item.Plan, undo: true); _renameHistory.RemoveAt(_renameHistory.Count - 1);
            if (_workspace is IProjectRefactoringService service) await service.RefreshFilesAsync(item.Plan.Files.Select(f => new OpenSourceFile(f.Path, f.Before)).ToArray());
            RefreshDocument(); ScheduleRecovery(); SetStatus("Project rename reverted; previous unsaved buffers and disk baselines were restored separately.");
        }
        finally { _refactoringBusy = false; _root.IsEnabled = true; _overlay.IsEnabled = true; }
    }
    private void ValidateOpenBuffers(RefactoringPlan plan, bool undo)
    {
        foreach (var file in plan.Files)
        {
            var expected = undo ? file.After : file.Before;
            foreach (var document in _documents)
            {
                if (SamePath(document.Path, file.Path) && document.Session.Source != expected) throw new InvalidOperationException("An open XAML buffer changed after the rename preview: " + document.Name);
                if (document.Path is not null && SamePath(document.Path + ".cs", file.Path) && !string.IsNullOrEmpty(document.CodeBehind) && document.CodeBehind != expected) throw new InvalidOperationException("An open code-behind buffer changed after the rename preview: " + document.Name);
            }
        }
    }
    private void SynchronizeRefactoredBuffers(RefactoringPlan plan, bool undo)
    {
        foreach (var file in plan.Files)
            foreach (var document in _documents)
            {
                if (SamePath(document.Path, file.Path))
                {
                    document.Session.SetSource(undo ? file.Before : file.After, undo ? "Undo project rename" : plan.Label);
                    document.Session.RestoreSavedBaseline(undo ? file.DiskBefore : file.After);
                    document.DiskHash = WorkspaceCodec.Hash(undo ? file.DiskBefore : file.After);
                }
                if (document.Path is not null && SamePath(document.Path + ".cs", file.Path)) document.CodeBehind = undo ? file.Before : file.After;
            }
        RefreshTabs();
    }
    private void ShowProjectRefactoring()
    {
        if (_workspace is not IProjectRefactoringService || _active.ProjectPath is null) { SetStatus("Project refactoring requires a trusted desktop solution and a view opened from its solution explorer."); return; }
        var type = Field(Session.Tree.Root.Get("x:Class") ?? "MyApp.Views.MainView", "Fully qualified type name");
        var member = Field(watermark: "Optional member name; leave empty to rename the type");
        var name = Field("RenamedView", "New identifier"); var status = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap };
        var prepare = Button("Preview cross-file changes", "Use Roslyn symbols and scoped XAML references to prepare a rename", () => { }, true);
        prepare.Click += async (_, _) =>
        {
            if (_refactoringBusy) return; prepare.IsEnabled = false; status.Text = "Resolving symbols and preparing source changes…";
            try { var plan = await PrepareProjectRenameAsync(type.Text ?? "", member.Text, name.Text ?? ""); if (!_disposed) ShowRenamePreview(plan); }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { prepare.IsEnabled = true; }
        };
        ShowDialog("Project-aware XAML / C# rename", Column(Caption("TYPE"), type, Caption("MEMBER (OPTIONAL)"), member, Caption("NEW NAME"), name,
            new TextBlock { Text = "C# references use Roslyn semantic rename. XAML type tags, property elements, directly bound properties, x:Class, x:DataType and code-behind handlers are updated without reformatting. Ambiguous binding paths are flagged for review.", FontSize = 11, TextWrapping = TextWrapping.Wrap },
            prepare, status, Button("Undo last project rename", "Restore the last rename's files and open buffers after conflict checks", () => _ = UndoRenameFromUiAsync())));
    }
    private async Task UndoRenameFromUiAsync()
    {
        try { await UndoProjectRenameAsync(); } catch (Exception ex) { SetStatus("Rename undo refused: " + ex.Message); }
    }
    private void ShowRenamePreview(RefactoringPlan plan)
    {
        var files = new ComboBox { ItemsSource = plan.Files.Select(f => f.Path).ToArray(), SelectedIndex = plan.Files.Length > 0 ? 0 : -1 };
        var before = new TextEditor { IsReadOnly = true, ShowLineNumbers = true, FontSize = 10, Height = 310 };
        var after = new TextEditor { IsReadOnly = true, ShowLineNumbers = true, FontSize = 10, Height = 310 };
        var editors = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,5,*") }; Place(editors, before, 0, 0); Place(editors, after, 0, 2);
        void ShowFile()
        {
            if (files.SelectedIndex < 0) return; var file = plan.Files[files.SelectedIndex];
            before.Text = file.Before; after.Text = file.After;
            before.SyntaxHighlighting = after.SyntaxHighlighting = file.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ? HighlightingManager.Instance.GetDefinition("C#") : StudioHighlighting.Xaml;
        }
        files.SelectionChanged += (_, _) => ShowFile(); ShowFile();
        var diagnostic = new TextBlock { Text = string.Join("\n", plan.Diagnostics.Select(d => d.Code + ": " + d.Message)), TextWrapping = TextWrapping.Wrap, FontSize = 10 };
        var status = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap };
        var apply = Button($"Apply to {plan.Files.Length} files", "Write the reviewed rename with conflict checks and a rollback journal", () => { }, true); apply.IsEnabled = plan.CanApply;
        apply.Click += async (_, _) =>
        {
            apply.IsEnabled = false;
            try { await ApplyProjectRenameAsync(plan); _overlay.IsVisible = false; }
            catch (Exception ex) { status.Text = ex.Message; apply.IsEnabled = plan.CanApply; }
        };
        ShowDialog("Review project rename", Column(files, Caption("BEFORE                                      AFTER"), editors, new ScrollViewer { Content = diagnostic, MaxHeight = 110 }, status, apply));
    }
}
