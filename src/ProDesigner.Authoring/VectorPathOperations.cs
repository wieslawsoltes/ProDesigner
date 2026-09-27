namespace ProDesigner.Authoring;

/// <summary>Double-precision affine transform using column-vector convention.</summary>
public readonly record struct VectorTransform(double M11, double M12, double M21, double M22, double X, double Y)
{
    public static VectorTransform Identity => new(1, 0, 0, 1, 0, 0);
    public VectorPoint Apply(VectorPoint point) => new(M11 * point.X + M12 * point.Y + X, M21 * point.X + M22 * point.Y + Y);
}
public sealed record ArcCenter(VectorPoint Center, double RadiusX, double RadiusY, double Rotation, double StartAngle, double SweepAngle)
{
    public VectorPoint Evaluate(double t)
    {
        var angle = StartAngle + SweepAngle * t; var cos = Math.Cos(Rotation); var sin = Math.Sin(Rotation);
        return new(Center.X + RadiusX * Math.Cos(angle) * cos - RadiusY * Math.Sin(angle) * sin,
            Center.Y + RadiusX * Math.Cos(angle) * sin + RadiusY * Math.Sin(angle) * cos);
    }
}

/// <summary>Analytic arc conversion, de Casteljau subdivision and affine editing without flattening ellipses.</summary>
public static class VectorPathOperations
{
    // W3C SVG endpoint-to-center parameterization, including radii correction and both flags.
    public static ArcCenter? GetArc(VectorPoint start, VectorPoint end, ArcParameters value)
    {
        if (start == end || value.RadiusX == 0 || value.RadiusY == 0) return null;
        var phi = value.Rotation * Math.PI / 180; var cos = Math.Cos(phi); var sin = Math.Sin(phi);
        var dx = (start.X - end.X) / 2; var dy = (start.Y - end.Y) / 2;
        var x = cos * dx + sin * dy; var y = -sin * dx + cos * dy;
        var rx = value.RadiusX; var ry = value.RadiusY;
        // Work in logarithms for radii correction: tiny radii must not overflow x/rx or underflow rx².
        var lx = x == 0 ? double.NegativeInfinity : Math.Log(Math.Abs(x)) - Math.Log(rx);
        var ly = y == 0 ? double.NegativeInfinity : Math.Log(Math.Abs(y)) - Math.Log(ry);
        var maximum = Math.Max(lx, ly);
        var logScale = maximum + .5 * Math.Log(Math.Exp(2 * (lx - maximum)) + Math.Exp(2 * (ly - maximum)));
        if (logScale > 0) { rx = Math.Exp(Math.Log(rx) + logScale); ry = Math.Exp(Math.Log(ry) + logScale); }
        if (!double.IsFinite(rx) || !double.IsFinite(ry) || rx > 1e12 || ry > 1e12)
            throw new InvalidOperationException("Corrected ellipse exceeds the analytic geometry budget.");
        var u = x / rx; var v = y / ry;
        var length = double.Hypot(u, v);
        if (length == 0) return null;
        var factor = (value.LargeArc == value.Sweep ? -1 : 1) * Math.Sqrt(Math.Max(0, 1 - length * length));
        var cx = factor * rx * (v / length); var cy = -factor * ry * (u / length);
        var center = new VectorPoint(cos * cx - sin * cy + (start.X + end.X) / 2, sin * cx + cos * cy + (start.Y + end.Y) / 2);
        var ux = (x - cx) / rx; var uy = (y - cy) / ry; var vx = (-x - cx) / rx; var vy = (-y - cy) / ry;
        var angle = Math.Atan2(uy, ux); var sweep = Math.Atan2(ux * vy - uy * vx, ux * vx + uy * vy);
        if (!value.Sweep && sweep > 0) sweep -= 2 * Math.PI;
        if (value.Sweep && sweep < 0) sweep += 2 * Math.PI;
        return new(center, rx, ry, phi, angle, sweep);
    }
    public static VectorPoint Evaluate(VectorPathModel model, int index, double t)
    {
        model.Validate(); if (!double.IsFinite(t) || t is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(t));
        var (start, subpath) = Start(model, index); var segment = model.Segments[index];
        return EvaluateSegment(segment, start, subpath, t);
    }
    private static VectorPoint EvaluateSegment(PathSegment segment, VectorPoint start, VectorPoint subpath, double t, ArcCenter? arc = null)
    {
        if (t == 0 && segment.Command != PathCommand.Move) return start;
        if (t == 1) return segment.Command == PathCommand.Close ? subpath : segment.Points[^1];
        return segment.Command switch
        {
            PathCommand.Move => segment.Points[0], PathCommand.Close => VectorPoint.Lerp(start, subpath, t),
            PathCommand.Line => VectorPoint.Lerp(start, segment.Points[0], t),
            PathCommand.Quadratic => Bezier([start, .. segment.Points], t),
            PathCommand.Cubic => Bezier([start, .. segment.Points], t),
            PathCommand.Arc => (arc ?? GetArc(start, segment.Points[0], segment.Arc!))?.Evaluate(t) ?? VectorPoint.Lerp(start, segment.Points[0], t),
            _ => throw new InvalidOperationException()
        };
    }
    public static void Split(VectorPathModel model, int index, double t)
    {
        model.Validate();
        if (t is <= 0 or >= 1 || !double.IsFinite(t) || model.Segments.Count >= VectorPathModel.MaximumSegments) throw new ArgumentOutOfRangeException(nameof(t));
        var segment = model.Segments[index]; var (start, subpath) = Start(model, index);
        PathSegment first, second;
        switch (segment.Command)
        {
            case PathCommand.Line: first = new(PathCommand.Line, [VectorPoint.Lerp(start, segment.Points[0], t)]); second = segment; break;
            case PathCommand.Close: first = new(PathCommand.Line, [VectorPoint.Lerp(start, subpath, t)]); second = segment; break;
            case PathCommand.Cubic: case PathCommand.Quadratic:
                var points = new List<VectorPoint> { start }; points.AddRange(segment.Points);
                var left = new List<VectorPoint> { points[0] }; var right = new List<VectorPoint> { points[^1] };
                while (points.Count > 1)
                {
                    points = Enumerable.Range(0, points.Count - 1).Select(i => VectorPoint.Lerp(points[i], points[i + 1], t)).ToList();
                    left.Add(points[0]); right.Add(points[^1]);
                }
                right.Reverse(); first = new(segment.Command, left.Skip(1).ToArray()); second = new(segment.Command, right.Skip(1).ToArray()); break;
            case PathCommand.Arc:
                var arc = GetArc(start, segment.Points[0], segment.Arc!);
                if (arc is null) { first = new(PathCommand.Line, [VectorPoint.Lerp(start, segment.Points[0], t)]); second = new(PathCommand.Line, segment.Points); break; }
                var parameters = segment.Arc! with { RadiusX = arc.RadiusX, RadiusY = arc.RadiusY };
                first = new(PathCommand.Arc, [arc.Evaluate(t)], parameters with { LargeArc = Math.Abs(arc.SweepAngle * t) > Math.PI });
                second = new(PathCommand.Arc, segment.Points, parameters with { LargeArc = Math.Abs(arc.SweepAngle * (1 - t)) > Math.PI }); break;
            default: throw new InvalidOperationException("A Move is an anchor, not a splittable segment.");
        }
        model.Segments[index] = first; model.Segments.Insert(index + 1, second);
    }
    public static VectorPathModel Transform(VectorPathModel model, VectorTransform matrix)
    {
        model.Validate();
        if (new[] { matrix.M11, matrix.M12, matrix.M21, matrix.M22, matrix.X, matrix.Y }.Any(v => !double.IsFinite(v))) throw new ArgumentException("Transform must be finite.");
        if (matrix.M11 * matrix.M22 - matrix.M12 * matrix.M21 == 0 && model.Segments.Any(s => s.Command == PathCommand.Arc && s.Arc!.RadiusX > 0 && s.Arc.RadiusY > 0))
            throw new InvalidOperationException("A singular transform can fold an ellipse into overlapping line intervals. Flatten explicitly before projecting it; do not silently replace its trace by its endpoints.");
        var result = new VectorPathModel { FillRule = model.FillRule, ExplicitFillRule = model.ExplicitFillRule };
        foreach (var segment in model.Segments)
        {
            var points = segment.Points.Select(matrix.Apply).ToArray(); var arc = segment.Arc;
            if (segment.Command == PathCommand.Arc)
            {
                var phi = arc!.Rotation * Math.PI / 180; var c = Math.Cos(phi); var s = Math.Sin(phi);
                var a = (matrix.M11 * c + matrix.M12 * s) * arc.RadiusX;
                var b = (-matrix.M11 * s + matrix.M12 * c) * arc.RadiusY;
                var d = (matrix.M21 * c + matrix.M22 * s) * arc.RadiusX;
                var e = (-matrix.M21 * s + matrix.M22 * c) * arc.RadiusY;
                var xx = a * a + b * b; var xy = a * d + b * e; var yy = d * d + e * e;
                var spread = Math.Sqrt((xx - yy) * (xx - yy) + 4 * xy * xy);
                var major = Math.Max(0, (xx + yy + spread) / 2);
                // Product of eigenvalues avoids catastrophic cancellation for very eccentric ellipses.
                var determinant = a * e - b * d; var minor = major == 0 ? 0 : determinant * determinant / major;
                arc = new(Math.Sqrt(major), Math.Sqrt(Math.Max(0, minor)), .5 * Math.Atan2(2 * xy, xx - yy) * 180 / Math.PI,
                    arc.LargeArc, matrix.M11 * matrix.M22 - matrix.M12 * matrix.M21 < 0 ? !arc.Sweep : arc.Sweep);
            }
            result.Segments.Add(new(segment.Command, points, arc));
        }
        result.Validate(); return result;
    }
    public static IReadOnlyList<VectorPoint[]> Flatten(VectorPathModel model, double tolerance = .25, int maximumPoints = 65536)
    {
        model.Validate();
        if (!double.IsFinite(tolerance) || tolerance <= 0 || maximumPoints is < 2 or > 1_000_000) throw new ArgumentOutOfRangeException(nameof(tolerance));
        var contours = new List<VectorPoint[]>(); var points = new List<VectorPoint>(); var total = 0;
        void Add(VectorPoint p) { if (++total > maximumPoints) throw new InvalidOperationException("Flattened geometry exceeds the point budget."); points.Add(p); }
        var current = new VectorPoint(); var subpath = current;
        for (var index = 0; index < model.Segments.Count; index++)
        {
            var segment = model.Segments[index];
            if (segment.Command == PathCommand.Move)
            {
                if (points.Count > 0) contours.Add(points.ToArray()); points.Clear(); current = subpath = segment.Points[0]; Add(current); continue;
            }
            var start = current; var arc = segment.Command == PathCommand.Arc ? GetArc(start, segment.Points[0], segment.Arc!) : null;
            VectorPoint At(double t) => EvaluateSegment(segment, start, subpath, t, arc);
            // Quarter/midpoint error checks detect inflections that a midpoint-only subdivision misses.
            void Subdivide(double t0, VectorPoint p0, double t1, VectorPoint p1, int depth)
            {
                var t = (t0 + t1) / 2; var middle = At(t);
                var q1 = At(t0 + (t1 - t0) / 4); var q3 = At(t0 + 3 * (t1 - t0) / 4);
                if (Math.Max(Distance(middle, VectorPoint.Lerp(p0, p1, .5)), Math.Max(Distance(q1, VectorPoint.Lerp(p0, p1, .25)), Distance(q3, VectorPoint.Lerp(p0, p1, .75)))) <= tolerance)
                { Add(p1); return; }
                if (depth >= 20) throw new InvalidOperationException("Flattening tolerance could not be reached within the recursion budget.");
                Subdivide(t0, p0, t, middle, depth + 1); Subdivide(t, middle, t1, p1, depth + 1);
            }
            Subdivide(0, At(0), 1, At(1), 0); current = At(1);
        }
        if (points.Count > 0) contours.Add(points.ToArray()); return contours;
    }
    private static double Distance(VectorPoint a, VectorPoint b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    private static VectorPoint Bezier(IReadOnlyList<VectorPoint> points, double t)
    {
        var p = points.ToArray();
        for (var count = p.Length - 1; count > 0; count--) for (var i = 0; i < count; i++) p[i] = VectorPoint.Lerp(p[i], p[i + 1], t);
        return p[0];
    }
    private static (VectorPoint Start, VectorPoint Subpath) Start(VectorPathModel model, int index)
    {
        var current = new VectorPoint(); var subpath = current;
        for (var i = 0; i < index; i++)
        {
            var segment = model.Segments[i];
            if (segment.Command == PathCommand.Move) subpath = segment.Points[0];
            current = segment.Command == PathCommand.Close ? subpath : segment.Points[^1];
        }
        return (current, subpath);
    }
}
