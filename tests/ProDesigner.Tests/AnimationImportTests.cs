using ProDesigner.Animation;
using Xunit;

namespace ProDesigner.Tests;
public class AnimationImportTests
{
    private static string Wrap(string styles)=>"<UserControl xmlns='https://github.com/avaloniaui'><UserControl.Styles>"+styles+"</UserControl.Styles></UserControl>";
    [Theory] [InlineData(EasingKind.Linear)] [InlineData(EasingKind.EaseIn)] [InlineData(EasingKind.EaseOut)] [InlineData(EasingKind.EaseInOut)]
    public void ImportedExportMatchesOriginalInterpolation(EasingKind easing)
    {
        var clip=new AnimationClip();var track=clip.GetTrack("Card","Opacity");track.SetKey(0,.1);track.SetKey(1,.9,easing);
        var imported=AnimationImporter.Import(Wrap(clip.ToAvaloniaStyles()));Assert.Empty(imported.Diagnostics);var restored=Assert.Single(imported.Clips).Tracks.Single();
        for(var i=0;i<=20;i++) Assert.InRange(Math.Abs(track.Evaluate(i/20d)-restored.Evaluate(i/20d)),0,1e-8);
    }
    [Fact] public void CustomSplineSurvivesImportAndExport()
    {
        var clip=new AnimationClip();var track=clip.GetTrack("Card","Width");track.SetKey(0,10);track.SetKey(1,200,EasingKind.Custom,new(.1,-.2,.9,1.2));
        var restored=AnimationImporter.Import(Wrap(clip.ToAvaloniaStyles())).Clips.Single().Tracks.Single();Assert.Equal(track.Keys[1].Spline,restored.Keys[1].Spline);
    }
    [Fact] public void ColorTrackRoundTripsThroughRealXaml()
    {
        var clip=new AnimationClip();var track=clip.GetColorTrack("Card","Background");track.SetKey(0,ColorValue.Parse("#FF0000"));track.SetKey(1,ColorValue.Parse("#0000FF"));
        var result=AnimationImporter.Import(Wrap(clip.ToAvaloniaStyles()));Assert.Empty(result.Diagnostics);Assert.Equal(track.Evaluate(.5),result.Clips.Single().ColorTracks.Single().Evaluate(.5));
    }
    [Fact] public void ImportReportsUnsupportedPlaybackInsteadOfChangingItsMeaning()
    {
        var result=AnimationImporter.Import(Wrap("<Style Selector='#Card'><Style.Animations><Animation Duration='0:0:1' PlaybackDirection='Alternate'><KeyFrame Cue='0%'><Setter Property='Opacity' Value='1'/></KeyFrame></Animation></Style.Animations></Style>"));
        Assert.Empty(result.Clips);Assert.NotEmpty(result.Diagnostics);
    }
    [Fact] public void IdenticalTimingMergesDisjointExportedTracks()
    {var clip=new AnimationClip();clip.GetTrack("A","Width").SetKey(0,100);clip.GetTrack("B","Height").SetKey(0,200);Assert.Equal(2,AnimationImporter.Import(Wrap(clip.ToAvaloniaStyles())).Clips.Single().Tracks.Count);}
}
