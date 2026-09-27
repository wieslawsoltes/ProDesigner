using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using ProDesigner.Prototyping;
using ProDesigner.Workbench;
using Xunit;

namespace ProDesigner.Tests;

public class ModalInputTests
{
    private static void Press(Window window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var physical = key switch
        {
            Key.Delete => PhysicalKey.Delete, Key.Left => PhysicalKey.ArrowLeft,
            Key.Right => PhysicalKey.ArrowRight, Key.Escape => PhysicalKey.Escape,
            Key.Z => PhysicalKey.KeyZ, _ => PhysicalKey.None
        };
        window.KeyPress(key, modifiers, physical, null);
        window.KeyRelease(key, modifiers, physical, null);
    }
    [AvaloniaFact]
    public void PrototypeKeyboardInputCannotDeleteNudgeOrUndoTheUnderlyingDocument()
    {
        using var workbench = new DesignerWorkbench();
        var window = new Window { Width = 1600, Height = 1000, Content = workbench };
        window.Show(); Dispatcher.UIThread.RunJobs();
        workbench.SelectByName("RevenueCard"); workbench.SetProperty("Width", "333");
        var before = workbench.Session.Source;
        workbench.RunPrototype(); Dispatcher.UIThread.RunJobs();
        var runner = Assert.IsType<PrototypeRunner>(workbench.ActivePrototypeRunner);
        Assert.True(runner.Focus());
        foreach (var (key, modifiers) in new[] { (Key.Delete, RawInputModifiers.None), (Key.Left, RawInputModifiers.None), (Key.Z, RawInputModifiers.Control) })
        {
            Press(window, key, modifiers); Assert.Equal(before, workbench.Session.Source);
        }
        Press(window, Key.Escape); Assert.Null(workbench.ActivePrototypeRunner);
        workbench.Focus(); Press(window, Key.Z, RawInputModifiers.Control);
        Assert.NotEqual(before, workbench.Session.Source); window.Close();
    }
    [AvaloniaFact]
    public void PrototypeArrowKeyNavigationStillReceivesInputInsideItsModalScope()
    {
        using var workbench = new DesignerWorkbench();
        var window = new Window { Width = 1600, Height = 1000, Content = workbench };
        window.Show(); Dispatcher.UIThread.RunJobs(); workbench.SelectByName("RevenueCard");
        var source = workbench.Session.Source;
        var start = workbench.DocumentId(workbench.Documents[1]); var target = workbench.DocumentId(workbench.Documents[2]);
        workbench.SetPrototype(new(start, [new("right", start, "", PrototypeTrigger.Key, PrototypeAction.Navigate, target, Key: "Right")], []));
        workbench.RunPrototype(); Dispatcher.UIThread.RunJobs();
        var runner = Assert.IsType<PrototypeRunner>(workbench.ActivePrototypeRunner); Assert.True(runner.Focus());
        Press(window, Key.Right); Assert.Equal(target, runner.Player.State.Document);
        Assert.Equal(source, workbench.Session.Source); window.Close();
    }
}
