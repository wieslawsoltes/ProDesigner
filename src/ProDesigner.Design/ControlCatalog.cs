namespace ProDesigner.Design;

public sealed record ToolboxItem(string Name, string Category, string Glyph, string Xaml, string Description);
public static class ControlCatalog
{
    public static IReadOnlyList<ToolboxItem> Items { get; } =
    [
        new("Button", "Input", "B", "<Button Content=\"Button\" Width=\"128\" Height=\"40\" />", "A command surface with pointer and keyboard interaction."),
        new("TextBlock", "Text", "T", "<TextBlock Text=\"Your text\" FontSize=\"24\" />", "Read-only typography."),
        new("TextBox", "Input", "I", "<TextBox Watermark=\"Enter a value\" Width=\"240\" />", "Editable text input."),
        new("Border", "Layout", "□", "<Border Width=\"240\" Height=\"160\" Background=\"#252936\" CornerRadius=\"16\" Padding=\"24\" />", "A styled container with one child."),
        new("Grid", "Layout", "▦", "<Grid Width=\"320\" Height=\"240\" ColumnDefinitions=\"*,*\" RowDefinitions=\"Auto,*\" />", "Rows and columns with attached placement properties."),
        new("StackPanel", "Layout", "☰", "<StackPanel Spacing=\"12\" />", "Vertical or horizontal automatic layout."),
        new("Canvas", "Layout", "⊞", "<Canvas Width=\"640\" Height=\"480\" Background=\"Transparent\" />", "Free placement, resizing, snapping, and alignment."),
        new("DockPanel", "Layout", "⊟", "<DockPanel Width=\"320\" Height=\"240\" />", "Dock children to edges."),
        new("WrapPanel", "Layout", "▤", "<WrapPanel Width=\"320\" />", "Wrap children at the available boundary."),
        new("ScrollViewer", "Layout", "↕", "<ScrollViewer Width=\"320\" Height=\"240\" />", "Scrollable single-child content."),
        new("CheckBox", "Input", "☑", "<CheckBox Content=\"Remember me\" IsChecked=\"True\" />", "Boolean selection."),
        new("RadioButton", "Input", "◉", "<RadioButton Content=\"Option\" GroupName=\"Options\" />", "Exclusive selection."),
        new("ToggleSwitch", "Input", "◐", "<ToggleSwitch Content=\"Notifications\" IsChecked=\"True\" />", "An on/off switch."),
        new("Slider", "Input", "⎯", "<Slider Width=\"220\" Minimum=\"0\" Maximum=\"100\" Value=\"65\" />", "Continuous numeric input."),
        new("ProgressBar", "Feedback", "▰", "<ProgressBar Width=\"240\" Height=\"8\" Value=\"65\" />", "Determinate progress."),
        new("ComboBox", "Input", "⌄", "<ComboBox Width=\"200\" SelectedIndex=\"0\"><ComboBoxItem Content=\"First option\" /><ComboBoxItem Content=\"Second option\" /></ComboBox>", "A compact list of choices."),
        new("ListBox", "Collections", "≡", "<ListBox Width=\"240\" Height=\"160\"><ListBoxItem Content=\"First item\" /><ListBoxItem Content=\"Second item\" /></ListBox>", "A selectable list."),
        new("Path", "Shapes", "◇", "<Path Width=\"200\" Height=\"160\" Data=\"M 10,80 C 30,0 150,0 180,80 Q 150,150 10,80 Z\" Stroke=\"#A79BFA\" StrokeThickness=\"3\" Fill=\"#403954\" />", "Editable line, quadratic and cubic Bezier geometry."),
        new("Rectangle", "Shapes", "▭", "<Rectangle Width=\"160\" Height=\"100\" Fill=\"#A79BFA\" RadiusX=\"12\" RadiusY=\"12\" />", "A rectangle with optional rounded corners."),
        new("Ellipse", "Shapes", "○", "<Ellipse Width=\"120\" Height=\"120\" Fill=\"#A79BFA\" />", "An ellipse or circle."),
        new("Separator", "Feedback", "—", "<Separator Width=\"240\" />", "A visual divider.")
    ];
    public static IReadOnlyList<string> CommonProperties { get; } = ["x:Name", "Width", "Height", "MinWidth", "MinHeight", "MaxWidth", "MaxHeight", "Margin", "Padding", "HorizontalAlignment", "VerticalAlignment", "Background", "Foreground", "BorderBrush", "BorderThickness", "CornerRadius", "Opacity", "FontSize", "FontWeight", "Text", "Content", "Spacing", "Orientation", "RowDefinitions", "ColumnDefinitions", "Grid.Row", "Grid.Column", "Grid.RowSpan", "Grid.ColumnSpan", "Canvas.Left", "Canvas.Top", "IsVisible", "IsEnabled", "Classes"];
}
