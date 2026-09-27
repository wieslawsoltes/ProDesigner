using ProDesigner.Core;
using ProDesigner.Prototyping;
using Xunit;

namespace ProDesigner.Tests;
public class PrototypeTests
{
    private static Dictionary<string, IReadOnlyCollection<string>> Documents() => new() { ["Home"] = new[] { "Next", "Open" }, ["Detail"] = new[] { "Back" }, ["Overlay"] = new[] { "Close" } };
    private static PrototypeGraph Graph() => new("Home", [new("next", "Home", "Next", PrototypeTrigger.Click, PrototypeAction.Navigate, "Detail"), new("back", "Detail", "Back", PrototypeTrigger.Click, PrototypeAction.Back), new("open", "Home", "Open", PrototypeTrigger.Click, PrototypeAction.OpenOverlay, "Overlay"), new("close", "Overlay", "Close", PrototypeTrigger.Click, PrototypeAction.CloseOverlay)], []);
    [Fact] public void NavigationAndBackPreserveTheViewStack()
    {
        var player = new PrototypePlayer(Graph(), Documents()); Assert.True(player.Dispatch(PrototypeTrigger.Click, "Next")); Assert.Equal("Detail", player.State.Document);
        Assert.Equal(1, player.State.BackCount); Assert.True(player.Back()); Assert.Equal("Home", player.State.Document); Assert.False(player.Back());
    }
    [Fact] public void OverlayRoutesInputToTheTopmostView()
    {
        var player = new PrototypePlayer(Graph(), Documents()); player.Dispatch(PrototypeTrigger.Click, "Open");
        Assert.Equal("Overlay", Assert.Single(player.State.Overlays)); Assert.False(player.Dispatch(PrototypeTrigger.Click, "Next"));
        Assert.True(player.Dispatch(PrototypeTrigger.Click, "Close")); Assert.Empty(player.State.Overlays);
    }
    [Fact] public void DelaysUseAMonotonicClockAndDoNotFireEarly()
    {
        var graph = Graph() with { Links = [new("timer", "Home", "", PrototypeTrigger.AfterDelay, PrototypeAction.Navigate, "Detail", DelaySeconds: 1)] };
        var player = new PrototypePlayer(graph, Documents()); player.Advance(TimeSpan.FromSeconds(.9)); Assert.Equal("Home", player.State.Document);
        player.Advance(TimeSpan.FromSeconds(1)); Assert.Equal("Detail", player.State.Document);
        Assert.Throws<ArgumentOutOfRangeException>(() => player.Advance(TimeSpan.Zero));
    }
    [Fact] public void ConditionalTriggersReadPrototypeVariables()
    {
        var graph = Graph() with { Links = [new("next", "Home", "Next", PrototypeTrigger.Click, PrototypeAction.Navigate, "Detail", Condition: new("ready", "yes"))], Variables = new() { ["ready"] = "no" } };
        var player = new PrototypePlayer(graph, Documents()); Assert.False(player.Dispatch(PrototypeTrigger.Click, "Next"));
        player = new PrototypePlayer(graph with { Variables = new() { ["ready"] = "yes" } }, Documents()); Assert.True(player.Dispatch(PrototypeTrigger.Click, "Next"));
    }
    [Fact] public void MissingDestinationsAreErrors()
    {
        var graph = Graph() with { Links = [new("bad", "Home", "Next", PrototypeTrigger.Click, PrototypeAction.Navigate, "Missing")] };
        Assert.Contains(graph.Validate(Documents()), d => d.Severity == DiagnosticSeverity.Error); Assert.Throws<InvalidOperationException>(() => new PrototypePlayer(graph, Documents()));
    }
    [Fact] public void IdenticalTriggerConditionsAreRejected()
    {
        var graph = Graph(); graph = graph with { Links = [graph.Links[0], graph.Links[0] with { Id = "duplicate" }] };
        Assert.Contains(graph.Validate(Documents()), d => d.Message.Contains("unambiguous"));
    }
    [Fact] public void UnreachableViewsAreDiagnosedWithoutPreventingExecution()
    {
        var graph = new PrototypeGraph("Home", [], []); Assert.Contains(graph.Validate(Documents()), d => d.Code == "FLOW002"); _ = new PrototypePlayer(graph, Documents());
    }
}
