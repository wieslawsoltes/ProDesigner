using System.Diagnostics;
using System.Xml;
using ProDesigner.Core;
using ProDesigner.Xaml;

namespace ProDesigner.Design;

public sealed class DesignerSession
{
    private readonly List<SourceHistoryEntry> _undo = [];
    private readonly List<SourceHistoryEntry> _redo = [];
    private readonly HashSet<string> _selection = [];
    private string _savedSource;
    private long _lastTypingTick;
    private const long HistoryBudget = 32 * 1024 * 1024;
    public string SavedSource => _savedSource;
    public void RestoreSavedBaseline(string source) => _savedSource = source;
    public string Source { get; private set; }
    public XamlSyntaxTree Tree { get; private set; }
    public long Version { get; private set; }
    public double LastParseMilliseconds { get; private set; }
    public bool IsValid { get; private set; } = true;
    public bool IsDirty => Source != _savedSource;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public IReadOnlyCollection<string> Selection => _selection;
    public XamlElement? Primary => _selection.Count == 0 ? null : Tree.Find(_selection.Last());
    public IReadOnlyList<DesignDiagnostic> Diagnostics { get; private set; } = [];
    public IReadOnlyList<string> History => _undo.Select(s => s.Label).Reverse().ToArray();
    public event Action<DocumentChange>? Changed;
    public event Action? SelectionChanged;
    public DesignerSession(string source)
    {
        Source = _savedSource = source; Tree = XamlSyntaxTree.Parse(source);
    }
    public SourceHistorySnapshot CaptureHistory(int maximumEntries = 50)
    {
        if (maximumEntries is < 0 or > 200) throw new ArgumentOutOfRangeException(nameof(maximumEntries));
        return new(Tree.Source, _undo.TakeLast(maximumEntries).Select(Clone).ToArray(), _redo.TakeLast(maximumEntries).Select(Clone).ToArray());
    }
    public void RestoreHistory(SourceHistorySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Undo is null || snapshot.Redo is null || snapshot.Undo.Length + snapshot.Redo.Length > 400) throw new InvalidDataException("Invalid source history size.");
        var validTree = XamlSyntaxTree.Parse(snapshot.ValidSource);
        long size = 0;
        foreach (var entry in snapshot.Undo.Concat(snapshot.Redo))
        {
            if (entry is null || entry.Source is null || entry.ValidSource is null || entry.Selection is null || entry.Label is null || entry.Source.Length > XamlSyntaxTree.MaximumLength || entry.Label.Length > 1024 || entry.Selection.Length > 10000)
                throw new InvalidDataException("Invalid source history entry.");
            size += ((long)entry.Source.Length + entry.ValidSource.Length) * 2;
            if (size > HistoryBudget * 2) throw new InvalidDataException("Source history exceeds the memory budget.");
            XamlSyntaxTree.Parse(entry.ValidSource);
        }
        _undo.Clear(); _undo.AddRange(snapshot.Undo.Select(Clone));
        _redo.Clear(); _redo.AddRange(snapshot.Redo.Select(Clone));
        if (!IsValid) Tree = validTree;
        _lastTypingTick = 0;
    }
    private static SourceHistoryEntry Clone(SourceHistoryEntry value) => value with { Selection = value.Selection.ToArray() };
    private SourceHistoryEntry Capture(string label) => new(Source, Tree.Source, _selection.ToArray(), label);
    public void MarkSaved() { _lastTypingTick = 0; _savedSource = Source; Changed?.Invoke(new(Version, "Saved", Source)); }
    public void Select(string? id, bool additive = false)
    {
        if (!additive && _selection.Count == 1 && id is not null && _selection.Contains(id)) return;
        if (!additive) _selection.Clear();
        if (id is not null && Tree.Find(id) is not null)
        {
            if (additive && _selection.Contains(id)) _selection.Remove(id); else _selection.Add(id);
        }
        SelectionChanged?.Invoke();
    }
    public void SelectAll()
    {
        _selection.Clear();
        foreach (var child in Tree.Root.Children.Where(c => !c.IsProperty)) _selection.Add(child.Id);
        SelectionChanged?.Invoke();
    }
    public void SetSource(string source, string label = "Edit XAML", bool mergeTyping = false)
    {
        if (source == Source) return;
        if (!mergeTyping || _undo.Count == 0 || _undo[^1].Label != label || Environment.TickCount64 - _lastTypingTick > 1000) PushUndo(label);
        _lastTypingTick = mergeTyping ? Environment.TickCount64 : 0;
        _redo.Clear(); Load(source, label);
    }
    public void Apply(string label, IEnumerable<TextEdit> edits, long? expectedVersion = null)
    {
        if (!IsValid) throw new InvalidOperationException("Correct the XAML errors before using visual tools.");
        if (expectedVersion is { } version && version != Version) throw new InvalidOperationException("This edit targets an outdated document revision.");
        var changes = edits.ToArray();
        var oldSelection = SelectedElements().Select(e => (e.Span.Start, e.Name)).ToArray();
        var source = EditApplication.Apply(Source, changes);
        XamlSyntaxTree.Parse(source);
        SetSource(source, label); _selection.Clear();
        foreach (var (offset, name) in oldSelection)
        {
            if (changes.Any(e => e.Span.Length > 0 && e.Span.Contains(offset) && e.NewText.Length == 0)) continue;
            var mapped = offset + changes.Where(e => e.Span.End <= offset).Sum(e => e.NewText.Length - e.Span.Length);
            var node = Tree.At(mapped); if (node is not null && node.Name == name) _selection.Add(node.Id);
        }
        SelectionChanged?.Invoke();
    }
    public void SetProperty(string name, string? value)
    {
        var selected = SelectedElements().ToArray();
        if (selected.Length == 1 && XamlNames.IsName(selected[0], name) && XamlNames.Name(selected[0]) is not null && value is not null)
            Apply("Rename control", XamlNames.Rename(Tree, selected[0], value));
        else
        {
            if (selected.Length > 1 && selected.Any(e => XamlNames.IsName(e, name))) throw new InvalidOperationException("Rename controls individually to keep names unique.");
            Apply($"Set {name}", selected.Select(e => XamlEdits.SetAttribute(Tree, e, name, value)));
        }
    }
    public IEnumerable<XamlElement> SelectedElements() => _selection.Select(Tree.Find).OfType<XamlElement>();
    public IEnumerable<XamlElement> SelectedRoots()
    {
        foreach (var element in SelectedElements())
        {
            var parent = element.Parent;
            while (parent is not null && !_selection.Contains(parent.Id)) parent = parent.Parent;
            if (parent is null) yield return element;
        }
    }
    public void DeleteSelection()
    {
        var roots = SelectedRoots().Where(e => e.Parent is not null).ToArray();
        Apply("Delete selection", roots.Select(e => XamlEdits.Delete(Tree, e))); Select(null);
    }
    public void DuplicateSelection()
    {
        var roots = SelectedRoots().Where(e => e.Parent is not null).ToArray(); Apply("Duplicate selection", XamlNames.Duplicate(Tree, roots));
    }
    public void Undo()
    {
        if (_undo.Count == 0) return;
        _lastTypingTick = 0; var state = _undo[^1]; _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(Capture(state.Label)); Restore(state, "Undo " + state.Label);
    }
    public void Redo()
    {
        if (_redo.Count == 0) return;
        _lastTypingTick = 0; var state = _redo[^1]; _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(Capture(state.Label)); Restore(state, "Redo " + state.Label);
    }
    private void PushUndo(string label)
    {
        _undo.Add(Capture(label));
        while (_undo.Count > 1 && (_undo.Count > 200 || _undo.Sum(s => ((long)s.Source.Length + s.ValidSource.Length) * 2) > HistoryBudget)) _undo.RemoveAt(0);
    }
    private void Restore(SourceHistoryEntry state, string label)
    {
        Tree = XamlSyntaxTree.Parse(state.ValidSource); Load(state.Source, label); _selection.Clear();
        foreach (var id in state.Selection.Where(id => Tree.Find(id) is not null)) _selection.Add(id);
        SelectionChanged?.Invoke();
    }
    private void Load(string source, string label)
    {
        Source = source; Version++; var watch = Stopwatch.StartNew();
        try { Tree = XamlSyntaxTree.Parse(source); IsValid = true; Diagnostics = []; }
        catch (XmlException ex)
        {
            IsValid = false;
            var lines = source.Split('\n');
            var offset = lines.Take(Math.Max(0, ex.LineNumber - 1)).Sum(l => l.Length + 1) + Math.Max(0, ex.LinePosition - 1);
            Diagnostics = [new("XAML001", ex.Message, DiagnosticSeverity.Error, Math.Min(source.Length, offset), 1)];
        }
        LastParseMilliseconds = watch.Elapsed.TotalMilliseconds;
        if (IsValid) _selection.RemoveWhere(id => Tree.Find(id) is null);
        Changed?.Invoke(new(Version, label, Source)); SelectionChanged?.Invoke();
    }
}
