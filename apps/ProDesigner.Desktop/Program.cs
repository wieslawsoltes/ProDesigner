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
        if (args.Length >= 2 && args[0] == "--preview-worker") { PreviewWorker.Run(args[1], args.Contains("--preview-headless")); return; }
        try { WorkspaceBootstrap.Initialize(); DesignerApplication.WorkspaceFactory = WorkspaceBootstrap.Create; }
        catch (Exception ex) { Console.Error.WriteLine("MSBuild workspace unavailable: " + ex.Message); }
        DesignerApplication.CodeFactory = () => new RoslynCodeService();
        DesignerApplication.ExternalPreviewFactory = ExternalPreviewSession.StartAsync;
        DesignerApplication.RecoveryFactory = () => new ProDesigner.Persistence.FileRecoveryStore(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProDesigner", "recovery.prodesigner"));
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<DesignerApplication>().UsePlatformDetect().LogToTrace();
}
