using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ProDesigner.Core;
using ProDesigner.PreviewProtocol;
using ProDesigner.Workbench;
using Xunit;

namespace ProDesigner.Tests;

public class EmbeddedPreviewTests
{
    private static string Worker()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "ProDesigner.slnx"))) root = root.Parent;
        var configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar) ? "Release" : "Debug";
        return Path.Combine(root!.FullName, "apps", "ProDesigner.Desktop", "bin", configuration, "net10.0", "ProDesigner.Desktop.dll");
    }
    private static async Task Until(Func<bool> condition, Window window, CancellationToken token)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Embedded preview did not reach its expected state.");
            await Task.Delay(40, token); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }
    [AvaloniaFact]
    public async Task WorkbenchEmbedsChildProcessFramesAndSynchronizesSourceWithoutLoadingProjectObjects()
    {
        var token = TestContext.Current.CancellationToken;
        TestPreview? service = null;
        using var workbench = new DesignerWorkbench(externalPreview: async (request, cancellation) =>
        {
            var client = await PreviewProcessClient.StartAsync(Worker(), true, cancellation);
            service = new(client, request); await service.UpdateAsync(request.Source, cancellation); return service;
        });
        const string source = "<Canvas xmlns='https://github.com/avaloniaui'><Border Name='Card' Canvas.Left='40' Canvas.Top='50' Width='100' Height='80' Background='Red'/></Canvas>";
        workbench.OpenDocument("Embedded.axaml", source);
        var window = new Window { Width = 1500, Height = 1000, Content = workbench }; window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            workbench.Execute("trusted-runtime");
            var confirm = workbench.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Build this project and execute its XAML");
            confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => workbench.RuntimeSurface.Frame is not null, window, token);
            Assert.True(workbench.RuntimeSurface.IsFrameCurrent);
            var frame = workbench.RuntimeSurface.Frame!; Assert.Equal(100, frame.Nodes.Single(n => n.Name == "Card").Width);
            Assert.False(workbench.Surface.IsVisible);
            workbench.SelectByName("Card"); workbench.SetProperty("Width", "177");
            Assert.False(workbench.RuntimeSurface.IsFrameCurrent);
            await Until(() => workbench.RuntimeSurface.IsFrameCurrent, window, token);
            Assert.Equal(177, workbench.RuntimeSurface.Frame!.Nodes.Single(n => n.Name == "Card").Width);
            Assert.False(workbench.RuntimeSurface.Present(frame)); // Sequence/source mismatch is never presented.
            var previousReset = service!.ResetCount;
            workbench.SwitchDocument(workbench.Documents[0]);
            await Until(() => service.ResetCount > previousReset, window, token);
            Assert.True(workbench.Surface.IsVisible);
            await workbench.StopExternalPreviewAsync(); Assert.False(service.IsAlive);
        }
        finally { await workbench.StopExternalPreviewAsync(); window.Close(); }
    }
    [AvaloniaFact]
    public async Task EditingWhileTheWorkerConnectsDoesNotMarkOldPixelsAsCurrent()
    {
        var token = TestContext.Current.CancellationToken;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var proceed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var workbench = new DesignerWorkbench(externalPreview: async (request, cancellation) =>
        {
            started.SetResult(); await proceed.Task.WaitAsync(cancellation);
            var client = await PreviewProcessClient.StartAsync(Worker(), true, cancellation);
            var preview = new TestPreview(client, request); await preview.UpdateAsync(request.Source, cancellation); return preview;
        });
        workbench.OpenDocument("Race.axaml", "<Border xmlns='https://github.com/avaloniaui' Name='Card' Width='100' Height='50' Background='Red'/>");
        var window = new Window { Width = 1500, Height = 1000, Content = workbench }; window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            workbench.Execute("trusted-runtime");
            workbench.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Build this project and execute its XAML").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await started.Task.WaitAsync(token); workbench.SelectByName("Card"); workbench.SetProperty("Width", "188"); proceed.SetResult();
            await Until(() => workbench.RuntimeSurface.IsFrameCurrent, window, token);
            Assert.Equal(RenderedPreviewFrame.HashSource(workbench.Session.Source), workbench.RuntimeSurface.Frame!.SourceHash);
            Assert.Equal(188, workbench.RuntimeSurface.Frame.Nodes.Single().Width);
        }
        finally { proceed.TrySetResult(); await workbench.StopExternalPreviewAsync(); window.Close(); }
    }
    private sealed class TestPreview(PreviewProcessClient client, TrustedPreviewRequest request) : IRenderedExternalPreview
    {
        public int ResetCount { get; private set; }
        public bool IsAlive => client.IsAlive;
        public async Task UpdateAsync(string source, CancellationToken cancellationToken = default)
        {
            var result = await client.UpdateAsync(request with { Source = source }, cancellationToken: cancellationToken);
            if (!result.Success) throw new InvalidOperationException(result.Error);
        }
        public async Task ResetInputAsync(CancellationToken cancellationToken = default) { await client.ResetInputAsync(cancellationToken); ResetCount++; }
        public Task<RenderedPreviewFrame> RenderAsync(PreviewViewport viewport, string expectedSourceHash, IReadOnlyList<PreviewInput>? input = null, CancellationToken cancellationToken = default) => client.RenderAsync(viewport, expectedSourceHash, input, cancellationToken);
        public ValueTask DisposeAsync() => client.DisposeAsync();
    }
}
