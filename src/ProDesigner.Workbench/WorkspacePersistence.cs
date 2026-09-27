using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ProDesigner.Animation;
using ProDesigner.Core;
using ProDesigner.Design;
using ProDesigner.Persistence;
using static ProDesigner.Workbench.StudioControls;

namespace ProDesigner.Workbench;

public sealed partial class DesignerWorkbench
{
    private readonly IRecoveryStore? _recovery;
    private readonly DispatcherTimer _recoveryTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly SemaphoreSlim _recoveryWrites = new(1, 1);
    private string? _pendingRecovery;
    private bool _recoveryReady;
    private readonly Func<TrustedPreviewRequest, CancellationToken, Task<IExternalPreview>>? _externalFactory;
    private IExternalPreview? _externalPreview;
    private DocumentTab? _previewDocument;
    private CancellationTokenSource? _previewCancellation;
    private long _previewRevision = -1;
    private bool _previewUpdating;
    private readonly Dictionary<string, string[]> _frameworks = [];
    private string? _chosenFramework;
    public string ExportWorkspace() => WorkspaceCodec.Serialize(CaptureWorkspace());
    public WorkspaceState CaptureWorkspace()
    {
        _editor.Flush();
        return new(WorkspaceState.CurrentSchema, "ProDesigner workspace", _documents.IndexOf(_active), _surface.Zoom, _surface.Profiles.ToArray(),
            _documents.Select(d => new DocumentState(d.Name, d.Session.Source, d.Session.SavedSource, d.CodeBehind, d.Path, d.ProjectPath, ClipState.Capture(d.Animation), d.Session.Selection.ToArray(), DocumentId(d), d.Session.CaptureHistory(20))).ToArray(), new Dictionary<string, string>(_surface.SampleData), _designSystem, _prototype);
    }
    public void ImportWorkspace(string json)
    {
        var state = WorkspaceCodec.Deserialize(json);
        // Fully validate and prepare before replacing any open tabs.
        var documents = state.Documents.Select(d =>
        {
            var session = new DesignerSession("<UserControl xmlns=\"https://github.com/avaloniaui\" />");
            session.SetSource(d.Source, "Restore workspace"); session.RestoreSavedBaseline(d.SavedSource);
            if (d.History is not null) session.RestoreHistory(d.History);
            foreach (var id in d.Selection) session.Select(id, true);
            return new DocumentTab(d.Name, session) { Path = d.FilePath, ProjectPath = d.ProjectPath, CodeBehind = d.CodeBehind, Animation = d.Animation.Restore(), DiskHash = d.FilePath is null ? null : WorkspaceCodec.Hash(d.SavedSource) };
        }).ToArray();
        _editor.Flush(); _ = StopExternalPreviewAsync(); _documents.Clear(); _documents.AddRange(documents);
        _documentIds.Clear();
        for (var i = 0; i < documents.Length; i++) _documentIds[documents[i]] = state.Documents[i].Id ?? Guid.NewGuid().ToString("N");
        _designSystem = state.DesignSystem ?? ProDesigner.DesignSystems.DesignSystemState.Empty;
        _prototype = state.Prototype ?? ProDesigner.Prototyping.PrototypeGraph.Empty;
        _studioUndo.Clear(); _studioRedo.Clear();
        _surface.Profiles = state.Profiles; if (state.SampleData is not null) _surface.SampleData = state.SampleData; SwitchDocument(documents[state.ActiveDocument]); _surface.SetZoom(state.Zoom);
        ScheduleRecovery();
    }
    private void InitializeRecovery()
    {
        _recoveryTimer.Tick += async (_, _) => { _recoveryTimer.Stop(); await SaveRecoveryAsync(); };
        AttachedToVisualTree += async (_, _) =>
        {
            if (_recoveryReady || _recovery is null) return;
            try { _pendingRecovery = await _recovery.ReadAsync(); if (_pendingRecovery is not null) ShowRecovery(); }
            catch (Exception ex) { SetStatus("Recovery journal unavailable: " + ex.Message); }
            finally { _recoveryReady = true; }
        };
    }
    private void ScheduleRecovery()
    {
        if (_disposed || _recovery is null || !_recoveryReady || _pendingRecovery is not null) return;
        _recoveryTimer.Stop(); _recoveryTimer.Start();
    }
    public async Task SaveRecoveryAsync()
    {
        if (_recovery is null || _pendingRecovery is not null || _active is null) return;
        try
        {
            var json = ExportWorkspace();
            await _recoveryWrites.WaitAsync();
            try { await _recovery.WriteAsync(json); }
            finally { _recoveryWrites.Release(); }
        }
        catch (Exception ex) { SetStatus("Recovery save failed; use Save workspace: " + ex.Message); }
    }
    public void RestoreRecovery()
    {
        if (_pendingRecovery is null) return;
        var json = _pendingRecovery; ImportWorkspace(json); _pendingRecovery = null; _overlay.IsVisible = false; ScheduleRecovery();
    }
    private void ShowRecovery()
    {
        if (_pendingRecovery is null) { SetStatus("No unreviewed recovery journal. Workspace changes are checkpointed after a short idle period."); return; }
        var state = WorkspaceCodec.Deserialize(_pendingRecovery);
        ShowDialog("Recover your workspace", Column(new TextBlock { Text = $"A previous workspace contains {state.Documents.Length} views, their XAML, code-behind, animation tracks and preview profiles. Restoring does not evaluate or execute projects.", TextWrapping = TextWrapping.Wrap },
            Button("Restore workspace", "Restore the recovery snapshot", () => Guard(() => { RestoreRecovery(); }), true),
            Button("Discard recovery", "Discard the old recovery snapshot", () => { _pendingRecovery = null; _overlay.IsVisible = false; ScheduleRecovery(); })));
    }
    private async Task SaveWorkspaceAsync()
    {
        try
        {
            var provider = TopLevel.GetTopLevel(this)?.StorageProvider; if (provider is null) return;
            var file = await provider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Save designer workspace", SuggestedFileName = "Workspace.prodesigner", DefaultExtension = "prodesigner", FileTypeChoices = [new("ProDesigner workspace") { Patterns = ["*.prodesigner"] }] });
            if (file is null) return;
            var json = ExportWorkspace();
            await using var stream = await file.OpenWriteAsync(); if (stream.CanSeek) stream.SetLength(0);
            await using var writer = new StreamWriter(stream); await writer.WriteAsync(json); await writer.FlushAsync();
            SetStatus("Saved complete workspace: " + file.Name);
        }
        catch (Exception ex) { SetStatus("Workspace save failed: " + ex.Message); }
    }
    private async Task OpenWorkspaceSnapshotAsync()
    {
        try
        {
            var provider = TopLevel.GetTopLevel(this)?.StorageProvider; if (provider is null) return;
            var files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Open designer workspace", FileTypeFilter = [new("ProDesigner workspace") { Patterns = ["*.prodesigner"] }] });
            var file = files.FirstOrDefault(); if (file is null) return;
            await using var stream = await file.OpenReadAsync();
            if (stream.CanSeek && stream.Length > WorkspaceCodec.MaximumBytes) throw new InvalidDataException("Workspace too large.");
            using var reader = new StreamReader(stream); var json = await reader.ReadToEndAsync(); WorkspaceCodec.Deserialize(json);
            ShowDialog("Replace the open workspace?", Column(Text("Save the current workspace before replacing its tabs."),
                Button("Save current workspace", "Save current workspace", () => _ = SaveWorkspaceAsync()),
                Button("Open selected workspace", "Replace open tabs with this workspace", () => Guard(() => { ImportWorkspace(json); _overlay.IsVisible = false; }), true)));
        }
        catch (Exception ex) { SetStatus("Workspace open failed: " + ex.Message); }
    }
    public async Task StopExternalPreviewAsync()
    {
        _previewCancellation?.Cancel();
        var preview = _externalPreview; _externalPreview = null; _previewDocument = null; _previewRevision = -1;
        if (preview is not null) await preview.DisposeAsync();
    }
    private async Task SynchronizeExternalPreviewAsync()
    {
        if (_previewUpdating || _externalPreview is null || _previewDocument != _active || !Session.IsValid || _previewRevision == Session.Version) return;
        _previewUpdating = true;
        try
        {
            while (_externalPreview is { IsAlive: true } preview && _previewDocument == _active && Session.IsValid && _previewRevision != Session.Version)
            {
                var version = Session.Version; var source = Session.Source;
                await preview.UpdateAsync(source, _previewCancellation?.Token ?? default); _previewRevision = version;
            }
        }
        catch (Exception ex) { if (!_disposed) SetStatus("Isolated preview: " + ex.Message); }
        finally { _previewUpdating = false; }
    }
    private void ImportAnimation()
    {
        Guard(() =>
        {
            var result = AnimationImporter.Import(Session.Source);
            if (result.Clips.Count == 0) { SetStatus(result.Diagnostics.FirstOrDefault()?.Message ?? "No named-target animations found."); return; }
            var body = new StackPanel { Spacing = 10 };
            foreach (var clip in result.Clips)
                body.Children.Add(Button($"Import {clip.Name} · {clip.DurationSeconds:0.##}s · {clip.Tracks.Count + clip.ColorTracks.Count} tracks", "Replace timeline with this imported clip", () =>
                { _active.Animation = clip; _timeline.Attach(Session, clip); _overlay.IsVisible = false; _bottom.SelectedIndex = 1; ScheduleRecovery(); }));
            foreach (var diagnostic in result.Diagnostics) body.Children.Add(new TextBlock { Text = diagnostic.Message, TextWrapping = TextWrapping.Wrap, FontSize = 11 });
            ShowDialog("Import XAML animation", new ScrollViewer { Content = body, MaxHeight = 460 });
        });
    }
}
