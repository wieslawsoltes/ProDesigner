using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ProDesigner.Animation;
using ProDesigner.Core;
using ProDesigner.Design;
using ProDesigner.Xaml;
using static ProDesigner.Workbench.StudioControls;

namespace ProDesigner.Workbench;

public sealed partial class DesignerWorkbench
{
    public void Execute(string command) => Guard(() =>
    {
        switch (command)
        {
            case "undo": Session.Undo(); break;
            case "redo": Session.Redo(); break;
            case "delete": Session.DeleteSelection(); break;
            case "duplicate": Session.DuplicateSelection(); break;
            case "select-all": Session.SelectAll(); break;
            case "copy": CopySelection(); break;
            case "paste": PasteSelection(); break;
            case "new": NewDocument(); break;
            case "fit": _surface.Fit(); break;
            case "preview": TogglePreview(); break;
            case "find": _bottom.SelectedIndex = 0; _editor.ShowFind(); break;
            case "authoring": ShowAuthoringTools(); break;
            case "components": ShowComponentTools(); break;
            case "prototype": ShowPrototypeTools(); break;
            case "prototype-run": RunPrototype(); break;
            case "constraints": ShowConstraintTools(); break;
            case "refactor": ShowProjectRefactoring(); break;
            case "refactor-undo": _ = UndoRenameFromUiAsync(); break;
            case "studio-undo": UndoStudioTransaction(); break;
            case "studio-redo": RedoStudioTransaction(); break;
            case "vector": ShowVectorEditor(); break;
            case "workspace-save": _ = SaveWorkspaceAsync(); break;
            case "workspace-open": _ = OpenWorkspaceSnapshotAsync(); break;
            case "recovery": ShowRecovery(); break;
            case "restore-recovery": RestoreRecovery(); break;
            case "animation-import": ImportAnimation(); break;
            case "animation": _bottom.SelectedIndex = 1; break;
            default: throw new ArgumentException("Unknown designer command: " + command);
        }
        RefreshDocument();
    });
    public void SetProperty(string name, string value) => Guard(() => Session.SetProperty(name, value));
    public void SelectByName(string name) => Session.Select(Session.Tree.Elements.FirstOrDefault(n => n.DisplayName == name)?.Id);
    public void InsertControl(string name) => Guard(() =>
    {
        var item = ControlCatalog.Items.FirstOrDefault(i => i.Name == name) ?? throw new ArgumentException("Unknown control: " + name);
        var parent = Session.Primary;
        while (parent is not null && parent.LocalName is not "Canvas" and not "Grid" and not "StackPanel" and not "DockPanel" and not "WrapPanel") parent = parent.Parent;
        parent ??= Session.Tree.Elements.FirstOrDefault(n => n.LocalName == "Canvas") ?? Session.Tree.Elements.FirstOrDefault(n => n.LocalName is "Grid" or "StackPanel") ?? Session.Tree.Root;
        var taken = Session.Tree.Elements.Select(n => n.DisplayName).ToHashSet(); var number = 1;
        while (taken.Contains(name + number)) number++;
        var controlName = name + number;
        var insertion = item.Xaml.IndexOf(' '); if (insertion < 0) insertion = item.Xaml.IndexOf('>');
        var attributes = $" xmlns:x=\"{XamlNames.LanguageNamespace}\" x:Name=\"{controlName}\"";
        if (parent.LocalName == "Canvas") attributes += " Canvas.Left=\"40\" Canvas.Top=\"40\"";
        var fragment = item.Xaml.Insert(insertion, attributes);
        Session.Apply("Insert " + name, FragmentImporter.Append(Session.Tree, parent, fragment));
        SelectByName(controlName); RefreshDocument();
    });
    private void NewDocument()
    {
        var number = 1; while (_documents.Any(d => d.Name == $"View{number}.axaml")) number++;
        var session = new DesignerSession("<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Background=\"#F8F7FC\">\n  <Canvas x:Name=\"RootCanvas\" Background=\"Transparent\" />\n</UserControl>");
        var document = new DocumentTab($"View{number}.axaml", session); _documents.Add(document); SwitchDocument(document);
    }
    private void CopySelection()
    {
        _copiedFragment = string.Join(Session.Tree.NewLine, Session.SelectedRoots().Where(n => n.Parent is not null).Select(n => XamlNames.ExportFragment(Session.Tree, n)));
        SetStatus("Copied selection to the designer clipboard.");
    }
    private void PasteSelection()
    {
        if (string.IsNullOrEmpty(_copiedFragment)) return;
        var parent = Session.Primary;
        while (parent is not null && parent.LocalName is not "Canvas" and not "Grid" and not "StackPanel") parent = parent.Parent;
        parent ??= Session.Tree.Elements.FirstOrDefault(n => n.LocalName is "Canvas" or "Grid" or "StackPanel") ?? Session.Tree.Root;
        var wrapper = "<Fragment xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"" + XamlNames.LanguageNamespace + "\">";
        var fragmentTree = XamlSyntaxTree.Parse(wrapper + _copiedFragment + "</Fragment>");
        var fragment = _copiedFragment;
        var taken = Session.Tree.Elements.Select(XamlNames.Name).Where(n => n is not null).ToHashSet();
        var rewrites = new List<TextEdit>(); var mapping = new Dictionary<(XamlElement, string), string>();
        foreach (var node in fragmentTree.Elements.Skip(1))
        {
            var name = XamlNames.Name(node); if (name is null) continue;
            var unique = name; var index = 2; while (!taken.Add(unique)) unique = name + "Copy" + index++;
            mapping[(XamlNames.Scope(node), name)] = unique;
        }
        foreach (var node in fragmentTree.Elements.Skip(1)) foreach (var attribute in node.Attributes)
        {
            var value = attribute.Value;
            foreach (var pair in mapping.Where(p => p.Key.Item1 == XamlNames.Scope(node)))
                value = XamlNames.IsName(node, attribute.Name) && value == pair.Key.Item2 ? pair.Value : XamlNames.RewriteReference(attribute.Name, value, pair.Key.Item2, pair.Value);
            if (value != attribute.Value) rewrites.Add(new(new(attribute.ValueSpan.Start - wrapper.Length, attribute.ValueSpan.Length), XamlEdits.Escape(value, attribute.Quote)));
        }
        fragment = EditApplication.Apply(fragment, rewrites);
        Session.Apply("Paste controls", FragmentImporter.Append(Session.Tree, parent, fragment));
    }
    private void TogglePreview() { _surface.SetInteractive(!_surface.Interactive); SetStatus(_surface.Interactive ? "Interactive preview · click controls to test built-in behavior. Click Preview to return to design." : "Design mode · source editing enabled."); }
    private void ExportAnimation() => Guard(() =>
    {
        if (_active.Animation.Tracks.Count + _active.Animation.ColorTracks.Count == 0) { SetStatus("Add animation tracks first."); return; }
        var styles = _active.Animation.ToAvaloniaStyles();
        var propertyName = Session.Tree.Root.Name + ".Styles";
        var property = Session.Tree.Root.Children.FirstOrDefault(n => n.Name == propertyName);
        var fragment = property is null ? $"<{propertyName}>\n{styles}</{propertyName}>" : styles;
        Session.Apply("Insert animation styles", [XamlEdits.AppendChild(Session.Tree, property ?? Session.Tree.Root, fragment)]);
        _bottom.SelectedIndex = 0; RefreshDocument();
    });
    private void KeyPressed(object? sender, KeyEventArgs e)
    {
        // Modal editors and prototypes own their keyboard input. Canvas shortcuts must never
        // delete, nudge or undo the document behind a dialog; child editors still receive keys.
        if (_overlay.IsVisible && e.Key != Key.Escape) return;
        var control = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        if (control)
        {
            if (e.Key == Key.S) { _editor.Flush(); _ = SaveDocumentAsync(); e.Handled = true; }
            else if (e.Key == Key.O) { _ = OpenDocumentsAsync(); e.Handled = true; }
            else if (e.Key == Key.K) { ShowCommandPalette(); e.Handled = true; }
            else if (e.Key == Key.Z && _timeline.IsKeyboardFocusWithin && e.Source is not TextBox) { if ((e.KeyModifiers & KeyModifiers.Shift) != 0) _timeline.Redo(); else _timeline.Undo(); e.Handled = true; }
            else if (e.Key == Key.Z && e.Source is not TextBox) { Execute((e.KeyModifiers & KeyModifiers.Shift) != 0 ? "redo" : "undo"); e.Handled = true; }
            else if (e.Key == Key.Y && e.Source is not TextBox) { Execute("redo"); e.Handled = true; }
            else if (e.Key == Key.D && !_editor.IsKeyboardFocusWithin) { Execute("duplicate"); e.Handled = true; }
            else if (e.Key == Key.C && !_editor.IsKeyboardFocusWithin && e.Source is not TextBox) { Execute("copy"); e.Handled = true; }
            else if (e.Key == Key.V && !_editor.IsKeyboardFocusWithin && e.Source is not TextBox) { Execute("paste"); e.Handled = true; }
            return;
        }
        if (e.Key == Key.Escape)
        {
            _overlay.IsVisible = false; _surface.CancelGesture(); _surface.SetInteractive(false);
            Focus(); e.Handled = true; return;
        }
        if (_editor.IsKeyboardFocusWithin || _inspector.IsKeyboardFocusWithin || e.Source is TextBox) return;
        if (e.Key is Key.Delete or Key.Back) { Execute("delete"); e.Handled = true; }
        else if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            var step = (e.KeyModifiers & KeyModifiers.Shift) != 0 ? 10 : 1;
            Guard(() => Session.Apply("Nudge selection", Session.SelectedRoots().SelectMany(n => LayoutEngine.Move(Session.Tree, n, e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0, e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0, false))));
            e.Handled = true;
        }
    }
    private void ShowCommandPalette()
    {
        var search = Field(watermark: "Type a command…");
        var items = new StackPanel { Spacing = 4 };
        var commands = new Dictionary<string, Action>
        {
            ["Project-aware XAML / C# rename"] = ShowProjectRefactoring, ["Undo last project rename"] = () => _ = UndoRenameFromUiAsync(),
            ["Components and variants"] = ShowComponentTools, ["Prototype connections and flow map"] = ShowPrototypeTools,
            ["Run interactive prototype"] = RunPrototype, ["Responsive constraints"] = ShowConstraintTools,
            ["Undo grouped workspace edit"] = () => Guard(UndoStudioTransaction), ["Redo grouped workspace edit"] = () => Guard(RedoStudioTransaction),
            ["Design system / resources / styles / paint"] = ShowAuthoringTools, ["Edit vector geometry"] = ShowVectorEditor,
            ["Save complete workspace"] = () => _ = SaveWorkspaceAsync(),
            ["Open saved workspace"] = () => _ = OpenWorkspaceSnapshotAsync(), ["Recovery journal"] = ShowRecovery,
            ["Stop isolated preview"] = () => _ = StopExternalPreviewAsync(),
            ["New view"] = NewDocument, ["Open XAML documents"] = () => _ = OpenDocumentsAsync(), ["Save active view"] = () => _ = SaveDocumentAsync(),
            ["Open solution / project"] = () => _ = OpenWorkspaceAsync(), ["Undo"] = () => Execute("undo"), ["Redo"] = () => Execute("redo"),
            ["Duplicate selection"] = () => Execute("duplicate"), ["Delete selection"] = () => Execute("delete"), ["Fit all artboards"] = _surface.Fit,
            ["Find in XAML"] = () => Execute("find"), ["Animation timeline"] = () => Execute("animation"), ["Preview profiles"] = ShowProfiles,
            ["Trusted runtime preview (desktop)"] = ShowTrustedPreview, ["Edit C# code-behind"] = ShowCodeBehind,
            ["Align left"] = () => Guard(() => _surface.Align(Alignment.Left)), ["Align center"] = () => Guard(() => _surface.Align(Alignment.HorizontalCenter)),
            ["Align right"] = () => Guard(() => _surface.Align(Alignment.Right)), ["Align top"] = () => Guard(() => _surface.Align(Alignment.Top)),
            ["Align bottom"] = () => Guard(() => _surface.Align(Alignment.Bottom))
        };
        foreach (var item in ControlCatalog.Items) commands["Insert " + item.Name] = () => InsertControl(item.Name);
        void Update()
        {
            items.Children.Clear(); foreach (var command in commands.Where(c => c.Key.Contains(search.Text ?? "", StringComparison.OrdinalIgnoreCase)))
            {
                var button = Button(command.Key, command.Key, () => { _overlay.IsVisible = false; command.Value(); }); button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Left; items.Children.Add(button);
            }
        }
        search.TextChanged += (_, _) => Update(); Update();
        ShowDialog("What would you like to do?", Column(search, new ScrollViewer { Content = items, MaxHeight = 420 })); search.Focus();
    }
    private void ShowProfiles()
    {
        var choices = new List<(PreviewProfile Profile, CheckBox Check)>(); var content = new StackPanel { Spacing = 12 };
        foreach (var profile in PreviewProfile.Defaults)
        {
            var check = new CheckBox { Content = $"{profile.Name}  ·  {profile.Width} × {profile.Height}", IsChecked = _surface.Profiles.Any(p => p.Name == profile.Name) };
            choices.Add((profile, check)); content.Children.Add(check);
        }
        content.Children.Add(Button("Apply preview profiles", "Update all artboards", () =>
        {
            var profiles = choices.Where(p => p.Check.IsChecked == true).Select(p => p.Profile).ToArray();
            if (profiles.Length == 0) return; _surface.Profiles = profiles; _surface.Rebuild(); _surface.Fit(); _overlay.IsVisible = false;
        }, true));
        ShowDialog("Preview your interface in context", content);
    }
    private void ShowDialog(string title, Control content)
    {
        _surface.CancelGesture();
        _overlay.Children.Clear();
        var heading = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto") };
        Place(heading, Text(title, 17, "#E2DCF5"), 0, 0); Place(heading, Button("×", "Close dialog", () => _overlay.IsVisible = false), 0, 1);
        var body = Column(heading, content); body.Spacing = 20;
        var border = new Border { Child = body, Background = Brush.Parse("#22252F"), BorderBrush = Brush.Parse("#454051"), BorderThickness = new(1), CornerRadius = new(14), Padding = new(24), Width = 620, MaxHeight = 760, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Focusable = true };
        KeyboardNavigation.SetTabNavigation(border, KeyboardNavigationMode.Cycle);
        _overlay.Children.Add(border); _overlay.IsVisible = true;
        if (!content.Focus()) border.Focus();
    }
}
