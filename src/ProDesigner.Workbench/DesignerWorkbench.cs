using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Controls.Templates;
using ProDesigner.Animation;
using ProDesigner.Core;
using ProDesigner.Persistence;
using ProDesigner.Design;
using ProDesigner.Preview;
using ProDesigner.Xaml;
using ProDesigner.XamlX;
using static ProDesigner.Workbench.StudioControls;

namespace ProDesigner.Workbench;

public sealed partial class DesignerWorkbench : UserControl, IDisposable
{
    public sealed class DocumentTab(string name, DesignerSession session)
    {
        public string Name { get; set; } = name;
        public string? Path { get; set; }
        public string? ProjectPath { get; set; }
        public DesignerSession Session { get; } = session;
        public AnimationClip Animation { get; set; } = new();
        public string? DiskHash { get; set; }
        public string CodeBehind { get; set; } = "";
    }
    private readonly List<DocumentTab> _documents = [];
    private readonly IWorkspaceService? _workspace;
    private readonly ICodeService? _codeService;
    private readonly Func<TrustedPreviewRequest, Task<TrustedPreview>>? _trustedPreview;
    private readonly XamlXSemanticService _semantics = new();
    private readonly DesignSurface _surface = new();
    private readonly EditorPane _editor = new();
    private readonly PropertyInspector _inspector = new();
    private readonly TimelineEditor _timeline = new();
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal, Spacing = 2 };
    private sealed record LayerRow(string Id, string Name, int Depth, bool HasChildren, bool Expanded, bool Selected);
    private readonly ListBox _layers = new() { Background = Brushes.Transparent, BorderThickness = new(0) };
    private readonly HashSet<string> _collapsedLayers = [];
    private bool _updatingLayers;
    private sealed record SolutionRow(string Project, string ProjectPath, WorkspaceFile File);
    private readonly ListBox _solutionFiles = new() { Background = Brushes.Transparent, FontSize = 11 };
    private IReadOnlyList<SolutionRow> _solutionRows = [];
    private readonly TextBox _solutionFilter = Field(watermark: "Filter solution files");
    private readonly StackPanel _navigation = new() { Spacing = 3 };
    private readonly StackPanel _toolbox = new() { Spacing = 2 };
    private readonly StackPanel _problems = new() { Spacing = 6, Margin = new(12) };
    private readonly StackPanel _history = new() { Spacing = 4, Margin = new(12) };
    private readonly TextBlock _status = Text("Ready", 10, "#9DA5BC");
    private readonly TextBlock _statistics = Text("", 10, "#858C9F");
    private readonly TextBlock _zoomLabel = Text("80%", 11);
    private readonly TextBox _layerFilter = Field(watermark: "Search layers");
    private readonly DispatcherTimer _renderDebounce = new() { Interval = TimeSpan.FromMilliseconds(160) };
    private readonly Grid _center = new() { RowDefinitions = RowDefinitions.Parse("38,*,5,255") };
    private readonly TabControl _bottom = new();
    private readonly Grid _root = new() { RowDefinitions = RowDefinitions.Parse("50,44,*,25"), ColumnDefinitions = ColumnDefinitions.Parse("230,4,*,4,280") };
    private readonly Grid _overlay = new() { IsVisible = false, Background = Brush.Parse("#B0101218") };
    private DocumentTab _active = null!;
    private bool _disposed;
    private CancellationTokenSource? _projectAnalysis;
    private string? _copiedFragment;
    public DesignerSession Session => _active.Session;
    public IReadOnlyList<DocumentTab> Documents => _documents;
    public DesignSurface Surface => _surface;
    public string ActiveDocumentName => _active.Name;
    public DesignerWorkbench(IWorkspaceService? workspace = null, ICodeService? codeService = null, Func<TrustedPreviewRequest, Task<TrustedPreview>>? trustedPreview = null, IRecoveryStore? recovery = null, Func<TrustedPreviewRequest, CancellationToken, Task<IExternalPreview>>? externalPreview = null)
    {
        _workspace = workspace; _codeService = codeService; _trustedPreview = trustedPreview; _recovery = recovery; _externalFactory = externalPreview;
        Classes.Add("studio"); Name = "DesignerWorkbench"; Focusable = true;
        BuildShell();
        InitializeRecovery();
        AttachedToVisualTree += async (_, _) => await CheckFileJournalsAsync();
        foreach (var sample in Samples.Documents) _documents.Add(new(sample.Key, new DesignerSession(sample.Value)));
        var clip = _documents[0].Animation; var track = clip.GetTrack("RevenueCard", "Opacity"); track.SetKey(0, .15); track.SetKey(1, 1);
        SwitchDocument(_documents[0]);
        _renderDebounce.Tick += (_, _) => { _renderDebounce.Stop(); RefreshDocument(); };
        _layerFilter.TextChanged += (_, _) => RefreshLayers();
        _surface.Status += SetStatus; _editor.Status += SetStatus; _inspector.Status += SetStatus; _timeline.Status += SetStatus;
        _surface.ZoomChanged += z => _zoomLabel.Text = $"{z:P0}";
        _inspector.GenerateHandler += GenerateEventHandler;
        _timeline.Seeked += (animation, time) => { foreach (var t in animation.Tracks) _surface.ApplyAnimation(t.Target, t.Property, t.Evaluate(time)); foreach (var t in animation.ColorTracks) _surface.ApplyAnimationValue(t.Target, t.Property, t.Evaluate(time).ToString()); };
        _timeline.ExportRequested += ExportAnimation;
        _timeline.ImportRequested += ImportAnimation;
        _timeline.Changed += ScheduleRecovery;
        AddHandler(KeyDownEvent, KeyPressed, RoutingStrategies.Tunnel);
        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() => _surface.Fit(), DispatcherPriority.Loaded);
    }
    private void BuildShell()
    {
        var title = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("230,*,Auto"), Background = Brush.Parse("#20222C") };
        Place(title, Row(new StudioIcon { Kind = "diamond", Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center }, Text(" ProDesigner", 16, "#ECE8F8")), 0, 0);
        title.Children[0].Margin = new(16, 0);
        Place(title, Row(Caption("AVALONIA"), Text(" / ", 12, "#5F6476"), Text("Untitled workspace", 12, "#AEB4C6")), 0, 1);
        var actions = Row(Button("⌘ K", "Open command palette", ShowCommandPalette), Button("Open", "Open XAML documents", () => _ = OpenDocumentsAsync()), Button("Save", "Save active document", () => _ = SaveDocumentAsync()), Button("▶ Preview", "Toggle interactive preview", TogglePreview, true));
        actions.Margin = new(8, 0, 14, 0); Place(title, actions, 0, 2); Place(_root, title, 0, 0, 5);
        var toolbar = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"), Background = Brush.Parse("#1B1D25") };
        var tools = Row(Button("↖", "Select and move · V", () => _surface.SetInteractive(false)), Button("＋", "Insert Button", () => InsertControl("Button")), Button("T", "Insert TextBlock", () => InsertControl("TextBlock")), Button("□", "Insert Rectangle", () => InsertControl("Rectangle")), Button("○", "Insert Ellipse", () => InsertControl("Ellipse")), new Border { Width = 1, Height = 20, Background = Line, Margin = new Thickness(6,0) }, Button("↶", "Undo · Ctrl/Cmd Z", () => Execute("undo")), Button("↷", "Redo · Ctrl/Cmd Shift Z", () => Execute("redo")), Button("⫷", "Align left", () => Guard(() => _surface.Align(Alignment.Left))), Button("↔", "Distribute horizontally", () => Guard(() => _surface.Distribute(true))), Button("⇅", "Distribute vertically", () => Guard(() => _surface.Distribute(false))), Button("Design", "Resources, styles, templates, brushes and sample data", ShowAuthoringTools), Button("Path", "Edit selected vector geometry", ShowVectorEditor), Button("Devices", "Choose preview profiles", ShowProfiles), Button("Solution", "Open a trusted solution or project (desktop)", () => _ = OpenWorkspaceAsync()));
        tools.Margin = new(12, 0); Place(toolbar, new ScrollViewer { Content = tools, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled }, 0, 0);
        var zoom = Row(Button("−", "Zoom out", () => _surface.SetZoom(_surface.Zoom / 1.2)), _zoomLabel, Button("＋", "Zoom in", () => _surface.SetZoom(_surface.Zoom * 1.2)), Button("Fit", "Fit artboards", _surface.Fit));
        zoom.Margin = new(0, 0, 12, 0); Place(toolbar, zoom, 0, 1); Place(_root, toolbar, 1, 0, 5);
        var left = new TabControl();
        var layersGrid = new Grid { RowDefinitions = RowDefinitions.Parse("Auto,Auto,*") };
        _layerFilter.Margin = new(12, 10); Place(layersGrid, _layerFilter, 0, 0); Place(layersGrid, Box(_navigation, new(10)), 1, 0); _layers.ItemTemplate = new FuncDataTemplate<LayerRow>((row, _) =>
        {
            if (row is null) return new TextBlock();
            var container = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4 + Math.Min(row.Depth, 12) * 12, 1, 0, 1), Spacing = 5 };
            if (row.HasChildren)
            {
                var toggle = Button(row.Expanded ? "⌄" : ">", "Expand or collapse " + row.Name, () => { if (!_collapsedLayers.Add(row.Id)) _collapsedLayers.Remove(row.Id); RefreshLayers(); });
                toggle.Width = 20; toggle.MinHeight = 20; toggle.Padding = new Thickness(0); container.Children.Add(toggle);
            }
            else container.Children.Add(new Border { Width = 20 });
            container.Children.Add(new TextBlock { Text = row.Name, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Foreground = row.Selected ? Brush.Parse("#C9BFF6") : Brush.Parse("#AEB5C9") });
            return container;
        }, true);
        _layers.SelectionChanged += (_, _) => { if (!_updatingLayers && _layers.SelectedItem is LayerRow row) Session.Select(row.Id); };
        Place(layersGrid, _layers, 2, 0);
        var search = Field(watermark: "Search controls"); search.Margin = new(12); search.TextChanged += (_, _) => RefreshToolbox(search.Text);
        var palette = new DockPanel(); DockPanel.SetDock(search, Dock.Top); palette.Children.Add(search); palette.Children.Add(new ScrollViewer { Content = _toolbox });
        _solutionFiles.ItemTemplate = new FuncDataTemplate<SolutionRow>((row, _) => row is null ? new TextBlock() : new TextBlock { Text = row.Project + "/" + row.File.Name, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis }, true);
        _solutionFiles.DoubleTapped += (_, _) => { if (_solutionFiles.SelectedItem is SolutionRow row && row.File.Kind is ".xaml" or ".axaml") _ = OpenWorkspaceFileAsync(row.File, row.ProjectPath); };
        _solutionFilter.TextChanged += (_, _) => _solutionFiles.ItemsSource = _solutionRows.Where(r => (r.Project + "/" + r.File.Name).Contains(_solutionFilter.Text ?? "", StringComparison.OrdinalIgnoreCase)).ToArray();
        var solutionPanel = new Grid { RowDefinitions = RowDefinitions.Parse("Auto,*") }; _solutionFilter.Margin = new(10);
        Place(solutionPanel, _solutionFilter, 0, 0); Place(solutionPanel, _solutionFiles, 1, 0);
        left.Items.Add(new TabItem { Header = "Layers", Content = layersGrid }); left.Items.Add(new TabItem { Header = "Assets", Content = palette }); left.Items.Add(new TabItem { Header = "Solution", Content = solutionPanel });
        Place(_root, left, 2, 0); Place(_root, new GridSplitter { ResizeDirection = GridResizeDirection.Columns, HorizontalAlignment = HorizontalAlignment.Stretch }, 2, 1);
        var tabBar = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"), Background = Brush.Parse("#20222B") };
        Place(tabBar, new ScrollViewer { Content = _tabs, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden }, 0, 0);
        Place(tabBar, Button("＋", "New view", NewDocument), 0, 1); Place(_center, tabBar, 0, 0); InitializeEmbeddedPreview(); Place(_center, _artboards, 1, 0);
        Place(_center, new GridSplitter { ResizeDirection = GridResizeDirection.Rows, VerticalAlignment = VerticalAlignment.Stretch }, 2, 0);
        _bottom.Items.Add(new TabItem { Header = "XAML", Content = _editor });
        _bottom.Items.Add(new TabItem { Header = "Animation", Content = _timeline });
        _bottom.Items.Add(new TabItem { Header = "Problems", Content = new ScrollViewer { Content = _problems } });
        _bottom.Items.Add(new TabItem { Header = "History", Content = new ScrollViewer { Content = _history } });
        Place(_center, _bottom, 3, 0); Place(_root, _center, 2, 2);
        Place(_root, new GridSplitter { ResizeDirection = GridResizeDirection.Columns, HorizontalAlignment = HorizontalAlignment.Stretch }, 2, 3);
        var right = new DockPanel(); var rightHeader = Box(Row(Text("Design", 12, "#CBBEF1"), Text("   Properties & layout", 10, "#747D91"))); DockPanel.SetDock(rightHeader, Dock.Top); right.Children.Add(rightHeader); right.Children.Add(_inspector); Place(_root, right, 2, 4);
        var footer = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"), Background = Brush.Parse("#23252F") };
        _status.Margin = new(12, 0); _statistics.Margin = new(12, 0); Place(footer, _status, 0, 0); Place(footer, _statistics, 0, 1); Place(_root, footer, 3, 0, 5);
        var host = new Grid(); host.Children.Add(_root); host.Children.Add(_overlay); Content = host; RefreshToolbox(null);
    }
    public void SwitchDocument(DocumentTab document)
    {
        HideEmbeddedPreview();
        _editor.Flush(); _timeline.Stop();
        if (_active is not null) { _active.Session.Changed -= DocumentChanged; _active.Session.SelectionChanged -= SelectionChanged; }
        _collapsedLayers.Clear(); _active = document; _active.Session.Changed += DocumentChanged; _active.Session.SelectionChanged += SelectionChanged;
        _editor.Attach(Session); _surface.Attach(Session); _inspector.Attach(Session); _timeline.Attach(Session, document.Animation);
        RefreshTabs(); RefreshDocument();
    }
    private void DocumentChanged(DocumentChange change)
    {
        RefreshTabs(); _renderDebounce.Stop(); _renderDebounce.Start(); ScheduleRecovery();
    }
    private void SelectionChanged()
    {
        _ = RefreshProjectDiagnosticsAsync();
        _inspector.Rebuild(); RefreshLayers();
        if (!_editor.IsKeyboardFocusWithin && Session.Primary is { } node) _editor.Navigate(node.NameSpan.Start, node.NameSpan.Length);
    }
    public void RefreshDocument()
    {
        if (_disposed) return;
        _surface.Rebuild(); RefreshLayers(); _inspector.Rebuild(); RefreshProblems();
        _ = RefreshProjectDiagnosticsAsync();
        _ = SynchronizeExternalPreviewAsync();
        _history.Children.Clear(); foreach (var item in Session.History.Take(80)) _history.Children.Add(Text("↶ " + item, 11, "#9AA3B9"));
        _statistics.Text = $"{Session.Tree.Elements.Count} nodes   •   parse {Session.LastParseMilliseconds:0.0} ms   •   v{Session.Version}   •   {_documents.Count} views";
        if (!Session.IsValid) SetStatus("XAML has errors. The last valid preview is retained; visual edits are paused.");
        else SetStatus(Session.IsDirty ? "● Unsaved changes  ·  source and canvas synchronized" : "✓ Source and canvas synchronized  ·  safe preview");
    }
    private void RefreshTabs()
    {
        _tabs.Children.Clear(); _navigation.Children.Clear();
        foreach (var document in _documents)
        {
            var tab = Button((document == _active ? "◈ " : "◇ ") + document.Name + (document.Session.IsDirty ? " •" : ""), "Open " + document.Name, () => SwitchDocument(document));
            if (document == _active) tab.Classes.Add("selected"); _tabs.Children.Add(tab);
            var nav = Button("▧  " + document.Name, "Navigate to " + document.Name, () => SwitchDocument(document)); nav.Classes.Add("layer"); if (document == _active) nav.Classes.Add("selected"); _navigation.Children.Add(nav);
        }
    }
    private void RefreshLayers()
    {
        if (_active is null) return;
        var filter = _layerFilter.Text ?? ""; var rows = new List<LayerRow>();
        foreach (var node in Session.Tree.Elements)
        {
            if (filter.Length > 0 && !node.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) && !node.LocalName.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            var hidden = false;
            if (filter.Length == 0) for (var parent = node.Parent; parent is not null; parent = parent.Parent) if (_collapsedLayers.Contains(parent.Id)) { hidden = true; break; }
            if (hidden) continue;
            rows.Add(new(node.Id, node.DisplayName, node.Id.Count(c => c == '/'), node.Children.Count > 0, !_collapsedLayers.Contains(node.Id), Session.Selection.Contains(node.Id)));
        }
        _updatingLayers = true;
        try { _layers.ItemsSource = rows; _layers.SelectedItem = rows.FirstOrDefault(r => Session.Primary?.Id == r.Id); }
        finally { _updatingLayers = false; }
    }
    private void RefreshToolbox(string? filter)
    {
        _toolbox.Children.Clear();
        foreach (var category in ControlCatalog.Items.Where(i => string.IsNullOrEmpty(filter) || i.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).GroupBy(i => i.Category))
        {
            var title = Caption(category.Key.ToUpperInvariant()); title.Margin = new(16, 14, 0, 8); _toolbox.Children.Add(title);
            foreach (var item in category)
            {
                var button = Button(item.Glyph + "   " + item.Name, item.Description, () => InsertControl(item.Name)); button.Classes.Add("layer"); button.Margin = new(8, 0); _toolbox.Children.Add(button);
            }
        }
    }
    private void RefreshProblems()
    {
        _problems.Children.Clear();
        var diagnostics = Session.Diagnostics.Concat(Session.IsValid ? _semantics.Validate(Session.Source) : []).Concat(_surface.Diagnostics).ToArray();
        foreach (var diagnostic in diagnostics.Take(200))
        {
            var row = Button($"{diagnostic.Code}   {diagnostic.Message}", diagnostic.Message, () => { _bottom.SelectedIndex = 0; _editor.Navigate(diagnostic.Offset, diagnostic.Length); });
            row.HorizontalAlignment = HorizontalAlignment.Stretch; row.HorizontalContentAlignment = HorizontalAlignment.Left; _problems.Children.Add(row);
        }
        if (diagnostics.Length == 0) _problems.Children.Add(Text("✓ No XAML or preview diagnostics", 12, "#9ACDAE"));
    }
    private void SetSolutionFiles(IReadOnlyList<ProjectSummary> projects)
    {
        _solutionRows = projects.SelectMany(p => p.Files.Where(f => f.Kind is ".axaml" or ".xaml").Select(f => new SolutionRow(p.Name, p.Path, f))).ToArray();
        _solutionFiles.ItemsSource = _solutionRows;
    }
    public void SetStatus(string message) => _status.Text = message;
    private void Guard(Action action)
    {
        try { _editor.Flush(); action(); }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException) { SetStatus(ex.Message); }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _runtimeTimer.Stop(); _runtimeSurface.Dispose(); _recoveryTimer.Stop(); _previewCancellation?.Cancel(); _ = StopExternalPreviewAsync(); _renderDebounce.Stop(); _timeline.Stop(); _projectAnalysis?.Cancel(); _projectAnalysis?.Dispose(); _workspace?.Dispose();
    }
}
