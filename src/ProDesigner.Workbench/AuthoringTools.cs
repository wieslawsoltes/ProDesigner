using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ProDesigner.Authoring;
using ProDesigner.Core;
using ProDesigner.Design;
using ProDesigner.Xaml;
using static ProDesigner.Workbench.StudioControls;

namespace ProDesigner.Workbench;

public sealed partial class DesignerWorkbench
{
    private void ShowAuthoringTools()
    {
        var tabs = new TabControl { MaxHeight = 600 };
        tabs.Items.Add(new TabItem { Header = "Resources", Content = ResourceTools() });
        tabs.Items.Add(new TabItem { Header = "Styles", Content = StyleTools() });
        tabs.Items.Add(new TabItem { Header = "Templates", Content = TemplateTools() });
        tabs.Items.Add(new TabItem { Header = "Paint", Content = BrushTools() });
        tabs.Items.Add(new TabItem { Header = "Data", Content = SampleDataTools() });
        ShowDialog("Design system & authoring", Column(Row(Button("Components", "Linked components and variants", ShowComponentTools), Button("Prototype", "Navigation flows", ShowPrototypeTools), Button("Constraints", "Responsive layout anchors", ShowConstraintTools)), tabs));
    }
    private static Dictionary<string, string> ReadSetters(string text)
    {
        var values = new Dictionary<string, string>();
        foreach (var line in text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = line.IndexOf('='); if (equals <= 0) throw new FormatException("Enter one Property=Value pair per line.");
            var property = line[..equals].Trim(); System.Xml.XmlConvert.VerifyName(property); values[property] = line[(equals + 1)..].Trim();
        }
        return values;
    }
    private Control ResourceTools()
    {
        var key = Field("AccentBrush", "Resource key"); var value = Field("#A899EC", "Color");
        var resources = new StackPanel { Spacing = 4 };
        foreach (var node in Session.Tree.Elements.Where(n => XamlAuthoring.Key(n) is not null))
        {
            var button = Button($"{XamlAuthoring.Key(node)} · {node.LocalName}", "Inspect resource source", () => { key.Text = XamlAuthoring.Key(node); value.Text = node.Get("Color"); Session.Select(node.Id); });
            resources.Children.Add(button);
        }
        return Column(Caption("DOCUMENT RESOURCES"), new ScrollViewer { Content = resources, MaxHeight = 180 }, key, value,
            Button("Save brush resource", "Insert or replace this brush resource without rewriting other resources", () => Guard(() =>
            {
                _ = Color.Parse(value.Text ?? ""); Session.Apply("Save brush resource", XamlAuthoring.UpsertResource(Session.Tree, key.Text ?? "", "SolidColorBrush", new Dictionary<string, string> { ["Color"] = value.Text! }));
                RefreshDocument(); SetStatus("Resource saved. Apply it with {StaticResource " + key.Text + "}.");
            }), true),
            Button("Use as selected Background", "Reference this resource from the selection", () => Guard(() => Session.SetProperty("Background", "{StaticResource " + key.Text + "}"))));
    }
    private Control StyleTools()
    {
        var selector = Field(Session.Primary is { } node ? node.LocalName + ".primary" : "Button.primary", "Avalonia selector");
        var setters = Field("Background=#8570D8\nForeground=White\nCornerRadius=8\nPadding=16,10"); setters.AcceptsReturn = true; setters.Height = 140;
        var key = Field("PrimaryTheme", "ControlTheme key"); var type = Field(Session.Primary?.LocalName ?? "Button", "Target type");
        return Column(Caption("STYLE SETTERS · PROPERTY=VALUE PER LINE"), selector, setters,
            Button("Save style", "Upsert a selector and its setters; retain animations and unedited setters", () => Guard(() => { Session.Apply("Save style", XamlAuthoring.UpsertStyle(Session.Tree, selector.Text ?? "", ReadSetters(setters.Text ?? ""))); RefreshDocument(); }), true),
            Caption("REUSABLE CONTROL THEME"), key, type,
            Button("Create ControlTheme", "Create or replace a named ControlTheme resource", () => Guard(() => { Session.Apply("Save control theme", XamlAuthoring.UpsertTheme(Session.Tree, key.Text ?? "", type.Text ?? "", ReadSetters(setters.Text ?? ""))); RefreshDocument(); })),
            Button("Apply selected theme", "Set the selected control's Theme resource reference", () => Guard(() => Session.SetProperty("Theme", "{StaticResource " + key.Text + "}"))),
            new TextBlock { Text = "The source editor retains all Avalonia selectors. Complex styles and themes can be inspected in the isolated runtime preview.", FontSize = 11, TextWrapping = TextWrapping.Wrap });
    }
    private Control TemplateTools()
    {
        var property = new ComboBox { ItemsSource = new[] { "ContentTemplate", "ItemTemplate", "Template" }, SelectedIndex = 0 };
        var content = Field("<StackPanel Spacing=\"8\"><TextBlock Text=\"{Binding Title}\" FontSize=\"20\" /><TextBlock Text=\"{Binding Description}\" /></StackPanel>");
        content.AcceptsReturn = true; content.Height = 200; content.TextWrapping = TextWrapping.Wrap;
        return Column(Caption("SELECT A CONTROL BEFORE OPENING THIS TOOL"), property, content,
            Button("Apply template", "Set a DataTemplate or ControlTemplate property without changing the rest of the document", () => Guard(() =>
            {
                var owner = Session.Primary ?? throw new InvalidOperationException("Select a control first.");
                var member = property.SelectedItem?.ToString() ?? "ContentTemplate"; var template = member == "Template" ? "ControlTemplate" : "DataTemplate";
                Session.Apply("Apply " + template, XamlAuthoring.SetPropertyObject(Session.Tree, owner, member, $"<{template}>{content.Text}</{template}>")); RefreshDocument();
            }), true),
            new TextBlock { Text = "Templates introduce their own namescope. Their source remains intact; full project rendering uses the isolated desktop preview.", TextWrapping = TextWrapping.Wrap, FontSize = 11 });
    }
    private Control BrushTools()
    {
        var property = new ComboBox { ItemsSource = new[] { "Background", "Foreground", "Fill", "Stroke", "BorderBrush" }, SelectedIndex = 0 };
        var hex = Field("#A899EC", "Color"); var swatch = new Border { Height = 45, CornerRadius = new(8), Background = Brush.Parse("#A899EC") };
        var sliders = new StackPanel { Spacing = 4 }; var channels = new Slider[4]; var updating = false;
        for (var i = 0; i < 4; i++)
        {
            var slider = channels[i] = new Slider { Minimum = 0, Maximum = 255, Value = i == 0 ? 255 : i == 1 ? 168 : i == 2 ? 153 : 236 };
            var row = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("24,*,40") };
            var number = Text(slider.Value.ToString("0", CultureInfo.InvariantCulture), 11);
            Place(row, Text(new[] { "A", "R", "G", "B" }[i], 11), 0, 0); Place(row, slider, 0, 1); Place(row, number, 0, 2); sliders.Children.Add(row);
            slider.ValueChanged += (_, _) =>
            {
                number.Text = slider.Value.ToString("0", CultureInfo.InvariantCulture);
                if (updating || channels.Any(c => c is null)) return;
                updating = true; hex.Text = $"#{(byte)channels[0].Value:X2}{(byte)channels[1].Value:X2}{(byte)channels[2].Value:X2}{(byte)channels[3].Value:X2}"; swatch.Background = Brush.Parse(hex.Text); updating = false;
            };
        }
        hex.TextChanged += (_, _) =>
        {
            if (updating) return;
            try { var color = Color.Parse(hex.Text ?? ""); updating = true; var values = new[] { color.A, color.R, color.G, color.B }; for (var i = 0; i < 4; i++) channels[i].Value = values[i]; swatch.Background = new SolidColorBrush(color); }
            catch (FormatException) { }
            finally { updating = false; }
        };
        var stops = Field("0 #A899EC\n0.5 #6F8BF2\n1 #57BEA5"); stops.AcceptsReturn = true; stops.Height = 90;
        return Column(property, swatch, hex, sliders,
            Button("Apply solid color", "Apply an RGBA brush to the selected control", () => Guard(() =>
            {
                _ = Color.Parse(hex.Text ?? ""); var node = Session.Primary ?? throw new InvalidOperationException("Select a control first."); var member = property.SelectedItem!.ToString()!;
                var edits = XamlAuthoring.SetPropertyObject(Session.Tree, node, member, null).ToList(); edits.RemoveAll(e => node.Attributes.Any(a => a.Span == e.Span || a.ValueSpan == e.Span)); edits.Add(XamlEdits.SetAttribute(Session.Tree, node, member, hex.Text)); Session.Apply("Set solid brush", edits); RefreshDocument();
            }), true), Caption("LINEAR GRADIENT · OFFSET COLOR PER LINE"), stops,
            Button("Apply gradient", "Create a real LinearGradientBrush property element", () => Guard(() =>
            {
                var values = (stops.Text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => { var pair = line.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries); if (pair.Length != 2) throw new FormatException("Use offset color on every line."); _ = Color.Parse(pair[1]); return new GradientStopModel(double.Parse(pair[0], CultureInfo.InvariantCulture), pair[1]); }).ToArray();
                var owner = Session.Primary ?? throw new InvalidOperationException("Select a control first.");
                Session.Apply("Set linear gradient", XamlAuthoring.SetPropertyObject(Session.Tree, owner, property.SelectedItem!.ToString()!, new GradientModel(0, 0, 1, 1, values).ToXaml())); RefreshDocument();
            })));
    }
    private Control SampleDataTools()
    {
        var editor = Field("{\"Title\":\"Your next great interface\",\"Description\":\"Live sample data\",\"User\":{\"Name\":\"Alex Morgan\"}}"); editor.AcceptsReturn = true; editor.Height = 220; editor.TextWrapping = TextWrapping.Wrap;
        return Column(Caption("SAFE DESIGN DATA · JSON OBJECT"), editor, Button("Apply sample data", "Use these values for safe Binding previews without executing a view model", () => Guard(() =>
        {
            using var json = JsonDocument.Parse(editor.Text ?? "", new JsonDocumentOptions { MaxDepth = 16 });
            if (json.RootElement.ValueKind != JsonValueKind.Object) throw new FormatException("Sample data must be a JSON object.");
            var values = new Dictionary<string, string>();
            void Flatten(JsonElement element, string prefix)
            {
                foreach (var item in element.EnumerateObject())
                {
                    var path = prefix.Length == 0 ? item.Name : prefix + "." + item.Name;
                    if (item.Value.ValueKind == JsonValueKind.Object) Flatten(item.Value, path);
                    else values[path] = item.Value.ValueKind == JsonValueKind.String ? item.Value.GetString()! : item.Value.ToString();
                    if (values.Count > 2000) throw new FormatException("Too many sample data fields.");
                }
            }
            Flatten(json.RootElement, ""); _surface.SampleData = values; _surface.Rebuild(); ScheduleRecovery(); SetStatus("Sample data updated.");
        }), true));
    }
    private void ShowVectorEditor()
    {
        Guard(() =>
        {
            var node = Session.Primary ?? throw new InvalidOperationException("Select a Path control first.");
            if (node.LocalName != "Path") throw new InvalidOperationException("Select a Path control, or insert one from Assets.");
            var editor = new VectorEditor(node.Get("Data") ?? "M 10,10 L 180,10 L 180,140 Z");
            editor.Committed += data => Guard(() => { var current = Session.Tree.Find(node.Id) ?? throw new InvalidOperationException("The path no longer exists."); Session.Apply("Edit vector geometry", [XamlEdits.SetAttribute(Session.Tree, current, "Data", data)]); RefreshDocument(); });
            ShowDialog("Vector geometry · drag endpoints and control points", editor);
        });
    }
}
