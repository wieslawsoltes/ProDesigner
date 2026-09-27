using System.Globalization;
using System.Xml;

namespace ProDesigner.Animation;

public sealed record CubicSpline(double X1, double Y1, double X2, double Y2)
{
    public static CubicSpline Linear { get; } = new(0, 0, 1, 1);
    public void Validate()
    {
        if (X1 is < 0 or > 1 || X2 is < 0 or > 1 || new[] { X1, Y1, X2, Y2 }.Any(n => !double.IsFinite(n))) throw new ArgumentOutOfRangeException(nameof(X1));
    }
    public double Evaluate(double t) => this == Linear ? Math.Clamp(t, 0, 1) : Easing.CubicBezier(t, X1, Y1, X2, Y2);
    public override string ToString() => FormattableString.Invariant($"{X1:R},{Y1:R},{X2:R},{Y2:R}");
    public static CubicSpline Parse(string value)
    {
        var values = value.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries).Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
        if (values.Length != 4) throw new FormatException("A spline requires four coordinates.");
        var spline = new CubicSpline(values[0], values[1], values[2], values[3]); spline.Validate(); return spline;
    }
}
public readonly record struct ColorValue(byte A, byte R, byte G, byte B)
{
    public static ColorValue Parse(string value)
    {
        if (!value.StartsWith('#') || value.Length is not 7 and not 9) throw new FormatException("Use #RRGGBB or #AARRGGBB for animation colors.");
        var argb = uint.Parse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        if (value.Length == 7) argb |= 0xff000000;
        return new((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
    }
    public override string ToString() => $"#{A:X2}{R:X2}{G:X2}{B:X2}";
    public static ColorValue Lerp(ColorValue a, ColorValue b, double progress)
    {
        static byte Channel(byte a, byte b, double t) => (byte)Math.Clamp(Math.Round(a + (b - a) * t), 0, 255);
        return new(Channel(a.A, b.A, progress), Channel(a.R, b.R, progress), Channel(a.G, b.G, progress), Channel(a.B, b.B, progress));
    }
}
public sealed record ColorKeyframe(double Time, ColorValue Value, CubicSpline? Spline = null);
public sealed class ColorAnimationTrack
{
    private readonly List<ColorKeyframe> _keys = [];
    public string Target { get; }
    public string Property { get; }
    public IReadOnlyList<ColorKeyframe> Keys => _keys;
    public ColorAnimationTrack(string target, string property) { XmlConvert.VerifyNCName(target); XmlConvert.VerifyName(property); Target = target; Property = property; }
    public void SetKey(double time, ColorValue value, CubicSpline? spline = null)
    {
        if (!double.IsFinite(time) || time is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(time));
        spline?.Validate(); _keys.RemoveAll(k => Math.Abs(k.Time - time) < .000001); _keys.Add(new(time, value, spline)); _keys.Sort((a, b) => a.Time.CompareTo(b.Time));
    }
    public void RemoveKey(double time) => _keys.RemoveAll(k => Math.Abs(k.Time - time) < .000001);
    public ColorValue Evaluate(double time)
    {
        if (_keys.Count == 0) return default;
        if (time <= _keys[0].Time) return _keys[0].Value;
        for (var i = 1; i < _keys.Count; i++)
            if (time <= _keys[i].Time)
            {
                var a = _keys[i - 1]; var b = _keys[i]; var t = (time - a.Time) / (b.Time - a.Time);
                return ColorValue.Lerp(a.Value, b.Value, b.Spline?.Evaluate(t) ?? t);
            }
        return _keys[^1].Value;
    }
}
