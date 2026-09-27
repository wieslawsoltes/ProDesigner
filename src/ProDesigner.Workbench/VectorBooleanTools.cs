using Avalonia.Controls;
using ProDesigner.Authoring;
using ProDesigner.Core;
using ProDesigner.Design;
using ProDesigner.Geometry;
using ProDesigner.Xaml;
using static ProDesigner.Workbench.StudioControls;

namespace ProDesigner.Workbench;

public sealed partial class DesignerWorkbench
{
    public void CombineSelectedPaths(PathBooleanOperation operation)
    {
        _editor.Flush();
        var paths = Session.SelectedRoots().OrderBy(n => n.Span.Start).ToArray();
        if (paths.Length < 2 || paths.Any(n => n.LocalName != "Path") || paths.Select(n => n.Parent?.Id).Distinct().Count() != 1 || paths[0].Parent?.LocalName != "Canvas")
            throw new InvalidOperationException("Select at least two sibling Path controls inside one Canvas.");
        foreach (var path in paths) ValidatePathOperand(path);
        foreach (var path in paths.Skip(1))
        {
            if (XamlNames.Name(path) is not { } name) continue;
            foreach (var node in Session.Tree.Elements.Except(paths.SelectMany(n => n.DescendantsAndSelf())))
                if (node.Attributes.Any(a => XamlNames.RewriteReference(a.Name, a.Value, name, "_removed_") != a.Value))
                    throw new InvalidOperationException($"'{name}' is referenced outside the operation. Relink its references before combining.");
        }
        var data = paths.Select(path => VectorPathOperations.Transform(VectorPathModel.Parse(path.Get("Data") ?? throw new InvalidOperationException("A path needs scalar Data.")),
            new VectorTransform(1, 0, 0, 1, Scalar(path, "Canvas.Left", 0), Scalar(path, "Canvas.Top", 0))).ToData()).ToArray();
        var combined = PathGeometryEngine.Combine(data, operation);
        var first = paths[0];
        var edits = paths.Skip(1).Select(path => XamlEdits.Delete(Session.Tree, path)).ToList();
        edits.Add(XamlEdits.SetAttribute(Session.Tree, first, "Data", combined));
        foreach (var value in new Dictionary<string, string?> { ["Canvas.Left"] = "0", ["Canvas.Top"] = "0", ["Width"] = null, ["Height"] = null, ["Stretch"] = "None" })
            edits.Add(XamlEdits.SetAttribute(Session.Tree, first, value.Key, value.Value));
        Session.Apply(operation + " paths", edits); Session.Select(Session.Tree.At(first.Span.Start)?.Id); RefreshDocument();
    }
    public void OutlineSelectedStroke()
    {
        _editor.Flush(); var path = Session.Primary ?? throw new InvalidOperationException("Select a stroked Path.");
        if (path.LocalName != "Path") throw new InvalidOperationException("Select a Path control.");
        ValidatePathOperand(path);
        if (path.Get("Stroke") is not { } stroke || path.Get("StrokeDashArray") is not null) throw new InvalidOperationException("Stroke outlining currently requires a solid, non-dashed stroke.");
        var cap = Enum.TryParse<StrokeCap>(path.Get("StrokeLineCap"), out var parsedCap) ? parsedCap : StrokeCap.Butt;
        var join = Enum.TryParse<StrokeJoin>(path.Get("StrokeJoin"), out var parsedJoin) ? parsedJoin : StrokeJoin.Miter;
        var result = PathGeometryEngine.Outline(path.Get("Data")!, Scalar(path, "StrokeThickness", 1), cap, join, Scalar(path, "StrokeMiterLimit", 10));
        Session.Apply("Outline stroke", [XamlEdits.SetAttribute(Session.Tree, path, "Data", result), XamlEdits.SetAttribute(Session.Tree, path, "Fill", stroke), XamlEdits.SetAttribute(Session.Tree, path, "Stroke", null)]);
        RefreshDocument();
    }
    private void ValidatePathOperand(XamlElement path)
    {
        if (_designSystem.Instances.Any(i => i.DocumentId == DocumentId(_active) && i.RootName == XamlNames.Name(path)))
            throw new InvalidOperationException("Detach linked component instances before changing their structure.");
        if (path.Children.Count > 0 || path.Get("Stretch") is { } stretch && stretch != "None" || path.Get("Margin") is { } margin && Avalonia.Thickness.Parse(margin) != default ||
            path.Get("Canvas.Right") is not null || path.Get("Canvas.Bottom") is not null || path.Get("RenderTransform") is not null || path.Get("LayoutTransform") is not null)
            throw new InvalidOperationException("Normalize path transforms, stretching, margins and property elements before this geometry operation.");
        _ = VectorPathModel.Parse(path.Get("Data") ?? throw new InvalidOperationException("Path Data is missing."));
    }
    private static double Scalar(XamlElement node, string property, double fallback)
    {
        var text = node.Get(property); if (text is null) return fallback;
        var result = LayoutEngine.Number(text, double.NaN);
        return double.IsFinite(result) ? result : throw new InvalidOperationException($"Resolve the '{property}' binding before editing geometry.");
    }
    private Control BooleanTools() => Row(
        Button("Union", "Union selected sibling paths", () => Guard(() => CombineSelectedPaths(PathBooleanOperation.Union))),
        Button("Subtract", "Subtract later source-order paths from the first selected path", () => Guard(() => CombineSelectedPaths(PathBooleanOperation.Difference))),
        Button("Intersect", "Intersect selected sibling paths", () => Guard(() => CombineSelectedPaths(PathBooleanOperation.Intersect))),
        Button("Xor", "Exclusive union of selected sibling paths", () => Guard(() => CombineSelectedPaths(PathBooleanOperation.Xor))),
        Button("Outline stroke", "Convert the selected solid stroke to a filled path", () => Guard(OutlineSelectedStroke)));
}
