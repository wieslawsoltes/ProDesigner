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
        var selected = new ComboBox { MinWidth = 170, FontSize = 11 };
        void ListSegments() { var current = selected.SelectedIndex; selected.ItemsSource = _canvas.Model.Segments.Select((s, i) => $"{i}: {s.Command}").ToArray(); selected.SelectedIndex = Math.Clamp(current, 0, _canvas.Model.Segments.Count - 1); }
        void ApplyEdit(Action action) { try { action(); text.Text = _canvas.Model.ToData(); ListSegments(); _canvas.InvalidateVisual(); message.Text = "Geometry updated."; } catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException) { message.Text = ex.Message; } }
        var tools = Row(Button("Parse", "Parse all SVG path command families and Avalonia fill rules", () => { Reload(); ListSegments(); }),
            Button("＋ Line", "Append a line segment", () => ApplyEdit(() => _canvas.Model.Segments.Add(new(PathCommand.Line, [new(120, 120)])))),
            Button("＋ Curve", "Append a cubic Bezier segment", () => ApplyEdit(() => _canvas.Model.Segments.Add(new(PathCommand.Cubic, [new(30, 30), new(150, 30), new(180, 100)])))),
            Button("＋ Arc", "Append an analytic elliptical arc", () => ApplyEdit(() => _canvas.Model.Segments.Add(new(PathCommand.Arc, [new(200, 140)], new(80, 50, 0, false, true))))));
        var rx = Field("80"); var ry = Field("50"); var rotation = Field("0"); rx.Width = ry.Width = rotation.Width = 55;
        var large = new CheckBox { Content = "Large", FontSize = 10 }; var sweep = new CheckBox { Content = "Sweep", FontSize = 10, IsChecked = true };
        selected.SelectionChanged += (_, _) => { if (selected.SelectedIndex < 0 || selected.SelectedIndex >= _canvas.Model.Segments.Count) return; if (_canvas.Model.Segments[selected.SelectedIndex].Arc is { } arc) { rx.Text = ProDesigner.Design.LayoutEngine.Format(arc.RadiusX); ry.Text = ProDesigner.Design.LayoutEngine.Format(arc.RadiusY); rotation.Text = ProDesigner.Design.LayoutEngine.Format(arc.Rotation); large.IsChecked = arc.LargeArc; sweep.IsChecked = arc.Sweep; } };
        var arcTools = Row(rx, ry, rotation, large, sweep, Button("Set arc", "Set selected arc radii, rotation and flags", () => ApplyEdit(() => _canvas.Model.SetArc(selected.SelectedIndex,
            new(ProDesigner.Design.LayoutEngine.Number(rx.Text, double.NaN), ProDesigner.Design.LayoutEngine.Number(ry.Text, double.NaN), ProDesigner.Design.LayoutEngine.Number(rotation.Text, double.NaN), large.IsChecked == true, sweep.IsChecked == true)))));
        ListSegments();
        Content = Column(_canvas, text, tools, Row(selected, Button("Split ½", "Subdivide the selected curve without changing its shape", () => ApplyEdit(() => VectorPathOperations.Split(_canvas.Model, selected.SelectedIndex, .5)))), arcTools, message,
            Button("Apply geometry", "Write the canonical geometry to Path.Data", () => { try { var model = VectorPathModel.Parse(text.Text ?? ""); Committed?.Invoke(model.ToData()); } catch (Exception ex) when (ex is FormatException or ArgumentException) { message.Text = ex.Message; } }, true));
    }
    private sealed class PathCanvas : Control
    {
        public VectorPathModel Model { get; set; }
        public event Action? Changed;
        private (int Segment, int Point)? _drag;
        private VectorPoint _original;
        private double _scale = 1;
        private string? _cachedData;
        private global::Avalonia.Media.Geometry? _geometry;
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
            var data = Model.ToData();
            if (data != _cachedData) { _geometry = global::Avalonia.Media.Geometry.Parse(data); _cachedData = data; }
            if (_drag is null)
            {
                var bounds = _geometry!.Bounds;
                var minX = Math.Min(bounds.X, points.Min(p => p.X)); var minY = Math.Min(bounds.Y, points.Min(p => p.Y)); var width = Math.Max(1, Math.Max(bounds.Right, points.Max(p => p.X)) - minX); var height = Math.Max(1, Math.Max(bounds.Bottom, points.Max(p => p.Y)) - minY);
                _scale = Math.Min(Math.Max(1, Bounds.Width - 40) / width, Math.Max(1, Bounds.Height - 40) / height); _left = 20 - minX * _scale; _top = 20 - minY * _scale;
            }
            Point Screen(VectorPoint p) => new(p.X * _scale + _left, p.Y * _scale + _top);
            using (context.PushTransform(Matrix.CreateScale(_scale, _scale) * Matrix.CreateTranslation(_left, _top)))
                context.DrawGeometry(Brush.Parse("#303049"), new Pen(Brush.Parse("#B7A2F1"), 2 / _scale), _geometry!);
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
