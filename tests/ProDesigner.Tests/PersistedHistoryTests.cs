using ProDesigner.Core;
using ProDesigner.Design;
using Xunit;

namespace ProDesigner.Tests;
public class PersistedHistoryTests
{
    [Fact] public void HistoryRestoresUndoAndRedoWithoutInventingASavedBaseline()
    {
        var session=new DesignerSession("<Grid/>");session.SetSource("<Grid Width='10'/>");session.SetSource("<Grid Width='20'/>");session.Undo();
        var restored=new DesignerSession(session.Source);restored.RestoreSavedBaseline(session.SavedSource);restored.RestoreHistory(session.CaptureHistory());
        Assert.True(restored.IsDirty);restored.Redo();Assert.Equal("<Grid Width='20'/>",restored.Source);restored.Undo();restored.Undo();Assert.Equal("<Grid/>",restored.Source);Assert.False(restored.IsDirty);
    }
    [Fact] public void InvalidTextRecoversItsActualLastValidPreview()
    {
        var session=new DesignerSession("<Grid><Button/></Grid>");session.SetSource("<Grid><Button");var snapshot=session.CaptureHistory();
        var restored=new DesignerSession("<Border/>");restored.SetSource(session.Source);restored.RestoreHistory(snapshot);
        Assert.False(restored.IsValid);Assert.Equal("Grid",restored.Tree.Root.Name);restored.Undo();Assert.True(restored.IsValid);
    }
    [Fact] public void WorkspaceTransactionsPreflightAllSourcesBeforeProducingChanges()
    {
        var before=new Dictionary<string,string>{{"a","A"},{"b","B"}};
        var transaction=new WorkspaceTransaction("Rename",[new("a","A","AA"),new("b","B","BB")]);
        var after=transaction.Apply(before);Assert.Equal("AA",after["a"]);Assert.Equal("BB",after["b"]);Assert.Equal("A",transaction.Inverse().Apply(after)["a"]);
        Assert.Throws<InvalidOperationException>(()=>transaction.Apply(new Dictionary<string,string>{{"a","A"},{"b","other"}}));Assert.Equal("A",before["a"]);
    }
    [Fact] public void ARepeatedDocumentCannotBeWrittenTwiceInOneTransaction()=>Assert.Throws<InvalidOperationException>(()=>new WorkspaceTransaction("Duplicate",[new("a","A","B"),new("a","A","C")]).Apply(new Dictionary<string,string>{{"a","A"}}));
}
