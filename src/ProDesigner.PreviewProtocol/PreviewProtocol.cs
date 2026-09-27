using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProDesigner.Core;

namespace ProDesigner.PreviewProtocol;

public sealed record PreviewRequest(long Revision, string Operation, TrustedPreviewRequest? Document = null, string? AssemblyPath = null, PreviewViewport? Viewport = null, string? SourceHash = null, PreviewInput[]? Input = null);
public sealed record PreviewResponse(long Revision, bool Success, string? Error = null, int ControlCount = 0, RenderedPreviewFrame? Frame = null, int ProtocolVersion = 2);
[JsonSerializable(typeof(PreviewRequest))]
[JsonSerializable(typeof(PreviewResponse))]
public partial class PreviewJsonContext : JsonSerializerContext;

/// <summary>Bounded length-prefixed IPC; stdout and application logging never share the protocol channel.</summary>
public static class PreviewWire
{
    public const int MaximumFrameLength = 16 * 1024 * 1024;
    public static async Task WriteAsync(Stream stream, byte[] payload, CancellationToken cancellationToken)
    {
        if (payload.Length is < 1 or > MaximumFrameLength) throw new InvalidDataException("Invalid preview frame size.");
        var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
    public static async Task<byte[]> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[4]; await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is < 1 or > MaximumFrameLength) throw new InvalidDataException("Invalid preview frame size.");
        var data = new byte[length]; await stream.ReadExactlyAsync(data, cancellationToken).ConfigureAwait(false); return data;
    }
}
