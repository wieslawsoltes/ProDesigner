using System.IO.Pipes;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ProDesigner.PreviewProtocol;
using ProDesigner.Runtime;

namespace ProDesigner.Desktop;

internal sealed class PreviewWorker : Application
{
    internal static string PipeName { get; set; } = "";
    private RuntimePreviewLease? _lease;
    private Window? _window;
    public override void Initialize() => Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
        {
            _window = new Window { Title = "ProDesigner · isolated trusted preview", Width = 1000, Height = 720 };
            lifetime.MainWindow = _window;
            _window.Closed += (_, _) => { _lease?.Dispose(); lifetime.Shutdown(); };
            _ = ServeAsync(lifetime);
        }
        base.OnFrameworkInitializationCompleted();
    }
    private async Task ServeAsync(IClassicDesktopStyleApplicationLifetime lifetime)
    {
        await using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            using var connection = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await pipe.ConnectAsync(connection.Token).ConfigureAwait(false);
            while (pipe.IsConnected)
            {
                var payload = await PreviewWire.ReadAsync(pipe, CancellationToken.None).ConfigureAwait(false);
                var request = JsonSerializer.Deserialize(payload, PreviewJsonContext.Default.PreviewRequest) ?? throw new InvalidDataException("Empty preview request.");
                var response = await Dispatcher.UIThread.InvokeAsync(() => Handle(request));
                await PreviewWire.WriteAsync(pipe, JsonSerializer.SerializeToUtf8Bytes(response, PreviewJsonContext.Default.PreviewResponse), CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or JsonException) { }
        finally { Dispatcher.UIThread.Post(() => lifetime.Shutdown()); }
    }
    private PreviewResponse Handle(PreviewRequest request)
    {
        if (request.Operation == "ping") return new(request.Revision, true);
        try
        {
            if (request.Operation != "update" || request.Document is null) throw new InvalidDataException("Unknown preview operation.");
            var next = new RuntimePreviewEngine().Load(request.Document, request.AssemblyPath);
            var previous = _lease; _lease = next; _window!.Content = next.Root;
            previous?.Dispose(); _window.UpdateLayout();
            return new(request.Revision, true, ControlCount: next.Root.GetVisualDescendants().Count() + 1);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        { return new(request.Revision, false, ex.GetBaseException().Message); }
    }
    internal static void Run(string pipeName, bool headless)
    {
        PipeName = pipeName;
        var builder = AppBuilder.Configure<PreviewWorker>();
        builder = headless ? builder.UseHeadless(new AvaloniaHeadlessPlatformOptions()) : builder.UsePlatformDetect();
        builder.StartWithClassicDesktopLifetime([]);
    }
}
