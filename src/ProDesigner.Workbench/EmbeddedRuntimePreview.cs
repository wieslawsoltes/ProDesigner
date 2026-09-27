using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using ProDesigner.Core;
using ProDesigner.Preview;
using static ProDesigner.Workbench.StudioControls;

namespace ProDesigner.Workbench;

public sealed partial class DesignerWorkbench
{
    private readonly Grid _artboards = new();
    private readonly RenderedPreviewSurface _runtimeSurface = new();
    private readonly Border _runtimePanel = new() { IsVisible = false };
    private readonly DispatcherTimer _runtimeTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly PreviewInputQueue _runtimeInput = new();
    private readonly TextBlock _runtimeStatus = Text("Runtime preview", 11);
    private readonly ComboBox _runtimeProfile = new() { MinWidth = 130, FontSize = 11 };
    private bool _runtimeBusy;
    private bool _runtimeResetNeeded;
    private void InitializeEmbeddedPreview()
    {
        var interactive = new CheckBox { Content = "Interact", FontSize = 11 };
        interactive.IsCheckedChanged += (_, _) => _runtimeSurface.Interactive = interactive.IsChecked == true;
        var header = Row(_runtimeProfile, interactive, Button("Safe artboards", "Return to the built-in safe preview", HideEmbeddedPreview),
            Button("Stop", "Terminate the trusted preview worker", () => _ = StopExternalPreviewAsync()), _runtimeStatus);
        header.Margin = new(10, 4);
        var grid = new Grid { RowDefinitions = RowDefinitions.Parse("Auto,*") };
        Place(grid, header, 0, 0); Place(grid, _runtimeSurface, 1, 0); _runtimePanel.Child = grid;
        _artboards.Children.Add(_surface); _artboards.Children.Add(_runtimePanel);
        _runtimeSurface.Status += SetStatus;
        _runtimeSurface.Input += input =>
        {
            try { _runtimeInput.Enqueue(input); }
            catch (InvalidOperationException ex) { _runtimeInput.Reset(); _runtimeSurface.Interactive = false; SetStatus(ex.Message); }
        };
        _runtimeTimer.Tick += async (_, _) => await CaptureRuntimeFrameAsync();
    }
    private void ShowEmbeddedPreview()
    {
        if (_externalPreview is not IRenderedExternalPreview || _previewDocument != _active) { SetStatus("Start a trusted rendered preview for this document first."); return; }
        _runtimeProfile.ItemsSource = _surface.Profiles.Select(p => p.Name).ToArray(); _runtimeProfile.SelectedIndex = 0;
        _runtimeSurface.Attach(Session); _runtimeInput.Reset();
        _surface.IsVisible = false; _runtimePanel.IsVisible = true; _runtimeTimer.Start();
    }
    private void HideEmbeddedPreview()
    {
        _runtimeSurface.Interactive = false; _runtimeInput.Reset(); _runtimeResetNeeded = true;
        _runtimePanel.IsVisible = false; _surface.IsVisible = true;
        // Keep polling a connected session to drain Reset, but skip expensive frame capture once input has drained.
    }
    private async Task CaptureRuntimeFrameAsync()
    {
        if (_runtimeBusy || _previewUpdating || _externalPreview is not IRenderedExternalPreview preview || !preview.IsAlive || _disposed) return;
        if (_runtimeResetNeeded)
        {
            _runtimeBusy = true; _runtimeResetNeeded = false;
            try { await preview.ResetInputAsync(_previewCancellation?.Token ?? default); }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException) { }
            catch (Exception ex) { SetStatus("Preview input reset: " + ex.Message); }
            finally { _runtimeBusy = false; }
        }
        if (_previewDocument != _active || _externalPreview != preview) return;
        if (!Session.IsValid || _previewRevision != Session.Version) return;
        var input = _runtimeInput.Drain();
        if (!_runtimePanel.IsVisible && input.Length == 0) return;
        _runtimeBusy = true;
        var source = Session.Source; var document = _active;
        try
        {
            var profiles = _surface.Profiles;
            var profile = profiles[Math.Clamp(_runtimeProfile.SelectedIndex, 0, profiles.Count - 1)];
            var viewport = new PreviewViewport((int)profile.Width, (int)profile.Height, Math.Clamp(profile.Scale, .25, 3), profile.Dark);
            var frame = await preview.RenderAsync(viewport, RenderedPreviewFrame.HashSource(source), input, _previewCancellation?.Token ?? default);
            if (_externalPreview != preview || _active != document || Session.Source != source || _disposed) return;
            if (_runtimeSurface.Present(frame)) _runtimeStatus.Text = $"{frame.Nodes.Length} mapped controls · {viewport.PixelWidth}×{viewport.PixelHeight} · #{frame.Sequence}";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _runtimeStatus.Text = "Preview paused: " + ex.Message; _runtimeTimer.Stop(); SetStatus(_runtimeStatus.Text); }
        finally { _runtimeBusy = false; }
    }
    public RenderedPreviewSurface RuntimeSurface => _runtimeSurface;
}
