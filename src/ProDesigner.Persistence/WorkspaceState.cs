using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProDesigner.Animation;
using ProDesigner.Core;
using ProDesigner.DesignSystems;
using ProDesigner.Prototyping;
using ProDesigner.Xaml;

namespace ProDesigner.Persistence;

public sealed record TrackState(string Target, string Property, Keyframe[] Keys);
public sealed record ColorTrackState(string Target, string Property, ColorKeyframe[] Keys);
public sealed record ClipState(string Name, double DurationSeconds, bool Loop, TrackState[] Tracks, ColorTrackState[] Colors)
{
    public static ClipState Capture(AnimationClip clip) => new(clip.Name, clip.DurationSeconds, clip.Loop, clip.Tracks.Select(t => new TrackState(t.Target, t.Property, t.Keys.ToArray())).ToArray(), clip.ColorTracks.Select(t => new ColorTrackState(t.Target, t.Property, t.Keys.ToArray())).ToArray());
    public AnimationClip Restore()
    {
        if (!double.IsFinite(DurationSeconds) || DurationSeconds is <= 0 or > 86400 || Tracks is null || Colors is null || Tracks.Length + Colors.Length > 2000) throw new InvalidDataException("Invalid animation metadata.");
        var clip = new AnimationClip { Name = Name, DurationSeconds = DurationSeconds, Loop = Loop };
        var used = new HashSet<(string, string)>();
        foreach (var track in Tracks)
        {
            if (!used.Add((track.Target, track.Property)) || track.Keys is null || track.Keys.Length > 10000) throw new InvalidDataException("Duplicate track or too many keyframes.");
            var restored = clip.GetTrack(track.Target, track.Property);
            foreach (var key in track.Keys) restored.SetKey(key.Time, key.Value, key.Easing, key.Spline);
        }
        foreach (var track in Colors)
        {
            if (!used.Add((track.Target, track.Property)) || track.Keys is null || track.Keys.Length > 10000) throw new InvalidDataException("Duplicate track or too many keyframes.");
            var restored = clip.GetColorTrack(track.Target, track.Property);
            foreach (var key in track.Keys) restored.SetKey(key.Time, key.Value, key.Spline);
        }
        return clip;
    }
}
public sealed record DocumentState(string Name, string Source, string SavedSource, string CodeBehind, string? FilePath, string? ProjectPath,
    ClipState Animation, string[] Selection, string? Id = null, SourceHistorySnapshot? History = null);
public sealed record WorkspaceState(int SchemaVersion, string Name, int ActiveDocument, double Zoom, PreviewProfile[] Profiles,
    DocumentState[] Documents, Dictionary<string, string>? SampleData = null, DesignSystemState? DesignSystem = null, PrototypeGraph? Prototype = null)
{
    public const int CurrentSchema = 2;
}
public sealed record WorkspaceEnvelope(string Format, string Payload, string Sha256);

[JsonSourceGenerationOptions(WriteIndented = false, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(WorkspaceEnvelope))]
[JsonSerializable(typeof(WorkspaceState))]
public partial class WorkspaceJsonContext : JsonSerializerContext;

