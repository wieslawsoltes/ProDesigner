using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ProDesigner.Roslyn;
using ProDesigner.Workbench;
using ProDesigner.Workspaces;
using ProDesigner.Xaml;
using ProDesigner.Core;

namespace ProDesigner.Desktop;
internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try { WorkspaceBootstrap.Initialize(); DesignerApplication.WorkspaceFactory = WorkspaceBootstrap.Create; }
        catch (Exception ex) { Console.Error.WriteLine("MSBuild workspace unavailable: " + ex.Message); }
        DesignerApplication.CodeFactory = () => new RoslynCodeService();
        DesignerApplication.TrustedPreviewFactory = source =>
        {
            var tree = XamlSyntaxTree.Parse(source);
            // Standalone preview does not pretend to have the user's compiled code-behind assembly.
            if (tree.Root.Get("x:Class") is not null)
                source = EditApplication.Apply(source, [XamlEdits.SetAttribute(tree, tree.Root, "x:Class", null)]);
            var result = AvaloniaRuntimeXamlLoader.Load(source);
            if (result is Window window)
            {
                var content = window.Content as Control ?? throw new InvalidOperationException("The window has no visual content.");
                window.Content = null; return content;
            }
            return result as Control ?? throw new InvalidOperationException("The XAML root must be an Avalonia control.");
        };
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<DesignerApplication>().UsePlatformDetect().LogToTrace();
}
