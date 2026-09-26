using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ProDesigner.Design;
using ProDesigner.Xaml;

namespace ProDesigner.Workbench;

public sealed class PropertyInspector : UserControl
{
    private readonly StackPanel _body = new() { Spacing = 16, Margin = new(16) };
    private readonly TextBox _filter = StudioControls.Field(watermark: "Filter properties  ⌕");
    private DesignerSession? _session;
    public event Action<string>? Status;
    public event Action<string>? GenerateHandler;
    public PropertyInspector()
    {
        var grid = new Grid { RowDefinitions = RowDefinitions.Parse("Auto,*") };
        _filter.Margin = new(14, 10); _filter.TextChanged += (_, _) => Rebuild();
        StudioControls.Place(grid, _filter, 0, 0); StudioControls.Place(grid, new ScrollViewer { Content = _body }, 1, 0);
        Content = grid;
    }
    public void Attach(DesignerSession session) { _session = session; Rebuild(); }
    public void Rebuild()
    {
        _body.Children.Clear(); if (_session is null) return;
        var node = _session.Primary;
        if (node is null)
        {
            _body.Children.Add(StudioControls.Caption("DOCUMENT"));
            _body.Children.Add(StudioControls.Text("A place for your next idea.", 16));
            _body.Children.Add(new TextBlock { Text = "Select a layer on the canvas or in the outline to inspect its properties. Changes are written directly to your XAML.", TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Brush.Parse("#8F95A8") });
            _body.Children.Add(StudioControls.Caption($"{_session.Tree.Elements.Count} SYNTAX NODES")); return;
        }
        _body.Children.Add(StudioControls.Caption(_session.Selection.Count > 1 ? $"{_session.Selection.Count} LAYERS SELECTED" : node.LocalName.ToUpperInvariant()));
        _body.Children.Add(StudioControls.Text(node.DisplayName, 18, "#DAD3F4"));
        AddSection("IDENTITY", ["x:Name", "Classes"]);
        AddSection("LAYOUT", ["Width", "Height", "MinWidth", "MinHeight", "MaxWidth", "MaxHeight", "Margin", "Padding", "HorizontalAlignment", "VerticalAlignment"]);
        if (node.Parent?.LocalName == "Canvas") AddSection("POSITION", ["Canvas.Left", "Canvas.Top"]);
        if (node.Parent?.LocalName == "Grid") AddSection("GRID PLACEMENT", ["Grid.Row", "Grid.Column", "Grid.RowSpan", "Grid.ColumnSpan"]);
        if (node.LocalName == "Grid") AddSection("GRID", ["RowDefinitions", "ColumnDefinitions", "ShowGridLines"]);
        if (node.LocalName is "StackPanel" or "WrapPanel") AddSection("AUTO LAYOUT", ["Orientation", "Spacing"]);
        AddSection("APPEARANCE", ["Background", "Foreground", "BorderBrush", "BorderThickness", "CornerRadius", "Opacity"]);
        if (node.LocalName is "TextBlock" or "TextBox" or "Button" or "CheckBox" or "RadioButton" or "ToggleSwitch")
            AddSection("TYPOGRAPHY & CONTENT", [node.LocalName is "TextBlock" or "TextBox" ? "Text" : "Content", "FontSize", "FontWeight", "TextWrapping", "Watermark"]);
        if (node.LocalName is "Rectangle" or "Ellipse") AddSection("SHAPE", ["Fill", "Stroke", "StrokeThickness", "RadiusX", "RadiusY"]);
        AddSection("STATE", ["IsVisible", "IsEnabled", "IsChecked", "Value"]);
        var extras = node.Attributes.Select(a => a.Name).Where(n => !ControlCatalog.CommonProperties.Contains(n) && !n.StartsWith("xmlns", StringComparison.Ordinal) && !n.StartsWith("x:", StringComparison.Ordinal)).ToArray();
        if (extras.Length > 0) AddSection("ADDITIONAL XAML", extras);
        var name = StudioControls.Field(watermark: "Property / attached property");
        var value = StudioControls.Field(watermark: "Value or {Binding Path}");
        _body.Children.Add(StudioControls.Caption("ADD PROPERTY")); _body.Children.Add(name); _body.Children.Add(value);
        _body.Children.Add(StudioControls.Button("＋ Add property", "Add any XAML attribute", () => Commit(name.Text ?? "", value.Text)));
        if (node.LocalName == "Button")
        {
            var handler = StudioControls.Field(node.Get("Click"), "OnButtonClick"); _body.Children.Add(StudioControls.Caption("EVENTS · ROSLYN")); _body.Children.Add(handler);
            _body.Children.Add(StudioControls.Button("Generate Click handler", "Generate a C# event handler and connect its XAML attribute", () => GenerateHandler?.Invoke(handler.Text ?? "OnButtonClick")));
        }
    }
    private void AddSection(string title, IEnumerable<string> properties)
    {
        var filter = _filter.Text ?? "";
        var names = properties.Where(p => p.Contains(filter, StringComparison.OrdinalIgnoreCase)).Distinct().ToArray();
        if (names.Length == 0 || _session?.Primary is not { } node) return;
        var section = new StackPanel { Spacing = 8 }; section.Children.Add(StudioControls.Caption(title));
        foreach (var property in names)
        {
            var row = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("96,*,22") };
            var label = StudioControls.Text(property, 10, "#A0A6B8"); ToolTip.SetTip(label, property);
            var field = StudioControls.Field(node.Get(property), "Auto"); field.Name = "Property_" + property.Replace('.', '_').Replace(':', '_');
            var before = field.Text;
            void Apply() { if (field.Text == before) return; before = field.Text; Commit(property, string.IsNullOrEmpty(field.Text) ? null : field.Text); }
            field.LostFocus += (_, _) => Apply();
            field.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Apply(); e.Handled = true; } };
            var reset = StudioControls.Button("↺", "Reset " + property, () => Commit(property, null)); reset.Padding = new(2);
            StudioControls.Place(row, label, 0, 0); StudioControls.Place(row, field, 0, 1); StudioControls.Place(row, reset, 0, 2); section.Children.Add(row);
        }
        _body.Children.Add(section);
    }
    private void Commit(string property, string? value)
    {
        try { _session?.SetProperty(property, value); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.Xml.XmlException) { Status?.Invoke(ex.Message); }
    }
}
