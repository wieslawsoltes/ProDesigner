namespace ProDesigner.Core;

public sealed record SourceHistoryEntry(string Source, string ValidSource, string[] Selection, string Label);
public sealed record SourceHistorySnapshot(string ValidSource, SourceHistoryEntry[] Undo, SourceHistoryEntry[] Redo);

/// <summary>A prepared cross-document replacement. The original text is an optimistic concurrency precondition.</summary>
public sealed record DocumentReplacement(string DocumentId, string Before, string After);
public sealed record WorkspaceTransaction(string Label, DocumentReplacement[] Changes)
{
    public WorkspaceTransaction Inverse() => new("Undo " + Label, Changes.Select(c => new DocumentReplacement(c.DocumentId, c.After, c.Before)).ToArray());
    public void Validate(IReadOnlyDictionary<string, string> documents)
    {
        if (string.IsNullOrWhiteSpace(Label) || Changes is null || Changes.Length == 0 || Changes.Length > 1024) throw new InvalidOperationException("Invalid transaction.");
        var ids = new HashSet<string>();
        foreach (var change in Changes)
        {
            if (change is null || !ids.Add(change.DocumentId) || change.Before is null || change.After is null) throw new InvalidOperationException("Duplicate or incomplete document replacement.");
            if (!documents.TryGetValue(change.DocumentId, out var current) || current != change.Before)
                throw new InvalidOperationException($"Document '{change.DocumentId}' changed after this transaction was prepared. Nothing was applied.");
        }
    }
    public IReadOnlyDictionary<string, string> Apply(IReadOnlyDictionary<string, string> documents)
    {
        Validate(documents); var result = new Dictionary<string, string>(documents);
        foreach (var change in Changes) result[change.DocumentId] = change.After;
        return result;
    }
}
