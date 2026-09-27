using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using ProDesigner.Core;
using ProDesigner.Design;
using ProDesigner.Xaml;

namespace ProDesigner.Preview;

public sealed record PreviewResult(Control Root, IReadOnlyDictionary<string, Control> Controls, IReadOnlyList<DesignDiagnostic> Diagnostics);

/// <summary>Bounded, non-executing preview for built-in Avalonia controls. Unknown syntax is retained in the source, never guessed away.</summary>
public sealed class PreviewBuilder
{
    private readonly Dictionary<string, Control> _controls = [];
    private readonly List<DesignDiagnostic> _diagnostics = [];
    private readonly Dictionary<string, string> _resources = [];
    public IReadOnlyDictionary<string, string> SampleData { get; init; } = new Dictionary<string, string>
    {
        ["Title"] = "Your next great interface", ["User.Name"] = "Alex Morgan", ["Description"] = "Live sample data", ["Count"] = "128"
    };
    public PreviewResult Build(XamlSyntaxTree tree, PreviewProfile profile)
    {
        _controls.Clear(); _diagnostics.Clear(); _resources.Clear();
        if (tree.Elements.Count > 10000)
            return new(new TextBlock { Text = "Safe preview limit reached (10,000 syntax nodes). Source remains editable." }, new Dictionary<string, Control>(), [new("PREVIEW010", "This document exceeds the safe preview control budget.", DiagnosticSeverity.Warning)]);
        foreach (var resource in tree.Elements.Where(e => e.Get("x:Key") is not null))
        {
            var key = resource.Get("x:Key")!;
            var value = resource.Get("Color") ?? resource.Get("Value");
            if (value is null && !resource.SelfClosing && resource.Children.Count == 0)
                value = tree.Source[resource.OpenSpan.End..resource.CloseSpan.Start].Trim();
            if (value is not null) _resources[key] = value;
        }
        var root = BuildNode(tree.Root);
        root.Width = profile.Width; root.Height = profile.Height;
        var host = new Border
        {
            Width = profile.Width, Height = profile.Height, Background = Brush.Parse(profile.Dark ? "#15171E" : "#FFFFFF"),
            Child = new ThemeVariantScope { RequestedThemeVariant = profile.Dark ? ThemeVariant.Dark : ThemeVariant.Light, Child = root },
            ClipToBounds = true
        };
        return new(host, new Dictionary<string, Control>(_controls), _diagnostics.ToArray());
    }
    private Control BuildNode(XamlElement node)
    {
        var name = IsAvaloniaNamespace(node) ? node.LocalName : "Unsupported";
        Control control = name switch
        {
            "Window" or "UserControl" => new UserControl(), "Grid" => new Grid(), "Canvas" => new Canvas(),
            "StackPanel" => new StackPanel(), "DockPanel" => new DockPanel(), "WrapPanel" => new WrapPanel(),
            "Border" => new Border(), "ScrollViewer" => new ScrollViewer(), "Viewbox" => new Viewbox(),
            "TextBlock" => new TextBlock(), "Button" => new Button(), "TextBox" => new TextBox(),
            "CheckBox" => new CheckBox(), "RadioButton" => new RadioButton(), "ToggleSwitch" => new ToggleSwitch(),
            "Slider" => new Slider(), "ProgressBar" => new ProgressBar(), "ComboBox" => new ComboBox(),
            "ComboBoxItem" => new ComboBoxItem(), "ListBox" => new ListBox(), "ListBoxItem" => new ListBoxItem(),
            "Path" => new Avalonia.Controls.Shapes.Path(), "Rectangle" => new Rectangle(), "Ellipse" => new Ellipse(), "Separator" => new Separator(),
            "ContentControl" => new ContentControl(), "ItemsControl" => new ItemsControl(),
            _ => Unsupported(node)
        };
        _controls[node.Id] = control;
        foreach (var attribute in node.Attributes)
        {
            if (attribute.Name == "xmlns" || attribute.Name.StartsWith("xmlns:", StringComparison.Ordinal) || attribute.Name is "x:Class" or "x:DataType" or "x:CompileBindings" or "x:Key" || attribute.Name.StartsWith("d:", StringComparison.Ordinal) || attribute.Name.StartsWith("mc:", StringComparison.Ordinal)) continue;
            try
            {
                var value = Resolve(attribute.Value, node);
                if (value is null) continue;
                if (!ApplyProperty(control, attribute.Name, value)) Warn(node, "PREVIEW002", $"'{attribute.Name}' is preserved but is not interpreted by the safe preview.");
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
            { Warn(node, "PREVIEW003", $"{node.LocalName}.{attribute.Name}: {ex.Message}"); }
        }
        var children = new List<XamlElement>();
        foreach (var child in node.Children)
        {
            if (!child.IsProperty) { children.Add(child); continue; }
            var member = child.LocalName[(child.LocalName.IndexOf('.') + 1)..];
            try
            {
                if (member is "Background" or "Foreground" or "BorderBrush" or "Fill" or "Stroke" && child.Children.Count == 1)
                {
                    var brush = ParseBrush(child.Children[0]);
                    if (brush is null || !ApplyBrush(control, member, brush)) Warn(child, "PREVIEW004", "Unsupported brush object.");
                }
                else if (member is "Children" or "Child" or "Content" or "Items") children.AddRange(child.Children);
                else if (member == "RowDefinitions" && control is Grid rowGrid)
                    foreach (var row in child.Children) rowGrid.RowDefinitions.Add(new RowDefinition(GridLength.Parse(row.Get("Height") ?? "*")));
                else if (member == "ColumnDefinitions" && control is Grid columnGrid)
                    foreach (var column in child.Children) columnGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Parse(column.Get("Width") ?? "*")));
                else if (member is not "Resources") Warn(child, "PREVIEW004", $"{child.LocalName} is retained. Use trusted runtime preview for styles, templates, and animations.");
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
            { Warn(child, "PREVIEW003", ex.Message); }
        }
        foreach (var childNode in children)
        {
            var child = BuildNode(childNode);
            switch (control)
            {
                case Panel panel: panel.Children.Add(child); break;
                case Decorator decorator when decorator.Child is null: decorator.Child = child; break;
                case ContentControl content when content.Content is null: content.Content = child; break;
                case ItemsControl items: items.Items.Add(child); break;
                default: Warn(childNode, "PREVIEW005", $"{node.LocalName} cannot host this additional child in safe preview."); break;
            }
        }
        return control;
    }
    private string? Resolve(string value, XamlElement node)
    {
        if (value.StartsWith("{}", StringComparison.Ordinal)) return value[2..];
        if (!value.StartsWith('{')) return value;
        foreach (var prefix in new[] { "{StaticResource ", "{DynamicResource " })
            if (value.StartsWith(prefix, StringComparison.Ordinal) && value.EndsWith('}'))
            {
                var key = value[prefix.Length..^1].Trim();
                if (_resources.TryGetValue(key, out var resource)) return resource;
                Warn(node, "PREVIEW006", $"Resource '{key}' is not available in this document's safe preview."); return null;
            }
        if (value.StartsWith("{Binding ", StringComparison.Ordinal) && value.EndsWith('}'))
        {
            var path = value[9..^1].Split(',')[0].Trim();
            if (path.StartsWith("Path=", StringComparison.Ordinal)) path = path[5..];
            if (SampleData.TryGetValue(path, out var data)) return data;
            Warn(node, "PREVIEW007", $"No sample data for binding '{path}'."); return null;
        }
        Warn(node, "PREVIEW008", "This markup extension requires the trusted runtime preview."); return null;
    }
    private static bool IsAvaloniaNamespace(XamlElement node)
    {
        var split = node.Name.IndexOf(':');
        var attribute = split < 0 ? "xmlns" : "xmlns:" + node.Name[..split];
        for (var current = node; current is not null; current = current.Parent)
            if (current.Get(attribute) is { } value) return value == "https://github.com/avaloniaui";
        return split < 0;
    }
    private Control Unsupported(XamlElement node)
    {
        Warn(node, "PREVIEW001", $"'{node.Name}' is not in the safe control catalog. Its XAML remains editable and unchanged.");
        return new Border { MinWidth = 140, MinHeight = 64, BorderBrush = Brush.Parse("#C89B49"), BorderThickness = new(1), Padding = new(12),
            Child = new TextBlock { Text = node.Name + "\nRuntime preview required", FontSize = 11, Foreground = Brush.Parse("#C89B49") } };
    }
    private void Warn(XamlElement node, string code, string message) => _diagnostics.Add(new(code, message, DiagnosticSeverity.Warning, node.Span.Start, node.Span.Length));
    private static IBrush? ParseBrush(XamlElement node)
    {
        if (node.LocalName == "SolidColorBrush") return Brush.Parse(node.Get("Color") ?? "Transparent");
        if (node.LocalName == "LinearGradientBrush")
        {
            var brush = new LinearGradientBrush { StartPoint = RelativePoint.Parse(node.Get("StartPoint") ?? "0%,0%"), EndPoint = RelativePoint.Parse(node.Get("EndPoint") ?? "100%,0%") };
            foreach (var stop in node.Children.Where(n => n.LocalName == "GradientStop")) brush.GradientStops.Add(new GradientStop(Color.Parse(stop.Get("Color") ?? "Transparent"), D(stop.Get("Offset") ?? "0")));
            return brush;
        }
        return null;
    }
    private static bool ApplyBrush(Control control, string property, IBrush brush)
    {
        switch (property)
        {
            case "Background" when control is Border border: border.Background = brush; return true;
            case "Background" when control is Panel panel: panel.Background = brush; return true;
            case "Background" when control is TemplatedControl templated: templated.Background = brush; return true;
            case "Foreground" when control is TemplatedControl templated: templated.Foreground = brush; return true;
            case "Foreground" when control is TextBlock text: text.Foreground = brush; return true;
            case "BorderBrush" when control is Border border: border.BorderBrush = brush; return true;
            case "BorderBrush" when control is TemplatedControl templated: templated.BorderBrush = brush; return true;
            case "Fill" when control is Shape shape: shape.Fill = brush; return true;
            case "Stroke" when control is Shape shape: shape.Stroke = brush; return true;
            default: return false;
        }
    }
    private static T Transform<T>(Control control) where T : Transform, new()
    {
        if (control.RenderTransform is not TransformGroup group)
        {
            group = new TransformGroup(); if (control.RenderTransform is Transform previous) group.Children.Add(previous); control.RenderTransform = group;
        }
        var result = group.Children.OfType<T>().FirstOrDefault();
        if (result is null) { result = new T(); group.Children.Add(result); } return result;
    }
    private static double D(string value) => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
    private static T E<T>(string value) where T : struct, Enum => Enum.Parse<T>(value, true);
    public static bool ApplyProperty(Control c, string property, string value)
    {
        switch (property)
        {
            case "Name": case "x:Name": c.Name = value; return true;
            case "Width": c.Width = D(value); return true;
            case "Height": c.Height = D(value); return true;
            case "MinWidth": c.MinWidth = D(value); return true;
            case "MinHeight": c.MinHeight = D(value); return true;
            case "MaxWidth": c.MaxWidth = D(value); return true;
            case "MaxHeight": c.MaxHeight = D(value); return true;
            case "Margin": c.Margin = Thickness.Parse(value); return true;
            case "HorizontalAlignment": c.HorizontalAlignment = E<HorizontalAlignment>(value); return true;
            case "VerticalAlignment": c.VerticalAlignment = E<VerticalAlignment>(value); return true;
            case "Opacity": c.Opacity = D(value); return true;
            case "IsVisible": c.IsVisible = bool.Parse(value); return true;
            case "IsEnabled": c.IsEnabled = bool.Parse(value); return true;
            case "ClipToBounds": c.ClipToBounds = bool.Parse(value); return true;
            case "Classes": foreach (var cls in value.Split(' ', StringSplitOptions.RemoveEmptyEntries)) c.Classes.Add(cls); return true;
            case "ToolTip.Tip": ToolTip.SetTip(c, value); return true;
            case "Grid.Row": Grid.SetRow(c, int.Parse(value, CultureInfo.InvariantCulture)); return true;
            case "Grid.Column": Grid.SetColumn(c, int.Parse(value, CultureInfo.InvariantCulture)); return true;
            case "Grid.RowSpan": Grid.SetRowSpan(c, int.Parse(value, CultureInfo.InvariantCulture)); return true;
            case "Grid.ColumnSpan": Grid.SetColumnSpan(c, int.Parse(value, CultureInfo.InvariantCulture)); return true;
            case "Canvas.Left": Canvas.SetLeft(c, D(value)); return true;
            case "Canvas.Top": Canvas.SetTop(c, D(value)); return true;
            case "DockPanel.Dock": DockPanel.SetDock(c, E<Dock>(value)); return true;
            case "Background":
                if (c is Border bb) bb.Background = Brush.Parse(value);
                else if (c is Panel bp) bp.Background = Brush.Parse(value);
                else if (c is TemplatedControl bt) bt.Background = Brush.Parse(value);
                else if (c is TextBlock btxt) btxt.Background = Brush.Parse(value);
                else return false;
                return true;
            case "Foreground":
                if (c is TextBlock ft) ft.Foreground = Brush.Parse(value); else if (c is TemplatedControl fc) fc.Foreground = Brush.Parse(value); else return false; return true;
            case "FontSize":
                if (c is TextBlock st) st.FontSize = D(value); else if (c is TemplatedControl sc) sc.FontSize = D(value); else return false; return true;
            case "FontWeight":
                var weight = value.Equals("Bold", StringComparison.OrdinalIgnoreCase) ? FontWeight.Bold : value.Equals("SemiBold", StringComparison.OrdinalIgnoreCase) ? FontWeight.SemiBold : FontWeight.Normal;
                if (c is TextBlock wt) wt.FontWeight = weight; else if (c is TemplatedControl wc) wc.FontWeight = weight; else return false; return true;
            case "Padding":
                if (c is Border pb) pb.Padding = Thickness.Parse(value); else if (c is TemplatedControl pc) pc.Padding = Thickness.Parse(value); else return false; return true;
            case "BorderBrush":
                if (c is Border brb) brb.BorderBrush = Brush.Parse(value); else if (c is TemplatedControl brc) brc.BorderBrush = Brush.Parse(value); else return false; return true;
            case "BorderThickness":
                if (c is Border btb) btb.BorderThickness = Thickness.Parse(value); else if (c is TemplatedControl btc) btc.BorderThickness = Thickness.Parse(value); else return false; return true;
            case "CornerRadius":
                if (c is Border crb) crb.CornerRadius = CornerRadius.Parse(value); else if (c is TemplatedControl crc) crc.CornerRadius = CornerRadius.Parse(value); else return false; return true;
            case "Text" when c is TextBlock text: text.Text = value; return true;
            case "Text" when c is TextBox input: input.Text = value; return true;
            case "Watermark" or "PlaceholderText" when c is TextBox input: input.PlaceholderText = value; return true;
            case "AcceptsReturn" when c is TextBox input: input.AcceptsReturn = bool.Parse(value); return true;
            case "TextWrapping" when c is TextBlock text: text.TextWrapping = E<TextWrapping>(value); return true;
            case "TextAlignment" when c is TextBlock text: text.TextAlignment = E<TextAlignment>(value); return true;
            case "Content" when c is ContentControl content: content.Content = value; return true;
            case "HorizontalContentAlignment" when c is ContentControl content: content.HorizontalContentAlignment = E<HorizontalAlignment>(value); return true;
            case "VerticalContentAlignment" when c is ContentControl content: content.VerticalContentAlignment = E<VerticalAlignment>(value); return true;
            case "Spacing" when c is StackPanel stack: stack.Spacing = D(value); return true;
            case "Orientation" when c is StackPanel stack: stack.Orientation = E<Orientation>(value); return true;
            case "Orientation" when c is WrapPanel wrap: wrap.Orientation = E<Orientation>(value); return true;
            case "RowDefinitions" when c is Grid grid: grid.RowDefinitions = RowDefinitions.Parse(value); return true;
            case "ColumnDefinitions" when c is Grid grid: grid.ColumnDefinitions = ColumnDefinitions.Parse(value); return true;
            case "ShowGridLines" when c is Grid grid: grid.ShowGridLines = bool.Parse(value); return true;
            case "LastChildFill" when c is DockPanel dock: dock.LastChildFill = bool.Parse(value); return true;
            case "IsChecked" when c is ToggleButton toggle: toggle.IsChecked = bool.Parse(value); return true;
            case "IsChecked" when c is ToggleSwitch toggle: toggle.IsChecked = bool.Parse(value); return true;
            case "GroupName" when c is RadioButton radio: radio.GroupName = value; return true;
            case "Minimum" when c is RangeBase range: range.Minimum = D(value); return true;
            case "Maximum" when c is RangeBase range: range.Maximum = D(value); return true;
            case "Value" when c is RangeBase range: range.Value = D(value); return true;
            case "SelectedIndex" when c is SelectingItemsControl select: select.SelectedIndex = int.Parse(value, CultureInfo.InvariantCulture); return true;
            case "Data" when c is Avalonia.Controls.Shapes.Path path: path.Data = Geometry.Parse(value); return true;
            case "Stretch" when c is Shape shape: shape.Stretch = E<Stretch>(value); return true;
            case "RotateTransform.Angle": Transform<RotateTransform>(c).Angle = D(value); return true;
            case "TranslateTransform.X": Transform<TranslateTransform>(c).X = D(value); return true;
            case "TranslateTransform.Y": Transform<TranslateTransform>(c).Y = D(value); return true;
            case "ScaleTransform.ScaleX": Transform<ScaleTransform>(c).ScaleX = D(value); return true;
            case "ScaleTransform.ScaleY": Transform<ScaleTransform>(c).ScaleY = D(value); return true;
            case "Fill" when c is Shape shape: shape.Fill = Brush.Parse(value); return true;
            case "Stroke" when c is Shape shape: shape.Stroke = Brush.Parse(value); return true;
            case "StrokeThickness" when c is Shape shape: shape.StrokeThickness = D(value); return true;
            case "RadiusX" when c is Rectangle rect: rect.RadiusX = D(value); return true;
            case "RadiusY" when c is Rectangle rect: rect.RadiusY = D(value); return true;
            default: return false;
        }
    }
}