/// <summary>Checksummed, trimming-safe workspace serialization. Schema 1 remains readable; new snapshots use schema 2.</summary>
public static class WorkspaceCodec
{
    public const int MaximumBytes = 64 * 1024 * 1024;
    public static string Serialize(WorkspaceState state)
    {
        Validate(state);
        var payload = JsonSerializer.Serialize(state, WorkspaceJsonContext.Default.WorkspaceState);
        var json = JsonSerializer.Serialize(new WorkspaceEnvelope("ProDesigner.Workspace", payload, Hash(payload)), WorkspaceJsonContext.Default.WorkspaceEnvelope);
        if (Encoding.UTF8.GetByteCount(json) > MaximumBytes) throw new InvalidDataException("Workspace exceeds 64 MiB.");
        return json;
    }
    public static WorkspaceState Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (Encoding.UTF8.GetByteCount(json) > MaximumBytes) throw new InvalidDataException("Workspace exceeds 64 MiB.");
        try
        {
            var envelope = JsonSerializer.Deserialize(json, WorkspaceJsonContext.Default.WorkspaceEnvelope) ?? throw new InvalidDataException("Empty workspace.");
            if (envelope.Format != "ProDesigner.Workspace" || envelope.Payload is null || !string.Equals(Hash(envelope.Payload), envelope.Sha256, StringComparison.Ordinal)) throw new InvalidDataException("Workspace checksum or format is invalid.");
            var state = JsonSerializer.Deserialize(envelope.Payload, WorkspaceJsonContext.Default.WorkspaceState) ?? throw new InvalidDataException("Workspace payload is empty.");
            Validate(state); return state;
        }
        catch (JsonException ex) { throw new InvalidDataException("Malformed workspace JSON.", ex); }
    }
    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static void Validate(WorkspaceState state)
    {
        if (state.SchemaVersion is not 1 and not WorkspaceState.CurrentSchema) throw new InvalidDataException($"Unsupported workspace schema {state.SchemaVersion}.");
        if (state.Documents is null || state.Documents.Length is < 1 or > 256 || state.ActiveDocument < 0 || state.ActiveDocument >= state.Documents.Length) throw new InvalidDataException("Invalid document set.");
        if (!double.IsFinite(state.Zoom) || state.Zoom is < .15 or > 3 || state.Profiles is null || state.Profiles.Length is < 1 or > 16) throw new InvalidDataException("Invalid preview configuration.");
        foreach (var profile in state.Profiles)
            if (profile is null || !double.IsFinite(profile.Width) || !double.IsFinite(profile.Height) || profile.Width is < 1 or > 16384 || profile.Height is < 1 or > 16384) throw new InvalidDataException("Invalid preview dimensions.");
        long total = 0; var documentIds = new HashSet<string>();
        void CountText(string? value)
        {
            if (value is null || value.Length > XamlSyntaxTree.MaximumLength) throw new InvalidDataException("Document exceeds size limits.");
            total += value.Length; if (total > MaximumBytes / 2) throw new InvalidDataException("Workspace text exceeds the memory budget.");
        }
        foreach (var document in state.Documents)
        {
            if (document is null || string.IsNullOrWhiteSpace(document.Name) || document.Animation is null || document.Selection is null) throw new InvalidDataException("Incomplete document.");
            CountText(document.Source); CountText(document.SavedSource); CountText(document.CodeBehind);
            if (document.Id is not null && (string.IsNullOrWhiteSpace(document.Id) || !documentIds.Add(document.Id))) throw new InvalidDataException("Duplicate document identity.");
            if (document.History is { } history)
            {
                if (history.Undo is null || history.Redo is null || history.Undo.Length + history.Redo.Length > 400) throw new InvalidDataException("Invalid source history.");
                CountText(history.ValidSource); XamlSyntaxTree.Parse(history.ValidSource);
                foreach (var entry in history.Undo.Concat(history.Redo))
                {
                    if (entry is null || entry.Selection is null || entry.Selection.Length > 10000 || entry.Label is null || entry.Label.Length > 1024) throw new InvalidDataException("Invalid history entry.");
                    CountText(entry.Source); CountText(entry.ValidSource); XamlSyntaxTree.Parse(entry.ValidSource);
                }
            }
            _ = document.Animation.Restore();
        }
        if (state.DesignSystem is { } system)
        {
            ComponentEngine.Validate(system);
            foreach (var definition in system.Components) CountText(definition.Xaml);
            foreach (var instance in system.Instances) { CountText(instance.Baseline); if (!documentIds.Contains(instance.DocumentId)) throw new InvalidDataException("Component instance document is missing."); }
        }
        // Broken flow endpoints are intentionally recoverable and are shown in the prototype validator.
        if (state.Prototype is { } flow && (flow.Links is null || flow.Variables is null || flow.Links.Length > 10000 || flow.Variables.Count > 1000 || flow.Links.Any(l => l is null))) throw new InvalidDataException("Invalid prototype data.");
    }
}
