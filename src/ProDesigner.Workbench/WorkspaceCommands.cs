using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using ProDesigner.Core;
using ProDesigner.Persistence;
using ProDesigner.Design;
using ProDesigner.Xaml;
using static ProDesigner.Workbench.StudioControls;

namespace ProDesigner.Workbench;

public sealed partial class DesignerWorkbench
{
    public async Task OpenDocumentsAsync()
    {
        try
        {
            var provider = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (provider is null) return;
            var files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open Avalonia views", AllowMultiple = true,
                FileTypeFilter = [new FilePickerFileType("Avalonia XAML") { Patterns = ["*.axaml", "*.xaml"] }]
            });
            foreach (var file in files)
            {
                await using var stream = await file.OpenReadAsync();
                if (stream.CanSeek && stream.Length > XamlSyntaxTree.MaximumLength * 4L) throw new IOException("The file is too large.");
                using var reader = new StreamReader(stream); var text = await reader.ReadToEndAsync();
                OpenDocument(file.Name, text, file.TryGetLocalPath());
            }
        }
        catch (Exception ex) { SetStatus("Open failed: " + ex.Message); }
    }
    public void OpenDocument(string name, string text, string? path = null)
    {
        var existing = path is null ? null : _documents.FirstOrDefault(d => d.Path == path);
        if (existing is not null) { SwitchDocument(existing); return; }
        var session = new DesignerSession("<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" />");
        session.SetSource(text, "Open document"); session.MarkSaved();
        var document = new DocumentTab(name, session) { Path = path, DiskHash = path is null ? null : WorkspaceCodec.Hash(text) }; _documents.Add(document); SwitchDocument(document);
    }
    public async Task SaveDocumentAsync()
    {
        try
        {
            _editor.Flush(); var document = _active;
            var provider = TopLevel.GetTopLevel(this)?.StorageProvider; if (provider is null) return;
            var file = await provider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Save Avalonia view", SuggestedFileName = document.Name, DefaultExtension = "axaml", FileTypeChoices = [new FilePickerFileType("Avalonia XAML") { Patterns = ["*.axaml"] }] });
            if (file is null) return;
            var source = document.Session.Source;
            var local = file.TryGetLocalPath();
            if (local is not null)
                document.DiskHash = await AtomicFileStore.WriteAsync(local, source, string.Equals(local, document.Path, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ? document.DiskHash : null);
            else
            {
                await using var stream = await file.OpenWriteAsync(); if (stream.CanSeek) stream.SetLength(0);
                await using var writer = new StreamWriter(stream, new UTF8Encoding(false)); await writer.WriteAsync(source); await writer.FlushAsync();
            }
            document.Name = file.Name; document.Path = file.TryGetLocalPath();
            if (source == document.Session.Source) document.Session.MarkSaved();
            RefreshTabs(); SetStatus("Saved " + file.Name);
        }
        catch (Exception ex) { SetStatus("Save failed: " + ex.Message); }
    }
    private async Task OpenWorkspaceAsync()
    {
        if (_workspace is null) { SetStatus("Solution loading and dependency resolution require the desktop host. The browser opens individual XAML files without executing project code."); return; }
        try
        {
            var provider = TopLevel.GetTopLevel(this)?.StorageProvider; if (provider is null) return;
            var files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Open a solution or project", FileTypeFilter = [new FilePickerFileType(".NET workspace") { Patterns = ["*.sln", "*.slnx", "*.csproj"] }] });
            var path = files.FirstOrDefault()?.TryGetLocalPath(); if (path is null) return;
            var text = new TextBlock { Text = "MSBuild project evaluation can execute code from this solution and its imported targets. Load only workspaces you trust. Opening XAML alone does not require this permission.\n\n" + path, TextWrapping = TextWrapping.Wrap, Foreground = Brush.Parse("#B5BCCD") };
            ShowDialog("Trust this workspace?", Column(text, Button("Trust and load workspace", "Allow project evaluation for this workspace", () => { _overlay.IsVisible = false; _ = LoadWorkspaceAsync(path); }, true)));
        }
        catch (Exception ex) { SetStatus("Workspace selection failed: " + ex.Message); }
    }
    private async Task LoadWorkspaceAsync(string path)
    {
        try
        {
            SetStatus("Loading project graph and resolving metadata…");
            var projects = await Task.Run(() => _workspace!.OpenAsync(path, true));
            SetSolutionFiles(projects);
            var content = new StackPanel { Spacing = 12 };
            foreach (var project in projects)
            {
                _frameworks[project.Path] = project.Frameworks.ToArray();
                content.Children.Add(Text(project.Name + "  ·  " + string.Join(", ", project.Frameworks), 14));
                content.Children.Add(new TextBlock { Text = $"{project.ProjectReferences.Count} project references · {project.Packages.Count} NuGet references", FontSize = 11, Foreground = Brush.Parse("#929DB5") });
                foreach (var file in project.Files.Where(f => f.Kind is ".axaml" or ".xaml"))
                {
                    var button = Button("▧  " + file.Name, file.Path, () => { _overlay.IsVisible = false; _ = OpenWorkspaceFileAsync(file, project.Path); });
                    button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Left; content.Children.Add(button);
                }
            }
            ShowDialog("Solution explorer", new ScrollViewer { Content = content, MaxHeight = 560 });
            SetStatus($"Loaded {projects.Count} projects. Dependency and build diagnostics are available in Problems.");
            var diagnostics = await Task.Run(() => _workspace!.GetDiagnosticsAsync());
            foreach (var diagnostic in diagnostics.Take(200)) _problems.Children.Add(Text(diagnostic.Code + "  " + diagnostic.Message, 11, "#C6A678"));
        }
        catch (Exception ex) { SetStatus("Workspace load failed: " + ex.Message); }
    }
    private async Task OpenWorkspaceFileAsync(WorkspaceFile file, string projectPath)
    {
        try
        {
            var text = await File.ReadAllTextAsync(file.Path); OpenDocument(Path.GetFileName(file.Path), text, file.Path); _active.ProjectPath = projectPath;
            var codePath = file.Path + ".cs"; if (File.Exists(codePath)) _active.CodeBehind = await File.ReadAllTextAsync(codePath);
        }
        catch (Exception ex) { SetStatus("Open failed: " + ex.Message); }
    }
    private async Task RefreshProjectDiagnosticsAsync()
    {
        _projectAnalysis?.Cancel(); _projectAnalysis?.Dispose(); _projectAnalysis = null;
        if (_workspace is null || _active.ProjectPath is null || !Session.IsValid) { _editor.ProjectCompletions = []; _inspector.ProjectProperties = []; return; }
        var document = _active; var version = document.Session.Version; var source = document.Session.Source; var offset = Session.Primary?.NameSpan.Start ?? 0;
        var cancellation = new CancellationTokenSource(); _projectAnalysis = cancellation;
        try
        {
            var diagnostics = await Task.Run(() => _workspace.AnalyzeXamlAsync(document.ProjectPath, source, cancellation.Token), cancellation.Token);
            if (_disposed || cancellation.IsCancellationRequested || document != _active || document.Session.Version != version) return;
            var completion = await _workspace.GetXamlCompletionsAsync(document.ProjectPath, source, offset, cancellation.Token);
            if (_disposed || cancellation.IsCancellationRequested || document != _active || document.Session.Version != version) return;
            _editor.ProjectCompletions = completion; _inspector.ProjectProperties = completion; if (!_inspector.IsKeyboardFocusWithin) _inspector.Rebuild();
            foreach (var diagnostic in diagnostics.Take(100))
            {
                var button = Button($"{diagnostic.Code}   {diagnostic.Message}", diagnostic.Message, () => { _bottom.SelectedIndex = 0; _editor.Navigate(diagnostic.Offset, diagnostic.Length); });
                button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Left; _problems.Children.Add(button);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception ex) { if (!_disposed) SetStatus("Project XAML analysis: " + ex.Message); }
    }
    private void GenerateEventHandler(string name) => Guard(() =>
    {
        if (_codeService is null) { SetStatus("Roslyn code generation is available in the desktop host. XAML event attributes can still be edited in the browser."); return; }
        if (Session.Primary is not { } node) return;
        var type = Session.Tree.Root.Get("x:Class") ?? "ProDesigner.Generated." + Path.GetFileNameWithoutExtension(_active.Name);
        var lastDot = type.LastIndexOf('.'); var className = lastDot < 0 ? type : type[(lastDot + 1)..]; var ns = lastDot < 0 ? "ProDesigner.Generated" : type[..lastDot];
        var code = string.IsNullOrWhiteSpace(_active.CodeBehind) ? $"namespace {ns};\n\npublic partial class {className} : global::Avalonia.Controls.UserControl\n{{\n}}\n" : _active.CodeBehind;
        var updated = _codeService.EnsureEventHandler(code, className, name);
        var edits = new List<TextEdit> { XamlEdits.SetAttribute(Session.Tree, node, "Click", name) };
        if (Session.Tree.Root.Get("x:Class") is null) edits.Add(XamlEdits.SetAttribute(Session.Tree, Session.Tree.Root, "x:Class", type));
        Session.Apply("Connect event " + name, edits); _active.CodeBehind = updated; ShowCodeBehind();
    });
    private void ShowCodeBehind()
    {
        var document = _active;
        var editor = new TextEditor { Text = document.CodeBehind, ShowLineNumbers = true, FontSize = 12, FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"), Height = 390, SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("C#") };
        var diagnostic = Text("C# code-behind", 11, "#98A4BB");
        editor.TextChanged += (_, _) =>
        {
            document.CodeBehind = editor.Text; ScheduleRecovery();
            var issues = _codeService?.Validate(editor.Text); diagnostic.Text = issues is null ? "Roslyn diagnostics require the desktop host." : issues.Count == 0 ? "✓ Roslyn: no syntax diagnostics" : string.Join("\n", issues.Take(3).Select(d => d.Message));
        };
        ShowDialog(document.Name + ".cs", Column(editor, diagnostic, Button("Save code-behind", "Save the companion C# source", () => _ = SaveCodeBehindAsync(document), true)));
    }
    private async Task SaveCodeBehindAsync(DocumentTab document)
    {
        try
        {
            var provider = TopLevel.GetTopLevel(this)?.StorageProvider; if (provider is null) return;
            var file = await provider.SaveFilePickerAsync(new FilePickerSaveOptions { SuggestedFileName = document.Name + ".cs", DefaultExtension = "cs" });
            if (file is null) return;
            await using var stream = await file.OpenWriteAsync(); if (stream.CanSeek) stream.SetLength(0);
            await using var writer = new StreamWriter(stream); await writer.WriteAsync(document.CodeBehind); SetStatus("Saved " + file.Name);
        }
        catch (Exception ex) { SetStatus("Code save failed: " + ex.Message); }
    }
    private void ShowTrustedPreview()
    {
        if (_trustedPreview is null && _externalFactory is null) { SetStatus("Runtime XAML execution is desktop-only. The browser uses the non-executing built-in preview."); return; }
        var framework = new ComboBox { ItemsSource = _active.ProjectPath is not null && _frameworks.TryGetValue(_active.ProjectPath, out var targets) ? targets : Array.Empty<string>(), SelectedIndex = 0 };
        framework.SelectionChanged += (_, _) => _chosenFramework = framework.SelectedItem?.ToString();
        _chosenFramework = framework.SelectedItem?.ToString();
        ShowDialog("Build and execute trusted XAML?", Column(framework, new TextBlock
        {
            Text = "Runtime preview can restore/build the selected project and execute its constructors, code-behind, converters, and markup extensions. It runs in a supervised separate process with the same OS permissions; it is not an OS security sandbox. Continue only for a project and dependencies you trust.", TextWrapping = TextWrapping.Wrap
        }, Button("Run trusted runtime preview", "Build this project and execute its XAML", () => { _overlay.IsVisible = false; _ = OpenTrustedPreviewAsync(); }, true)));
    }
    private async Task OpenTrustedPreviewAsync()
    {
        try
        {
            _editor.Flush(); var document = _active;
            if (_externalFactory is not null)
            {
                await StopExternalPreviewAsync(); _previewCancellation?.Dispose(); _previewCancellation = new CancellationTokenSource();
                SetStatus("Building and starting isolated preview. Use Stop isolated preview to cancel.");
                var token = _previewCancellation.Token;
                var external = await _externalFactory(new(document.Session.Source, document.Path, document.ProjectPath, true, _chosenFramework), token);
                if (token.IsCancellationRequested) { await external.DisposeAsync(); return; }
                _externalPreview = external; _previewDocument = document; _previewRevision = document.Session.Version;
                SetStatus("Isolated project preview connected · valid XAML edits update its window live."); return;
            }
            SetStatus(document.ProjectPath is null ? "Loading trusted XAML…" : "Restoring and building the trusted project…");
            var preview = await _trustedPreview!(new(document.Session.Source, document.Path, document.ProjectPath, Trusted: true));
            var window = new Window { Title = "ProDesigner · trusted project preview", Width = 1000, Height = 720, Content = preview.Root };
            window.Closed += (_, _) => preview.Dispose(); window.Show();
            SetStatus("Trusted runtime preview opened. Project execution is not sandboxed.");
        }
        catch (Exception ex) { SetStatus("Runtime preview failed: " + ex.GetBaseException().Message); }
    }
}
