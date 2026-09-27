using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ProDesigner.Animation;
using ProDesigner.Design;

namespace ProDesigner.Workbench;

public sealed class TimelineEditor : UserControl
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _clock = new();
    private readonly TimelineCanvas _canvas = new();
    private readonly TextBox _duration = StudioControls.Field("1");
    private readonly TextBox _value = StudioControls.Field("1");
    private readonly ComboBox _property = new() { ItemsSource = new[] { "Opacity", "Width", "Height", "Canvas.Left", "Canvas.Top", "FontSize" }, SelectedIndex = 0, Width = 115, FontSize = 11 };
    private readonly ComboBox _easing = new() { ItemsSource = Enum.GetNames<EasingKind>(), SelectedIndex = 3, Width = 110, FontSize = 11 };
    private readonly TextBlock _time = StudioControls.Text("0.00 s", 11, "#BBAAF1");
    private readonly CheckBox _loop = new() { Content = "Loop", FontSize = 11 };
    private AnimationClip _clip = new();
    private DesignerSession? _session;
    private double _position;
    public AnimationClip Animation => _clip;
    public event Action<AnimationClip, double>? Seeked;
    public event Action? ExportRequested;
    public event Action<string>? Status;
    public TimelineEditor()
    {
        _duration.Width = 48; _value.Width = 60;
        var toolbar = StudioControls.Row(StudioControls.Button("▶", "Play animation", Play), StudioControls.Button("■", "Stop animation", Stop), _time, StudioControls.Caption("DURATION"), _duration, _loop, _property, _value, _easing, StudioControls.Button("◇ Key", "Add keyframe at the playhead", AddKey), StudioControls.Button("Export XAML", "Insert animation styles into the document", () => ExportRequested?.Invoke()));
        toolbar.Margin = new(10, 4);
        var grid = new Grid { RowDefinitions = RowDefinitions.Parse("Auto,*") };
        StudioControls.Place(grid, toolbar, 0, 0); StudioControls.Place(grid, _canvas, 1, 0); Content = grid;
        _canvas.SeekRequested += t => { _timer.Stop(); Seek(t); };
        _canvas.DeleteRequested += (track, time) => { track.RemoveKey(time); _canvas.InvalidateVisual(); };
        _timer.Tick += (_, _) =>
        {
            var t = _clock.Elapsed.TotalSeconds / _clip.DurationSeconds;
            if (_clip.Loop) t %= 1; else if (t >= 1) { t = 1; _timer.Stop(); }
            Seek(t);
        };
    }
    public void Attach(DesignerSession session, AnimationClip clip)
    {
        Stop(); _session = session; _clip = clip; _canvas.Animation = clip; _duration.Text = LayoutEngine.Format(clip.DurationSeconds); _loop.IsChecked = clip.Loop; Seek(0);
    }
    public void Play()
    {
        _clip.DurationSeconds = Math.Max(.05, LayoutEngine.Number(_duration.Text, 1)); _clip.Loop = _loop.IsChecked == true;
        _clock.Restart(); _timer.Start();
    }
    public void Stop() { _timer.Stop(); _clock.Stop(); Seek(0); }
    public void Seek(double time)
    {
        _position = Math.Clamp(time, 0, 1); _canvas.Position = _position; _canvas.InvalidateVisual();
        _time.Text = $"{_position * _clip.DurationSeconds:0.00} s"; Seeked?.Invoke(_clip, _position);
    }
    public void AddKey()
    {
        if (_session?.Primary is not { } node) { Status?.Invoke("Select a named control before adding a keyframe."); return; }
        var target = node.Get("x:Name") ?? node.Get("Name");
        if (target is null) { Status?.Invoke("Give the selected control an x:Name first."); return; }
        var property = _property.SelectedItem?.ToString() ?? "Opacity";
        var value = LayoutEngine.Number(_value.Text, double.NaN);
        if (!double.IsFinite(value)) { Status?.Invoke("Enter a finite numeric keyframe value."); return; }
        _clip.GetTrack(target, property).SetKey(_position, value, Enum.Parse<EasingKind>(_easing.SelectedItem?.ToString() ?? "Linear"));
        _canvas.InvalidateVisual(); Seeked?.Invoke(_clip, _position);
    }
    private sealed class TimelineCanvas : Control
    {
        public AnimationClip Animation { get; set; } = new();
        public double Position { get; set; }
        public event Action<double>? SeekRequested;
        public event Action<AnimationTrack, double>? DeleteRequested;
        public TimelineCanvas()
        {
            MinHeight = 110;
            PointerPressed += (_, e) =>
            {
                var point = e.GetPosition(this);
                var t = Math.Clamp((point.X - 215) / Math.Max(1, Bounds.Width - 240), 0, 1);
                var row = (int)((point.Y - 34) / 30);
                if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed && row >= 0 && row < Animation.Tracks.Count)
                {
                    var track = Animation.Tracks[row]; var key = track.Keys.OrderBy(k => Math.Abs(k.Time - t)).FirstOrDefault();
                    if (key is not null && Math.Abs(key.Time - t) < .025) DeleteRequested?.Invoke(track, key.Time);
                }
                else { SeekRequested?.Invoke(t); e.Pointer.Capture(this); }
                e.Handled = true;
            };
            PointerMoved += (_, e) => { if (e.Pointer.Captured == this) SeekRequested?.Invoke(Math.Clamp((e.GetPosition(this).X - 215) / Math.Max(1, Bounds.Width - 240), 0, 1)); };
            PointerReleased += (_, e) => e.Pointer.Capture(null);
        }
        public override void Render(DrawingContext context)
        {
            context.DrawRectangle(Brush.Parse("#181A22"), null, new Rect(Bounds.Size));
            var width = Math.Max(1, Bounds.Width - 240);
            void Text(string text, double x, double y, IBrush? brush = null) => context.DrawText(new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 10, brush ?? Brush.Parse("#858DA2")), new(x, y));
            Text("ANIMATION TRACKS", 16, 10);
            for (var i = 0; i <= 10; i++)
            {
                var x = 215 + i * width / 10; Text($"{i * Animation.DurationSeconds / 10:0.0}s", x, 10);
                context.DrawLine(new Pen(Brush.Parse("#292D38"), 1), new(x, 28), new(x, Bounds.Height));
            }
            if (Animation.Tracks.Count == 0) Text("Select a named layer, then add keyframes. Right-click a key to remove it.", 16, 52);
            for (var i = 0; i < Animation.Tracks.Count; i++)
            {
                var y = 48 + i * 30; var track = Animation.Tracks[i]; Text(track.Target + " · " + track.Property, 16, y - 6);
                context.DrawLine(new Pen(Brush.Parse("#343447"), 2), new(215, y), new(215 + width, y));
                foreach (var key in track.Keys)
                {
                    var x = 215 + key.Time * width;
                    var geometry = new StreamGeometry(); using (var g = geometry.Open()) { g.BeginFigure(new(x, y - 5), true); g.LineTo(new(x + 5, y)); g.LineTo(new(x, y + 5)); g.LineTo(new(x - 5, y)); g.EndFigure(true); }
                    context.DrawGeometry(Brush.Parse("#B8A9F1"), null, geometry);
                }
            }
            var playhead = 215 + Position * width;
            context.DrawLine(new Pen(Brush.Parse("#E1C1FB"), 1.5), new(playhead, 28), new(playhead, Bounds.Height));
        }
    }
}
