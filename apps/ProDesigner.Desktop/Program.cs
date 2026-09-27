using Avalonia;
using ProDesigner.Roslyn;
using ProDesigner.Runtime;
using ProDesigner.Workbench;
using ProDesigner.Workspaces;

namespace ProDesigner.Desktop;
internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try { WorkspaceBootstrap.Initialize(); DesignerApplication.WorkspaceFactory = WorkspaceBootstrap.Create; }
        catch (Exception ex) { Console.Error.WriteLine("MSBuild workspace unavailable: " + ex.Message); }
        DesignerApplication.CodeFactory = () => new RoslynCodeService();
        var builder = new DotNetProjectBuilder(); var runtime = new RuntimePreviewEngine();
        DesignerApplication.TrustedPreviewFactory = async request =>
        {
            string? assembly = null;
            if (request.ProjectPath is not null)
            {
                var build = await builder.BuildAsync(new(request.ProjectPath, request.Trusted));
                if (!build.Success) throw new InvalidOperationException(string.Join("\n", build.Diagnostics.Select(d => d.Message)) + "\n" + build.Log);
                assembly = build.AssemblyPath;
            }
            var preview = runtime.Load(request, assembly);
            return new TrustedPreview(preview.Root, preview.Dispose);
        };
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<DesignerApplication>().UsePlatformDetect().LogToTrace();
}
