using Avalonia.Controls;
using Avalonia.Layout;
using ProDesigner.Core;
using ProDesigner.DesignSystems;
using ProDesigner.Xaml;
using static ProDesigner.Workbench.StudioControls;

namespace ProDesigner.Workbench;

public sealed partial class DesignerWorkbench
{
    private DesignSystemState _designSystem = DesignSystemState.Empty;
    private readonly Dictionary<DocumentTab, string> _documentIds = [];
    private sealed record StudioTransaction(WorkspaceTransaction Text, DesignSystemState Before, DesignSystemState After);
    private readonly Stack<StudioTransaction> _studioUndo = [];
    private readonly Stack<StudioTransaction> _studioRedo = [];
    public DesignSystemState DesignSystem => _designSystem;
    public string DocumentId(DocumentTab document)
    {
        if (!_documentIds.TryGetValue(document, out var id)) _documentIds[document] = id = Guid.NewGuid().ToString("N");
        return id;
    }
    public ComponentDefinition CaptureComponent(string name)
    {
        _editor.Flush();
        var node = Session.Primary ?? throw new InvalidOperationException("Select a visual element to capture.");
        var definition = ComponentEngine.Capture(name, Session.Tree, node);
        var state = _designSystem with { Components = [.. _designSystem.Components, definition] };
        ComponentEngine.Validate(state); _designSystem = state; ScheduleRecovery(); return definition;
    }
    public ComponentInstance InsertComponent(string componentId, string? variant = null)
    {
        _editor.Flush();
        var definition = _designSystem.Components.Single(c => c.Id == componentId);
        var parent = Session.Primary;
        while (parent is not null && parent.LocalName is not "Canvas" and not "Grid" and not "StackPanel" and not "WrapPanel" and not "DockPanel") parent = parent.Parent;
        parent ??= Session.Tree.Elements.FirstOrDefault(n => n.LocalName is "Canvas" or "Grid" or "StackPanel") ?? throw new InvalidOperationException("Select a supported container.");
        if (XamlNames.Scope(parent) != Session.Tree.Root) throw new InvalidOperationException("Linked instances currently belong to the view's root namescope, not a template namescope.");
        var used = Session.Tree.Elements.Select(XamlNames.Name).ToHashSet(); var index = 1;
        while (used.Contains("Component" + index)) index++;
        var instance = ComponentEngine.CreateInstance(definition, DocumentId(_active), "Component" + index, variant);
        var edits = FragmentImporter.Append(Session.Tree, parent, instance.Baseline);
        var source = EditApplication.Apply(Session.Source, edits); var updated = XamlSyntaxTree.Parse(source);
        var root = updated.Elements.Single(n => XamlNames.Name(n) == instance.RootName && XamlNames.Scope(n) == updated.Root);
        instance = instance with { Baseline = source.Substring(root.Span.Start, root.Span.Length), NamespaceContext = new(XamlNames.Namespaces(root)) };
        var after = _designSystem with { Instances = [.. _designSystem.Instances, instance] };
        ApplyStudioTransaction(new("Insert linked component", [new(instance.DocumentId, Session.Source, source)]), after);
        SelectByName(instance.RootName); return instance;
    }
    public void UpdateComponent(string componentId, string xaml, ComponentVariant[] variants)
    {
        _editor.Flush();
        var current = _designSystem.Components.Single(c => c.Id == componentId);
        var definition = ComponentEngine.Revise(current, xaml, variants);
        // Prepare every instance before touching any document; a conflict in one view aborts the entire update.
        var documents = _documents.ToDictionary(DocumentId);
        var updates = _designSystem.Instances.Where(i => i.ComponentId == componentId).Select(i =>
        {
            if (!documents.TryGetValue(i.DocumentId, out var document)) throw new ComponentConflictException("An instance document is not open.");
            if (!document.Session.IsValid) throw new ComponentConflictException("Correct invalid instance XAML before updating the component.");
            return ComponentEngine.PrepareUpdate(definition, i, document.Session.Tree);
        }).ToArray();
        var changes = updates.GroupBy(u => u.DocumentId).Select(g =>
        {
            var document = documents[g.Key]; return new DocumentReplacement(g.Key, document.Session.Source, EditApplication.Apply(document.Session.Source, g.SelectMany(u => u.NamespaceEdits ?? []).Distinct().Concat(g.Select(u => u.Edit))));
        }).ToArray();
        var after = _designSystem with
        {
            Components = _designSystem.Components.Select(c => c.Id == componentId ? definition : c).ToArray(),
            Instances = _designSystem.Instances.Select(i => updates.FirstOrDefault(u => u.InstanceId == i.Id)?.UpdatedInstance ?? i).ToArray()
        };
        if (changes.Length == 0) { _designSystem = after; ScheduleRecovery(); }
        else ApplyStudioTransaction(new("Update linked component", changes), after);
    }
    public void SetInstanceOverrides(string instanceId, string? variant, ComponentOverride[] overrides)
    {
        _editor.Flush();
        var instance = _designSystem.Instances.Single(i => i.Id == instanceId);
        var definition = _designSystem.Components.Single(c => c.Id == instance.ComponentId);
        var document = _documents.Single(d => DocumentId(d) == instance.DocumentId);
        var pending = instance with { Variant = variant, Overrides = overrides };
        var update = ComponentEngine.PrepareUpdate(definition, pending, document.Session.Tree);
        var source = EditApplication.Apply(document.Session.Source, (update.NamespaceEdits ?? []).Concat([update.Edit]));
        var after = _designSystem with { Instances = _designSystem.Instances.Select(i => i.Id == instanceId ? update.UpdatedInstance : i).ToArray() };
        ApplyStudioTransaction(new("Override linked instance", [new(instance.DocumentId, document.Session.Source, source)]), after);
    }
    public void DetachInstance(string id)
    {
        _designSystem = _designSystem with { Instances = _designSystem.Instances.Where(i => i.Id != id).ToArray() };
        _studioUndo.Clear(); _studioRedo.Clear(); ScheduleRecovery(); SetStatus("Instance detached; its XAML remains unchanged.");
    }
    private void ApplyStudioTransaction(WorkspaceTransaction transaction, DesignSystemState after, bool record = true)
    {
        var documents = _documents.ToDictionary(DocumentId);
        transaction.Validate(documents.ToDictionary(p => p.Key, p => p.Value.Session.Source));
        ComponentEngine.Validate(after);
        foreach (var change in transaction.Changes) XamlSyntaxTree.Parse(change.After);
        var before = _designSystem;
        foreach (var change in transaction.Changes) documents[change.DocumentId].Session.SetSource(change.After, transaction.Label);
        _designSystem = after;
        if (record) { _studioUndo.Push(new(transaction, before, after)); _studioRedo.Clear(); }
        RefreshTabs(); RefreshDocument(); ScheduleRecovery();
    }
    public void UndoStudioTransaction()
    {
        _editor.Flush(); if (!_studioUndo.TryPeek(out var operation)) return;
        if (_designSystem != operation.After) throw new InvalidOperationException("Component metadata changed after this operation. Resolve or detach it before grouped undo.");
        var documents = _documents.ToDictionary(DocumentId);
        operation.Text.Inverse().Validate(documents.ToDictionary(p => p.Key, p => p.Value.Session.Source));
        foreach (var change in operation.Text.Changes.Where(c => c.Before != c.After))
            if (documents[change.DocumentId].Session.CaptureHistory(1).Undo.LastOrDefault()?.Source != change.Before)
                throw new InvalidOperationException("Another source operation must be undone before this workspace edit.");
        foreach (var change in operation.Text.Changes.Where(c => c.Before != c.After)) documents[change.DocumentId].Session.Undo();
        _designSystem = operation.Before; _studioUndo.Pop(); _studioRedo.Push(operation); RefreshDocument(); ScheduleRecovery();
    }
    public void RedoStudioTransaction()
    {
        _editor.Flush(); if (!_studioRedo.TryPeek(out var operation)) return;
        if (_designSystem != operation.Before) throw new InvalidOperationException("Component metadata changed after undo.");
        var documents = _documents.ToDictionary(DocumentId);
        operation.Text.Validate(documents.ToDictionary(p => p.Key, p => p.Value.Session.Source));
        foreach (var change in operation.Text.Changes.Where(c => c.Before != c.After))
            if (documents[change.DocumentId].Session.CaptureHistory(1).Redo.LastOrDefault()?.Source != change.After)
                throw new InvalidOperationException("Source redo history no longer contains this workspace edit.");
        foreach (var change in operation.Text.Changes.Where(c => c.Before != c.After)) documents[change.DocumentId].Session.Redo();
        _designSystem = operation.After; _studioRedo.Pop(); _studioUndo.Push(operation); RefreshDocument(); ScheduleRecovery();
    }
    private void ShowComponentTools()
    {
        var content = new StackPanel { Spacing = 12 };
        var name = Field(Session.Primary?.DisplayName ?? "Component", "Component name");
        content.Children.Add(Caption("CAPTURE THE SELECTED SUBTREE")); content.Children.Add(name);
        content.Children.Add(Button("Create reusable component", "Capture selection with its namespace context", () => Guard(() => { CaptureComponent(name.Text ?? "Component"); ShowComponentTools(); }), true));
        content.Children.Add(Row(Button("Undo workspace edit", "Undo a grouped component operation across all affected views", () => Guard(UndoStudioTransaction)), Button("Redo workspace edit", "Redo a grouped component operation", () => Guard(RedoStudioTransaction))));
        foreach (var component in _designSystem.Components)
        {
            content.Children.Add(Caption(component.Name + " · REVISION " + component.Revision));
            var variant = new ComboBox { ItemsSource = new[] { "Default" }.Concat(component.Variants.Select(v => v.Name)), SelectedIndex = 0, MinWidth = 130 };
            content.Children.Add(Row(variant,
                Button("Insert instance", "Insert a source-linked component instance", () => Guard(() => { InsertComponent(component.Id, variant.SelectedIndex == 0 ? null : variant.SelectedItem?.ToString()); _overlay.IsVisible = false; })),
                Button("Edit component", "Edit template source and variants, then update all non-conflicting instances", () => ShowComponentDefinition(component))));
            foreach (var instance in _designSystem.Instances.Where(i => i.ComponentId == component.Id))
            {
                var document = _documents.FirstOrDefault(d => DocumentId(d) == instance.DocumentId);
                content.Children.Add(Row(Text((document?.Name ?? "Missing view") + " · " + instance.RootName, 11),
                    Button("Overrides", "Edit this instance's variant and explicit property overrides", () => ShowInstanceOverrides(instance)),
                    Button("Detach", "Keep the XAML, remove the source link", () => { DetachInstance(instance.Id); ShowComponentTools(); })));
            }
        }
        if (_designSystem.Components.Length == 0) content.Children.Add(new TextBlock { Text = "Components are stored in the workspace. Instances materialize as ordinary Avalonia XAML; no ProDesigner runtime dependency is required.", TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 11 });
        ShowDialog("Components & variants", new ScrollViewer { Content = content, MaxHeight = 570 });
    }
    private void ShowComponentDefinition(ComponentDefinition component)
    {
        var editor = new AvaloniaEdit.TextEditor { Text = component.Xaml, ShowLineNumbers = true, FontSize = 11, Height = 220, SyntaxHighlighting = StudioHighlighting.Xaml };
        var variantName = Field("Featured", "Variant name"); var setters = Field("$root.Background=#8570D8\n$root.Opacity=1"); setters.AcceptsReturn = true; setters.Height = 85;
        var variants = component.Variants.ToList(); var status = Text(string.Join(", ", variants.Select(v => v.Name)), 11);
        ShowDialog("Edit " + component.Name, Column(editor, Caption("VARIANT · TARGET.PROPERTY=VALUE"), variantName, setters,
            Button("Add / replace variant", "Stage a variant without altering component identity", () => Guard(() =>
            {
                var variant = new ComponentVariant(variantName.Text ?? "", ReadOverrides(setters.Text ?? ""));
                variants.RemoveAll(v => v.Name == variant.Name); variants.Add(variant); status.Text = string.Join(", ", variants.Select(v => v.Name));
            })), status, Button("Validate and update all instances", "Preflight all instances, preserve explicit overrides, and update the workspace", () => Guard(() => { UpdateComponent(component.Id, editor.Text, variants.ToArray()); ShowComponentTools(); }), true)));
    }
    private void ShowInstanceOverrides(ComponentInstance instance)
    {
        var component = _designSystem.Components.Single(c => c.Id == instance.ComponentId);
        var names = new[] { "Default" }.Concat(component.Variants.Select(v => v.Name)).ToArray();
        var variant = new ComboBox { ItemsSource = names, SelectedIndex = instance.Variant is null ? 0 : Array.IndexOf(names, instance.Variant) };
        var values = Field(string.Join("\n", instance.Overrides.Select(v => v.Target + "." + v.Property + "=" + v.Value)));
        values.AcceptsReturn = true; values.Height = 180;
        ShowDialog(instance.RootName + " overrides", Column(Caption("VARIANT"), variant, Caption("TARGET.PROPERTY=VALUE · $root targets the root"), values,
            Button("Apply overrides", "Apply overrides without changing the component definition", () => Guard(() => { SetInstanceOverrides(instance.Id, variant.SelectedIndex <= 0 ? null : variant.SelectedItem?.ToString(), ReadOverrides(values.Text ?? "")); ShowComponentTools(); }), true)));
    }
    private static ComponentOverride[] ReadOverrides(string text)
    {
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(line =>
        {
            var equals = line.IndexOf('='); var dot = line.IndexOf('.');
            if (dot <= 0 || equals <= dot + 1) throw new FormatException("Use Target.Property=Value on each line; attached properties may contain another dot.");
            return new ComponentOverride(line[..dot].Trim(), line[(dot + 1)..equals].Trim(), line[(equals + 1)..]);
        }).ToArray();
    }
}
