using ProDesigner.Animation;
using ProDesigner.Core;
using ProDesigner.Persistence;
using Xunit;

namespace ProDesigner.Tests;
public class PersistenceTests
{
    private static WorkspaceState State(string source="<Grid/>") => new(1,"Test",0,1,[PreviewProfile.Defaults[0]],[new("View.axaml",source,"<Grid/>","class View {}",null,null,ClipState.Capture(new AnimationClip()),[])]);
    [Fact] public void WorkspaceRestoresInvalidIntermediateSourceExactly()
    {var json=WorkspaceCodec.Serialize(State("<Grid>broken"));Assert.Equal("<Grid>broken",WorkspaceCodec.Deserialize(json).Documents[0].Source);}
    [Fact] public void CorruptPayloadIsRejected() => Assert.Throws<InvalidDataException>(()=>WorkspaceCodec.Deserialize(WorkspaceCodec.Serialize(State()).Replace("Test","Tampered",StringComparison.Ordinal)));
    [Fact] public void FutureSchemaIsNotSilentlyDowngraded() => Assert.Throws<InvalidDataException>(()=>WorkspaceCodec.Serialize(State() with {SchemaVersion=100}));
    [Fact] public void InvalidPreviewDimensionsAreRejected() => Assert.Throws<InvalidDataException>(()=>WorkspaceCodec.Serialize(State() with {Profiles=[new("Bad",double.NaN,100)]}));
    [Fact] public void AnimationPersistenceRetainsSplinesAndColors()
    {
        var clip=new AnimationClip();clip.GetTrack("Card","Opacity").SetKey(.4,.8,EasingKind.Custom,new(.2,0,.8,1));clip.GetColorTrack("Card","Background").SetKey(.6,ColorValue.Parse("#80402010"));
        var state=State();state=state with {Documents=[state.Documents[0] with {Animation=ClipState.Capture(clip)}]};
        var restored=WorkspaceCodec.Deserialize(WorkspaceCodec.Serialize(state)).Documents[0].Animation.Restore();Assert.Equal(clip.Tracks[0].Keys[0],restored.Tracks[0].Keys[0]);Assert.Equal(clip.ColorTracks[0].Keys[0],restored.ColorTracks[0].Keys[0]);
    }
    [Fact] public async Task AtomicSaveDetectsExternalChanges()
    {
        var folder=Path.Combine(Path.GetTempPath(),"prodesigner-atomic-"+Guid.NewGuid());Directory.CreateDirectory(folder);
        try
        {
            var path=Path.Combine(folder,"View.axaml");var token=TestContext.Current.CancellationToken;
            var hash=await AtomicFileStore.WriteAsync(path,"before",null,token);await File.WriteAllTextAsync(path,"external",token);
            await Assert.ThrowsAsync<FileConflictException>(()=>AtomicFileStore.WriteAsync(path,"lost-update",hash,token));Assert.Equal("external",await File.ReadAllTextAsync(path,token));
        }finally{Directory.Delete(folder,true);}
    }
    [Fact] public async Task RecoveryFallsBackToVerifiedPreviousGeneration()
    {
        var folder=Path.Combine(Path.GetTempPath(),"prodesigner-recovery-"+Guid.NewGuid());Directory.CreateDirectory(folder);
        try
        {
            var path=Path.Combine(folder,"recovery");var store=new FileRecoveryStore(path);var token=TestContext.Current.CancellationToken;
            var before=WorkspaceCodec.Serialize(State("<Grid/>"));await store.WriteAsync(before,token);await store.WriteAsync(WorkspaceCodec.Serialize(State("<Canvas/>")),token);
            await File.WriteAllTextAsync(path,"partial corrupt bytes",token);Assert.Equal(before,await store.ReadAsync(token));
        }finally{Directory.Delete(folder,true);}
    }
}
