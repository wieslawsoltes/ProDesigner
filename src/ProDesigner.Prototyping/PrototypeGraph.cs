using ProDesigner.Core;

namespace ProDesigner.Prototyping;

public enum PrototypeTrigger { Click, PointerEnter, Key, AfterDelay }
public enum PrototypeAction { Navigate, Back, OpenOverlay, CloseOverlay, SetVariable }
public sealed record PrototypeCondition(string Variable, string EqualsValue);
public sealed record PrototypeLink(string Id, string SourceDocument, string SourceElement, PrototypeTrigger Trigger,
    PrototypeAction Action, string? TargetDocument = null, string? Key = null, double DelaySeconds = 0,
    string? Variable = null, string? Value = null, PrototypeCondition? Condition = null);
public sealed record PrototypeGraph(string? StartDocument, PrototypeLink[] Links, Dictionary<string, string> Variables)
{
    public static PrototypeGraph Empty => new(null, [], []);
    public IReadOnlyList<DesignDiagnostic> Validate(IReadOnlyDictionary<string, IReadOnlyCollection<string>> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        var diagnostics = new List<DesignDiagnostic>();
        void Error(string message) => diagnostics.Add(new("FLOW001", message, DiagnosticSeverity.Error));
        if (Links is null || Variables is null || Links.Length > 10000 || Variables.Count > 1000) { Error("Invalid prototype graph size."); return diagnostics; }
        if (StartDocument is not null && !documents.ContainsKey(StartDocument)) Error("The prototype start view does not exist.");
        if (Variables.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Key.Length > 256 || p.Value is null || p.Value.Length > 65536)) Error("Prototype variables exceed their name/value limits.");
        var ids = new HashSet<string>();
        var dispatch = new HashSet<(string, string, PrototypeTrigger, string?, double, string?, string?)>();
        foreach (var link in Links)
        {
            if (link is null || string.IsNullOrWhiteSpace(link.Id) || link.Id.Length > 256 || string.IsNullOrWhiteSpace(link.SourceDocument) || link.SourceElement is null || link.SourceElement.Length > 256)
            { Error("Every prototype link needs valid source and identity metadata."); continue; }
            if (!ids.Add(link.Id)) Error("Every prototype link needs a unique identity.");
            if (!Enum.IsDefined(link.Trigger) || !Enum.IsDefined(link.Action)) Error("Unknown prototype operation.");
            if (!documents.TryGetValue(link.SourceDocument, out var elements)) Error($"Source view '{link.SourceDocument}' does not exist.");
            else if (link.Trigger is PrototypeTrigger.Click or PrototypeTrigger.PointerEnter && elements.Count(e => e == link.SourceElement) != 1) Error($"Source control '{link.SourceElement}' is missing or ambiguous.");
            if (link.Action is PrototypeAction.Navigate or PrototypeAction.OpenOverlay && (link.TargetDocument is null || !documents.ContainsKey(link.TargetDocument))) Error("The destination view does not exist.");
            if (!double.IsFinite(link.DelaySeconds) || link.DelaySeconds is < 0 or > 86400 || link.Trigger == PrototypeTrigger.AfterDelay && link.DelaySeconds < .05) Error("Delay must be between 0.05 seconds and one day.");
            if (link.Trigger == PrototypeTrigger.Key && (string.IsNullOrWhiteSpace(link.Key) || link.Key.Length > 64)) Error("A key trigger needs a valid key.");
            if (link.Action == PrototypeAction.SetVariable && (string.IsNullOrWhiteSpace(link.Variable) || link.Variable.Length > 256 || link.Value is null || link.Value.Length > 65536)) Error("Set-variable requires a bounded name and value.");
            if (link.Condition is { } condition && (string.IsNullOrWhiteSpace(condition.Variable) || condition.Variable.Length > 256 || condition.EqualsValue is null || condition.EqualsValue.Length > 65536)) Error("Invalid prototype condition.");
            var element = link.Trigger is PrototypeTrigger.Key or PrototypeTrigger.AfterDelay ? "" : link.SourceElement;
            var key = link.Trigger == PrototypeTrigger.Key ? link.Key?.ToUpperInvariant() : null;
            var delay = link.Trigger == PrototypeTrigger.AfterDelay ? link.DelaySeconds : 0;
            if (!dispatch.Add((link.SourceDocument, element, link.Trigger, key, delay, link.Condition?.Variable, link.Condition?.EqualsValue))) Error("Two links have the same trigger and condition; make dispatch unambiguous.");
        }
        if (StartDocument is { } start && documents.ContainsKey(start))
        {
            var reached = new HashSet<string> { start }; var queue = new Queue<string>(); queue.Enqueue(start);
            while (queue.TryDequeue(out var document))
                foreach (var target in Links.Where(l => l is not null && l.SourceDocument == document && l.Action is PrototypeAction.Navigate or PrototypeAction.OpenOverlay).Select(l => l.TargetDocument).OfType<string>())
                    if (reached.Add(target)) queue.Enqueue(target);
            foreach (var document in documents.Keys.Where(d => !reached.Contains(d))) diagnostics.Add(new("FLOW002", $"View '{document}' is not reachable from the start view.", DiagnosticSeverity.Warning));
        }
        return diagnostics;
    }
}
public sealed record PrototypeState(string Document, string[] Overlays, IReadOnlyDictionary<string, string> Variables, int BackCount);

