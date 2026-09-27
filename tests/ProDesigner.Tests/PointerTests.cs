using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using ProDesigner.Workbench;
using Xunit;

namespace ProDesigner.Tests;
public class PointerTests
{
    private static (DesignerWorkbench Workbench, Window Window) Create()
    {
        var workbench = new DesignerWorkbench();
        workbench.OpenDocument("Pointer.axaml", "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"><Canvas><Border x:Name=\"Card\" Canvas.Left=\"80\" Canvas.Top=\"80\" Width=\"160\" Height=\"100\" Background=\"Purple\"/></Canvas></UserControl>");
        workbench.Surface.Profiles = [new("Test", 960,620)]; workbench.Surface.Rebuild(); workbench.Surface.SetZoom(1);
        var window = new Window { Width = 1600, Height = 1000, Content = workbench }; window.Show();
        Dispatcher.UIThread.RunJobs(); workbench.Surface.SetZoom(1); window.UpdateLayout();
        return (workbench, window);
    }
    [AvaloniaFact]
    public void PointerDragCommitsOneUndoableSourceTransaction()
    {
        var (workbench, window) = Create(); using var disposable = workbench;
        var source = workbench.Session.Source;
        var rect = workbench.Surface.GetElementBounds("Card")!.Value;
        var start = new Point(rect.X+20,rect.Y+20); var end = start + new Vector(24,16);
        window.MouseDown(start, MouseButton.Left); window.MouseMove(end, RawInputModifiers.LeftMouseButton | RawInputModifiers.Alt); window.UpdateLayout(); window.MouseUp(end,MouseButton.Left,RawInputModifiers.Alt);
        Assert.Equal("104", workbench.Session.Primary!.Get("Canvas.Left")); Assert.Equal("96", workbench.Session.Primary.Get("Canvas.Top"));
        workbench.Execute("undo"); Assert.Equal(source, workbench.Session.Source); window.Close();
    }
    [AvaloniaFact]
    public void NorthWestHandleChangesSizeAndPosition()
    {
        var (workbench, window) = Create(); using var disposable = workbench; workbench.SelectByName("Card");
        var rect = workbench.Surface.GetElementBounds("Card")!.Value;
        var start = new Point(rect.X,rect.Y); var end = start + new Vector(16,16);
        window.MouseDown(start, MouseButton.Left); window.MouseMove(end,RawInputModifiers.LeftMouseButton|RawInputModifiers.Alt); window.UpdateLayout(); window.MouseUp(end,MouseButton.Left,RawInputModifiers.Alt);
        Assert.Equal("144",workbench.Session.Primary!.Get("Width")); Assert.Equal("84",workbench.Session.Primary.Get("Height"));
        Assert.Equal("96",workbench.Session.Primary.Get("Canvas.Left")); Assert.Equal("96",workbench.Session.Primary.Get("Canvas.Top")); window.Close();
    }
    [AvaloniaFact]
    public void EscapeCancelsAnInFlightGestureWithoutChangingSource()
    {
        var (workbench, window) = Create(); using var disposable = workbench;
        var source = workbench.Session.Source; var before = workbench.Surface.GetElementBounds("Card")!.Value;
        var start = new Point(before.X+20,before.Y+20); var end = start + new Vector(70,20);
        window.MouseDown(start,MouseButton.Left); window.MouseMove(end,RawInputModifiers.LeftMouseButton); workbench.Surface.CancelGesture(); window.UpdateLayout(); window.MouseUp(end,MouseButton.Left);
        Assert.Equal(source,workbench.Session.Source); Assert.Equal(before,workbench.Surface.GetElementBounds("Card")!.Value); window.Close();
    }
}
