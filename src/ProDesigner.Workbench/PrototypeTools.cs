using Avalonia.Controls;
using Avalonia.Layout;
using ProDesigner.Core;
using ProDesigner.Design;
using ProDesigner.Prototyping;
using ProDesigner.Xaml;
using static ProDesigner.Workbench.StudioControls;

namespace ProDesigner.Workbench;

public sealed partial class DesignerWorkbench
{
    private PrototypeGraph _prototype = PrototypeGraph.Empty;
    public PrototypeGraph Prototype => _prototype;
    public IReadOnlyDictionary<string, IReadOnlyCollection<string>> PrototypeDocuments() => _documents.ToDictionary(DocumentId,
        d => (IReadOnlyCollection<string>)d.Session.Tree.Elements.Where(n => XamlNames.Scope(n) == d.Session.Tree.Root).Select(XamlNames.Name).OfType<string>().ToArray());
    public void SetPrototype(PrototypeGraph graph)
    {
        var errors = graph.Validate(PrototypeDocuments()).Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        if (errors.Length > 0) throw new InvalidOperationException(string.Join("\n", errors.Select(d => d.Message)));
        _prototype = graph; ScheduleRecovery();
    }
    public void AddPrototypeLink(PrototypeLink link)
    {
        SetPrototype(_prototype with { StartDocument = _prototype.StartDocument ?? DocumentId(_active), Links = [.. _prototype.Links.Where(l => l.Id != link.Id), link] });
    }
    private void ShowPrototypeTools()
    {
        _editor.Flush();
        var documents = _documents.ToArray(); var names = documents.Select(d => d.Name).ToArray();
        var source = new ComboBox { ItemsSource = names, SelectedIndex = Array.IndexOf(documents, _active) };
        var destination = new ComboBox { ItemsSource = names, SelectedIndex = Math.Min(1, names.Length - 1) };
        var control = Field(Session.Primary is { } node ? XamlNames.Name(node) : null, "Source x:Name");
        var trigger = new ComboBox { ItemsSource = Enum.GetNames<PrototypeTrigger>(), SelectedIndex = 0 };
        var action = new ComboBox { ItemsSource = Enum.GetNames<PrototypeAction>(), SelectedIndex = 0 };
        var key = Field("Enter", "Keyboard key"); var delay = Field("1", "Delay seconds");
        var variable = Field(watermark: "Variable name (SetVariable)"); var value = Field(watermark: "Value");
        var conditionVariable = Field(watermark: "Optional condition variable"); var conditionValue = Field(watermark: "Equals value");
        var rows = new StackPanel { Spacing = 8 };
        string Name(string? id) => documents.FirstOrDefault(d => DocumentId(d) == id)?.Name ?? id ?? "";
        foreach (var link in _prototype.Links)
        {
            rows.Children.Add(Row(Text($"{Name(link.SourceDocument)} · {link.SourceElement} → {Name(link.TargetDocument)} ({link.Action})", 10),
                Button("Remove", "Remove this prototype link", () => { _prototype = _prototype with { Links = _prototype.Links.Where(l => l.Id != link.Id).ToArray() }; ScheduleRecovery(); ShowPrototypeTools(); })));
        }
        var configuration = Column(Caption("FROM VIEW / NAMED CONTROL"), source, control, Caption("TRIGGER / ACTION"), Row(trigger, action),
            Caption("DESTINATION"), destination, Row(key, delay), Row(variable, value), Row(conditionVariable, conditionValue),
            Button("Create connection", "Validate and add an executable prototype connection", () => Guard(() =>
            {
                var selectedTrigger = Enum.Parse<PrototypeTrigger>(trigger.SelectedItem!.ToString()!);
                var selectedAction = Enum.Parse<PrototypeAction>(action.SelectedItem!.ToString()!);
                var link = new PrototypeLink(Guid.NewGuid().ToString("N"), DocumentId(documents[source.SelectedIndex]), control.Text ?? "", selectedTrigger, selectedAction,
                    selectedAction is PrototypeAction.Navigate or PrototypeAction.OpenOverlay ? DocumentId(documents[destination.SelectedIndex]) : null,
                    selectedTrigger == PrototypeTrigger.Key ? key.Text : null, selectedTrigger == PrototypeTrigger.AfterDelay ? LayoutEngine.Number(delay.Text, double.NaN) : 0,
                    selectedAction == PrototypeAction.SetVariable ? variable.Text : null, selectedAction == PrototypeAction.SetVariable ? value.Text : null,
                    string.IsNullOrWhiteSpace(conditionVariable.Text) ? null : new(conditionVariable.Text, conditionValue.Text ?? ""));
                AddPrototypeLink(link); ShowPrototypeTools();
            }), true));
        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "Connections", Content = new ScrollViewer { Content = Column(configuration, Caption("EXISTING CONNECTIONS"), rows), MaxHeight = 430 } });
        tabs.Items.Add(new TabItem { Header = "Flow map", Content = new PrototypeMap(_prototype, documents.ToDictionary(DocumentId, d => d.Name)) { Height = 330 } });
        var diagnostics = _prototype.Validate(PrototypeDocuments());
        var summary = new TextBlock { Text = diagnostics.Count == 0 ? "No flow diagnostics." : string.Join("\n", diagnostics.Select(d => d.Message)), TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 10 };
        ShowDialog("Prototype connections & flows", Column(tabs, summary,
            Row(Button("Use current view as start", "Set the prototype entry point", () => { _prototype = _prototype with { StartDocument = DocumentId(_active) }; ScheduleRecovery(); ShowPrototypeTools(); }),
                Button("Run prototype", "Run the prototype with real built-in Avalonia controls", RunPrototype, true))));
    }
    public void RunPrototype()
    {
        Guard(() =>
        {
            var sources = _documents.ToDictionary(DocumentId, d => new PrototypeViewDocument(d.Name, d.Session.Source));
            var graph = _prototype with { StartDocument = _prototype.StartDocument ?? DocumentId(_active) };
            var player = new PrototypePlayer(graph, PrototypeDocuments());
            var runner = new PrototypeRunner(player, sources, _surface.SampleData);
            ShowDialog("Interactive prototype", runner);
        });
    }
}
