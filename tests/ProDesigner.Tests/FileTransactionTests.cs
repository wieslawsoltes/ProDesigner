using System.Text;
using ProDesigner.Persistence;
using Xunit;

namespace ProDesigner.Tests;

public class FileTransactionTests
{
    private static string DirectoryPath()
    {
        var root = Path.Combine(Path.GetTempPath(), "prodesigner-transaction-" + Guid.NewGuid().ToString("N"));
        // macOS exposes /var through a system symlink; exercise the store using the canonical physical temp path.
        if (OperatingSystem.IsMacOS() && root.StartsWith("/var/", StringComparison.Ordinal)) root = "/private" + root;
        Directory.CreateDirectory(root); return root;
    }
    [Fact]
    public async Task CommitAndUndoPreserveEncodingAndOriginalBytes()
    {
        var root=DirectoryPath();var token=TestContext.Current.CancellationToken;
        try
        {
            var a=Path.Combine(root,"A.cs");var b=Path.Combine(root,"B.axaml");
            await File.WriteAllTextAsync(a,"class Old {}\r\n",new UTF8Encoding(true),token);await File.WriteAllTextAsync(b,"<Old/>\r\n",Encoding.Unicode,token);
            var originalA=await File.ReadAllBytesAsync(a,token);var originalB=await File.ReadAllBytesAsync(b,token);
            var store=new JournaledFileTransaction(Path.Combine(root,"journal"));
            var receipt=await store.ApplyAsync([new(a,"class Old {}\r\n","class New {}\r\n"),new(b,"<Old/>\r\n","<New/>\r\n")],token);
            Assert.Equal("class New {}\r\n",await File.ReadAllTextAsync(a,token));Assert.Equal("<New/>\r\n",await File.ReadAllTextAsync(b,token));
            Assert.True((await File.ReadAllBytesAsync(a,token)).Take(3).SequenceEqual(new byte[]{0xef,0xbb,0xbf}));
            await store.RevertAsync(receipt,[a,b],token);Assert.Equal(originalA,await File.ReadAllBytesAsync(a,token));Assert.Equal(originalB,await File.ReadAllBytesAsync(b,token));
        }
        finally {Directory.Delete(root,true);}
    }
    [Fact]
    public async Task AStaleFileAbortsBeforeAnyFileIsReplaced()
    {
        var root=DirectoryPath();var token=TestContext.Current.CancellationToken;
        try
        {
            var a=Path.Combine(root,"A");var b=Path.Combine(root,"B");await File.WriteAllTextAsync(a,"A",token);await File.WriteAllTextAsync(b,"external",token);
            await Assert.ThrowsAsync<IOException>(()=>new JournaledFileTransaction(Path.Combine(root,"journal")).ApplyAsync([new(a,"A","AA"),new(b,"B","BB")],token));
            Assert.Equal("A",await File.ReadAllTextAsync(a,token));Assert.Equal("external",await File.ReadAllTextAsync(b,token));
        }
        finally {Directory.Delete(root,true);}
    }
    [Fact]
    public async Task FailureMidCommitRollsBackAlreadyReplacedFiles()
    {
        var root=DirectoryPath();var token=TestContext.Current.CancellationToken;
        try
        {
            var a=Path.Combine(root,"A");var b=Path.Combine(root,"B");await File.WriteAllTextAsync(a,"A",token);await File.WriteAllTextAsync(b,"B",token);
            await Assert.ThrowsAsync<IOException>(()=>new FailingStore(Path.Combine(root,"journal")).ApplyAsync([new(a,"A","AA"),new(b,"B","BB")],token));
            Assert.Equal("A",await File.ReadAllTextAsync(a,token));Assert.Equal("B",await File.ReadAllTextAsync(b,token));
        }
        finally {Directory.Delete(root,true);}
    }
    [Fact]
    public async Task UndoDoesNotDestroyEditsMadeAfterTheRefactoring()
    {
        var root=DirectoryPath();var token=TestContext.Current.CancellationToken;
        try
        {
            var a=Path.Combine(root,"A");var b=Path.Combine(root,"B");await File.WriteAllTextAsync(a,"A",token);await File.WriteAllTextAsync(b,"B",token);
            var store=new JournaledFileTransaction(Path.Combine(root,"journal"));var receipt=await store.ApplyAsync([new(a,"A","AA"),new(b,"B","BB")],token);
            await File.WriteAllTextAsync(b,"new external edit",token);
            await Assert.ThrowsAsync<IOException>(()=>store.RevertAsync(receipt,[a,b],token));Assert.Equal("AA",await File.ReadAllTextAsync(a,token));Assert.Equal("new external edit",await File.ReadAllTextAsync(b,token));
        }
        finally {Directory.Delete(root,true);}
    }
    [Fact]
    public async Task RecoveryRequiresTheCallerToApproveEveryTargetPath()
    {
        var root=DirectoryPath();var token=TestContext.Current.CancellationToken;
        try
        {
            var path=Path.Combine(root,"View.cs");await File.WriteAllTextAsync(path,"before",token);
            var store=new JournaledFileTransaction(Path.Combine(root,"journal"));var receipt=await store.ApplyAsync([new(path,"before","after")],token);
            await Assert.ThrowsAsync<InvalidDataException>(()=>store.RevertAsync(receipt,[],token));Assert.Equal("after",await File.ReadAllTextAsync(path,token));
            var reopened=new JournaledFileTransaction(Path.Combine(root,"journal"));await reopened.RevertAsync(receipt,[path],token);Assert.Equal("before",await File.ReadAllTextAsync(path,token));
        }
        finally {Directory.Delete(root,true);}
    }
    private sealed class FailingStore(string directory) : JournaledFileTransaction(directory)
    {
        protected override void ReplaceStagedFile(string stagedPath,string destination,int index)
        {if(index==1)throw new IOException("Injected filesystem failure.");base.ReplaceStagedFile(stagedPath,destination,index);}
    }
}
