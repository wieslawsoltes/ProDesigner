using System.Globalization;
using ProDesigner.Core;
using ProDesigner.Xaml;

namespace ProDesigner.DesignSystems;

public enum AxisAnchor { Start, Center, End, Stretch }
public readonly record struct LayoutBox(double X, double Y, double Width, double Height);
public sealed record LayoutConstraint(double ReferenceWidth, double ReferenceHeight, LayoutBox Bounds,
    AxisAnchor Horizontal, AxisAnchor Vertical, double MinWidth = 0, double MinHeight = 0);

/// <summary>Responsive anchor mathematics and standard Avalonia Grid serialization; no runtime designer dependency.</summary>
public static class ResponsiveLayout
{
    public static LayoutBox Resolve(LayoutConstraint constraint, double width, double height)
    {
        Validate(constraint);
        if (!double.IsFinite(width) || !double.IsFinite(height) || width < 0 || height < 0) throw new ArgumentOutOfRangeException(nameof(width));
        var x = Axis(constraint.Bounds.X, constraint.Bounds.Width, constraint.ReferenceWidth, width, constraint.Horizontal, constraint.MinWidth);
        var y = Axis(constraint.Bounds.Y, constraint.Bounds.Height, constraint.ReferenceHeight, height, constraint.Vertical, constraint.MinHeight);
        return new(x.Position, y.Position, x.Size, y.Size);
    }
    public static IReadOnlyList<TextEdit> Apply(XamlSyntaxTree tree, XamlElement element, LayoutConstraint constraint)
    {
        Validate(constraint);
        if (element.Parent?.LocalName != "Grid") throw new InvalidOperationException("Responsive anchors use a Grid parent. Convert the container to Grid first.");
        if (element.Get("RenderTransform") is not null || element.Get("LayoutTransform") is not null) throw new InvalidOperationException("Remove or resolve transforms before converting layout constraints.");
        var b = constraint.Bounds;
        var horizontal = Margins(b.X, b.Width, constraint.ReferenceWidth, constraint.Horizontal);
        var vertical = Margins(b.Y, b.Height, constraint.ReferenceHeight, constraint.Vertical);
        var values = new Dictionary<string, string?>
        {
            ["Canvas.Left"] = null, ["Canvas.Top"] = null, ["Canvas.Right"] = null, ["Canvas.Bottom"] = null,
            ["HorizontalAlignment"] = constraint.Horizontal switch { AxisAnchor.Start => "Left", AxisAnchor.End => "Right", AxisAnchor.Center => "Center", _ => "Stretch" },
            ["VerticalAlignment"] = constraint.Vertical switch { AxisAnchor.Start => "Top", AxisAnchor.End => "Bottom", AxisAnchor.Center => "Center", _ => "Stretch" },
            ["Width"] = constraint.Horizontal == AxisAnchor.Stretch ? null : Number(b.Width),
            ["Height"] = constraint.Vertical == AxisAnchor.Stretch ? null : Number(b.Height),
            ["Margin"] = string.Join(",", Number(horizontal.Start), Number(vertical.Start), Number(horizontal.End), Number(vertical.End)),
            ["MinWidth"] = Number(constraint.MinWidth), ["MinHeight"] = Number(constraint.MinHeight)
        };
        return values.Select(p => XamlEdits.SetAttribute(tree, element, p.Key, p.Value)).ToArray();
    }
    public static string ConvertCanvasToGrid(XamlSyntaxTree tree, XamlElement canvas, double width, double height)
    {
        if (canvas.LocalName != "Canvas" || canvas.Children.Any(c => c.IsProperty)) throw new InvalidOperationException("Choose a Canvas without property-element children for this conversion.");
        var constraints = canvas.Children.ToDictionary(c => c.Id, c => new LayoutConstraint(width, height,
            new(Parse(c.Get("Canvas.Left"), 0), Parse(c.Get("Canvas.Top"), 0), Parse(c.Get("Width"), double.NaN), Parse(c.Get("Height"), double.NaN)), AxisAnchor.Start, AxisAnchor.Start));
        foreach (var constraint in constraints.Values) Validate(constraint);
        var prefix = canvas.Name.Contains(':') ? canvas.Name[..(canvas.Name.IndexOf(':') + 1)] : "";
        var source = EditApplication.Apply(tree.Source, XamlEdits.RenameType(tree, canvas, prefix + "Grid"));
        var updated = XamlSyntaxTree.Parse(source);
        return EditApplication.Apply(source, constraints.SelectMany(p => Apply(updated, updated.Find(p.Key)!, p.Value)));
    }
    private static (double Position, double Size) Axis(double position, double size, double reference, double available, AxisAnchor anchor, double minimum)
    {
        var delta = available - reference;
        return anchor switch
        {
            AxisAnchor.Start => (position, Math.Max(minimum, size)),
            AxisAnchor.Center => (position + delta / 2, Math.Max(minimum, size)),
            AxisAnchor.End => (position + delta, Math.Max(minimum, size)),
            _ => (position, Math.Max(minimum, size + delta))
        };
    }
    private static (double Start, double End) Margins(double position, double size, double reference, AxisAnchor anchor) => anchor switch
    {
        AxisAnchor.Start => (position, 0), AxisAnchor.End => (0, reference - position - size),
        AxisAnchor.Center => (2 * position + size - reference, 0), _ => (position, reference - position - size)
    };
    private static double Parse(string? value, double fallback) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : fallback;
    private static string Number(double value) => value.ToString("0.########", CultureInfo.InvariantCulture);
    private static void Validate(LayoutConstraint value)
    {
        if (!Enum.IsDefined(value.Horizontal) || !Enum.IsDefined(value.Vertical) ||
            new[] { value.ReferenceWidth, value.ReferenceHeight, value.Bounds.X, value.Bounds.Y, value.Bounds.Width, value.Bounds.Height, value.MinWidth, value.MinHeight }.Any(x => !double.IsFinite(x)) ||
            value.ReferenceWidth <= 0 || value.ReferenceHeight <= 0 || value.Bounds.Width < 0 || value.Bounds.Height < 0 || value.MinWidth < 0 || value.MinHeight < 0)
            throw new ArgumentException("Constraints require finite positions, nonnegative sizes, and positive reference dimensions.");
    }
}
