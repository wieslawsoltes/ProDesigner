using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using ProDesigner.Prototyping;
using ProDesigner.Workbench;
using Xunit;

namespace ProDesigner.Tests;

public class PrototypeInputTests
{
    [AvaloniaFact]
    public void ButtonPointerClickNavigatesExactlyOnce()
    {
        var docs = new Dictionary<string, PrototypeViewDocument>
        {
            ["a"] = new("A", "<UserControl xmlns='https://github.com/avaloniaui' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'><Button x:Name='Next' Width='150' Height='50' Content='Next'/></UserControl>"),
            ["b"] = new("B", "<UserControl xmlns='https://github.com/avaloniaui'><TextBlock Text='Destination'/></UserControl>")
        };
        var player = new PrototypePlayer(new("a", [new("next", "a", "Next", PrototypeTrigger.Click, PrototypeAction.Navigate, "b")], []),
            new Dictionary<string, IReadOnlyCollection<string>> { ["a"] = new[] { "Next" }, ["b"] = Array.Empty<string>() });
        var runner = new PrototypeRunner(player, docs, new Dictionary<string,string>());
        var window = new Window { Width = 700, Height = 600, Content = runner }; window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        var rect = runner.GetElementBounds("Next")!.Value; var point = new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
        Assert.Equal("b", player.State.Document); Assert.Equal(1, player.State.BackCount); window.Close();
    }
    [Fact]
    public void BackEmitsOneNotificationAndInputGraphIsSnapshotted()
    {
        var documents = new Dictionary<string,IReadOnlyCollection<string>> { ["a"] = new[] {"Next"}, ["b"] = new[] {"Back"} };
        var links = new[] { new PrototypeLink("next","a","Next",PrototypeTrigger.Click,PrototypeAction.Navigate,"b"), new PrototypeLink("back","b","Back",PrototypeTrigger.Click,PrototypeAction.Back) };
        var player = new PrototypePlayer(new("a",links,[]),documents); links[0] = links[0] with { TargetDocument = "a" };
        Assert.True(player.Dispatch(PrototypeTrigger.Click,"Next")); Assert.Equal("b",player.State.Document);
        var notifications=0;player.Changed+=_=>notifications++;player.Dispatch(PrototypeTrigger.Click,"Back");Assert.Equal(1,notifications);
    }
    [Fact]
    public void KeyboardTriggersAreCaseInsensitiveDuringValidationToo()
    {
        var graph=new PrototypeGraph("a",[new("one","a","",PrototypeTrigger.Key,PrototypeAction.Back,Key:"Enter"),new("two","a","",PrototypeTrigger.Key,PrototypeAction.Back,Key:"enter")],[]);
        Assert.Contains(graph.Validate(new Dictionary<string,IReadOnlyCollection<string>>{{"a",Array.Empty<string>()}}),d=>d.Code=="FLOW001");
    }
}
