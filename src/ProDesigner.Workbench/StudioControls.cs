using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ProDesigner.Workbench;

internal static class StudioControls
{
    public static readonly IBrush Panel = Brush.Parse("#1B1D25");
    public static readonly IBrush Line = Brush.Parse("#30333E");
    public static TextBlock Text(string text, double size = 12, string? color = null) => new() { Text = text, FontSize = size, Foreground = color is null ? Brush.Parse("#D5D8E3") : Brush.Parse(color), VerticalAlignment = VerticalAlignment.Center };
    public static TextBlock Caption(string text) { var label = Text(text, 10); label.Classes.Add("caption"); return label; }
    public static Button Button(string text, string tooltip, Action action, bool accent = false)
    {
        var button = new Button { Content = text, VerticalAlignment = VerticalAlignment.Center };
        button.Classes.Add(accent ? "accent" : "chrome"); ToolTip.SetTip(button, tooltip); AutomationProperties.SetName(button, tooltip);
        button.Click += (_, _) => action(); return button;
    }
    public static TextBox Field(string? text = null, string? watermark = null)
    {
        var field = new TextBox { Text = text, Watermark = watermark }; field.Classes.Add("field"); return field;
    }
    public static Border Box(Control child, Thickness? padding = null) => new() { Child = child, Background = Panel, Padding = padding ?? new Thickness(12), BorderBrush = Line, BorderThickness = new(0, 0, 0, 1) };
    public static StackPanel Row(params Control[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 }; foreach (var child in children) row.Children.Add(child); return row;
    }
    public static StackPanel Column(params Control[] children)
    {
        var column = new StackPanel { Spacing = 10 }; foreach (var child in children) column.Children.Add(child); return column;
    }
    public static void Place(Grid grid, Control control, int row, int column, int columnSpan = 1)
    { Grid.SetRow(control, row); Grid.SetColumn(control, column); Grid.SetColumnSpan(control, columnSpan); grid.Children.Add(control); }
}
