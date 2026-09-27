using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ProDesigner.Core;
using ProDesigner.Design;
using ProDesigner.Xaml;

namespace ProDesigner.Preview;

/// <summary>Source-linked, embeddable remote bitmap artboard. Project controls remain entirely outside this process.</summary>
public sealed class RenderedPreviewSurface : Control, IDisposable
{
    private Bitmap? _bitmap;
    private RenderedPreviewFrame? _frame;
    private DesignerSession? _session;
    private Point _start;
    private DesignRect _original;
    private DesignRect? _ghost;
    private string? _gestureId;
    private ResizeEdges _resize;
    private long _version;
    private string? _expectedHash;
    private bool _interactive;
    private IPointer? _pointer;
    private double _scale = 1;
    private Vector _offset;
    public event Action<PreviewInput>? Input;
    public event Action<string>? Status;
    public RenderedPreviewFrame? Frame => _frame;
    public bool IsFrameCurrent => _frame is not null && _frame.SourceHash == _expectedHash && _session?.IsValid == true;
    public bool Interactive
    {
        get => _interactive;
        set { if (_interactive == value) return; CancelGesture(); Input?.Invoke(new(PreviewInputKind.Reset)); _interactive = value; InvalidateVisual(); }
    }
    public RenderedPreviewSurface()
    {
        Focusable = true; ClipToBounds = true;
        PointerPressed += Pressed; PointerMoved += Moved; PointerReleased += Released;
        PointerCaptureLost += (_, _) => { if (_interactive) Input?.Invoke(new(PreviewInputKind.Reset)); CancelGesture(); };
        PointerWheelChanged += (_, e) =>
        {
            if (!_interactive || !IsFrameCurrent) return;
            var p = Logical(e.GetPosition(this)); Input?.Invoke(new(PreviewInputKind.Wheel, p.X, p.Y, DeltaX: e.Delta.X, DeltaY: e.Delta.Y, Modifiers: Modifiers(e.KeyModifiers))); e.Handled = true;
        };
        KeyDown += (_, e) =>
        {
            if (_interactive && IsFrameCurrent) { Input?.Invoke(new(PreviewInputKind.KeyDown, Key: e.Key.ToString(), Modifiers: Modifiers(e.KeyModifiers))); e.Handled = true; }
            else if (e.Key == Key.Escape) { CancelGesture(); e.Handled = true; }
        };
        KeyUp += (_, e) => { if (_interactive && IsFrameCurrent) { Input?.Invoke(new(PreviewInputKind.KeyUp, Key: e.Key.ToString(), Modifiers: Modifiers(e.KeyModifiers))); e.Handled = true; } };
        TextInput += (_, e) => { if (_interactive && IsFrameCurrent) { Input?.Invoke(new(PreviewInputKind.Text, Text: e.Text)); e.Handled = true; } };
        LostFocus += (_, _) => { if (_interactive) Input?.Invoke(new(PreviewInputKind.Reset)); };
    }
    public void Attach(DesignerSession session)
    {
        if (_session is not null) { _session.Changed -= SourceChanged; _session.SelectionChanged -= InvalidateVisual; }
        CancelGesture(); _session = session; _session.Changed += SourceChanged; _session.SelectionChanged += InvalidateVisual;
        _expectedHash = RenderedPreviewFrame.HashSource(session.Source); ClearFrame();
    }
    private void SourceChanged(DocumentChange change)
    {
        _expectedHash = RenderedPreviewFrame.HashSource(change.Source); CancelGesture(); InvalidateVisual();
    }
    public bool Present(RenderedPreviewFrame frame)
    {
        frame.Validate();
        if (_session is null || frame.SourceHash != _expectedHash || (_frame is not null && frame.Sequence <= _frame.Sequence)) return false;
        // Decode only a bounded PNG with pre-validated dimensions. Keep the last valid frame on decode failure.
        using var stream = new MemoryStream(frame.Png, writable: false);
        var next = new Bitmap(stream);
        var previous = _bitmap; _bitmap = next; _frame = frame; previous?.Dispose(); InvalidateVisual(); return true;
    }
    public void ClearFrame() { _frame = null; _bitmap?.Dispose(); _bitmap = null; InvalidateVisual(); }
    public void CancelGesture()
    {
        _gestureId = null; _ghost = null;
        var pointer = _pointer; _pointer = null; pointer?.Capture(null); InvalidateVisual();
    }
    public Point Logical(Point surfacePoint) => new((surfacePoint.X - _offset.X) / _scale, (surfacePoint.Y - _offset.Y) / _scale);
    public Rect? GetElementBounds(string name)
    {
        var node = _frame?.Nodes.FirstOrDefault(n => n.Name == name);
        return node is null ? null : new Rect(node.X * _scale + _offset.X, node.Y * _scale + _offset.Y, node.Width * _scale, node.Height * _scale);
    }
    private PreviewNode? Selected() => _frame?.Nodes.FirstOrDefault(n => n.Id == _session?.Primary?.Id);
    private void Pressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsFrameCurrent) { Status?.Invoke("Await the source-matched runtime frame before editing or interacting."); return; }
        Focus(); var point = Logical(e.GetPosition(this));
        var properties = e.GetCurrentPoint(this).Properties;
        if (_interactive)
        {
            var button = properties.IsRightButtonPressed ? 2 : properties.IsMiddleButtonPressed ? 1 : 0;
            Input?.Invoke(new(PreviewInputKind.Down, point.X, point.Y, button, Modifiers: Modifiers(e.KeyModifiers)));
            _pointer = e.Pointer; e.Pointer.Capture(this); e.Handled = true; return;
        }
        if (!properties.IsLeftButtonPressed) return;
        _resize = Selected() is { } selected ? ResizeGeometry.HitTest(Box(selected), point.X, point.Y, 7 / _scale) : ResizeEdges.None;
        var hit = _resize != ResizeEdges.None ? Selected() : _frame!.Nodes.LastOrDefault(n => new Rect(n.X, n.Y, n.Width, n.Height).Contains(point));
        if (hit is null) { _session!.Select(null); return; }
        _session!.Select(hit.Id, (e.KeyModifiers & KeyModifiers.Shift) != 0);
        _gestureId = hit.Id; _start = point; _original = Box(hit); _version = _session.Version;
        _pointer = e.Pointer; e.Pointer.Capture(this); e.Handled = true;
    }
    private void Moved(object? sender, PointerEventArgs e)
    {
        var point = Logical(e.GetPosition(this));
        if (_interactive && IsFrameCurrent) { Input?.Invoke(new(PreviewInputKind.Move, point.X, point.Y, Modifiers: Modifiers(e.KeyModifiers))); return; }
        if (_gestureId is null || !IsFrameCurrent) return;
        var delta = point - _start;
        if (_resize != ResizeEdges.None) _ghost = ResizeGeometry.Resize(_original, _resize, delta.X, delta.Y, (e.KeyModifiers & KeyModifiers.Alt) == 0, (e.KeyModifiers & KeyModifiers.Shift) != 0);
        else if (_session!.Tree.Find(_gestureId)?.Parent?.LocalName == "Canvas") _ghost = _original with { X = _original.X + delta.X, Y = _original.Y + delta.Y };
        InvalidateVisual();
    }
    private void Released(object? sender, PointerReleasedEventArgs e)
    {
        var point = Logical(e.GetPosition(this));
        if (_interactive)
        {
            if (IsFrameCurrent) Input?.Invoke(new(PreviewInputKind.Up, point.X, point.Y, e.InitialPressMouseButton switch { MouseButton.Middle => 1, MouseButton.Right => 2, _ => 0 }, Modifiers: Modifiers(e.KeyModifiers)));
            _pointer = null; e.Pointer.Capture(null); e.Handled = true; return;
        }
        var id = _gestureId; var ghost = _ghost; var resizing = _resize; var version = _version; var original = _original;
        CancelGesture();
        if (!IsFrameCurrent || id is null || ghost is not { } rect || _session!.Tree.Find(id) is not { } node) return;
        try
        {
            var edits = new List<TextEdit>(); var dx = rect.X - original.X; var dy = rect.Y - original.Y;
            // World-space bounds cannot be safely inverted into a rotated/skewed layout box.
            for (var parent = node; parent is not null; parent = parent.Parent)
                if (parent.Get("RenderTransform") is not null || parent.Get("LayoutTransform") is not null || parent.Children.Any(n => n.LocalName.EndsWith("Transform", StringComparison.Ordinal)))
                    throw new InvalidOperationException("Use source/inspector editing for transformed runtime controls.");
            if (resizing != ResizeEdges.None)
            {
                edits.Add(XamlEdits.SetAttribute(_session.Tree, node, "Width", LayoutEngine.Format(rect.Width)));
                edits.Add(XamlEdits.SetAttribute(_session.Tree, node, "Height", LayoutEngine.Format(rect.Height)));
            }
            if (node.Parent?.LocalName == "Canvas") edits.AddRange(LayoutEngine.Move(_session.Tree, node, dx, dy, snap: false));
            if (edits.Count > 0) _session.Apply(resizing == ResizeEdges.None ? "Move runtime selection" : "Resize runtime selection", edits, version);
        }
        catch (InvalidOperationException ex) { Status?.Invoke(ex.Message); }
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context); context.DrawRectangle(Brush.Parse("#101218"), null, new Rect(Bounds.Size));
        if (_bitmap is null || _frame is null) return;
        _scale = Math.Min(Math.Max(1, Bounds.Width - 40) / _frame.Viewport.Width, Math.Max(1, Bounds.Height - 40) / _frame.Viewport.Height);
        _offset = new((Bounds.Width - _frame.Viewport.Width * _scale) / 2, (Bounds.Height - _frame.Viewport.Height * _scale) / 2);
        context.DrawImage(_bitmap, new Rect(_offset.X, _offset.Y, _frame.Viewport.Width * _scale, _frame.Viewport.Height * _scale));
        if (!IsFrameCurrent)
        {
            context.DrawRectangle(Brush.Parse("#77101218"), null, new Rect(Bounds.Size)); return;
        }
        if (_interactive) return;
        foreach (var node in _frame.Nodes.Where(n => _session!.Selection.Contains(n.Id)))
        {
            var box = _gestureId == node.Id && _ghost is { } ghost ? ghost : Box(node);
            var rect = new Rect(box.X * _scale + _offset.X, box.Y * _scale + _offset.Y, box.Width * _scale, box.Height * _scale);
            var pen = new Pen(Brush.Parse("#A899EC"), 1.5); context.DrawRectangle(null, pen, rect);
            foreach (var handle in ResizeGeometry.Handles(new(rect.X, rect.Y, rect.Width, rect.Height)))
                context.DrawRectangle(Brushes.White, pen, new Rect(handle.X - 3, handle.Y - 3, 6, 6));
        }
    }
    private static DesignRect Box(PreviewNode value) => new(value.X, value.Y, value.Width, value.Height);
    private static PreviewModifiers Modifiers(KeyModifiers value) => ((value & KeyModifiers.Shift) != 0 ? PreviewModifiers.Shift : 0) |
        ((value & KeyModifiers.Control) != 0 ? PreviewModifiers.Control : 0) | ((value & KeyModifiers.Alt) != 0 ? PreviewModifiers.Alt : 0) |
        ((value & KeyModifiers.Meta) != 0 ? PreviewModifiers.Meta : 0);
    public void Dispose()
    {
        CancelGesture();
        if (_session is not null) { _session.Changed -= SourceChanged; _session.SelectionChanged -= InvalidateVisual; _session = null; }
        ClearFrame();
    }
}
