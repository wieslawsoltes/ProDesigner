using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit;
using ProDesigner.Core;
using ProDesigner.Persistence;
using static ProDesigner.Workbench.StudioControls;

namespace ProDesigner.Workbench;

public sealed partial class DesignerWorkbench
{
    private readonly List<(RefactoringPlan Plan, FileTransactionReceipt Receipt)> _renameRedo = [];
    private bool _checkedFileJournals;
    public async Task RedoProjectRenameAsync(CancellationToken cancellationToken = default)
    {
        if (_refactoringBusy || _renameRedo.Count == 0) return;
        _editor.Flush(); var item = _renameRedo[^1]; ValidateOpenBuffers(item.Plan, undo: false);
        _refactoringBusy = true; _root.IsEnabled = false; _overlay.IsEnabled = false;
        try
        {
            await StopExternalPreviewAsync();
            var receipt = await RenameStore.ReplayAsync(item.Receipt, item.Plan.Files.Select(f => f.Path).ToArray(), cancellationToken);
            SynchronizeRefactoredBuffers(item.Plan, undo: false); _renameRedo.RemoveAt(_renameRedo.Count - 1); _renameHistory.Add((item.Plan, receipt));
            if (_workspace is IProjectRefactoringService service) await service.RefreshFilesAsync(item.Plan.Files.Select(f => new OpenSourceFile(f.Path, f.After)).ToArray());
            RefreshDocument(); ScheduleRecovery(); SetStatus("Project rename replayed with a new verified recovery journal.");
        }
        finally { _refactoringBusy = false; _root.IsEnabled = true; _overlay.IsEnabled = true; }
    }
    private async Task CheckFileJournalsAsync()
    {
        if (_checkedFileJournals || OperatingSystem.IsBrowser()) return; _checkedFileJournals = true;
        try
        {
            var pending = (await RenameStore.ListAsync()).Count(j => j.State == "Prepared");
            if (pending > 0 && !_disposed) SetStatus($"{pending} interrupted file transaction(s). Open Review file transaction journals in the command palette.");
        }
        catch (Exception ex) { if (!_disposed) SetStatus("File journal discovery: " + ex.Message); }
    }
    private async Task ShowFileJournalsAsync()
    {
        if (OperatingSystem.IsBrowser()) { SetStatus("Project-file journals are desktop-only. Browser workspace recovery remains available."); return; }
        try
        {
            var journals = await RenameStore.ListAsync(); var rows = new StackPanel { Spacing = 10 };
            foreach (var journal in journals)
            {
                rows.Children.Add(Caption(journal.State + " · " + journal.Receipt.Id));
                rows.Children.Add(new TextBlock { Text = journal.Error ?? string.Join("\n", journal.Paths), TextWrapping = TextWrapping.Wrap, FontSize = 10 });
                if (journal.Error is null) rows.Children.Add(Button("Review these files", "Approve reading the listed paths and review recovery differences", () => _ = ReviewFileJournalAsync(journal)));
            }
            if (journals.Count == 0) rows.Children.Add(Text("No retained file transactions."));
            ShowDialog("File transaction journals", Column(new TextBlock { Text = "A journal is recovery data, not a trust grant. Review the displayed paths before opening them. No files are restored automatically.", FontSize = 11, TextWrapping = TextWrapping.Wrap }, new ScrollViewer { Content = rows, MaxHeight = 500 }));
        }
        catch (Exception ex) { SetStatus("Cannot read file journals: " + ex.Message); }
    }
    private async Task ReviewFileJournalAsync(FileJournalSummary summary)
    {
        try
        {
            var review = await RenameStore.ReviewAsync(summary.Receipt, summary.Paths);
            var files = new ComboBox { ItemsSource = review.Files.Select(f => f.State + " · " + f.Path).ToArray(), SelectedIndex = 0 };
            var before = new TextEditor { IsReadOnly = true, ShowLineNumbers = true, FontSize = 10, Height = 155 };
            var after = new TextEditor { IsReadOnly = true, ShowLineNumbers = true, FontSize = 10, Height = 155 };
            void Display() { if (files.SelectedIndex < 0) return; var file = review.Files[files.SelectedIndex]; before.Text = file.Before; after.Text = file.After; }
            files.SelectionChanged += (_, _) => Display(); Display();
            var restore = Button("Restore original files", "Recheck all files and restore the recorded original bytes", () => _ = RestoreReviewedJournalAsync(summary, review), true);
            restore.IsEnabled = review.CanRestore;
            ShowDialog("Review file recovery", Column(files, Caption("ORIGINAL"), before, Caption("APPLIED"), after,
                new TextBlock { Text = review.CanRestore ? "Every file still matches a known original/applied snapshot. Restore will check again before writing." : "Restoration is disabled: the journal is reverted, a file is missing, or an external edit conflicts with the snapshots.", TextWrapping = TextWrapping.Wrap, FontSize = 11 }, restore));
        }
        catch (Exception ex) { SetStatus("Journal review failed: " + ex.Message); }
    }
    private async Task RestoreReviewedJournalAsync(FileJournalSummary summary, FileJournalReview review)
    {
        if (_refactoringBusy) return;
        _editor.Flush();
        try
        {
            foreach (var file in review.Files)
                foreach (var document in _documents)
                {
                    if (SamePath(document.Path, file.Path) && document.Session.Source != file.Before && document.Session.Source != file.After)
                        throw new InvalidOperationException("Save or close the modified open buffer before file recovery: " + document.Name);
                    if (document.Path is not null && SamePath(document.Path + ".cs", file.Path) && !string.IsNullOrEmpty(document.CodeBehind) && document.CodeBehind != file.Before && document.CodeBehind != file.After)
                        throw new InvalidOperationException("Save or close the modified code-behind before file recovery: " + document.Name);
                }
            _refactoringBusy = true; _root.IsEnabled = false; _overlay.IsEnabled = false;
            await StopExternalPreviewAsync(); await RenameStore.RevertAsync(summary.Receipt, summary.Paths);
            foreach (var file in review.Files)
                foreach (var document in _documents)
                {
                    if (SamePath(document.Path, file.Path))
                    {
                        document.Session.SetSource(file.Before, "Restore file journal"); document.Session.RestoreSavedBaseline(file.Before); document.DiskHash = WorkspaceCodec.Hash(file.Before);
                    }
                    if (document.Path is not null && SamePath(document.Path + ".cs", file.Path)) document.CodeBehind = file.Before;
                }
            _renameHistory.Clear(); _renameRedo.Clear(); _preparedRename = null;
            if (_workspace is IProjectRefactoringService service) await service.RefreshFilesAsync(review.Files.Select(f => new OpenSourceFile(f.Path, f.Before)).ToArray());
            _overlay.IsVisible = false; RefreshDocument(); ScheduleRecovery(); SetStatus("Reviewed journal restored. Project execution trust was not changed.");
        }
        catch (Exception ex) { SetStatus("Journal restoration refused: " + ex.Message); }
        finally { _refactoringBusy = false; _root.IsEnabled = true; _overlay.IsEnabled = true; }
    }
    private async Task RedoRenameFromUiAsync()
    {
        try { await RedoProjectRenameAsync(); } catch (Exception ex) { SetStatus("Rename redo refused: " + ex.Message); }
    }
}
