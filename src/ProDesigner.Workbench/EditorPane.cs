using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Highlighting;
using ProDesigner.Design;

namespace ProDesigner.Workbench;

public sealed class EditorPane : UserControl
{
    private readonly TextEditor _editor;
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private readonly StackPanel _findRow;
    private readonly TextBox _query = StudioControls.Field(watermark: "Find in document");
    private readonly TextBox _replacement = StudioControls.Field(watermark: "Replace with");
    private DesignerSession? _session;
    private bool _synchronizing;
    public event Action<string>? Status;
    public TextEditor Editor => _editor;
    public EditorPane()
    {
        _editor = new TextEditor
        {
            Name = "XamlEditor", ShowLineNumbers = true, FontSize = 12,
            FontFamily = new FontFamily("Cascadia Code, Consolas, Menlo, monospace"),
            Background = Brush.Parse("#14161D"), Foreground = Brush.Parse("#CED3E2"),
            SyntaxHighlighting = StudioHighlighting.Xaml,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        _editor.Options.ConvertTabsToSpaces = true; _editor.Options.IndentationSize = 2;
        _query.Width = 210; _replacement.Width = 180;
        _findRow = StudioControls.Row(_query, _replacement, StudioControls.Button("Next", "Find next", FindNext), StudioControls.Button("Replace", "Replace current match", Replace), StudioControls.Button("×", "Close find", () => _findRow!.IsVisible = false));
        _findRow.Margin = new(8); _findRow.IsVisible = false;
        var grid = new Grid { RowDefinitions = RowDefinitions.Parse("Auto,*") };
        StudioControls.Place(grid, _findRow, 0, 0); StudioControls.Place(grid, _editor, 1, 0); Content = grid;
        _editor.TextChanged += (_, _) => { if (_synchronizing) return; _debounce.Stop(); _debounce.Start(); };
        _debounce.Tick += (_, _) => Flush();
        _editor.TextArea.Caret.PositionChanged += (_, _) =>
        {
            if (_synchronizing || _session is null || _editor.Text != _session.Source || !_editor.IsKeyboardFocusWithin) return;
            var node = _session.Tree.At(_editor.CaretOffset); if (node is not null) _session.Select(node.Id);
        };
        _editor.KeyDown += (_, e) =>
        {
            if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) == 0) return;
            if (e.Key == Key.F) { ShowFind(); e.Handled = true; }
            else if (e.Key == Key.Space) { ShowCompletion(); e.Handled = true; }
        };
        _query.KeyDown += (_, e) => { if (e.Key == Key.Enter) { FindNext(); e.Handled = true; } };
    }
    public void Attach(DesignerSession session)
    {
        Flush();
        if (_session is not null) _session.Changed -= SessionChanged;
        _session = session; _session.Changed += SessionChanged; Synchronize(session.Source);
    }
    public void Flush()
    {
        _debounce.Stop(); if (!_synchronizing && _session is not null) _session.SetSource(_editor.Text ?? "", mergeTyping: true);
    }
    private void SessionChanged(ProDesigner.Core.DocumentChange change) => Synchronize(change.Source);
    private void Synchronize(string source)
    {
        if (_editor.Text == source) return;
        _synchronizing = true;
        try
        {
            var old = _editor.Text ?? ""; var start = 0;
            while (start < old.Length && start < source.Length && old[start] == source[start]) start++;
            var oldEnd = old.Length; var newEnd = source.Length;
            while (oldEnd > start && newEnd > start && old[oldEnd - 1] == source[newEnd - 1]) { oldEnd--; newEnd--; }
            var caret = _editor.CaretOffset;
            _editor.Document.Replace(start, oldEnd - start, source[start..newEnd]);
            _editor.CaretOffset = Math.Clamp(caret <= start ? caret : caret >= oldEnd ? caret + newEnd - oldEnd : newEnd, 0, source.Length);
        }
        finally { _synchronizing = false; }
    }
    public void Navigate(int offset, int length = 0)
    {
        offset = Math.Clamp(offset, 0, _editor.Document.TextLength);
        _editor.Select(offset, Math.Clamp(length, 0, _editor.Document.TextLength - offset));
        _editor.ScrollToLine(_editor.Document.GetLineByOffset(offset).LineNumber);
    }
    public void ShowFind() { _findRow.IsVisible = true; _query.Focus(); }
    public void FindNext()
    {
        var query = _query.Text ?? ""; if (query.Length == 0) return;
        var source = _editor.Text ?? "";
        var start = Math.Clamp(_editor.SelectionStart + Math.Max(1, _editor.SelectionLength), 0, source.Length);
        var index = source.IndexOf(query, start, StringComparison.OrdinalIgnoreCase);
        if (index < 0) index = source.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (index >= 0) Navigate(index, query.Length); else Status?.Invoke("No matches.");
    }
    public void Replace()
    {
        if (_editor.SelectedText.Equals(_query.Text, StringComparison.OrdinalIgnoreCase))
            _editor.Document.Replace(_editor.SelectionStart, _editor.SelectionLength, _replacement.Text ?? "");
        Flush(); FindNext();
    }
    public void ShowCompletion()
    {
        var completion = new CompletionWindow(_editor.TextArea);
        var items = ControlCatalog.Items.Select(i => i.Name).Concat(ControlCatalog.CommonProperties).Concat(["{Binding }", "{StaticResource }", "{DynamicResource }", "UserControl", "Styles", "ControlTheme", "DataTemplate"]);
        foreach (var item in items.Distinct().OrderBy(s => s)) completion.CompletionList.CompletionData.Add(new Completion(item));
        completion.Show();
    }
    private sealed class Completion(string text) : ICompletionData
    {
        public IImage? Image => null;
        public string Text => text;
        public object Content => text;
        public object Description => "Avalonia XAML: " + text;
        public double Priority => 0;
        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs) => textArea.Document.Replace(completionSegment, text);
    }
}
