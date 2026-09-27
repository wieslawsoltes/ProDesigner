using Avalonia.Controls;
using ProDesigner.Core;
using ProDesigner.Design;
using ProDesigner.DesignSystems;
using ProDesigner.Xaml;
using static ProDesigner.Workbench.StudioControls;

namespace ProDesigner.Workbench;

public sealed partial class DesignerWorkbench
{
    public void ApplyAnchors(AxisAnchor horizontal, AxisAnchor vertical)
    {
        _editor.Flush(); var node = Session.Primary ?? throw new InvalidOperationException("Select a control in a single-cell Grid.");
        var parent = node.Parent ?? throw new InvalidOperationException("The root has no parent constraints.");
        if (parent.Get("ColumnDefinitions") is { } columns && columns.Contains(',') || parent.Get("RowDefinitions") is { } rows && rows.Contains(',')) throw new InvalidOperationException("Anchor conversion currently targets a single-cell Grid. Existing row/column layouts remain unchanged.");
        var bounds = _surface.BoundsMap;
        if (!bounds.TryGetValue(node.Id, out var child) || !bounds.TryGetValue(parent.Id, out var container)) throw new InvalidOperationException("Wait for a measured preview before applying constraints.");
        var value = new LayoutConstraint(container.Width, container.Height, new(child.X - container.X, child.Y - container.Y, child.Width, child.Height), horizontal, vertical,
            LayoutEngine.Number(node.Get("MinWidth")), LayoutEngine.Number(node.Get("MinHeight")));
        Session.Apply("Apply responsive anchors", ResponsiveLayout.Apply(Session.Tree, node, value)); RefreshDocument();
    }
    public void ConvertSelectedCanvasToGrid()
    {
        _editor.Flush(); var canvas = Session.Primary ?? throw new InvalidOperationException("Select a Canvas.");
        if (canvas.LocalName != "Canvas") throw new InvalidOperationException("Select the Canvas container, not one of its children.");
        var bounds = _surface.BoundsMap;
        if (!bounds.TryGetValue(canvas.Id, out var container)) throw new InvalidOperationException("The Canvas has not been measured.");
        var edits = new List<TextEdit>();
        foreach (var child in canvas.Children.Where(c => !c.IsProperty))
        {
            if (!bounds.TryGetValue(child.Id, out var rect)) throw new InvalidOperationException("A child has no preview bounds.");
            foreach (var value in new Dictionary<string, string> { ["Width"] = LayoutEngine.Format(rect.Width), ["Height"] = LayoutEngine.Format(rect.Height), ["Canvas.Left"] = LayoutEngine.Format(rect.X - container.X), ["Canvas.Top"] = LayoutEngine.Format(rect.Y - container.Y) })
                edits.Add(XamlEdits.SetAttribute(Session.Tree, child, value.Key, value.Value));
        }
        var measured = XamlSyntaxTree.Parse(EditApplication.Apply(Session.Source, edits));
        var source = ResponsiveLayout.ConvertCanvasToGrid(measured, measured.Find(canvas.Id)!, container.Width, container.Height);
        Session.SetSource(source, "Convert Canvas to responsive Grid"); RefreshDocument();
    }
    private void ShowConstraintTools()
    {
        var horizontal = new ComboBox { ItemsSource = Enum.GetNames<AxisAnchor>(), SelectedIndex = 0 };
        var vertical = new ComboBox { ItemsSource = Enum.GetNames<AxisAnchor>(), SelectedIndex = 0 };
        ShowDialog("Responsive constraints", Column(Caption("HORIZONTAL ANCHOR"), horizontal, Caption("VERTICAL ANCHOR"), vertical,
            new TextBlock { Text = "Start and End pin to an edge. Center preserves the center offset. Stretch preserves both edge distances. Results are ordinary Grid alignment, margin and size properties.", TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 12 },
            Button("Apply to selection", "Convert the measured selection to responsive Grid anchors", () => Guard(() => { ApplyAnchors(Enum.Parse<AxisAnchor>(horizontal.SelectedItem!.ToString()!), Enum.Parse<AxisAnchor>(vertical.SelectedItem!.ToString()!)); _overlay.IsVisible = false; }), true),
            Button("Convert selected Canvas to Grid", "Preserve the measured child positions and switch to a responsive Grid container", () => Guard(() => { ConvertSelectedCanvasToGrid(); _overlay.IsVisible = false; }))));
    }
}
