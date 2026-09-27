using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ProDesigner.Core;
using ProDesigner.Design;
using ProDesigner.Xaml;

namespace ProDesigner.Preview;

public enum PreviewRefreshKind { FullBuild, PropertyDelta, Unchanged }

public sealed class DesignSurface : UserControl
{
    private readonly Canvas _canvas = new() { Width = 2800, Height = 2000 };
    private readonly LayoutTransformControl _zoomHost;
    private readonly ScrollViewer _scroll;
    private readonly List<PreviewFrame> _frames = [];
    private DesignerSession? _session;
    private double _zoom = .8;
    private XamlSyntaxTree? _builtTree;
    private DesignerSession? _builtSession;
    private PreviewProfile[] _builtProfiles = [];
    private Dictionary<string, string> _builtData = [];
    private bool _animationMutated;
    public PreviewRefreshKind LastRefresh { get; private set; }
    public long FullBuildCount { get; private set; }
    public long PropertyDeltaCount { get; private set; }
    public Control? GetPreviewControl(string name) => _frames.FirstOrDefault()?.FindByName(name);
    public IReadOnlyList<PreviewProfile> Profiles { get; set; } = [PreviewProfile.Defaults[0], PreviewProfile.Defaults[1]];
    public IReadOnlyList<DesignDiagnostic> Diagnostics { get; private set; } = [];
    public IReadOnlyDictionary<string, string> SampleData { get; set; } = new PreviewBuilder().SampleData;
    public bool Interactive { get; private set; }
    public double Zoom => _zoom;
    public event Action<string>? Status;
    public event Action<double>? ZoomChanged;
    public IReadOnlyDictionary<string, DesignRect> BoundsMap => _frames.FirstOrDefault()?.GetBounds() ?? new Dictionary<string, DesignRect>();
    public DesignSurface()
    {
        Name = "DesignSurface";
        _canvas.Background = Brush.Parse("#101218");
        _zoomHost = new LayoutTransformControl { Child = _canvas, LayoutTransform = new ScaleTransform(_zoom, _zoom) };
        _scroll = new ScrollViewer { Content = _zoomHost, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Content = _scroll;
        PointerWheelChanged += (_, e) =>
        {
            if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) == 0) return;
            SetZoom(_zoom * (e.Delta.Y > 0 ? 1.12 : 1 / 1.12)); e.Handled = true;
        };
    }
    public void Attach(DesignerSession session)
    {
        if (_session is not null) _session.SelectionChanged -= RefreshSelection;
        _session = session; _session.SelectionChanged += RefreshSelection;
        Rebuild();
    }
    public void SetZoom(double zoom)
    {
        var previous = _zoom; _zoom = Math.Clamp(zoom, .15, 3);
        var offset = _scroll.Offset;
        _zoomHost.LayoutTransform = new ScaleTransform(_zoom, _zoom);
        _scroll.Offset = offset * (_zoom / previous);
        ZoomChanged?.Invoke(_zoom);
    }
    public void Fit()
    {
        var width = Profiles.Take(2).Sum(p => p.Width + 56) + 72;
        SetZoom(Math.Min(1, Math.Max(300, Bounds.Width) / width));
        _scroll.Offset = default;
    }
    public DesignRect? GetElementBounds(string name)
    {
        var control = _frames.FirstOrDefault()?.FindByName(name);
        var root = TopLevel.GetTopLevel(this);
        if (control is null || root is null) return null;
        var a = control.TranslatePoint(default, root);
        var b = control.TranslatePoint(new Point(control.Bounds.Width, control.Bounds.Height), root);
        return a is { } start && b is { } end ? new(start.X, start.Y, end.X - start.X, end.Y - start.Y) : null;
    }
    public void CancelGesture() { foreach (var frame in _frames) frame.CancelGesture(); }
    public void SetInteractive(bool interactive)
    {
        Interactive = interactive;
        foreach (var frame in _frames) frame.SetInteractive(interactive);
    }
    public void Rebuild()
    {
        if (_session is null || !_session.IsValid) return;
        if (!_animationMutated && _builtSession == _session && _builtTree is not null && _builtProfiles.SequenceEqual(Profiles)
            && _builtData.Count == SampleData.Count && _builtData.All(p => SampleData.TryGetValue(p.Key, out var value) && value == p.Value)
            && Diagnostics.Count == 0 && PreviewPropertyDelta.Prepare(_builtTree, _session.Tree) is { } delta)
        {
            if (_frames.All(frame => frame.ApplyDelta(delta)))
            {
                _builtTree = _session.Tree;
                LastRefresh = delta.Count == 0 ? PreviewRefreshKind.Unchanged : PreviewRefreshKind.PropertyDelta;
                if (delta.Count > 0) PropertyDeltaCount++;
                RefreshSelection(); return;
            }
        }
        CancelGesture(); _canvas.Children.Clear(); _frames.Clear();
        var diagnostics = new List<DesignDiagnostic>();
        double x = 56, y = 64;
        foreach (var profile in Profiles)
        {
            var result = new PreviewBuilder { SampleData = SampleData }.Build(_session.Tree, profile);
            diagnostics.AddRange(result.Diagnostics);
            var frame = new PreviewFrame(_session, result, profile);
            frame.Status += message => Status?.Invoke(message);
            frame.SetInteractive(Interactive);
            var label = new TextBlock { Text = $"{profile.Name}   {profile.Width:0} × {profile.Height:0}", Foreground = Brush.Parse("#A6AAB9"), FontSize = 12 };
            Canvas.SetLeft(label, x); Canvas.SetTop(label, y - 28); _canvas.Children.Add(label);
            Canvas.SetLeft(frame, x); Canvas.SetTop(frame, y); _canvas.Children.Add(frame); _frames.Add(frame);
            x += profile.Width + 56;
        }
        _canvas.Width = Math.Max(1600, x + 80);
        _canvas.Height = Math.Max(1200, Profiles.Max(p => p.Height) + 200);
        Diagnostics = diagnostics.DistinctBy(d => (d.Code, d.Offset, d.Message)).ToArray();
        _builtTree = _session.Tree; _builtSession = _session; _builtProfiles = Profiles.ToArray(); _builtData = new(SampleData);
        _animationMutated = false; FullBuildCount++; LastRefresh = PreviewRefreshKind.FullBuild;
    }
    public void ApplyAnimation(string target, string property, double value)
    {
        _animationMutated = true; foreach (var frame in _frames) frame.ApplyAnimation(target, property, value);
    }
    public void ApplyAnimationValue(string target, string property, string value)
    {
        _animationMutated = true; foreach (var frame in _frames) if (frame.FindByName(target) is { } control) PreviewBuilder.ApplyProperty(control, property, value);
    }
    public void RefreshSelection() { foreach (var frame in _frames) frame.RefreshSelection(); }
    public void Align(Alignment alignment)
    {
        if (_session is null) return;
        _session.Apply("Align " + alignment, LayoutEngine.Align(_session.Tree, BoundsMap, _session.Selection, alignment));
    }
    public void Distribute(bool horizontal)
    {
        if (_session is null) return;
        _session.Apply("Distribute", LayoutEngine.Distribute(_session.Tree, BoundsMap, _session.Selection, horizontal));
    }
}

