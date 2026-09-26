using System.Globalization;
using ProDesigner.Core;
using ProDesigner.Xaml;

namespace ProDesigner.Design;

public readonly record struct DesignRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public bool Intersects(DesignRect other) => X < other.Right && Right > other.X && Y < other.Bottom && Bottom > other.Y;
    public static DesignRect Union(IEnumerable<DesignRect> rectangles)
    {
        var all = rectangles.ToArray();
        if (all.Length == 0) return default;
        var x = all.Min(r => r.X); var y = all.Min(r => r.Y);
        return new(x, y, all.Max(r => r.Right) - x, all.Max(r => r.Bottom) - y);
    }
}
public enum Alignment { Left, HorizontalCenter, Right, Top, VerticalCenter, Bottom }
public sealed record SnapResult(double X, double Y, IReadOnlyList<double> VerticalGuides, IReadOnlyList<double> HorizontalGuides);
public static class LayoutEngine
{
    public static double Snap(double value, double grid = 8) => grid > 0 ? Math.Round(value / grid, MidpointRounding.AwayFromZero) * grid : value;
    public static SnapResult SnapToObjects(DesignRect moving, IEnumerable<DesignRect> objects, double threshold = 6, double grid = 8)
    {
        var x = Snap(moving.X, grid); var y = Snap(moving.Y, grid);
        double? guideX = null, guideY = null;
        var bestX = threshold; var bestY = threshold;
        foreach (var target in objects)
        {
            foreach (var tx in new[] { target.X, target.X + target.Width / 2, target.Right })
                foreach (var mx in new[] { moving.X, moving.X + moving.Width / 2, moving.Right })
                    if (Math.Abs(tx - mx) < bestX) { bestX = Math.Abs(tx - mx); x = moving.X + tx - mx; guideX = tx; }
            foreach (var ty in new[] { target.Y, target.Y + target.Height / 2, target.Bottom })
                foreach (var my in new[] { moving.Y, moving.Y + moving.Height / 2, moving.Bottom })
                    if (Math.Abs(ty - my) < bestY) { bestY = Math.Abs(ty - my); y = moving.Y + ty - my; guideY = ty; }
        }
        return new(x, y, guideX is { } gx ? [gx] : [], guideY is { } gy ? [gy] : []);
    }
    public static IReadOnlyList<TextEdit> Move(XamlSyntaxTree tree, XamlElement element, double dx, double dy, bool snap = true)
    {
        if (element.Parent?.LocalName != "Canvas")
            throw new InvalidOperationException("Free movement is available inside Canvas. Use layout properties for Grid and StackPanel children.");
        var x = Number(element.Get("Canvas.Left")) + dx;
        var y = Number(element.Get("Canvas.Top")) + dy;
        if (snap) { x = Snap(x); y = Snap(y); }
        return [XamlEdits.SetAttribute(tree, element, "Canvas.Left", Format(x)), XamlEdits.SetAttribute(tree, element, "Canvas.Top", Format(y))];
    }
    public static IReadOnlyList<TextEdit> Align(XamlSyntaxTree tree, IReadOnlyDictionary<string, DesignRect> bounds, IEnumerable<string> ids, Alignment alignment)
    {
        var nodes = ids.Select(tree.Find).OfType<XamlElement>().Where(n => bounds.ContainsKey(n.Id)).ToArray();
        if (nodes.Length < 2) return [];
        if (nodes.Select(n => n.Parent?.Id).Distinct().Count() != 1 || nodes[0].Parent?.LocalName != "Canvas")
            throw new InvalidOperationException("Alignment requires siblings inside the same Canvas.");
        var union = DesignRect.Union(nodes.Select(n => bounds[n.Id]));
        var edits = new List<TextEdit>();
        foreach (var node in nodes)
        {
            var rect = bounds[node.Id];
            var horizontal = alignment is Alignment.Left or Alignment.HorizontalCenter or Alignment.Right;
            var value = alignment switch
            {
                Alignment.Left => union.X, Alignment.HorizontalCenter => union.X + (union.Width - rect.Width) / 2,
                Alignment.Right => union.Right - rect.Width, Alignment.Top => union.Y,
                Alignment.VerticalCenter => union.Y + (union.Height - rect.Height) / 2, _ => union.Bottom - rect.Height
            };
            edits.Add(XamlEdits.SetAttribute(tree, node, horizontal ? "Canvas.Left" : "Canvas.Top", Format(value)));
        }
        return edits;
    }
    public static IReadOnlyList<TextEdit> Distribute(XamlSyntaxTree tree, IReadOnlyDictionary<string, DesignRect> bounds, IEnumerable<string> ids, bool horizontal)
    {
        var nodes = ids.Select(tree.Find).OfType<XamlElement>().Where(n => bounds.ContainsKey(n.Id)).OrderBy(n => horizontal ? bounds[n.Id].X : bounds[n.Id].Y).ToArray();
        if (nodes.Length < 3) return [];
        if (nodes.Select(n => n.Parent?.Id).Distinct().Count() != 1 || nodes[0].Parent?.LocalName != "Canvas") throw new InvalidOperationException("Distribution requires Canvas siblings.");
        var first = bounds[nodes[0].Id]; var last = bounds[nodes[^1].Id];
        var total = nodes.Sum(n => horizontal ? bounds[n.Id].Width : bounds[n.Id].Height);
        var start = horizontal ? first.X : first.Y;
        var gap = ((horizontal ? last.Right : last.Bottom) - start - total) / (nodes.Length - 1);
        var position = start; var edits = new List<TextEdit>();
        foreach (var node in nodes)
        {
            edits.Add(XamlEdits.SetAttribute(tree, node, horizontal ? "Canvas.Left" : "Canvas.Top", Format(position)));
            position += (horizontal ? bounds[node.Id].Width : bounds[node.Id].Height) + gap;
        }
        return edits;
    }
    public static double Number(string? value, double fallback = 0) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) ? n : fallback;
    public static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
