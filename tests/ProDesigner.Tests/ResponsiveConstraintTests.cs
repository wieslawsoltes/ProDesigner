using ProDesigner.Core;
using ProDesigner.DesignSystems;
using ProDesigner.Xaml;
using Xunit;

namespace ProDesigner.Tests;
public class ResponsiveConstraintTests
{
    [Theory] [InlineData(AxisAnchor.Start, 20, 100)] [InlineData(AxisAnchor.Center, 70, 100)] [InlineData(AxisAnchor.End, 120, 100)] [InlineData(AxisAnchor.Stretch, 20, 200)]
    public void AnchorsResolveAgainstTheReferenceViewport(AxisAnchor mode, double x, double width)
    {
        var result = ResponsiveLayout.Resolve(new(400,300,new(20,30,100,50),mode,AxisAnchor.Start),500,300);
        Assert.Equal(new LayoutBox(x,30,width,50), result);
    }
    [Fact] public void StretchExportsStandardGridProperties()
    {
        var tree = XamlSyntaxTree.Parse("<Grid><Button Width='100' Height='50' Canvas.Left='20'/></Grid>");
        var updated = XamlSyntaxTree.Parse(EditApplication.Apply(tree.Source, ResponsiveLayout.Apply(tree, tree.Root.Children[0],new(400,300,new(20,30,100,50),AxisAnchor.Stretch,AxisAnchor.End))));
        var child=updated.Root.Children[0]; Assert.Null(child.Get("Width")); Assert.Null(child.Get("Canvas.Left")); Assert.Equal("Stretch",child.Get("HorizontalAlignment")); Assert.Equal("20,0,280,220", child.Get("Margin"));
    }
    [Fact] public void CanvasConversionRetainsCommentsAndTransformsOnlyLayoutAttributes()
    {
        var tree=XamlSyntaxTree.Parse("<Canvas><!-- keep --><Button Width='100' Height='50' Canvas.Left='20' Canvas.Top='30'/></Canvas>");
        var result=ResponsiveLayout.ConvertCanvasToGrid(tree,tree.Root,400,300); Assert.Contains("<!-- keep -->",result); Assert.Equal("Grid",XamlSyntaxTree.Parse(result).Root.Name);
    }
    [Fact] public void ConversionRefusesUnknownMeasuredSizes() {var tree=XamlSyntaxTree.Parse("<Canvas><Button/></Canvas>");Assert.Throws<ArgumentException>(()=>ResponsiveLayout.ConvertCanvasToGrid(tree,tree.Root,400,300));}
}