/// <summary>Deterministic prototype execution with immutable input snapshots, bounded navigation, and a monotonic host clock.</summary>
public sealed class PrototypePlayer
{
    private readonly PrototypeGraph _graph;
    private readonly List<string> _history = [];
    private readonly List<string> _overlays = [];
    private readonly Dictionary<string, string> _variables;
    private readonly HashSet<string> _firedDelays = [];
    private TimeSpan _enteredAt;
    private TimeSpan _lastTime;
    private string _current;
    private bool _dispatching;
    public PrototypeState State => new(_current, _overlays.ToArray(), new Dictionary<string, string>(_variables), _history.Count);
    public event Action<PrototypeState>? Changed;
    public PrototypePlayer(PrototypeGraph graph, IReadOnlyDictionary<string, IReadOnlyCollection<string>> documents)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var errors = graph.Validate(documents).Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        if (errors.Length > 0) throw new InvalidOperationException(string.Join("\n", errors.Select(d => d.Message)));
        _graph = graph with { Links = graph.Links.ToArray(), Variables = new(graph.Variables) };
        _current = graph.StartDocument ?? documents.Keys.FirstOrDefault() ?? throw new InvalidOperationException("A prototype needs a view.");
        _variables = new(graph.Variables);
    }
    public bool Dispatch(PrototypeTrigger trigger, string element = "", string? key = null)
    {
        if (trigger == PrototypeTrigger.AfterDelay) throw new ArgumentException("Use Advance for delayed transitions.", nameof(trigger));
        if (_dispatching) return false;
        var visible = _overlays.LastOrDefault() ?? _current;
        var link = _graph.Links.Where(l => l.SourceDocument == visible && l.Trigger == trigger &&
                (trigger == PrototypeTrigger.Key ? string.Equals(l.Key, key, StringComparison.OrdinalIgnoreCase) : l.SourceElement == element) && Matches(l.Condition))
            .OrderByDescending(l => l.Condition is not null).FirstOrDefault();
        if (link is null) return false;
        _dispatching = true;
        try { if (Apply(link)) Changed?.Invoke(State); return true; }
        finally { _dispatching = false; }
    }
    public void Advance(TimeSpan elapsed)
    {
        if (elapsed < _lastTime) throw new ArgumentOutOfRangeException(nameof(elapsed), "The prototype clock must be monotonic.");
        _lastTime = elapsed;
        var visible = _overlays.LastOrDefault() ?? _current;
        foreach (var link in _graph.Links.Where(l => l.SourceDocument == visible && l.Trigger == PrototypeTrigger.AfterDelay).OrderBy(l => l.DelaySeconds))
        {
            if (_firedDelays.Contains(link.Id) || elapsed - _enteredAt < TimeSpan.FromSeconds(link.DelaySeconds) || !Matches(link.Condition)) continue;
            _firedDelays.Add(link.Id); if (Apply(link)) Changed?.Invoke(State); break;
        }
    }
    public bool Back()
    {
        if (!BackCore()) return false;
        Changed?.Invoke(State); return true;
    }
    private bool BackCore()
    {
        if (_overlays.Count > 0) _overlays.RemoveAt(_overlays.Count - 1);
        else if (_history.Count > 0) { _current = _history[^1]; _history.RemoveAt(_history.Count - 1); }
        else return false;
        Enter(); return true;
    }
    private bool Matches(PrototypeCondition? condition) => condition is null || _variables.GetValueOrDefault(condition.Variable) == condition.EqualsValue;
    private bool Apply(PrototypeLink link)
    {
        switch (link.Action)
        {
            case PrototypeAction.Navigate:
                if (_current == link.TargetDocument && _overlays.Count == 0) return false;
                _history.Add(_current); if (_history.Count > 256) _history.RemoveAt(0);
                _current = link.TargetDocument!; _overlays.Clear(); Enter(); return true;
            case PrototypeAction.Back: return BackCore();
            case PrototypeAction.OpenOverlay:
                if (_overlays.Count >= 16) throw new InvalidOperationException("Maximum overlay depth reached.");
                _overlays.Add(link.TargetDocument!); Enter(); return true;
            case PrototypeAction.CloseOverlay:
                if (_overlays.Count == 0) return false;
                _overlays.RemoveAt(_overlays.Count - 1); Enter(); return true;
            case PrototypeAction.SetVariable:
                if (_variables.GetValueOrDefault(link.Variable!) == link.Value) return false;
                if (_variables.Count >= 1000 && !_variables.ContainsKey(link.Variable!)) throw new InvalidOperationException("Prototype variable limit reached.");
                _variables[link.Variable!] = link.Value!; return true;
            default: return false;
        }
    }
    private void Enter() { _enteredAt = _lastTime; _firedDelays.Clear(); }
}
