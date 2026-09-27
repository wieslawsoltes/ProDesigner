using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ProDesigner.Workbench;

/// <summary>Resolution-independent workbench iconography; no symbol-font dependency.</summary>
public sealed class StudioIcon : Control
{
    private static readonly IReadOnlyDictionary<string, Geometry> Paths = new Dictionary<string, string>
    {
        ["select"] = "M3,2 L13,9 L8,10 L6,15 Z M8,10 L12,15",
        ["plus"] = "M8,2 L8,14 M2,8 L14,8",
        ["minus"] = "M2,8 L14,8",
        ["text"] = "M2,3 L14,3 M8,3 L8,14 M5,14 L11,14",
        ["rectangle"] = "M2,3 L14,3 L14,13 L2,13 Z",
        ["ellipse"] = "M8,2 A6,6 0 1 1 7.999,2 Z",
        ["undo"] = "M6,3 L2,7 L6,11 M2,7 L10,7 Q14,7 14,12",
        ["redo"] = "M10,3 L14,7 L10,11 M14,7 L6,7 Q2,7 2,12",
        ["align"] = "M2,1 L2,15 M5,3 L14,3 L14,6 L5,6 Z M5,10 L11,10 L11,13 L5,13 Z",
        ["horizontal"] = "M1,2 L1,14 M15,2 L15,14 M5,5 L3,8 L5,11 M3,8 L13,8 M11,5 L13,8 L11,11",
        ["vertical"] = "M2,1 L14,1 M2,15 L14,15 M5,5 L8,3 L11,5 M8,3 L8,13 M5,11 L8,13 L11,11",
        ["play"] = "M4,2 L14,8 L4,14 Z",
        ["stop"] = "M3,3 L13,3 L13,13 L3,13 Z",
        ["diamond"] = "M8,1 L15,8 L8,15 L1,8 Z",
        ["view"] = "M2,2 L14,2 L14,14 L2,14 Z M2,5 L14,5 M5,5 L5,14",
        ["chevron"] = "M3,6 L8,11 L13,6",
        ["reset"] = "M3,6 A5.5,5.5 0 1 1 3,11 M3,2 L3,6 L7,6",
        ["close"] = "M3,3 L13,13 M13,3 L3,13",
        ["clock"] = "M8,2 A6,6 0 1 1 7.999,2 Z M8,4 L8,8 L11,10",
        ["grid"] = "M2,2 L14,2 L14,14 L2,14 Z M2,8 L14,8 M8,2 L8,14",
        ["check"] = "M2,8 L6,12 L14,3",
        ["input"] = "M2,4 L14,4 L14,12 L2,12 Z M5,6 L5,10",
        ["list"] = "M2,3 L3,3 M6,3 L14,3 M2,8 L3,8 M6,8 L14,8 M2,13 L3,13 M6,13 L14,13",
        ["toggle"] = "M5,4 L11,4 A4,4 0 0 1 11,12 L5,12 A4,4 0 0 1 5,4 Z M11,6 L11,10"
    }.ToDictionary(p => p.Key, p => Geometry.Parse(p.Value));
    private static readonly IReadOnlyDictionary<string, string> Symbols = new Dictionary<string, string>
    {
        ["↖"] = "select", ["＋"] = "plus", ["+"] = "plus", ["−"] = "minus", ["T"] = "text", ["□"] = "rectangle", ["○"] = "ellipse",
        ["↶"] = "undo", ["↷"] = "redo", ["⫷"] = "align", ["↔"] = "horizontal", ["⇅"] = "vertical", ["▶"] = "play", ["■"] = "stop",
        ["◇"] = "diamond", ["◈"] = "diamond", ["▧"] = "view", ["⌄"] = "chevron", ["↺"] = "reset", ["×"] = "close", ["◷"] = "clock",
        ["▦"] = "grid", ["⊞"] = "grid", ["⊟"] = "view", ["☑"] = "check", ["◉"] = "ellipse", ["◐"] = "toggle", ["I"] = "input",
        ["☰"] = "list", ["▤"] = "list", ["≡"] = "list", ["↕"] = "vertical", ["▭"] = "rectangle", ["▰"] = "rectangle", ["⎯"] = "minus", ["—"] = "minus"
    };
    public string Kind { get; set; } = "diamond";
    public IBrush Stroke { get; set; } = Brush.Parse("#B9B3CC");
    public StudioIcon() { Width = 16; Height = 16; IsHitTestVisible = false; }
    public override void Render(DrawingContext context)
    {
        if (!Paths.TryGetValue(Kind, out var geometry)) return;
        using (context.PushTransform(Matrix.CreateScale(Bounds.Width / 16, Bounds.Height / 16)))
            context.DrawGeometry(null, new Pen(Stroke, 1.35, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geometry);
    }
    internal static (string? Icon, string Label) SplitLabel(string label)
    {
        var text = label.TrimStart();
        var first = text.Split(' ', 2)[0];
        if (!Symbols.TryGetValue(first, out var icon)) return (null, label);
        text = text[first.Length..].TrimStart();
        var next = text.Split(' ', 2)[0];
        if (Symbols.TryGetValue(next, out var second)) { icon = second; text = text[next.Length..].TrimStart(); }
        return (icon, text);
    }
}
