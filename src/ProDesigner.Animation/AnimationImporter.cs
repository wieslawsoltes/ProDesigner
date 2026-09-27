using System.Globalization;
using System.Xml.Linq;
using ProDesigner.Core;
using ProDesigner.Xaml;

namespace ProDesigner.Animation;

public sealed record AnimationImportResult(IReadOnlyList<AnimationClip> Clips, IReadOnlyList<DesignDiagnostic> Diagnostics);
/// <summary>Imports representable named-target scalar/color animations. Unsupported semantics are reported, not silently dropped.</summary>
public static class AnimationImporter
{
    public static AnimationImportResult Import(string source)
    {
        XamlSyntaxTree.Parse(source); // Enforce the same security/resource limits as the editing engine.
        var document = XDocument.Parse(source, LoadOptions.SetLineInfo);
        var clips = new List<AnimationClip>(); var diagnostics = new List<DesignDiagnostic>();
        foreach (var style in document.Descendants().Where(e => e.Name.LocalName == "Style"))
        {
            var selector = (string?)style.Attribute("Selector");
            foreach (var animation in style.Elements().Where(e => e.Name.LocalName == "Style.Animations").SelectMany(e => e.Elements()).Where(e => e.Name.LocalName == "Animation"))
            {
                try
                {
                    if (selector is null || !selector.StartsWith('#') || selector.Length < 2) throw new FormatException("Only exact #Name selectors can be represented by a timeline target.");
                    var target = selector[1..]; System.Xml.XmlConvert.VerifyNCName(target);
                    foreach (var attribute in animation.Attributes().Where(a => !a.IsNamespaceDeclaration))
                    {
                        var supported = attribute.Name.LocalName switch
                        {
                            "Duration" => true, "IterationCount" => attribute.Value is "1" or "INFINITE" or "Infinite" or "infinite",
                            "FillMode" => attribute.Value == "Forward", "Easing" => attribute.Value == "LinearEasing", "PlaybackDirection" => attribute.Value == "Normal",
                            _ => false
                        };
                        if (!supported) throw new FormatException($"Animation option '{attribute.Name}={attribute.Value}' is not represented by this timeline.");
                    }
                    var duration = TimeSpan.Parse((string?)animation.Attribute("Duration") ?? "0:0:1", CultureInfo.InvariantCulture).TotalSeconds;
                    if (duration <= 0 || !double.IsFinite(duration)) throw new FormatException("Animation duration must be positive.");
                    var clip = new AnimationClip { Name = target, DurationSeconds = duration, Loop = !string.Equals((string?)animation.Attribute("IterationCount") ?? "1", "1", StringComparison.Ordinal) };
                    foreach (var frame in animation.Elements())
                    {
                        if (frame.Name.LocalName != "KeyFrame") throw new FormatException("Unexpected animation child.");
                        var cue = (string?)frame.Attribute("Cue") ?? throw new FormatException("Each imported keyframe needs a Cue.");
                        var time = double.Parse(cue.TrimEnd('%'), CultureInfo.InvariantCulture) / (cue.EndsWith('%') ? 100 : 1);
                        var spline = frame.Attribute("KeySpline") is { } curve ? CubicSpline.Parse(curve.Value) : null;
                        foreach (var setter in frame.Elements())
                        {
                            if (setter.Name.LocalName != "Setter" || setter.HasElements) throw new FormatException("Only scalar or hexadecimal color setters are supported in imported keyframes.");
                            var property = (string?)setter.Attribute("Property") ?? throw new FormatException("A setter property is missing.");
                            var value = (string?)setter.Attribute("Value") ?? throw new FormatException("A setter value is missing.");
                            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) clip.GetTrack(target, property).SetKey(time, number, spline is null ? EasingKind.Linear : EasingKind.Custom, spline);
                            else clip.GetColorTrack(target, property).SetKey(time, ColorValue.Parse(value), spline);
                        }
                    }
                    if (clip.Tracks.Count + clip.ColorTracks.Count == 0) throw new FormatException("The animation contains no usable tracks.");
                    clips.Add(clip);
                }
                catch (Exception ex) when (ex is FormatException or ArgumentException or System.Xml.XmlException or OverflowException)
                { diagnostics.Add(new("ANIM001", ex.Message, DiagnosticSeverity.Warning)); }
            }
        }
        // Export creates one Animation per track. Coalesce compatible disjoint tracks to one editable clip.
        var merged = new List<AnimationClip>();
        foreach (var clip in clips)
        {
            var existing = merged.FirstOrDefault(c => c.DurationSeconds == clip.DurationSeconds && c.Loop == clip.Loop &&
                !c.Tracks.Any(t => clip.Tracks.Any(u => u.Target == t.Target && u.Property == t.Property)) &&
                !c.ColorTracks.Any(t => clip.ColorTracks.Any(u => u.Target == t.Target && u.Property == t.Property)));
            if (existing is null) merged.Add(clip);
            else { existing.Tracks.AddRange(clip.Tracks); existing.ColorTracks.AddRange(clip.ColorTracks); }
        }
        return new(merged, diagnostics);
    }
}
