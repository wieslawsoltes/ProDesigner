using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using ProDesigner.Core;
using ProDesigner.Design;
using ProDesigner.Preview;
using Xunit;

namespace ProDesigner.Tests;

public class PreviewDeltaTests
{
    private const string Xaml = "<UserControl xmlns='https://github.com/avaloniaui'><Canvas><Button Name='Go' Width='120' Height='40' Content='Go'/><TextBox Name='Input' Text='Initial'/></Canvas></UserControl>";
    [AvaloniaFact] public void ScalarEditsReuseControlsAndPreserveUnrelatedInteractiveState()
    {
        var session = new DesignerSession(Xaml); var surface = new DesignSurface { Profiles = [new("Desktop", 640, 480)] }; surface.Attach(session);
        var button = Assert.IsType<Button>(surface.GetPreviewControl("Go")); var input = Assert.IsType<TextBox>(surface.GetPreviewControl("Input"));
        input.Text = "Interactive state"; session.Select("0/0/0"); session.SetProperty("Width", "222"); surface.Rebuild();
        Assert.Equal(PreviewRefreshKind.PropertyDelta, surface.LastRefresh); Assert.Same(button, surface.GetPreviewControl("Go"));
        Assert.Equal(222, button.Width); Assert.Equal("Interactive state", input.Text); Assert.Equal(1, surface.FullBuildCount);
        surface.Rebuild(); Assert.Equal(PreviewRefreshKind.Unchanged, surface.LastRefresh);
        session.Undo(); surface.Rebuild(); Assert.Equal(120, button.Width); Assert.Same(button, surface.GetPreviewControl("Go"));
    }
    [AvaloniaFact] public void StructuralEditsAndPropertyResetFallBackToAFullBuild()
    {
        var session = new DesignerSession(Xaml); var surface = new DesignSurface(); surface.Attach(session);
        var first = surface.GetPreviewControl("Go"); session.Select("0/0/0"); session.SetProperty("Width", null); surface.Rebuild();
        Assert.Equal(PreviewRefreshKind.FullBuild, surface.LastRefresh); Assert.NotSame(first, surface.GetPreviewControl("Go"));
        first = surface.GetPreviewControl("Go"); session.SetSource(session.Source.Replace("<Button", "<!-- retain source change --><Button")); surface.Rebuild();
        Assert.Equal(PreviewRefreshKind.FullBuild, surface.LastRefresh); Assert.NotSame(first, surface.GetPreviewControl("Go"));
    }
    [AvaloniaFact] public void ProfilesSampleDataAndAnimationInvalidationAreNotMistakenForNoOps()
    {
        var session = new DesignerSession(Xaml); var surface = new DesignSurface(); surface.Attach(session); var original = surface.GetPreviewControl("Go");
        surface.ApplyAnimation("Go", "Width", 444); Assert.Equal(444, original!.Width); surface.Rebuild(); Assert.Equal(120, surface.GetPreviewControl("Go")!.Width);
        Assert.Equal(PreviewRefreshKind.FullBuild, surface.LastRefresh);
        surface.SampleData = new Dictionary<string,string>{{"Title","New"}}; surface.Rebuild(); Assert.Equal(PreviewRefreshKind.FullBuild, surface.LastRefresh);
        surface.Profiles = [new PreviewProfile("Phone",320,640)]; surface.Rebuild(); Assert.Equal(PreviewRefreshKind.FullBuild, surface.LastRefresh);
    }
    [AvaloniaFact] public void InvalidPropertyValueProducesDiagnosticsInsteadOfLeavingAPartialDelta()
    {
        var session = new DesignerSession(Xaml); var surface = new DesignSurface(); surface.Attach(session); session.Select("0/0/0");
        session.SetProperty("Width", "not-a-number"); surface.Rebuild(); Assert.Equal(PreviewRefreshKind.FullBuild, surface.LastRefresh); Assert.NotEmpty(surface.Diagnostics);
        session.SetProperty("Width", "180"); surface.Rebuild(); Assert.Empty(surface.Diagnostics); Assert.Equal(180, surface.GetPreviewControl("Go")!.Width);
    }
}
