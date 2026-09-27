using ProDesigner.Core;
using ProDesigner.PreviewProtocol;
using Xunit;

namespace ProDesigner.Tests;

public class RenderedPreviewTests
{
    private static string Worker()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "ProDesigner.slnx"))) root = root.Parent;
        var configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar) ? "Release" : "Debug";
        return Path.Combine(root!.FullName, "apps", "ProDesigner.Desktop", "bin", configuration, "net10.0", "ProDesigner.Desktop.dll");
    }
    [Fact]
    public async Task WorkerReturnsActualPixelsSourceBoundsAndResponsiveViewport()
    {
        var token = TestContext.Current.CancellationToken;
        await using var worker = await PreviewProcessClient.StartAsync(Worker(), true, token);
        const string source = "<Grid xmlns='https://github.com/avaloniaui' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'><Border x:Name='Box' Width='100' Height='50' Background='Red' HorizontalAlignment='Right'/></Grid>";
        var loaded = await worker.UpdateAsync(new(source, Trusted: true), cancellationToken: token); Assert.True(loaded.Success, loaded.Error);
        var hash = RenderedPreviewFrame.HashSource(source);
        var frame = await worker.RenderAsync(new(320, 240), hash, cancellationToken: token); frame.Validate();
        var box = Assert.Single(frame.Nodes, n => n.Name == "Box"); Assert.Equal(220, box.X); Assert.Equal(100, box.Width);
        Assert.True(frame.Png.Length > 100); Assert.Equal(hash, frame.SourceHash);
        var wide = await worker.RenderAsync(new(640, 240, 2), hash, cancellationToken: token);
        Assert.Equal(540, wide.Nodes.Single(n => n.Name == "Box").X); Assert.Equal(1280, wide.Viewport.PixelWidth);
        var blueSource = source.Replace("Background='Red'", "Background='Blue'");
        Assert.True((await worker.UpdateAsync(new(blueSource, Trusted: true), cancellationToken: token)).Success);
        var blue = await worker.RenderAsync(new(320, 240), RenderedPreviewFrame.HashSource(blueSource), cancellationToken: token);
        Assert.NotEqual(Convert.ToBase64String(frame.Png), Convert.ToBase64String(blue.Png));
    }
    [Fact]
    public async Task RealMouseInputChangesTheRenderedControlWithoutChangingDesignSource()
    {
        var token = TestContext.Current.CancellationToken;
        await using var worker = await PreviewProcessClient.StartAsync(Worker(), true, token);
        const string source = "<CheckBox xmlns='https://github.com/avaloniaui' Name='Toggle' Content='Toggle me' IsChecked='False' Width='180' Height='50'/>";
        Assert.True((await worker.UpdateAsync(new(source, Trusted: true), cancellationToken: token)).Success);
        var hash = RenderedPreviewFrame.HashSource(source);
        var before = await worker.RenderAsync(new(240, 100), hash, cancellationToken: token);
        var node = before.Nodes.Single();
        var inputs = new PreviewInput[] { new(PreviewInputKind.Move, node.X + 12, node.Y + 25), new(PreviewInputKind.Down, node.X + 12, node.Y + 25), new(PreviewInputKind.Up, node.X + 12, node.Y + 25) };
        var after = await worker.RenderAsync(new(240, 100), hash, inputs, token);
        Assert.Equal(hash, after.SourceHash); Assert.True(after.Sequence > before.Sequence);
        Assert.NotEqual(Convert.ToBase64String(before.Png), Convert.ToBase64String(after.Png));
        var textSource = "<TextBox xmlns='https://github.com/avaloniaui' Name='Edit' Width='200' Height='45'/>";
        Assert.True((await worker.UpdateAsync(new(textSource, Trusted: true), cancellationToken: token)).Success);
        hash = RenderedPreviewFrame.HashSource(textSource);
        before = await worker.RenderAsync(new(240, 100), hash, cancellationToken: token); node = before.Nodes.Single();
        after = await worker.RenderAsync(new(240, 100), hash, [new(PreviewInputKind.Down, node.X + 10, node.Y + 20), new(PreviewInputKind.Up, node.X + 10, node.Y + 20), new(PreviewInputKind.Text, Text: "Runtime text")], token);
        Assert.NotEqual(Convert.ToBase64String(before.Png), Convert.ToBase64String(after.Png));
    }
    [Fact]
    public async Task InvalidSourceRetainsLastGoodPixelsAndStaleInputIsRejected()
    {
        var token = TestContext.Current.CancellationToken;
        await using var worker = await PreviewProcessClient.StartAsync(Worker(), true, token);
        const string source = "<Border xmlns='https://github.com/avaloniaui' Background='Red'/>";
        await worker.UpdateAsync(new(source, Trusted: true), cancellationToken: token);
        var hash = RenderedPreviewFrame.HashSource(source);
        var before = await worker.RenderAsync(new(100, 100), hash, cancellationToken: token);
        Assert.False((await worker.UpdateAsync(new("<Grid>", Trusted: true), cancellationToken: token)).Success);
        var after = await worker.RenderAsync(new(100, 100), hash, cancellationToken: token);
        Assert.Equal(before.Png, after.Png);
        await Assert.ThrowsAsync<InvalidOperationException>(() => worker.RenderAsync(new(100, 100), RenderedPreviewFrame.HashSource("other"), [new(PreviewInputKind.Down)], token));
        Assert.True((await worker.PingAsync(token)).Success);
    }
    [Fact]
    public void InputQueueCoalescesOnlyAdjacentMovesAndKeepsEventOrdering()
    {
        var queue = new PreviewInputQueue(4);
        queue.Enqueue(new(PreviewInputKind.Move, 1)); queue.Enqueue(new(PreviewInputKind.Move, 2)); queue.Enqueue(new(PreviewInputKind.Down));
        queue.Enqueue(new(PreviewInputKind.Move, 3)); queue.Enqueue(new(PreviewInputKind.Up));
        Assert.Throws<InvalidOperationException>(() => queue.Enqueue(new(PreviewInputKind.KeyDown, Key: "A")));
        var items = queue.Drain(); Assert.Equal(new[] { PreviewInputKind.Move, PreviewInputKind.Down, PreviewInputKind.Move, PreviewInputKind.Up }, items.Select(i => i.Kind)); Assert.Equal(2, items[0].X);
        queue.Reset(); Assert.Equal(PreviewInputKind.Reset, Assert.Single(queue.Drain()).Kind);
    }
    [Theory]
    [InlineData(0, 10, 1)] [InlineData(4096, 4096, 3)] [InlineData(100, 100, 10)] [InlineData(10, 10, double.NaN)]
    public void UnboundedViewportsAreRefusedBeforeAllocation(int width, int height, double scale) => Assert.Throws<InvalidDataException>(() => new PreviewViewport(width, height, scale).Validate());
}
