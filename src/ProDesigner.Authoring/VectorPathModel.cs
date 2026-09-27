using System.Globalization;

namespace ProDesigner.Authoring;

public readonly record struct VectorPoint(double X, double Y)
{
    public override string ToString() => FormattableString.Invariant($"{X:G17},{Y:G17}");
    public static VectorPoint Lerp(VectorPoint a, VectorPoint b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
}
public enum PathCommand { Move, Line, Cubic, Quadratic, Close, Arc }
public enum VectorFillRule { EvenOdd, NonZero }
public sealed record ArcParameters(double RadiusX, double RadiusY, double Rotation, bool LargeArc, bool Sweep);
public sealed record PathSegment(PathCommand Command, IReadOnlyList<VectorPoint> Points, ArcParameters? Arc = null);

/// <summary>Editable SVG/Avalonia geometry: M/L/H/V/C/S/Q/T/A/Z, relative forms, and Avalonia F0/F1 fill rules.</summary>
/// <remarks>Shorthand is expanded; arcs stay analytic. Canonical output preserves double precision, not lexical formatting.</remarks>
public sealed class VectorPathModel
{
    public const int MaximumSegments = 4096;
    public List<PathSegment> Segments { get; } = [];
    public VectorFillRule FillRule { get; set; }
    public bool ExplicitFillRule { get; set; }
    public string ToData()
    {
        Validate();
        return (ExplicitFillRule || FillRule == VectorFillRule.NonZero ? (FillRule == VectorFillRule.NonZero ? "F1 " : "F0 ") : "") + ToSvgData();
    }
    public string ToSvgData() => string.Join(" ", Segments.Select(s => s.Command switch
    {
        PathCommand.Move => "M " + s.Points[0], PathCommand.Line => "L " + s.Points[0],
        PathCommand.Cubic => "C " + string.Join(" ", s.Points), PathCommand.Quadratic => "Q " + string.Join(" ", s.Points),
        PathCommand.Arc => FormattableString.Invariant($"A {s.Arc!.RadiusX:G17},{s.Arc.RadiusY:G17} {s.Arc.Rotation:G17} {(s.Arc.LargeArc ? 1 : 0)},{(s.Arc.Sweep ? 1 : 0)} {s.Points[0]}"),
        _ => "Z"
    }));
    public void SetPoint(int segment, int point, VectorPoint value)
    {
        ValidatePoint(value);
        var previous = Segments[segment]; var points = previous.Points.ToArray(); points[point] = value;
        Segments[segment] = previous with { Points = points };
    }
    public void SetArc(int segment, ArcParameters value)
    {
        if (Segments[segment].Command != PathCommand.Arc) throw new InvalidOperationException("Select an elliptical arc segment.");
        ValidateArc(value); Segments[segment] = Segments[segment] with { Arc = value };
    }
    public void Validate()
    {
        if (Segments.Count is < 1 or > MaximumSegments || Segments[0].Command != PathCommand.Move || !Enum.IsDefined(FillRule)) throw new FormatException("A bounded path must begin with Move.");
        foreach (var segment in Segments)
        {
            if (segment is null || !Enum.IsDefined(segment.Command) || segment.Points is null) throw new FormatException("Invalid path segment.");
            var count = segment.Command switch { PathCommand.Close => 0, PathCommand.Cubic => 3, PathCommand.Quadratic => 2, _ => 1 };
            if (segment.Points.Count != count) throw new FormatException("Wrong number of path control points.");
            foreach (var point in segment.Points) ValidatePoint(point);
            if (segment.Command == PathCommand.Arc) ValidateArc(segment.Arc ?? throw new FormatException("Arc parameters missing."));
        }
    }
    private static void ValidatePoint(VectorPoint point)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || Math.Abs(point.X) > 1e9 || Math.Abs(point.Y) > 1e9)
            throw new ArgumentOutOfRangeException(nameof(point), "Coordinates must be finite and within ±1e9.");
    }
    private static void ValidateArc(ArcParameters arc)
    {
        if (!double.IsFinite(arc.RadiusX) || !double.IsFinite(arc.RadiusY) || !double.IsFinite(arc.Rotation) ||
            arc.RadiusX is < 0 or > 1e9 || arc.RadiusY is < 0 or > 1e9 || Math.Abs(arc.Rotation) > 1e9) throw new FormatException("Invalid elliptical arc parameters.");
    }
    public static VectorPathModel Parse(string data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length > 262144) throw new FormatException("Path data exceeds the 256 KiB editing limit.");
        var reader = new PathDataReader(data); var result = new VectorPathModel();
        var current = new VectorPoint(); var start = current; var cubicControl = current; var quadraticControl = current;
        char command = '\0', previous = '\0';
        reader.Skip();
        if (reader.Peek is 'F' or 'f')
        {
            reader.Take(); result.FillRule = reader.Flag() ? VectorFillRule.NonZero : VectorFillRule.EvenOdd; result.ExplicitFillRule = true;
        }
        while (true)
        {
            reader.Skip(); if (reader.End) break;
            if (char.IsAsciiLetter(reader.Peek)) command = reader.Take();
            var upper = char.ToUpperInvariant(command); var relative = char.IsLower(command);
            if (result.Segments.Count == 0 && upper != 'M') throw new FormatException("A path must begin with Move.");
            VectorPoint Point()
            {
                var x = reader.Number(); var y = reader.Number();
                return new(x + (relative ? current.X : 0), y + (relative ? current.Y : 0));
            }
            PathSegment segment;
            switch (upper)
            {
                case 'M': case 'L':
                    segment = new(upper == 'M' ? PathCommand.Move : PathCommand.Line, [Point()]); break;
                case 'H': segment = new(PathCommand.Line, [new(reader.Number() + (relative ? current.X : 0), current.Y)]); break;
                case 'V': segment = new(PathCommand.Line, [new(current.X, reader.Number() + (relative ? current.Y : 0))]); break;
                case 'C': segment = new(PathCommand.Cubic, [Point(), Point(), Point()]); cubicControl = segment.Points[1]; break;
                case 'S':
                    var reflectedC = previous is 'C' or 'S' ? new VectorPoint(2 * current.X - cubicControl.X, 2 * current.Y - cubicControl.Y) : current;
                    segment = new(PathCommand.Cubic, [reflectedC, Point(), Point()]); cubicControl = segment.Points[1]; break;
                case 'Q': segment = new(PathCommand.Quadratic, [Point(), Point()]); quadraticControl = segment.Points[0]; break;
                case 'T':
                    var reflectedQ = previous is 'Q' or 'T' ? new VectorPoint(2 * current.X - quadraticControl.X, 2 * current.Y - quadraticControl.Y) : current;
                    segment = new(PathCommand.Quadratic, [reflectedQ, Point()]); quadraticControl = reflectedQ; break;
                case 'A':
                    var rx = Math.Abs(reader.Number()); var ry = Math.Abs(reader.Number()); var rotation = reader.Number() % 360;
                    var large = reader.Flag(); var sweep = reader.Flag();
                    segment = new(PathCommand.Arc, [Point()], new(rx, ry, rotation, large, sweep)); break;
                case 'Z': segment = new(PathCommand.Close, []); current = start; command = '\0'; break;
                default: throw new FormatException($"Unknown or missing path command '{command}' at {reader.Position}.");
            }
            result.Segments.Add(segment);
            if (segment.Points.Count > 0) current = segment.Points[^1];
            if (upper == 'M') { start = current; command = relative ? 'l' : 'L'; }
            previous = upper;
            if (result.Segments.Count > MaximumSegments) throw new FormatException("Too many path segments.");
        }
        result.Validate(); return result;
    }
}

