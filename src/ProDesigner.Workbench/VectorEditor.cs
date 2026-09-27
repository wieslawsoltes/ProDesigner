using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using ProDesigner.Authoring;
using static ProDesigner.Workbench.StudioControls;

namespace ProDesigner.Workbench;

/// <summary>An embeddable control-point editor for supported line and Bezier path geometry.</summary>
public sealed class VectorEditor : UserControl
{
    private readonly PathCanvas _canvas;
    public event Action<string>? Committed;
    public VectorEditor(string data)
    {
        _canvas = new PathCanvas(VectorPathModel.Parse(data));
        var text = Field(data); text.AcceptsReturn = true; text.Height = 80; text.TextWrapping = TextWrapping.Wrap;
        var message = Text("Drag a point. Changes are committed only with Apply.", 11);
        _canvas.Changed += () => text.Text = _canvas.Model.ToData();
        void Reload() { try { _canvas.Model = VectorPathModel.Parse(text.Text ?? ""); _canvas.InvalidateVisual(); message.Text = "Geometry parsed."; } catch (FormatException ex) { message.Text = ex.Message; } }
        var tools = Row(Button("Parse", "Parse supported path commands", Reload), Button("＋ Line", "Append a line segment", () => { _canvas.Model.Segments.Add(new(PathCommand.Line, [new(120, 120)])); text.Text = _canvas.Model.ToData(); _canvas.InvalidateVisual(); }), Button("＋ Curve", "Append a cubic Bezier segment", () => { _canvas.Model.Segments.Add(new(PathCommand.Cubic, [new(30, 30), new(150, 30), new(180, 100)])); text.Text = _canvas.Model.ToData(); _canvas.InvalidateVisual(); }));
        Content = Column(_canvas, text, tools, message, Button("Apply geometry", "Write this geometry to the selected Path.Data", () => { try { var model = VectorPathModel.Parse(text.Text ?? ""); Committed?.Invoke(model.ToData()); } catch (FormatException ex) { message.Text = ex.Message; } }, true));
    }
    private sealed class PathCanvas : Control
    {
        public VectorPathModel Model { get; set; }
        public event Action? Changed;
        private (int Segment, int Point)? _drag;
        private VectorPoint _original;
        private double _scale = 1;
        private double _left = 10, _top = 10;
        public PathCanvas(VectorPathModel model)
        {
            Model = model; Height = 260; ClipToBounds = true; Focusable = true;
            PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
                var p = e.GetPosition(this);
                for (var i = Model.Segments.Count - 1; i >= 0; i--) for (var j = 0; j < Model.Segments[i].Points.Count; j++)
                {
                    var v = Model.Segments[i].Points[j];
                    if (Math.Abs(p.X - (v.X * _scale + _left)) > 8 || Math.Abs(p.Y - (v.Y * _scale + _top)) > 8) continue;
                    _drag = (i, j); _original = v; e.Pointer.Capture(this); Focus(); e.Handled = true; return;
                }
            };
            PointerMoved += (_, e) => { if (_drag is not { } index) return; var p = e.GetPosition(this); Model.SetPoint(index.Segment, index.Point, new((p.X - _left) / _scale, (p.Y - _top) / _scale)); InvalidateVisual(); e.Handled = true; };
            PointerReleased += (_, e) => { if (_drag is null) return; _drag = null; e.Pointer.Capture(null); InvalidateVisual(); Changed?.Invoke(); e.Handled = true; };
            PointerCaptureLost += (_, _) => Cancel();
            KeyDown += (_, e) => { if (e.Key == Key.Escape) { Cancel(); e.Handled = true; } };
        }
        private void Cancel() { if (_drag is { } index) Model.SetPoint(index.Segment, index.Point, _original); _drag = null; InvalidateVisual(); }
        public override void Render(DrawingContext context)
        {
            context.DrawRectangle(Brush.Parse("#151720"), null, new Rect(Bounds.Size));
            var points = Model.Segments.SelectMany(s => s.Points).ToArray(); if (points.Length == 0) return;
            if (_drag is null)
            {
                var minX = points.Min(p => p.X); var minY = points.Min(p => p.Y); var width = Math.Max(1, points.Max(p => p.X) - minX); var height = Math.Max(1, points.Max(p => p.Y) - minY);
                _scale = Math.Min(Math.Max(1, Bounds.Width - 40) / width, Math.Max(1, Bounds.Height - 40) / height); _left = 20 - minX * _scale; _top = 20 - minY * _scale;
            }
            Point Screen(VectorPoint p) => new(p.X * _scale + _left, p.Y * _scale + _top);
            using (context.PushTransform(Matrix.CreateScale(_scale, _scale) * Matrix.CreateTranslation(_left, _top)))
                context.DrawGeometry(Brush.Parse("#303049"), new Pen(Brush.Parse("#B7A2F1"), 2 / _scale), Geometry.Parse(Model.ToData()));
            VectorPoint previous = points[0];
            foreach (var segment in Model.Segments)
            {
                if (segment.Points.Count > 1)
                {
                    context.DrawLine(new Pen(Brush.Parse("#7F91A8"), 1), Screen(previous), Screen(segment.Points[0]));
                    context.DrawLine(new Pen(Brush.Parse("#7F91A8"), 1), Screen(segment.Points[^2]), Screen(segment.Points[^1]));
                }
                for (var i = 0; i < segment.Points.Count; i++) context.DrawEllipse(i == segment.Points.Count - 1 ? Brush.Parse("#BEABFA") : Brush.Parse("#E9A9C4"), new Pen(Brushes.White, 1), Screen(segment.Points[i]), 4, 4);
                if (segment.Points.Count > 0) previous = segment.Points[^1];
            }
        }
    }
}
