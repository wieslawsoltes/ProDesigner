using System.Globalization;
using System.Text;
using System.Xml;
using ProDesigner.Xaml;

namespace ProDesigner.Animation;

public enum EasingKind { Linear, EaseIn, EaseOut, EaseInOut, Step, Custom }
public sealed record Keyframe(double Time, double Value, EasingKind Easing = EasingKind.EaseInOut, CubicSpline? Spline = null);
public sealed class AnimationTrack
{
    private readonly List<Keyframe> _keys = [];
    public event Action? Changed;
    public string Target { get; }
    public string Property { get; }
    public IReadOnlyList<Keyframe> Keys => _keys;
    public AnimationTrack(string target, string property) { XmlConvert.VerifyNCName(target); XmlConvert.VerifyName(property); Target = target; Property = property; }
    public void SetKey(double time, double value, EasingKind easing = EasingKind.EaseInOut, CubicSpline? spline = null)
    {
        if (!double.IsFinite(time) || time < 0 || time > 1 || !double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(time));
        if (!Enum.IsDefined(easing)) throw new ArgumentOutOfRangeException(nameof(easing));
        if (easing == EasingKind.Custom && spline is null) throw new ArgumentException("Custom easing needs a spline.");
        spline?.Validate();
        _keys.RemoveAll(k => Math.Abs(k.Time - time) < 0.000001);
        _keys.Add(new(time, value, easing, spline)); _keys.Sort((a, b) => a.Time.CompareTo(b.Time)); Changed?.Invoke();
    }
    public void RemoveKey(double time) { if (_keys.RemoveAll(k => Math.Abs(k.Time - time) < 0.000001) > 0) Changed?.Invoke(); }
    public double Evaluate(double time)
    {
        if (_keys.Count == 0) return 0;
        if (time <= _keys[0].Time) return _keys[0].Value;
        if (time >= _keys[^1].Time) return _keys[^1].Value;
        for (var i = 1; i < _keys.Count; i++)
        {
            if (time > _keys[i].Time) continue;
            var a = _keys[i - 1]; var b = _keys[i]; var t = (time - a.Time) / (b.Time - a.Time);
            return a.Value + (b.Value - a.Value) * (b.Spline?.Evaluate(t) ?? Easing.Apply(t, b.Easing));
        }
        return _keys[^1].Value;
    }
}
public static class Easing
{
    public static double Apply(double t, EasingKind kind) => kind switch
    {
        EasingKind.EaseIn => t * t * t,
        EasingKind.EaseOut => 1 - Math.Pow(1 - t, 3),
        EasingKind.EaseInOut => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2,
        EasingKind.Step => t < 1 ? 0 : 1,
        _ => t
    };
    public static double CubicBezier(double x, double x1, double y1, double x2, double y2)
    {
        if (x1 is < 0 or > 1 || x2 is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(x1));
        x = Math.Clamp(x, 0, 1); var low = 0d; var high = 1d;
        static double Curve(double t, double a, double b) => 3 * (1 - t) * (1 - t) * t * a + 3 * (1 - t) * t * t * b + t * t * t;
        for (var i = 0; i < 40; i++) { var mid = (low + high) / 2; if (Curve(mid, x1, x2) < x) low = mid; else high = mid; }
        return Curve((low + high) / 2, y1, y2);
    }
}
public sealed class AnimationClip
{
    public string Name { get; set; } = "Entrance";
    public double DurationSeconds { get; set; } = 1;
    public bool Loop { get; set; }
    public List<AnimationTrack> Tracks { get; } = [];
    public List<ColorAnimationTrack> ColorTracks { get; } = [];
    public AnimationTrack GetTrack(string target, string property)
    {
        var track = Tracks.FirstOrDefault(t => t.Target == target && t.Property == property);
        if (track is not null) return track;
        track = new(target, property); Tracks.Add(track); return track;
    }
    public ColorAnimationTrack GetColorTrack(string target, string property)
    {
        var track = ColorTracks.FirstOrDefault(t => t.Target == target && t.Property == property);
        if (track is not null) return track;
        track = new(target, property); ColorTracks.Add(track); return track;
    }
    public string ToAvaloniaStyles()
    {
        if (!double.IsFinite(DurationSeconds) || DurationSeconds <= 0) throw new InvalidOperationException("Animation duration must be positive.");
        var sb = new StringBuilder();
        var duration = TimeSpan.FromSeconds(DurationSeconds).ToString("c", CultureInfo.InvariantCulture);
        foreach (var target in Tracks.GroupBy(t => t.Target))
        {
            sb.AppendLine($"<Style Selector=\"#{XamlEdits.Escape(target.Key)}\">");
            sb.AppendLine("  <Style.Animations>");
            foreach (var track in target)
            {
                sb.AppendLine($"    <Animation Duration=\"{duration}\" IterationCount=\"{(Loop ? "INFINITE" : "1")}\" FillMode=\"Forward\">");
                for (var index = 0; index < track.Keys.Count; index++)
                {
                    var key = track.Keys[index];
                    if (index > 0 && key.Easing == EasingKind.Step)
                        throw new InvalidOperationException("Step interpolation cannot be exported faithfully as a numeric Avalonia spline. Choose a continuous easing before export.");
                    if (index > 0 && key.Easing == EasingKind.EaseInOut)
                    {
                        var previous = track.Keys[index - 1];
                        AppendKey(sb, track.Property, (previous.Time + key.Time) / 2, (previous.Value + key.Value) / 2, EasingKind.EaseIn);
                        AppendKey(sb, track.Property, key.Time, key.Value, EasingKind.EaseOut);
                    }
                    else AppendKey(sb, track.Property, key.Time, key.Value, index == 0 ? EasingKind.Linear : key.Easing, key.Spline);
                }
                sb.AppendLine("    </Animation>");
            }
            sb.AppendLine("  </Style.Animations>\n</Style>");
        }
        foreach (var track in ColorTracks)
        {
            sb.AppendLine($"<Style Selector=\"#{XamlEdits.Escape(track.Target)}\"><Style.Animations><Animation Duration=\"{duration}\" IterationCount=\"{(Loop ? "INFINITE" : "1")}\" FillMode=\"Forward\">");
            foreach (var key in track.Keys)
                sb.AppendLine($"<KeyFrame Cue=\"{(key.Time * 100).ToString("R", CultureInfo.InvariantCulture)}%\" KeySpline=\"{key.Spline ?? CubicSpline.Linear}\"><Setter Property=\"{XamlEdits.Escape(track.Property)}\" Value=\"{key.Value}\" /></KeyFrame>");
            sb.AppendLine("</Animation></Style.Animations></Style>");
        }
        return sb.ToString();
    }
    private static void AppendKey(StringBuilder builder, string property, double time, double value, EasingKind easing, CubicSpline? custom = null)
    {
        var spline = custom?.ToString() ?? (easing switch
        {
            EasingKind.EaseIn => "0.3333333333333333,0,0.6666666666666666,0",
            EasingKind.EaseOut => "0.3333333333333333,1,0.6666666666666666,1",
            _ => "0,0,1,1"
        });
        builder.AppendLine($"      <KeyFrame Cue=\"{(time * 100).ToString("0.########", CultureInfo.InvariantCulture)}%\" KeySpline=\"{spline}\">");
        builder.AppendLine($"        <Setter Property=\"{XamlEdits.Escape(property)}\" Value=\"{value.ToString("R", CultureInfo.InvariantCulture)}\" />");
        builder.AppendLine("      </KeyFrame>");
    }

}
