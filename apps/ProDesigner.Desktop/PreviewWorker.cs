using System.IO.Pipes;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Fonts.Inter;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ProDesigner.Core;
using ProDesigner.PreviewProtocol;
using ProDesigner.Runtime;
using ProDesigner.Xaml;

namespace ProDesigner.Desktop;

internal sealed class PreviewWorker : Application
{
    internal static string PipeName { get; set; } = "";
    internal static bool IsHeadless { get; set; }
    private RuntimePreviewLease? _lease;
    private Window? _window;
    private readonly ThemeVariantScope _scope = new();
    private readonly Border _stage = new() { ClipToBounds = true };
    private XamlSyntaxTree? _source;
    private string? _hash;
    private long _frameSequence;
    private readonly PreviewWorkerInput _input = new();
    public override void Initialize()
    {
        FontManager.Current.AddFontCollection(new InterFontCollection());
        Styles.Add(new FluentTheme());
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
        {
            _stage.Child = _scope;
            _window = new Window { Title = "ProDesigner · isolated trusted preview", Width = 960, Height = 620,
                Content = _stage, WindowDecorations = WindowDecorations.None };
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
            if (request.Operation == "reset-input")
            {
                if (IsHeadless) _input.Reset(_window!);
                return new(request.Revision, true);
            }
            if (request.Operation == "update" && request.Document is not null)
            {
                // Build a complete replacement before publishing it; failed edits preserve the last valid runtime tree.
                var source = XamlSyntaxTree.Parse(request.Document.Source);
                var next = new RuntimePreviewEngine().Load(request.Document, request.AssemblyPath);
                if (IsHeadless) _input.Reset(_window!);
                var previous = _lease;
                _scope.Child = next.Root; _lease = next; _source = source;
                _hash = RenderedPreviewFrame.HashSource(source.Source);
                previous?.Dispose(); _window!.UpdateLayout();
                return new(request.Revision, true, ControlCount: next.Root.GetVisualDescendants().Count() + 1);
            }
            if (request.Operation == "render" && request.Viewport is { } viewport)
            {
                viewport.Validate();
                if (_lease is null || _source is null) throw new InvalidOperationException("Load trusted XAML before rendering.");
                if (request.SourceHash != _hash) throw new InvalidOperationException("Stale preview input or capture revision; synchronize the source first.");
                var batch = request.Input ?? [];
                if (batch.Length > 128) throw new InvalidDataException("Input batch exceeds the bound.");
                foreach (var input in batch) input.Validate();
                if (batch.Length > 0 && !IsHeadless) throw new InvalidOperationException("Remote input requires the headless rendered worker.");
                _window!.Width = viewport.Width; _window.Height = viewport.Height;
                _stage.Width = viewport.Width; _stage.Height = viewport.Height;
                _stage.Background = Brush.Parse(viewport.Dark ? "#15171E" : "#FFFFFF");
                _scope.RequestedThemeVariant = viewport.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
                _window.UpdateLayout();
                foreach (var input in batch) _input.Apply(_window, input);
                _window.UpdateLayout();
                if (IsHeadless) AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var bitmap = new RenderTargetBitmap(new PixelSize(viewport.PixelWidth, viewport.PixelHeight), new Vector(96 * viewport.Scale, 96 * viewport.Scale));
                bitmap.Render(_stage);
                using var data = new MemoryStream(); bitmap.Save(data, PngBitmapEncoderOptions.Default);
                if (data.Length > RenderedPreviewFrame.MaximumPngBytes) throw new InvalidOperationException("Rendered PNG exceeds the transport budget; reduce viewport scale.");
                var frame = new RenderedPreviewFrame(_hash!, ++_frameSequence, viewport, data.ToArray(), CaptureNodes());
                frame.Validate();
                return new(request.Revision, true, Frame: frame);
            }
            throw new InvalidDataException("Unknown preview operation.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        { return new(request.Revision, false, ex.GetBaseException().Message); }
    }
    private PreviewNode[] CaptureNodes()
    {
        var root = _lease!.Root;
        var controls = root.GetVisualDescendants().OfType<Control>().Prepend(root).Where(c => c.IsEffectivelyVisible).ToArray();
        var names = controls.Where(c => c.Name is not null).GroupBy(c => c.Name!, StringComparer.Ordinal)
            .Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single(), StringComparer.Ordinal);
        var result = new List<PreviewNode>();
        foreach (var node in _source!.Elements.Where(n => !n.IsProperty))
        {
            Control? control = node == _source.Root ? root : null;
            var name = XamlNames.Name(node);
            if (control is null && name is not null && XamlNames.Scope(node) == _source.Root) names.TryGetValue(name, out control);
            if (control is null || !control.IsEffectivelyVisible) continue;
            var transform = control.TransformToVisual(_stage); if (transform is null) continue;
            var bounds = new Rect(control.Bounds.Size).TransformToAABB(transform.Value);
            if (bounds.Width <= 0 || bounds.Height <= 0 || new[] { bounds.X, bounds.Y, bounds.Width, bounds.Height }.Any(v => !double.IsFinite(v) || Math.Abs(v) > 1_000_000)) continue;
            result.Add(new(node.Id, name, bounds.X, bounds.Y, bounds.Width, bounds.Height));
        }
        // Only named root-scope controls and the root are exposed; guessing runtime/template correspondence is unsafe.
        return result.Take(10000).ToArray();
    }
    internal static void Run(string pipeName, bool headless)
    {
        PipeName = pipeName; IsHeadless = headless;
        var builder = AppBuilder.Configure<PreviewWorker>();
        builder = headless ? builder.UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }) : builder.UsePlatformDetect();
        builder.StartWithClassicDesktopLifetime([]);
    }
}
