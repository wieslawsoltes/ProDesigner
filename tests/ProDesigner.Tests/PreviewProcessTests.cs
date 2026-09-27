using System.Buffers.Binary;
using ProDesigner.Core;
using ProDesigner.PreviewProtocol;
using Xunit;

namespace ProDesigner.Tests;
public class PreviewProcessTests
{
    private static string Worker()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"ProDesigner.slnx")))root=root.Parent;
        if(root is null)throw new DirectoryNotFoundException("Cannot locate repository root.");
        var configuration=AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar+"Release"+Path.DirectorySeparatorChar)?"Release":"Debug";
        return Path.Combine(root.FullName,"apps","ProDesigner.Desktop","bin",configuration,"net10.0","ProDesigner.Desktop.dll");
    }
    [Fact] public async Task WireRoundTripsAndRejectsOversizedFrames()
    {
        using var stream=new MemoryStream();var token=TestContext.Current.CancellationToken;
        await PreviewWire.WriteAsync(stream,[1,2,3],token);stream.Position=0;Assert.Equal(new byte[]{1,2,3},await PreviewWire.ReadAsync(stream,token));
        stream.SetLength(0);var header=new byte[4];BinaryPrimitives.WriteInt32LittleEndian(header,int.MaxValue);await stream.WriteAsync(header,token);stream.Position=0;
        await Assert.ThrowsAsync<InvalidDataException>(()=>PreviewWire.ReadAsync(stream,token));
    }
    [Fact] public async Task IsolatedWorkerRendersUpdatesAndSurvivesMalformedXaml()
    {
        var token=TestContext.Current.CancellationToken;
        await using var worker=await PreviewProcessClient.StartAsync(Worker(),headless:true,cancellationToken:token);
        Assert.NotEqual(Environment.ProcessId,worker.ProcessId);
        var response=await worker.UpdateAsync(new("<Border xmlns='https://github.com/avaloniaui'><Button Content='Preview'/></Border>",Trusted:true),cancellationToken:token);Assert.True(response.Success,response.Error);Assert.True(response.ControlCount>=2);
        var failed=await worker.UpdateAsync(new("<Grid>",Trusted:true),cancellationToken:token);Assert.False(failed.Success);
        Assert.True((await worker.PingAsync(token)).Success);Assert.True(worker.IsAlive);
    }
    [Fact] public async Task KillingWorkerDoesNotKillDesignerAndDisconnectIsObserved()
    {
        var token=TestContext.Current.CancellationToken;await using var worker=await PreviewProcessClient.StartAsync(Worker(),true,token);
        using(var child=System.Diagnostics.Process.GetProcessById(worker.ProcessId)){child.Kill(entireProcessTree:true);await child.WaitForExitAsync(token);}
        await Assert.ThrowsAnyAsync<Exception>(()=>worker.PingAsync(token));Assert.True(Environment.ProcessId>0);
    }
    [Fact] public async Task UntrustedRequestNeverReachesTheWorker()
    {
        var token=TestContext.Current.CancellationToken;await using var worker=await PreviewProcessClient.StartAsync(Worker(),true,token);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>worker.UpdateAsync(new("<Grid/>"),cancellationToken:token));Assert.True((await worker.PingAsync(token)).Success);
    }
}
