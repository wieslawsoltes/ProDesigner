using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ProDesigner.Core;
using ProDesigner.Preview;
using ProDesigner.Prototyping;
using ProDesigner.Xaml;

namespace ProDesigner.Workbench;

public sealed record PrototypeViewDocument(string Name, string Source);

/// <summary>Embeddable prototype player rendering the actual safe Avalonia preview catalog.</summary>
public sealed class PrototypeRunner : UserControl
{
    private readonly PrototypePlayer _player;
    private readonly IReadOnlyDictionary<string, PrototypeViewDocument> _documents;
    private readonly IReadOnlyDictionary<string, string> _sampleData;
    private readonly Grid _stage = new();
    private readonly TextBlock _title = new() { FontSize = 11 };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(32) };
    private readonly Stopwatch _clock = new();
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11 };
    public PrototypeRunner(PrototypePlayer player, IReadOnlyDictionary<string, PrototypeViewDocument> documents, IReadOnlyDictionary<string, string> sampleData)
    {
        _player = player; _documents = documents; _sampleData = sampleData;
        var body = new Grid { RowDefinitions = RowDefinitions.Parse("Auto,*,Auto"), Height = 540 };
        var header = StudioControls.Row(StudioControls.Button("← Back", "Prototype back", () => _player.Back()), _title);
        StudioControls.Place(body, header, 0, 0); StudioControls.Place(body, _stage, 1, 0); StudioControls.Place(body, _error, 2, 0); Content = body;
        _player.Changed += Changed;
        _timer.Tick += (_, _) => { try { _player.Advance(_clock.Elapsed); } catch (Exception ex) { _timer.Stop(); _error.Text = ex.Message; } };
        AttachedToVisualTree += (_, _) => { _clock.Start(); _timer.Start(); };
        DetachedFromVisualTree += (_, _) => { _clock.Stop(); _timer.Stop(); };
        KeyDown += (_, args) => { if (_player.Dispatch(PrototypeTrigger.Key, key: args.Key.ToString())) args.Handled = true; };
        Changed(player.State);
    }
    private void Changed(PrototypeState state)
    {
        _stage.Children.Clear(); _error.Text = "";
        _title.Text = _documents[state.Document].Name + (state.Overlays.Length == 0 ? "" : " · overlay " + _documents[state.Overlays[^1]].Name);
        _stage.Children.Add(Render(state.Document, state));
        foreach (var overlay in state.Overlays)
        {
            _stage.Children.Add(new Border { Background = Brush.Parse("#66000000") });
            _stage.Children.Add(new Border { Child = Render(overlay, state), Margin = new Thickness(20), BorderThickness = new Thickness(1), BorderBrush = Brush.Parse("#AA99DD"), Background = Brush.Parse("#181B22") });
        }
    }
    private Control Render(string id, PrototypeState state)
    {
        var tree = XamlSyntaxTree.Parse(_documents[id].Source);
        var data = new Dictionary<string, string>(_sampleData); foreach (var pair in state.Variables) data[pair.Key] = pair.Value;
        var preview = new PreviewBuilder { SampleData = data }.Build(tree, new PreviewProfile("Prototype", 960, 620));
        var names = preview.Controls.Where(p => tree.Find(p.Key) is { } node && XamlNames.Name(node) is not null)
            .ToDictionary(p => p.Value, p => XamlNames.Name(tree.Find(p.Key)!)!);
        string? FindName(object? source)
        {
            for (var visual = source as Visual; visual is not null; visual = visual.GetVisualParent())
                if (visual is Control control && names.TryGetValue(control, out var name)) return name;
            return null;
        }
        preview.Root.AddHandler(InputElement.PointerReleasedEvent, (_, e) =>
        {
            if (FindName(e.Source) is { } name && _player.Dispatch(PrototypeTrigger.Click, name)) e.Handled = true;
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        foreach (var pair in names)
        {
            var name = pair.Value;
            pair.Key.PointerEntered += (_, _) => _player.Dispatch(PrototypeTrigger.PointerEnter, name);
        }
        return new Viewbox { Child = preview.Root, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Top };
    }
}

/// <summary>A compact, deterministic visualization of the configured navigation graph.</summary>
public sealed class PrototypeMap(PrototypeGraph graph, IReadOnlyDictionary<string, string> documents) : Control
{
    public override void Render(DrawingContext context)
    {
        base.Render(context); context.DrawRectangle(Brush.Parse("#161821"), null, new Rect(Bounds.Size));
        var columns = Math.Max(1, (int)(Bounds.Width / 175)); var positions = new Dictionary<string, Rect>(); var index = 0;
        foreach (var document in documents)
        {
            var row = index / columns; var column = index++ % columns;
            positions[document.Key] = new Rect(12 + column * 175, 20 + row * 95, 150, 52);
        }
        foreach (var link in graph.Links.Where(l => l.TargetDocument is not null))
        {
            if (!positions.TryGetValue(link.SourceDocument, out var from) || !positions.TryGetValue(link.TargetDocument!, out var to)) continue;
            var a = new Point(from.Right, from.Center.Y); var b = new Point(to.X, to.Center.Y);
            var geometry = new StreamGeometry();
            using (var g = geometry.Open()) { g.BeginFigure(a, false); g.CubicBezierTo(a + new Vector(28, 0), b - new Vector(28, 0), b); g.EndFigure(false); }
            context.DrawGeometry(null, new Pen(Brush.Parse("#7E70A8"), 1.5), geometry);
            context.DrawLine(new Pen(Brush.Parse("#B7A4E9"), 1.5), b, b + new Vector(-6, -4));
            context.DrawLine(new Pen(Brush.Parse("#B7A4E9"), 1.5), b, b + new Vector(-6, 4));
        }
        foreach (var document in documents)
        {
            var rect = positions[document.Key]; var start = graph.StartDocument == document.Key;
            context.DrawRectangle(Brush.Parse(start ? "#3B3258" : "#252936"), new Pen(Brush.Parse(start ? "#C4ABF5" : "#51576A"), 1), rect, 8, 8);
            var label = new FormattedText(document.Value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 11, Brush.Parse("#DDD7ED"));
            context.DrawText(label, new Point(rect.X + 10, rect.Y + 17));
        }
    }
}
