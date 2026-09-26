using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using ProDesigner.Core;
using ProDesigner.Preview;
using ProDesigner.Workbench;
using ProDesigner.Xaml;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(ProDesigner.Tests.TestApplication))]
namespace ProDesigner.Tests;
public static class TestApplication
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<DesignerApplication>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
public class WorkbenchTests
{
    [AvaloniaFact] public void EveryBundledSampleBuildsActualAvaloniaControls()
    {
        foreach (var source in Samples.Documents.Values)
        {
            var tree = XamlSyntaxTree.Parse(source); var preview = new PreviewBuilder().Build(tree, PreviewProfile.Defaults[0]);
            Assert.IsType<Border>(preview.Root); Assert.Equal(tree.Elements.Count(n => !n.IsProperty), preview.Controls.Count);
            Assert.Empty(preview.Diagnostics);
        }
    }
    [AvaloniaFact] public void UnknownControlsAreVisibleAndNeverDiscarded()
    {
        var tree = XamlSyntaxTree.Parse("<custom:Widget xmlns:custom=\"using:Untrusted\" />");
        var result = new PreviewBuilder().Build(tree, PreviewProfile.Defaults[0]); Assert.Contains(result.Diagnostics, d => d.Code == "PREVIEW001"); Assert.Equal(tree.Source, "<custom:Widget xmlns:custom=\"using:Untrusted\" />");
    }
    [AvaloniaFact] public void WorkbenchBindsCanvasPropertiesAndCodeToOneDocument()
    {
        using var workbench = new DesignerWorkbench();
        var window = new Window { Width = 1500, Height = 960, Content = workbench }; window.Show();
        workbench.SelectByName("RevenueCard"); workbench.SetProperty("Width", "264");
        Assert.Equal("264", workbench.Session.Primary!.Get("Width")); Assert.Contains("Width=\"264\"", workbench.Session.Source);
        workbench.Execute("undo"); Assert.Equal("220", workbench.Session.Primary!.Get("Width"));
        workbench.Execute("redo"); Assert.Equal("264", workbench.Session.Primary!.Get("Width")); window.Close();
    }
    [AvaloniaFact] public void ToolboxInsertionProducesNamedXamlAndUndoRestoresIt()
    {
        using var workbench = new DesignerWorkbench(); var before = workbench.Session.Source;
        workbench.SelectByName("DashboardCanvas"); workbench.InsertControl("Button"); Assert.Contains("x:Name=\"Button1\"", workbench.Session.Source);
        workbench.Execute("undo"); Assert.Equal(before, workbench.Session.Source);
    }
    [AvaloniaFact] public void ViewsKeepIndependentDocumentState()
    {
        using var workbench = new DesignerWorkbench(); var original = workbench.Documents[0];
        workbench.SelectByName("RevenueCard"); workbench.SetProperty("Width", "300"); workbench.SwitchDocument(workbench.Documents[1]);
        Assert.Equal("SignIn.axaml", workbench.ActiveDocumentName); workbench.SwitchDocument(original); Assert.Contains("Width=\"300\"", workbench.Session.Source);
    }
    [AvaloniaFact] public void NewViewIsUsableImmediately()
    {
        using var workbench = new DesignerWorkbench(); workbench.Execute("new"); workbench.InsertControl("Rectangle");
        Assert.Equal(4, workbench.Documents.Count); Assert.Contains("Rectangle", workbench.Session.Source); Assert.True(workbench.Session.IsValid);
    }
}
