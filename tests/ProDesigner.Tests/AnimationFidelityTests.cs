using System.Globalization;
using System.Xml.Linq;
using Avalonia.Animation;
using Avalonia.Headless.XUnit;
using ProDesigner.Animation;
using Xunit;

namespace ProDesigner.Tests;
public class AnimationFidelityTests
{
    // KeySpline derives from AvaloniaObject. Construct it on the shared headless UI dispatcher,
    // never on an ordinary xUnit worker that could initialize Avalonia's dispatcher first.
    [AvaloniaTheory]
    [InlineData(EasingKind.Linear)] [InlineData(EasingKind.EaseIn)] [InlineData(EasingKind.EaseOut)] [InlineData(EasingKind.EaseInOut)]
    public void ExportedAvaloniaSplinesMatchTimelineInterpolation(EasingKind easing)
    {
        var clip = new AnimationClip(); var track = clip.GetTrack("Card", "Opacity"); track.SetKey(0, .1); track.SetKey(1, .9, easing);
        var frames = XElement.Parse(clip.ToAvaloniaStyles()).Descendants("KeyFrame").Select(e => new
        {
            Time = double.Parse(e.Attribute("Cue")!.Value.TrimEnd('%'), CultureInfo.InvariantCulture) / 100,
            Value = double.Parse(e.Element("Setter")!.Attribute("Value")!.Value, CultureInfo.InvariantCulture),
            Spline = KeySpline.Parse(e.Attribute("KeySpline")!.Value, CultureInfo.InvariantCulture)
        }).ToArray();
        for (var step = 1; step < 20; step++)
        {
            var time = step / 20d; var end = Array.FindIndex(frames, k => k.Time >= time); var a = frames[end - 1]; var b = frames[end];
            var actual = a.Value + (b.Value - a.Value) * b.Spline.GetSplineProgress((time - a.Time) / (b.Time - a.Time));
            Assert.InRange(Math.Abs(track.Evaluate(time) - actual), 0, .001);
        }
    }
    [Fact] public void UnsupportedDiscontinuousEasingCannotBeSilentlyExported()
    {
        var clip = new AnimationClip(); var track = clip.GetTrack("Card", "Opacity"); track.SetKey(0, 0); track.SetKey(1, 1, EasingKind.Step);
        Assert.Throws<InvalidOperationException>(() => clip.ToAvaloniaStyles());
    }
}
