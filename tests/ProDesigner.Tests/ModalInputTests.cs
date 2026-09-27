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
            window.KeyPress(key, modifiers); window.KeyRelease(key, modifiers);
            Assert.Equal(before, workbench.Session.Source);
        }
        window.KeyPress(Key.Escape, RawInputModifiers.None); window.KeyRelease(Key.Escape, RawInputModifiers.None);
        Assert.Null(workbench.ActivePrototypeRunner);
        workbench.Focus();
        window.KeyPress(Key.Z, RawInputModifiers.Control); window.KeyRelease(Key.Z, RawInputModifiers.Control);
        Assert.NotEqual(before, workbench.Session.Source);
        window.Close();
    }

    [AvaloniaFact]
    public void PrototypeArrowKeyNavigationStillReceivesInputInsideItsModalScope()
    {
        using var workbench = new DesignerWorkbench();
        var window = new Window { Width = 1600, Height = 1000, Content = workbench };
        window.Show(); Dispatcher.UIThread.RunJobs();
        workbench.SelectByName("RevenueCard");
        var source = workbench.Session.Source;
        var start = workbench.DocumentId(workbench.Documents[1]);
        var target = workbench.DocumentId(workbench.Documents[2]);
        workbench.SetPrototype(new(start, [new("right", start, "", PrototypeTrigger.Key, PrototypeAction.Navigate, target, Key: "Right")], []));
        workbench.RunPrototype(); Dispatcher.UIThread.RunJobs();
        var runner = Assert.IsType<PrototypeRunner>(workbench.ActivePrototypeRunner);
        Assert.True(runner.Focus());
        window.KeyPress(Key.Right, RawInputModifiers.None); window.KeyRelease(Key.Right, RawInputModifiers.None);
        Assert.Equal(target, runner.Player.State.Document);
        Assert.Equal(source, workbench.Session.Source);
        window.Close();
    }
}