internal sealed class PathDataReader(string data)
{
    private int _position;
    public int Position => _position;
    public bool End => _position == data.Length;
    public char Peek => End ? '\0' : data[_position];
    public char Take() => End ? throw new FormatException("Unexpected end of path data.") : data[_position++];
    public void Skip() { while (!End && (char.IsWhiteSpace(Peek) || Peek == ',')) _position++; }
    public bool Flag()
    {
        Skip(); var value = Take();
        return value switch { '0' => false, '1' => true, _ => throw new FormatException($"An arc/fill flag must be 0 or 1 at {_position - 1}.") };
    }
    public double Number()
    {
        Skip(); var begin = _position; var digits = 0;
        if (Peek is '+' or '-') Take();
        while (char.IsAsciiDigit(Peek)) { Take(); digits++; }
        if (Peek == '.') { Take(); while (char.IsAsciiDigit(Peek)) { Take(); digits++; } }
        if (digits == 0) throw new FormatException($"Expected a path coordinate at {begin}.");
        if (Peek is 'e' or 'E')
        {
            Take(); if (Peek is '+' or '-') Take(); var exponentStart = _position;
            while (char.IsAsciiDigit(Peek)) Take();
            if (_position == exponentStart) throw new FormatException($"Invalid coordinate exponent at {begin}.");
        }
        if (!double.TryParse(data.AsSpan(begin, _position - begin), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value) || Math.Abs(value) > 1e9)
            throw new FormatException($"Invalid or oversized coordinate at {begin}.");
        return value;
    }
}
