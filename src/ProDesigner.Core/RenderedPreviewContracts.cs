using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace ProDesigner.Core;

/// <summary>A bounded logical viewport. Device pixels are rounded up; scale is pixels per logical unit.</summary>
public sealed record PreviewViewport(int Width = 960, int Height = 620, double Scale = 1, bool Dark = false)
{
    public const int MaximumPixels = 4 * 1024 * 1024;
    public int PixelWidth => checked((int)Math.Ceiling(Width * Scale));
    public int PixelHeight => checked((int)Math.Ceiling(Height * Scale));
    public void Validate()
    {
        if (Width is < 1 or > 4096 || Height is < 1 or > 4096 || !double.IsFinite(Scale) || Scale is < .25 or > 3 ||
            (long)PixelWidth * PixelHeight > MaximumPixels)
            throw new InvalidDataException("Preview viewport exceeds the 4-megapixel rendering budget.");
    }
}

public enum PreviewInputKind { Move, Down, Up, Wheel, KeyDown, KeyUp, Text, Reset }
[Flags]
public enum PreviewModifiers { None = 0, Shift = 1, Control = 2, Alt = 4, Meta = 8 }
public sealed record PreviewInput(PreviewInputKind Kind, double X = 0, double Y = 0, int Button = 0,
    double DeltaX = 0, double DeltaY = 0, string? Key = null, string? Text = null, PreviewModifiers Modifiers = PreviewModifiers.None)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Kind) || (Modifiers & ~(PreviewModifiers.Shift | PreviewModifiers.Control | PreviewModifiers.Alt | PreviewModifiers.Meta)) != 0 ||
            new[] { X, Y, DeltaX, DeltaY }.Any(v => !double.IsFinite(v) || Math.Abs(v) > 1_000_000) || Button is < 0 or > 2 ||
            Key?.Length > 64 || Text?.Length > 4096)
            throw new InvalidDataException("Invalid remote preview input.");
        if ((Kind is PreviewInputKind.KeyDown or PreviewInputKind.KeyUp) && string.IsNullOrWhiteSpace(Key))
            throw new InvalidDataException("Keyboard input requires a key name.");
    }
}
public sealed record PreviewNode(string Id, string? Name, double X, double Y, double Width, double Height);

/// <summary>PNG pixels plus source-revision-matched hit-test geometry. No executable project objects cross IPC.</summary>
public sealed record RenderedPreviewFrame(string SourceHash, long Sequence, PreviewViewport Viewport, byte[] Png, PreviewNode[] Nodes)
{
    public const int MaximumPngBytes = 8 * 1024 * 1024;
    public static string HashSource(string source) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Viewport); Viewport.Validate();
        if (Sequence < 1 || SourceHash is null || SourceHash.Length != 64 || !SourceHash.All(Uri.IsHexDigit) ||
            Png is null || Png.Length is < 33 or > MaximumPngBytes || Nodes is null || Nodes.Length > 10000)
            throw new InvalidDataException("Invalid rendered preview frame.");
        // Check image dimensions before allowing a UI decoder to allocate an image from another process.
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (!Png.AsSpan(0, 8).SequenceEqual(signature) || BinaryPrimitives.ReadInt32BigEndian(Png.AsSpan(8, 4)) != 13 ||
            !Png.AsSpan(12, 4).SequenceEqual("IHDR"u8) || BinaryPrimitives.ReadInt32BigEndian(Png.AsSpan(16, 4)) != Viewport.PixelWidth ||
            BinaryPrimitives.ReadInt32BigEndian(Png.AsSpan(20, 4)) != Viewport.PixelHeight)
            throw new InvalidDataException("Preview PNG dimensions do not match the negotiated viewport.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in Nodes)
            if (node is null || string.IsNullOrWhiteSpace(node.Id) || node.Id.Length > 2048 || !ids.Add(node.Id) || node.Name?.Length > 512 ||
                new[] { node.X, node.Y, node.Width, node.Height }.Any(v => !double.IsFinite(v) || Math.Abs(v) > 1_000_000) || node.Width < 0 || node.Height < 0)
                throw new InvalidDataException("Invalid source-linked preview bounds.");
    }
}

/// <summary>Optional pixel/input extension to the existing supervised preview contract.</summary>
public interface IRenderedExternalPreview : IExternalPreview
{
    Task ResetInputAsync(CancellationToken cancellationToken = default);
    Task<RenderedPreviewFrame> RenderAsync(PreviewViewport viewport, string expectedSourceHash,
        IReadOnlyList<PreviewInput>? input = null, CancellationToken cancellationToken = default);
}

/// <summary>Ordered, bounded input. Only adjacent pointer moves are coalesced; releases and keys are never silently dropped.</summary>
public sealed class PreviewInputQueue(int capacity = 128)
{
    private readonly List<PreviewInput> _items = [];
    private readonly object _sync = new();
    public void Enqueue(PreviewInput input)
    {
        input.Validate();
        lock (_sync)
        {
            if (input.Kind == PreviewInputKind.Move && _items.LastOrDefault()?.Kind == PreviewInputKind.Move) _items[^1] = input;
            else
            {
                if (_items.Count >= capacity) throw new InvalidOperationException("Preview input queue is full. Reset interaction before continuing.");
                _items.Add(input);
            }
        }
    }
    public PreviewInput[] Drain()
    {
        lock (_sync) { var items = _items.ToArray(); _items.Clear(); return items; }
    }
    public void Reset()
    {
        lock (_sync) { _items.Clear(); _items.Add(new(PreviewInputKind.Reset)); }
    }
}
