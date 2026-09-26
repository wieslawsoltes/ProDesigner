using ProDesigner.Core;
using ProDesigner.Design;
using ProDesigner.Xaml;
using Xunit;

namespace ProDesigner.Tests;
public class LayoutCoordinateTests
{
    [Fact]
    public void AlignmentConvertsArtboardBoundsToCanvasCoordinates()
    {
        var tree = XamlSyntaxTree.Parse("<Canvas><Button Canvas.Left=\"20\"/><Button Canvas.Left=\"90\"/></Canvas>");
        var bounds = new Dictionary<string, DesignRect> { ["0"] = new(176, 64, 500, 500), ["0/0"] = new(196, 84, 50, 30), ["0/1"] = new(266, 154, 50, 30) };
        var updated = XamlSyntaxTree.Parse(EditApplication.Apply(tree.Source, LayoutEngine.Align(tree, bounds, ["0/0", "0/1"], Alignment.Left)));
        Assert.All(updated.Root.Children, n => Assert.Equal("20", n.Get("Canvas.Left")));
    }
    [Fact]
    public void DistributionPreservesTheFirstAndLastLocalPositions()
    {
        var tree = XamlSyntaxTree.Parse("<Canvas><Button/><Button/><Button/></Canvas>");
        var bounds = new Dictionary<string, DesignRect> { ["0"] = new(176, 64, 500, 500), ["0/0"] = new(196, 84, 50, 30), ["0/1"] = new(266, 84, 50, 30), ["0/2"] = new(396, 84, 50, 30) };
        var updated = XamlSyntaxTree.Parse(EditApplication.Apply(tree.Source, LayoutEngine.Distribute(tree, bounds, ["0/0", "0/1", "0/2"], true)));
        Assert.Equal(new[] { "20", "120", "220" }, updated.Root.Children.Select(n => n.Get("Canvas.Left")));
    }
}
