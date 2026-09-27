using ProDesigner.Core;
using ProDesigner.Xaml;

namespace ProDesigner.Preview;

internal sealed record PreviewPropertyChange(string NodeId, string Property, string Value);

/// <summary>Conservative source equivalence check for updating real controls without rebuilding their visual tree.</summary>
internal static class PreviewPropertyDelta
{
    // Name, Classes, events, namespaces, resources, collections and property removal need a rebuild.
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        "Width", "Height", "MinWidth", "MinHeight", "MaxWidth", "MaxHeight", "Margin", "Padding",
        "HorizontalAlignment", "VerticalAlignment", "Opacity", "IsVisible", "IsEnabled", "ClipToBounds",
        "Background", "Foreground", "FontSize", "FontWeight", "BorderBrush", "BorderThickness", "CornerRadius",
        "Text", "Content", "Watermark", "PlaceholderText", "TextWrapping", "TextAlignment", "AcceptsReturn",
        "HorizontalContentAlignment", "VerticalContentAlignment", "Spacing", "Orientation", "Value", "Minimum", "Maximum",
        "IsChecked", "SelectedIndex", "Canvas.Left", "Canvas.Top", "Grid.Row", "Grid.Column", "Grid.RowSpan", "Grid.ColumnSpan",
        "DockPanel.Dock", "Fill", "Stroke", "StrokeThickness", "RadiusX", "RadiusY", "Data", "Stretch"
    };
    public static IReadOnlyList<PreviewPropertyChange>? Prepare(XamlSyntaxTree before, XamlSyntaxTree after)
    {
        if (ReferenceEquals(before, after)) return [];
        if (before.Elements.Count != after.Elements.Count) return null;
        var edits = new List<TextEdit>(); var changes = new List<PreviewPropertyChange>();
        for (var i = 0; i < before.Elements.Count; i++)
        {
            var old = before.Elements[i]; var node = after.Elements[i];
            if (old.Identity != node.Identity || old.Id != node.Id || old.Name != node.Name || old.Attributes.Count != node.Attributes.Count) return null;
            for (var j = 0; j < old.Attributes.Count; j++)
            {
                var a = old.Attributes[j]; var b = node.Attributes[j];
                if (a.Name != b.Name || a.Quote != b.Quote) return null;
                var raw = after.Source.Substring(b.ValueSpan.Start, b.ValueSpan.Length);
                if (before.Source.AsSpan(a.ValueSpan.Start, a.ValueSpan.Length).SequenceEqual(raw)) continue;
                if (node.IsProperty || !Allowed.Contains(a.Name) || a.Value.StartsWith('{') || b.Value.StartsWith('{')) return null;
                edits.Add(new(a.ValueSpan, raw));
                if (a.Value != b.Value) changes.Add(new(node.Id, b.Name, b.Value));
            }
        }
        // Never infer structural/text equivalence from equal element counts or names alone.
        return EditApplication.Apply(before.Source, edits) == after.Source ? changes : null;
    }
}
