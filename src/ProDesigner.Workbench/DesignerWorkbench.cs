using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ProDesigner.Animation;
using ProDesigner.Core;
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
        public AnimationClip Animation { get; } = new();
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
    private readonly StackPanel _layers = new() { Spacing = 1 };
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
    public DesignerWorkbench(IWorkspaceService? workspace = null, ICodeService? codeService = null, Func<TrustedPreviewRequest, Task<TrustedPreview>>? trustedPreview = null)
    {
        _workspace = workspace; _codeService = codeService; _trustedPreview = trustedPreview;
        Classes.Add("studio"); Name = "DesignerWorkbench"; Focusable = true;
        BuildShell();
        foreach (var sample in Samples.Documents) _documents.Add(new(sample.Key, new DesignerSession(sample.Value)));
        var clip = _documents[0].Animation; var track = clip.GetTrack("RevenueCard", "Opacity"); track.SetKey(0, .15); track.SetKey(1, 1);
        SwitchDocument(_documents[0]);
        _renderDebounce.Tick += (_, _) => { _renderDebounce.Stop(); RefreshDocument(); };
        _layerFilter.TextChanged += (_, _) => RefreshLayers();
        _surface.Status += SetStatus; _editor.Status += SetStatus; _inspector.Status += SetStatus; _timeline.Status += SetStatus;
        _surface.ZoomChanged += z => _zoomLabel.Text = $"{z:P0}";
        _inspector.GenerateHandler += GenerateEventHandler;
        _timeline.Seeked += (animation, time) => { foreach (var t in animation.Tracks) _surface.ApplyAnimation(t.Target, t.Property, t.Evaluate(time)); };
        _timeline.ExportRequested += ExportAnimation;
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
        var tools = Row(Button("↖", "Select and move · V", () => _surface.SetInteractive(false)), Button("＋", "Insert Button", () => InsertControl("Button")), Button("T", "Insert TextBlock", () => InsertControl("TextBlock")), Button("□", "Insert Rectangle", () => InsertControl("Rectangle")), Button("○", "Insert Ellipse", () => InsertControl("Ellipse")), new Border { Width = 1, Height = 20, Background = Line, Margin = new Thickness(6,0) }, Button("↶", "Undo · Ctrl/Cmd Z", () => Execute("undo")), Button("↷", "Redo · Ctrl/Cmd Shift Z", () => Execute("redo")), Button("⫷", "Align left", () => Guard(() => _surface.Align(Alignment.Left))), Button("↔", "Distribute horizontally", () => Guard(() => _surface.Distribute(true))), Button("⇅", "Distribute vertically", () => Guard(() => _surface.Distribute(false))), Button("Devices", "Choose preview profiles", ShowProfiles), Button("Solution", "Open a trusted solution or project (desktop)", () => _ = OpenWorkspaceAsync()));
        tools.Margin = new(12, 0); Place(toolbar, tools, 0, 0);
        var zoom = Row(Button("−", "Zoom out", () => _surface.SetZoom(_surface.Zoom / 1.2)), _zoomLabel, Button("＋", "Zoom in", () => _surface.SetZoom(_surface.Zoom * 1.2)), Button("Fit", "Fit artboards", _surface.Fit));
        zoom.Margin = new(0, 0, 12, 0); Place(toolbar, zoom, 0, 1); Place(_root, toolbar, 1, 0, 5);
        var left = new TabControl();
        var layersGrid = new Grid { RowDefinitions = RowDefinitions.Parse("Auto,Auto,*") };
        _layerFilter.Margin = new(12, 10); Place(layersGrid, _layerFilter, 0, 0); Place(layersGrid, Box(_navigation, new(10)), 1, 0); Place(layersGrid, new ScrollViewer { Content = _layers }, 2, 0);
        var search = Field(watermark: "Search controls"); search.Margin = new(12); search.TextChanged += (_, _) => RefreshToolbox(search.Text);
        var palette = new DockPanel(); DockPanel.SetDock(search, Dock.Top); palette.Children.Add(search); palette.Children.Add(new ScrollViewer { Content = _toolbox });
        left.Items.Add(new TabItem { Header = "Layers", Content = layersGrid }); left.Items.Add(new TabItem { Header = "Assets", Content = palette });
        Place(_root, left, 2, 0); Place(_root, new GridSplitter { ResizeDirection = GridResizeDirection.Columns, HorizontalAlignment = HorizontalAlignment.Stretch }, 2, 1);
        var tabBar = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"), Background = Brush.Parse("#20222B") };
        Place(tabBar, new ScrollViewer { Content = _tabs, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden }, 0, 0);
        Place(tabBar, Button("＋", "New view", NewDocument), 0, 1); Place(_center, tabBar, 0, 0); Place(_center, _surface, 1, 0);
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
        _editor.Flush(); _timeline.Stop();
        if (_active is not null) { _active.Session.Changed -= DocumentChanged; _active.Session.SelectionChanged -= SelectionChanged; }
        _active = document; _active.Session.Changed += DocumentChanged; _active.Session.SelectionChanged += SelectionChanged;
        _editor.Attach(Session); _surface.Attach(Session); _inspector.Attach(Session); _timeline.Attach(Session, document.Animation);
        RefreshTabs(); RefreshDocument();
    }
    private void DocumentChanged(DocumentChange change)
    {
        RefreshTabs(); _renderDebounce.Stop(); _renderDebounce.Start();
    }
    private void SelectionChanged()
    {
        _inspector.Rebuild(); RefreshLayers();
        if (!_editor.IsKeyboardFocusWithin && Session.Primary is { } node) _editor.Navigate(node.NameSpan.Start, node.NameSpan.Length);
    }
    public void RefreshDocument()
    {
        if (_disposed) return;
        _surface.Rebuild(); RefreshLayers(); _inspector.Rebuild(); RefreshProblems();
        _ = RefreshProjectDiagnosticsAsync();
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
        if (_active is null) return; _layers.Children.Clear(); var filter = _layerFilter.Text ?? "";
        foreach (var node in Session.Tree.Elements)
        {
            if (!node.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) && !node.LocalName.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            var depth = node.Id.Count(c => c == '/');
            var button = Button((node.Children.Count > 0 ? "⌄ " : "  ") + (node.LocalName == "TextBlock" ? "T  " : node.IsProperty ? "·  " : "◇  ") + node.DisplayName, node.Name, () => Session.Select(node.Id));
            button.Classes.Add("layer"); button.Padding = new(10 + depth * 12, 5, 6, 5);
            if (Session.Selection.Contains(node.Id)) button.Classes.Add("selected");
            _layers.Children.Add(button);
        }
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
    public void SetStatus(string message) => _status.Text = message;
    private void Guard(Action action)
    {
        try { _editor.Flush(); action(); }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException) { SetStatus(ex.Message); }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _renderDebounce.Stop(); _timeline.Stop(); _projectAnalysis?.Cancel(); _projectAnalysis?.Dispose(); _workspace?.Dispose();
    }
}
