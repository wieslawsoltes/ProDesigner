using Avalonia.Headless.XUnit;
using ProDesigner.Geometry;
using ProDesigner.Workbench;
using ProDesigner.Xaml;
using Xunit;

namespace ProDesigner.Tests;
public class VectorWorkbenchTests
{
    [AvaloniaFact]
    public void BooleanWorkbenchOperationUsesCommonCoordinatesAndUndoRestoresExactSource()
    {
        using var workbench = new DesignerWorkbench();
        const string source = "<Canvas xmlns='https://github.com/avaloniaui' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'><!-- keep --><Path x:Name='A' Data='M0 0H100V100H0Z' Canvas.Left='20' Fill='Red'/><Path x:Name='B' Data='M0 0H100V100H0Z' Canvas.Left='70' Fill='Blue'/></Canvas>";
        workbench.OpenDocument("Boolean.axaml", source); workbench.SelectByName("A"); workbench.Session.Select(workbench.Session.Tree.Elements.Single(n => XamlNames.Name(n) == "B").Id, true);
        workbench.CombineSelectedPaths(PathBooleanOperation.Union);
        var path = Assert.Single(workbench.Session.Tree.Root.Children); Assert.Contains("<!-- keep -->", workbench.Session.Source);
        Assert.Equal("0", path.Get("Canvas.Left")); Assert.True(PathGeometryEngine.Contains(path.Get("Data")!, 160, 50)); Assert.Equal("Red", path.Get("Fill"));
        workbench.Execute("undo"); Assert.Equal(source, workbench.Session.Source);
    }
    [AvaloniaFact]
    public void CombiningReferencedNamedPathsIsRefusedBeforeSourceChanges()
    {
        using var workbench = new DesignerWorkbench();
        const string source = "<Canvas xmlns='https://github.com/avaloniaui' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'><Path x:Name='A' Data='M0 0H100V100Z'/><Path x:Name='B' Data='M0 0H100V100Z'/><TextBlock Text='{Binding Width, ElementName=B}'/></Canvas>";
        workbench.OpenDocument("Reference.axaml", source); workbench.SelectByName("A"); workbench.Session.Select("0/1", true);
        Assert.Throws<InvalidOperationException>(() => workbench.CombineSelectedPaths(PathBooleanOperation.Union)); Assert.Equal(source, workbench.Session.Source);
    }
    [AvaloniaFact]
    public void OutlineReplacesStrokeWithEquivalentFilledGeometry()
    {
        using var workbench = new DesignerWorkbench();
        workbench.OpenDocument("Stroke.axaml", "<Canvas xmlns='https://github.com/avaloniaui'><Path Name='P' Data='M10 10H100' Stroke='Purple' StrokeThickness='10'/></Canvas>");
        workbench.SelectByName("P"); workbench.OutlineSelectedStroke(); var path = workbench.Session.Primary!;
        Assert.Null(path.Get("Stroke")); Assert.Equal("Purple", path.Get("Fill")); Assert.True(PathGeometryEngine.Contains(path.Get("Data")!, 50, 12));
    }
}
