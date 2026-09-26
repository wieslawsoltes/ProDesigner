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

public sealed class DesignSurface : UserControl
{
    private readonly Canvas _canvas = new() { Width = 2800, Height = 2000 };
    private readonly LayoutTransformControl _zoomHost;
    private readonly ScrollViewer _scroll;
    private readonly List<PreviewFrame> _frames = [];
    private DesignerSession? _session;
    private double _zoom = .8;
    public IReadOnlyList<PreviewProfile> Profiles { get; set; } = [PreviewProfile.Defaults[0], PreviewProfile.Defaults[1]];
    public IReadOnlyList<DesignDiagnostic> Diagnostics { get; private set; } = [];
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
    public void SetInteractive(bool interactive)
    {
        Interactive = interactive;
        foreach (var frame in _frames) frame.SetInteractive(interactive);
    }
    public void Rebuild()
    {
        if (_session is null || !_session.IsValid) return;
        _canvas.Children.Clear(); _frames.Clear();
        var diagnostics = new List<DesignDiagnostic>();
        double x = 56, y = 64;
        foreach (var profile in Profiles)
        {
            var result = new PreviewBuilder().Build(_session.Tree, profile);
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
    }
    public void ApplyAnimation(string target, string property, double value)
    {
        foreach (var frame in _frames) frame.ApplyAnimation(target, property, value);
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
    private readonly DesignerSession _session;
    private readonly PreviewResult _preview;
    private readonly SelectionOverlay _overlay;
    private Point _start;
    private string? _dragId;
    private DesignRect _initial;
    private bool _resize;
    private long _version;
    public event Action<string>? Status;
    public PreviewFrame(DesignerSession session, PreviewResult preview, PreviewProfile profile)
    {
        _session = session; _preview = preview;
        Width = profile.Width; Height = profile.Height;
        Children.Add(preview.Root);
        _overlay = new SelectionOverlay(); Children.Add(_overlay);
        _overlay.PointerPressed += Pressed;
        _overlay.PointerMoved += Moved;
        _overlay.PointerReleased += Released;
        _overlay.PointerCaptureLost += (_, _) => { _dragId = null; _overlay.Marquee = null; _overlay.InvalidateVisual(); };
        LayoutUpdated += (_, _) => RefreshSelection();
    }
    public void SetInteractive(bool value) { _overlay.IsVisible = !value; _preview.Root.IsHitTestVisible = value; }
    public Dictionary<string, DesignRect> GetBounds()
    {
        var result = new Dictionary<string, DesignRect>();
        foreach (var pair in _preview.Controls)
        {
            var p = pair.Value.TranslatePoint(default, this);
            if (p is { } point) result[pair.Key] = new(point.X, point.Y, pair.Value.Bounds.Width, pair.Value.Bounds.Height);
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
        if (!e.GetCurrentPoint(_overlay).Properties.IsLeftButtonPressed) return;
        _start = e.GetPosition(this); _version = _session.Version;
        var bounds = GetBounds();
        var primary = _session.Primary;
        _resize = primary is not null && bounds.TryGetValue(primary.Id, out var selected) &&
            Math.Abs(_start.X - selected.Right) < 9 && Math.Abs(_start.Y - selected.Bottom) < 9;
        var hit = _resize ? primary : _session.Tree.Elements.LastOrDefault(n => !n.IsProperty && bounds.TryGetValue(n.Id, out var r) && _start.X >= r.X && _start.X <= r.Right && _start.Y >= r.Y && _start.Y <= r.Bottom);
        if (hit is not null)
        {
            if (!_resize) _session.Select(hit.Id, (e.KeyModifiers & KeyModifiers.Shift) != 0);
            _dragId = hit.Id; _initial = bounds[hit.Id];
        }
        else { _session.Select(null); _overlay.Marquee = new(_start.X, _start.Y, 0, 0); }
        e.Pointer.Capture(_overlay); e.Handled = true;
    }
    private void Moved(object? sender, PointerEventArgs e)
    {
        if (e.Pointer.Captured != _overlay) return;
        var point = e.GetPosition(this); var delta = point - _start;
        if (_dragId is null)
        {
            _overlay.Marquee = new(Math.Min(point.X, _start.X), Math.Min(point.Y, _start.Y), Math.Abs(delta.X), Math.Abs(delta.Y));
            _overlay.InvalidateVisual(); return;
        }
        if (!_preview.Controls.TryGetValue(_dragId, out var control)) return;
        if (_resize)
        {
            control.Width = Math.Max(8, LayoutEngine.Snap(_initial.Width + delta.X));
            control.Height = Math.Max(8, LayoutEngine.Snap(_initial.Height + delta.Y));
        }
        else if (_session.Tree.Find(_dragId)?.Parent?.LocalName == "Canvas")
        {
            var parent = _session.Tree.Find(_dragId)!.Parent!;
            var bounds = GetBounds();
            var other = parent.Children.Where(c => c.Id != _dragId && bounds.ContainsKey(c.Id)).Select(c => bounds[c.Id]);
            var snap = LayoutEngine.SnapToObjects(new(_initial.X + delta.X, _initial.Y + delta.Y, _initial.Width, _initial.Height), other);
            var origin = bounds.GetValueOrDefault(parent.Id);
            Canvas.SetLeft(control, snap.X - origin.X); Canvas.SetTop(control, snap.Y - origin.Y);
            _overlay.VerticalGuides = snap.VerticalGuides; _overlay.HorizontalGuides = snap.HorizontalGuides;
        }
        RefreshSelection();
    }
    private void Released(object? sender, PointerReleasedEventArgs e)
    {
        var id = _dragId; _dragId = null;
        try
        {
            if (id is not null && _session.Tree.Find(id) is { } node && _preview.Controls.TryGetValue(id, out var control))
            {
                var delta = e.GetPosition(this) - _start;
                if (Math.Abs(delta.X) + Math.Abs(delta.Y) > 2)
                {
                    if (_resize)
                        _session.Apply("Resize " + node.DisplayName, [XamlEdits.SetAttribute(_session.Tree, node, "Width", LayoutEngine.Format(control.Width)), XamlEdits.SetAttribute(_session.Tree, node, "Height", LayoutEngine.Format(control.Height))], _version);
                    else if (node.Parent?.LocalName == "Canvas")
                        _session.Apply("Move " + node.DisplayName, [XamlEdits.SetAttribute(_session.Tree, node, "Canvas.Left", LayoutEngine.Format(Canvas.GetLeft(control))), XamlEdits.SetAttribute(_session.Tree, node, "Canvas.Top", LayoutEngine.Format(Canvas.GetTop(control)))], _version);
                    else Status?.Invoke("This control uses automatic layout. Edit its Margin, Grid placement, or parent layout instead.");
                }
            }
            else if (_overlay.Marquee is { } marquee)
            {
                _session.Select(null);
                foreach (var pair in GetBounds().Where(p => p.Value.Intersects(marquee) && _session.Tree.Find(p.Key)?.Parent?.LocalName == "Canvas")) _session.Select(pair.Key, true);
            }
        }
        catch (InvalidOperationException ex) { Status?.Invoke(ex.Message); }
        finally
        {
            _overlay.Marquee = null; _overlay.VerticalGuides = []; _overlay.HorizontalGuides = [];
            e.Pointer.Capture(null); RefreshSelection();
        }
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
