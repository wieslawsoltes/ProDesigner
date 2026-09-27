using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Fonts.Inter;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ProDesigner.Core;
using ProDesigner.Persistence;

namespace ProDesigner.Workbench;

public sealed partial class DesignerApplication : Application
{
    public static Func<IWorkspaceService?>? WorkspaceFactory { get; set; }
    public static Func<ICodeService?>? CodeFactory { get; set; }
    public static Func<TrustedPreviewRequest, Task<TrustedPreview>>? TrustedPreviewFactory { get; set; }
    public static Func<TrustedPreviewRequest, CancellationToken, Task<IExternalPreview>>? ExternalPreviewFactory { get; set; }
    public static Func<IRecoveryStore?>? RecoveryFactory { get; set; }
    public static DesignerWorkbench? Workbench { get; private set; }
    public override void Initialize()
    {
        FontManager.Current.AddFontCollection(new InterFontCollection());
        AvaloniaXamlLoader.Load(this);
    }
    public override void OnFrameworkInitializationCompleted()
    {
        Workbench = new DesignerWorkbench(WorkspaceFactory?.Invoke(), CodeFactory?.Invoke(), TrustedPreviewFactory, RecoveryFactory?.Invoke(), ExternalPreviewFactory);
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new Window { Title = "ProDesigner — Avalonia visual studio", Width = 1560, Height = 1000, MinWidth = 960, MinHeight = 640, Content = Workbench };
            var allowClose = false;
            desktop.MainWindow.Closing += async (_, e) =>
            {
                if (allowClose) return;
                e.Cancel = true;
                await Workbench.SaveRecoveryAsync();
                await Workbench.StopExternalPreviewAsync();
                allowClose = true; desktop.MainWindow.Close();
            };
            desktop.Exit += (_, _) => Workbench.Dispose();
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime single) single.MainView = Workbench;
        base.OnFrameworkInitializationCompleted();
    }
}
