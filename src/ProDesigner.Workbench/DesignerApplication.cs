using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using ProDesigner.Core;

namespace ProDesigner.Workbench;

public sealed class DesignerApplication : Application
{
    public static Func<IWorkspaceService?>? WorkspaceFactory { get; set; }
    public static Func<ICodeService?>? CodeFactory { get; set; }
    public static Func<string, Control>? TrustedPreviewFactory { get; set; }
    public static DesignerWorkbench? Workbench { get; private set; }
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://ProDesigner.Workbench/")) { Source = new Uri("avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml") });
        Styles.Add(new StyleInclude(new Uri("avares://ProDesigner.Workbench/")) { Source = new Uri("avares://ProDesigner.Workbench/Themes/Studio.axaml") });
    }
    public override void OnFrameworkInitializationCompleted()
    {
        Workbench = new DesignerWorkbench(WorkspaceFactory?.Invoke(), CodeFactory?.Invoke(), TrustedPreviewFactory);
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new Window { Title = "ProDesigner — Avalonia visual studio", Width = 1560, Height = 1000, MinWidth = 960, MinHeight = 640, Content = Workbench };
            desktop.Exit += (_, _) => Workbench.Dispose();
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime single) single.MainView = Workbench;
        base.OnFrameworkInitializationCompleted();
    }
}