internal sealed class PreviewFrame : Grid
{
    private sealed record GestureItem(Control Control, DesignRect Bounds, double Width, double Height, double Left, double Top, bool CanvasChild);
    private readonly DesignerSession _session;
    private readonly PreviewResult _preview;
    private readonly PreviewProfile _profile;
    private readonly SelectionOverlay _overlay;
    private readonly Dictionary<string, GestureItem> _initial = [];
    private Point _start;
    private string? _dragId;
    private string? _marqueeParent;
    private ResizeEdges _resize;
    private long _version;
    private IPointer? _pointer;
    private bool _moved;
    public event Action<string>? Status;
    public PreviewFrame(DesignerSession session, PreviewResult preview, PreviewProfile profile)
    {
        _session = session; _preview = preview; _profile = profile;
        Width = profile.Width; Height = profile.Height;
        Children.Add(preview.Root);
        _overlay = new SelectionOverlay(); Children.Add(_overlay);
        _overlay.PointerPressed += Pressed;
        _overlay.PointerMoved += Moved;
        _overlay.PointerReleased += Released;
        _overlay.PointerCaptureLost += (_, _) => CancelGesture();
        LayoutUpdated += (_, _) => RefreshSelection();
    }
    public Control? FindByName(string name)
    {
        var node = _session.Tree.Elements.FirstOrDefault(n => n.DisplayName == name);
        return node is not null ? _preview.Controls.GetValueOrDefault(node.Id) : null;
    }
    public bool ApplyDelta(IReadOnlyList<PreviewPropertyChange> changes)
    {
        if (changes.Count == 0) return true;
        CancelGesture();
        try
        {
            foreach (var change in changes)
                if (!_preview.Controls.TryGetValue(change.NodeId, out var control) || !PreviewBuilder.ApplyProperty(control, change.Property, change.Value)) return false;
            // Artboards own root dimensions, even when the source carries design-time Width/Height.
            if (_preview.Controls.TryGetValue(_session.Tree.Root.Id, out var root)) { root.Width = _profile.Width; root.Height = _profile.Height; }
            return true;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException) { return false; }
    }
    public void SetInteractive(bool value) { CancelGesture(); _overlay.IsVisible = !value; _preview.Root.IsHitTestVisible = value; }
    public Dictionary<string, DesignRect> GetBounds()
    {
        var result = new Dictionary<string, DesignRect>();
        foreach (var pair in _preview.Controls)
        {
            var p = pair.Value.TranslatePoint(default, this);
            if (p is { } point && pair.Value.IsVisible) result[pair.Key] = new(point.X, point.Y, pair.Value.Bounds.Width, pair.Value.Bounds.Height);
        }
        return result;
    }
    public void RefreshSelection()
    {
        var bounds = GetBounds();
        _overlay.Rectangles = _session.Selection.Where(bounds.ContainsKey).Select(id => bounds[id]).ToArray();
        _overlay.InvalidateVisual();
    }
    public void ApplyAnimation(string target, string property, double value)
    {
        var node = _session.Tree.Elements.FirstOrDefault(e => e.Get("x:Name") == target || e.Get("Name") == target);
        if (node is not null && _preview.Controls.TryGetValue(node.Id, out var control))
            PreviewBuilder.ApplyProperty(control, property, LayoutEngine.Format(value));
    }
    private void Pressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_overlay).Properties.IsLeftButtonPressed || !_session.IsValid) return;
        CancelGesture(); _start = e.GetPosition(this); _version = _session.Version;
        var bounds = GetBounds(); var primary = _session.Primary;
        _resize = primary is not null && bounds.TryGetValue(primary.Id, out var selected) ? ResizeGeometry.HitTest(selected, _start.X, _start.Y) : ResizeEdges.None;
        var hit = _resize != ResizeEdges.None ? primary : _session.Tree.Elements.LastOrDefault(n => !n.IsProperty && bounds.TryGetValue(n.Id, out var rect) && _start.X >= rect.X && _start.X <= rect.Right && _start.Y >= rect.Y && _start.Y <= rect.Bottom);
        var additive = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        if (_resize == ResizeEdges.None && hit is not null && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) == 0)
        {
            for (var parent = hit; parent.Parent is not null; parent = parent.Parent)
                if (parent.Parent.LocalName == "Canvas") { hit = parent; break; }
        }
        if (hit is null || (_resize == ResizeEdges.None && (hit.LocalName == "Canvas" || hit.Parent is null)))
        {
            if (!additive) _session.Select(null);
            _marqueeParent = hit?.LocalName == "Canvas" ? hit.Id : null;
            _overlay.Marquee = new(_start.X, _start.Y, 0, 0);
        }
        else
        {
            if (_resize == ResizeEdges.None && (additive || !_session.Selection.Contains(hit.Id))) _session.Select(hit.Id, additive);
            _dragId = hit.Id;
            var nodes = _resize != ResizeEdges.None ? new[] { hit } : _session.SelectedRoots().ToArray();
            foreach (var node in nodes)
                if (_preview.Controls.TryGetValue(node.Id, out var control) && bounds.TryGetValue(node.Id, out var rect))
                    _initial[node.Id] = new(control, rect, control.Width, control.Height, Canvas.GetLeft(control), Canvas.GetTop(control), node.Parent?.LocalName == "Canvas");
        }
        _pointer = e.Pointer; e.Pointer.Capture(_overlay); e.Handled = true;
    }
    private void Moved(object? sender, PointerEventArgs e)
    {
        if (e.Pointer.Captured != _overlay) return;
        var point = e.GetPosition(this); var delta = point - _start;
        _moved |= Math.Abs(delta.X) + Math.Abs(delta.Y) > 2;
        if (_dragId is null)
        {
            _overlay.Marquee = new(Math.Min(point.X, _start.X), Math.Min(point.Y, _start.Y), Math.Abs(delta.X), Math.Abs(delta.Y));
            _overlay.InvalidateVisual(); return;
        }
        if (!_initial.TryGetValue(_dragId, out var initial)) return;
        var bounds = GetBounds();
        var parent = _session.Tree.Find(_dragId)?.Parent;
        var origin = parent is null ? default : bounds.GetValueOrDefault(parent.Id);
        if (_resize != ResizeEdges.None)
        {
            var local = initial.Bounds with { X = initial.Bounds.X - origin.X, Y = initial.Bounds.Y - origin.Y };
            var resized = ResizeGeometry.Resize(local, _resize, delta.X, delta.Y, (e.KeyModifiers & KeyModifiers.Alt) == 0, (e.KeyModifiers & KeyModifiers.Shift) != 0);
            initial.Control.Width = resized.Width; initial.Control.Height = resized.Height;
            if (initial.CanvasChild) { Canvas.SetLeft(initial.Control, resized.X); Canvas.SetTop(initial.Control, resized.Y); }
        }
        else if (initial.CanvasChild && parent is not null)
        {
            var other = parent.Children.Where(n => !_initial.ContainsKey(n.Id) && bounds.ContainsKey(n.Id)).Select(n => bounds[n.Id]);
            var moving = new DesignRect(initial.Bounds.X + delta.X, initial.Bounds.Y + delta.Y, initial.Bounds.Width, initial.Bounds.Height);
            var snap = (e.KeyModifiers & KeyModifiers.Alt) != 0 ? new SnapResult(moving.X, moving.Y, [], []) : LayoutEngine.SnapToObjects(moving, other, grid: 0);
            var x = snap.VerticalGuides.Count > 0 || (e.KeyModifiers & KeyModifiers.Alt) != 0 ? snap.X - origin.X : LayoutEngine.Snap(snap.X - origin.X);
            var y = snap.HorizontalGuides.Count > 0 || (e.KeyModifiers & KeyModifiers.Alt) != 0 ? snap.Y - origin.Y : LayoutEngine.Snap(snap.Y - origin.Y);
            var dx = x - (initial.Bounds.X - origin.X); var dy = y - (initial.Bounds.Y - origin.Y);
            foreach (var pair in _initial.Where(p => p.Value.CanvasChild))
            {
                var item = pair.Value; var itemParent = _session.Tree.Find(pair.Key)?.Parent;
                var parentOrigin = itemParent is null ? default : bounds.GetValueOrDefault(itemParent.Id);
                Canvas.SetLeft(item.Control, item.Bounds.X - parentOrigin.X + dx);
                Canvas.SetTop(item.Control, item.Bounds.Y - parentOrigin.Y + dy);
            }
            _overlay.VerticalGuides = snap.VerticalGuides; _overlay.HorizontalGuides = snap.HorizontalGuides;
        }
        RefreshSelection();
    }
    private void Released(object? sender, PointerReleasedEventArgs e)
    {
        if (e.Pointer.Captured != _overlay) return;
        try
        {
            if (_dragId is not null && _moved)
            {
                var edits = new List<TextEdit>();
                foreach (var pair in _initial)
                {
                    var node = _session.Tree.Find(pair.Key); if (node is null) continue;
                    var item = pair.Value;
                    if (_resize != ResizeEdges.None)
                    {
                        edits.Add(XamlEdits.SetAttribute(_session.Tree, node, "Width", LayoutEngine.Format(item.Control.Width)));
                        edits.Add(XamlEdits.SetAttribute(_session.Tree, node, "Height", LayoutEngine.Format(item.Control.Height)));
                    }
                    if (item.CanvasChild)
                    {
                        edits.Add(XamlEdits.SetAttribute(_session.Tree, node, "Canvas.Left", LayoutEngine.Format(Canvas.GetLeft(item.Control))));
                        edits.Add(XamlEdits.SetAttribute(_session.Tree, node, "Canvas.Top", LayoutEngine.Format(Canvas.GetTop(item.Control))));
                    }
                }
                if (edits.Count > 0) _session.Apply(_resize != ResizeEdges.None ? "Resize selection" : "Move selection", edits, _version);
                else Status?.Invoke("This control uses automatic layout. Edit its layout properties to reposition it.");
            }
            else if (_overlay.Marquee is { } marquee && _moved)
            {
                foreach (var pair in GetBounds().Where(p => p.Value.Intersects(marquee) && _session.Tree.Find(p.Key)?.Parent?.Id == _marqueeParent))
                    if (!_session.Selection.Contains(pair.Key)) _session.Select(pair.Key, true);
            }
            _initial.Clear(); // Successful commit: capture loss must not roll back the preview.
        }
        catch (InvalidOperationException ex) { Status?.Invoke(ex.Message); }
        finally { CancelGesture(); e.Handled = true; }
    }
    public void CancelGesture()
    {
        foreach (var item in _initial.Values)
        {
            item.Control.Width = item.Width; item.Control.Height = item.Height;
            if (item.CanvasChild) { Canvas.SetLeft(item.Control, item.Left); Canvas.SetTop(item.Control, item.Top); }
        }
        _initial.Clear(); _dragId = null; _marqueeParent = null; _resize = ResizeEdges.None; _moved = false;
        _overlay.Marquee = null; _overlay.VerticalGuides = []; _overlay.HorizontalGuides = [];
        var pointer = _pointer; _pointer = null; pointer?.Capture(null); RefreshSelection();
    }
}
internal sealed class SelectionOverlay : Control
{
    public IReadOnlyList<DesignRect> Rectangles { get; set; } = [];
    public DesignRect? Marquee { get; set; }
    public IReadOnlyList<double> VerticalGuides { get; set; } = [];
    public IReadOnlyList<double> HorizontalGuides { get; set; } = [];
    private static readonly IBrush Accent = Brush.Parse("#9D8CFF");
    public override void Render(DrawingContext context)
    {
        base.Render(context); context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
        var pen = new Pen(Accent, 1.5);
        foreach (var rect in Rectangles)
        {
            context.DrawRectangle(null, pen, new Rect(rect.X, rect.Y, rect.Width, rect.Height));
            foreach (var p in new[] { new Point(rect.X, rect.Y), new Point(rect.Right, rect.Y), new Point(rect.X, rect.Bottom), new Point(rect.Right, rect.Bottom), new Point(rect.X + rect.Width / 2, rect.Y), new Point(rect.X + rect.Width / 2, rect.Bottom), new Point(rect.X, rect.Y + rect.Height / 2), new Point(rect.Right, rect.Y + rect.Height / 2) })
                context.DrawRectangle(Brushes.White, pen, new Rect(p.X - 3, p.Y - 3, 6, 6));
        }
        if (Marquee is { } m) context.DrawRectangle(Brush.Parse("#229D8CFF"), pen, new Rect(m.X, m.Y, m.Width, m.Height));
        foreach (var x in VerticalGuides) context.DrawLine(new Pen(Brush.Parse("#F29CBB"), 1), new(x, 0), new(x, Bounds.Height));
        foreach (var y in HorizontalGuides) context.DrawLine(new Pen(Brush.Parse("#F29CBB"), 1), new(0, y), new(Bounds.Width, y));
    }
}
