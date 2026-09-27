using ProDesigner.Authoring;
using ProDesigner.Geometry;
using Xunit;

namespace ProDesigner.Tests;

public class VectorParityTests
{
    [Theory]
    [InlineData("M0 0 H30 v40 h-30 Z")]
    [InlineData("M10 10 C20 30 40 50 60 70 s20 30 40 50")]
    [InlineData("M0 0 Q10 20 30 40 t50 60 T80 90")]
    [InlineData("F1 M0 0 A20 30 45 0 1 40 50 a30 40 0 1 0 50 60Z")]
    [InlineData("M.5-.5L1e2-2e1A-10-20 0 0110 20")]
    public void FullPathGrammarRoundTripsCanonicalGeometry(string source)
    {
        var path = VectorPathModel.Parse(source); Assert.Equal(path.ToData(), VectorPathModel.Parse(path.ToData()).ToData());
    }
    [Fact]
    public void SmoothCommandsReflectOnlyTheCorrectPreviousControl()
    {
        var path = VectorPathModel.Parse("M0 0 C10 20 30 40 50 60 S70 80 90 100 L110 120 S130 140 150 160 Q170 180 190 200 T210 220");
        Assert.Equal(new VectorPoint(70, 80), path.Segments[2].Points[0]);
        Assert.Equal(new VectorPoint(110, 120), path.Segments[4].Points[0]);
        Assert.Equal(new VectorPoint(210, 220), path.Segments[6].Points[0]);
    }
    [Theory]
    [InlineData("M0 0 A10 10 0 2 1 20 20")]
    [InlineData("M0 0 A10 10 0 0 1")]
    [InlineData("M0 0 L1e 2")]
    [InlineData("M0 0 S10 20")]
    [InlineData("F2 M0 0")]
    public void InvalidCommandArgumentsAreNeverSilentlyAccepted(string source) => Assert.Throws<FormatException>(() => VectorPathModel.Parse(source));
    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public void AnalyticArcFlagsChooseTheCorrectAngularSweep(bool large, bool sweep)
    {
        var arc = VectorPathOperations.GetArc(new(0, 0), new(50, 40), new(50, 80, 25, large, sweep))!;
        Assert.Equal(sweep, arc.SweepAngle > 0); Assert.Equal(large, Math.Abs(arc.SweepAngle) > Math.PI);
        Near(new(0, 0), arc.Evaluate(0)); Near(new(50, 40), arc.Evaluate(1));
    }
    [Fact]
    public void TooSmallRadiiAreCorrectedAndTinyRadiiDoNotOverflow()
    {
        var arc = VectorPathOperations.GetArc(new(0, 0), new(100, 0), new(1e-200, 1e-200, 0, false, true))!;
        Assert.InRange(arc.RadiusX, 49.999999, 50.000001); Near(new(100, 0), arc.Evaluate(1));
        Assert.Null(VectorPathOperations.GetArc(new(1, 2), new(1, 2), new(20, 30, 0, true, true)));
    }
    [Theory]
    [InlineData("M0 0 L100 50")]
    [InlineData("M0 0 C30 100 80 -50 100 50")]
    [InlineData("M0 0 Q30 100 100 50")]
    [InlineData("M0 0 A80 50 30 1 1 100 50")]
    public void SubdivisionPreservesTheAnalyticCurve(string source)
    {
        var before = VectorPathModel.Parse(source); var after = VectorPathModel.Parse(source);
        VectorPathOperations.Split(after, 1, .3);
        for (var step = 0; step <= 20; step++)
        {
            var t = step / 20d;
            Near(VectorPathOperations.Evaluate(before, 1, t), t <= .3 ? VectorPathOperations.Evaluate(after, 1, t / .3) : VectorPathOperations.Evaluate(after, 2, (t - .3) / .7), 1e-5);
        }
    }
    [Fact]
    public void AffineShearAndReflectionPreserveEllipticalArcs()
    {
        var model = VectorPathModel.Parse("M10 20 A100 40 30 1 1 150 90");
        foreach (var matrix in new[] { new VectorTransform(2, .7, .1, 1.2, 15, -8), new VectorTransform(-2, .7, .1, 1.2, 15, -8) })
        {
            var transformed = VectorPathOperations.Transform(model, matrix);
            Assert.Equal(PathCommand.Arc, transformed.Segments[1].Command);
            for (var i = 0; i <= 20; i++) Near(matrix.Apply(VectorPathOperations.Evaluate(model, 1, i / 20d)), VectorPathOperations.Evaluate(transformed, 1, i / 20d), 1e-5);
        }
    }
    [Fact]
    public void FlatteningFindsCurveInflectionsAndEnforcesItsPointBudget()
    {
        var model = VectorPathModel.Parse("M0 0 C0 100 100 -100 100 0Z");
        var contour = Assert.Single(VectorPathOperations.Flatten(model, .1)); Assert.True(contour.Length > 10);
        Assert.Contains(contour, p => p.Y > 20); Assert.Contains(contour, p => p.Y < -20);
        Assert.Throws<InvalidOperationException>(() => VectorPathOperations.Flatten(model, .001, maximumPoints: 4));
    }
    [Theory]
    [InlineData(PathBooleanOperation.Union, true, true, true)]
    [InlineData(PathBooleanOperation.Intersect, false, true, false)]
    [InlineData(PathBooleanOperation.Difference, true, false, false)]
    [InlineData(PathBooleanOperation.Xor, true, false, true)]
    public void BooleanOperationsHaveTheExpectedFilledRegions(PathBooleanOperation op, bool left, bool center, bool right)
    {
        var result = PathGeometryEngine.Combine(["M0 0H100V100H0Z", "M50 0H150V100H50Z"], op);
        Assert.Equal(left, PathGeometryEngine.Contains(result, 25, 50)); Assert.Equal(center, PathGeometryEngine.Contains(result, 75, 50)); Assert.Equal(right, PathGeometryEngine.Contains(result, 125, 50));
    }
    [Fact]
    public void StrokeOutlineExpandsCapsAndProducesOrdinaryPathData()
    {
        var data = PathGeometryEngine.Outline("M10 10H100", 10, StrokeCap.Round, StrokeJoin.Round);
        Assert.True(PathGeometryEngine.Contains(data, 7, 10)); Assert.True(PathGeometryEngine.Contains(data, 103, 10)); Assert.False(PathGeometryEngine.Contains(data, 105.1, 10));
        Assert.Equal(data, VectorPathModel.Parse(data).ToData());
    }
    private static void Near(VectorPoint a, VectorPoint b, double tolerance = 1e-6)
    {
        Assert.InRange(Math.Abs(a.X - b.X), 0, tolerance); Assert.InRange(Math.Abs(a.Y - b.Y), 0, tolerance);
    }
}
