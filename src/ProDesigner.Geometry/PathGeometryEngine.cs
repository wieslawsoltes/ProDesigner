using ProDesigner.Authoring;
using SkiaSharp;

namespace ProDesigner.Geometry;

public enum PathBooleanOperation { Union, Intersect, Difference, Xor }
public enum StrokeCap { Butt, Round, Square }
public enum StrokeJoin { Miter, Round, Bevel }

/// <summary>Skia path operations producing reusable, standalone Avalonia path data. Native calculations use single-precision coordinates.</summary>
public static class PathGeometryEngine
{
    public static string Combine(IReadOnlyList<string> paths, PathBooleanOperation operation)
    {
        if (paths.Count is < 2 or > 128 || !Enum.IsDefined(operation)) throw new ArgumentException("Choose 2–128 paths and a supported operation.");
        using var result = Parse(paths[0]);
        foreach (var path in paths.Skip(1))
        {
            using var other = Parse(path);
            using var next = new SKPath();
            var mode = operation switch { PathBooleanOperation.Union => SKPathOp.Union, PathBooleanOperation.Intersect => SKPathOp.Intersect, PathBooleanOperation.Difference => SKPathOp.Difference, _ => SKPathOp.Xor };
            if (!result.Op(other, mode, next)) throw new InvalidOperationException("Skia could not resolve the path operation.");
            result.Reset(); result.AddPath(next); result.FillType = next.FillType;
        }
        return Export(result);
    }
    public static string Outline(string data, double width, StrokeCap cap = StrokeCap.Butt, StrokeJoin join = StrokeJoin.Miter, double miterLimit = 4)
    {
        if (!double.IsFinite(width) || width is <= 0 or > 100000 || !double.IsFinite(miterLimit) || miterLimit is < 1 or > 1000 || !Enum.IsDefined(cap) || !Enum.IsDefined(join)) throw new ArgumentOutOfRangeException(nameof(width));
        using var path = Parse(data); using var outline = new SKPath();
        using var paint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = (float)width, StrokeMiter = (float)miterLimit,
            StrokeCap = cap switch { StrokeCap.Round => SKStrokeCap.Round, StrokeCap.Square => SKStrokeCap.Square, _ => SKStrokeCap.Butt },
            StrokeJoin = join switch { StrokeJoin.Round => SKStrokeJoin.Round, StrokeJoin.Bevel => SKStrokeJoin.Bevel, _ => SKStrokeJoin.Miter } };
        if (!paint.GetFillPath(path, outline)) throw new InvalidOperationException("The stroke could not be converted to filled geometry.");
        return Export(outline);
    }
    public static bool Contains(string data, double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y)) return false;
        using var path = Parse(data); return path.Contains((float)x, (float)y);
    }
    private static SKPath Parse(string data)
    {
        var model = VectorPathModel.Parse(data);
        var path = SKPath.ParseSvgPathData(model.ToSvgData()) ?? throw new FormatException("Skia rejected the path data.");
        path.FillType = model.FillRule == VectorFillRule.EvenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding; return path;
    }
    private static string Export(SKPath path)
    {
        // Empty intersections are representable as an empty Move, not malformed/absent source.
        if (path.IsEmpty) return "M 0,0";
        var data = path.ToSvgPathData();
        var model = VectorPathModel.Parse(data); model.FillRule = path.FillType == SKPathFillType.EvenOdd ? VectorFillRule.EvenOdd : VectorFillRule.NonZero;
        model.ExplicitFillRule = true; return model.ToData();
    }
}
