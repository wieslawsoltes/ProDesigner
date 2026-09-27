using System.Globalization;
using ProDesigner.Design;

namespace ProDesigner.Authoring;

public readonly record struct VectorPoint(double X, double Y)
{
    public override string ToString() => FormattableString.Invariant($"{X:0.######},{Y:0.######}");
}
public enum PathCommand { Move, Line, Cubic, Quadratic, Close }
public sealed record PathSegment(PathCommand Command, IReadOnlyList<VectorPoint> Points);
/// <summary>Editable M/L/C/Q/Z geometry. Unknown SVG commands are rejected, never discarded.</summary>
public sealed class VectorPathModel
{
    public List<PathSegment> Segments { get; } = [];
    public string ToData() => string.Join(" ", Segments.Select(s => s.Command switch
    {
        PathCommand.Move => "M " + s.Points[0], PathCommand.Line => "L " + s.Points[0],
        PathCommand.Cubic => "C " + string.Join(" ", s.Points), PathCommand.Quadratic => "Q " + string.Join(" ", s.Points), _ => "Z"
    }));
    public void SetPoint(int segment, int point, VectorPoint value)
    {
        if (!double.IsFinite(value.X) || !double.IsFinite(value.Y)) throw new ArgumentOutOfRangeException(nameof(value));
        var previous = Segments[segment]; var points = previous.Points.ToArray(); points[point] = value;
        Segments[segment] = previous with { Points = points };
    }
    public static VectorPathModel Parse(string data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length > 65536) throw new ArgumentException("Path data exceeds the editing limit.");
        var result = new VectorPathModel(); var i = 0; char command = '\0'; var current = new VectorPoint(); var start = current;
        void Skip() { while (i < data.Length && (char.IsWhiteSpace(data[i]) || data[i] == ',')) i++; }
        double Read()
        {
            Skip(); var begin = i;
            if (i < data.Length && data[i] is '+' or '-') i++;
            while (i < data.Length && (char.IsDigit(data[i]) || data[i] == '.')) i++;
            if (i < data.Length && data[i] is 'e' or 'E') { i++; if (i < data.Length && data[i] is '+' or '-') i++; while (i < data.Length && char.IsDigit(data[i])) i++; }
            if (i == begin || !double.TryParse(data[begin..i], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value)) throw new FormatException($"Invalid path coordinate at {begin}.");
            return value;
        }
        while (true)
        {
            Skip(); if (i >= data.Length) break;
            if (char.IsLetter(data[i])) command = data[i++];
            var upper = char.ToUpperInvariant(command); var relative = char.IsLower(command);
            if (upper == 'Z') { result.Segments.Add(new(PathCommand.Close, [])); current = start; command = '\0'; continue; }
            var count = upper switch { 'M' or 'L' => 1, 'Q' => 2, 'C' => 3, _ => throw new FormatException($"Unsupported path command '{command}'. Supported commands: M, L, Q, C, Z (absolute or relative).") };
            var points = new List<VectorPoint>();
            for (var p = 0; p < count; p++) { var x = Read(); var y = Read(); points.Add(new(x + (relative ? current.X : 0), y + (relative ? current.Y : 0))); }
            if (result.Segments.Count == 0 && upper != 'M') throw new FormatException("A path must begin with Move.");
            result.Segments.Add(new(upper switch { 'M' => PathCommand.Move, 'L' => PathCommand.Line, 'C' => PathCommand.Cubic, _ => PathCommand.Quadratic }, points));
            current = points[^1]; if (upper == 'M') { start = current; command = relative ? 'l' : 'L'; }
            if (result.Segments.Count > 4096) throw new FormatException("Too many path segments.");
        }
        if (result.Segments.Count == 0) throw new FormatException("Path geometry is empty.");
        return result;
    }
}
