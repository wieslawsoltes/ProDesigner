using ProDesigner.Design;
using Xunit;

namespace ProDesigner.Tests;
public class ResizeTests
{
    [Theory]
    [InlineData(ResizeEdges.Left, 120, 100, 80, 80)]
    [InlineData(ResizeEdges.Right, 100, 100, 120, 80)]
    [InlineData(ResizeEdges.Top, 100, 120, 100, 60)]
    [InlineData(ResizeEdges.Bottom, 100, 100, 100, 100)]
    [InlineData(ResizeEdges.Left | ResizeEdges.Top, 120, 120, 80, 60)]
    [InlineData(ResizeEdges.Right | ResizeEdges.Bottom, 100, 100, 120, 100)]
    public void ResizingKeepsTheOppositeEdgesFixed(ResizeEdges edges, double x, double y, double w, double h)
    {
        Assert.Equal(new DesignRect(x, y, w, h), ResizeGeometry.Resize(new(100,100,100,80), edges, 20,20,false));
    }
    [Fact] public void AllEightHandlesAreInteractive() => Assert.Equal(8, ResizeGeometry.Handles(new(0,0,100,100)).Select(h => ResizeGeometry.HitTest(new(0,0,100,100), h.X,h.Y)).Distinct().Count());
    [Fact] public void ResizingCannotInvertTheBox() => Assert.Equal(8, ResizeGeometry.Resize(new(0,0,100,100), ResizeEdges.Left, 500,0,false).Width);
    [Fact] public void ShiftResizePreservesAspectRatio()
    {
        var resized = ResizeGeometry.Resize(new(0,0,100,50), ResizeEdges.Right|ResizeEdges.Bottom, 40,0,false,true);
        Assert.Equal(2, resized.Width/resized.Height);
    }
}
