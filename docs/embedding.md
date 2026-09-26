# Embedding and extending

## Standalone editing

```csharp
var session = new ProDesigner.Design.DesignerSession(source);
var element = session.Tree.Elements.First(e => e.Get("x:Name") == "Card");
var edit = ProDesigner.Xaml.XamlEdits.SetAttribute(session.Tree, element, "Opacity", "0.8");
session.Apply("Dim card", new[] { edit }, session.Version);
```

`TextEdit.ExpectedText` protects prepared edits against stale source spans. Keep syntax-tree nodes scoped to one session version. Call source-changing operations on your UI/session owner thread; DesignerSession is not a concurrent mutable store.

## Real-control previews

```csharp
var tree = ProDesigner.Xaml.XamlSyntaxTree.Parse(source);
var result = new ProDesigner.Preview.PreviewBuilder().Build(
    tree, new ProDesigner.Core.PreviewProfile("Phone", 390, 844));
previewContainer.Content = result.Root;
foreach (var diagnostic in result.Diagnostics) Log(diagnostic.Message);
```

Call preview construction on Avalonia's UI thread. `result.Controls` maps syntax node IDs to actual controls. The safe factory is intentionally bounded and emits diagnostics instead of invoking arbitrary user types. Do not silently replace it with a reflection-based arbitrary constructor loader.

## Embed the workbench

Reference ProDesigner.Workbench, register FluentTheme and the workbench/editor styles as shown in DesignerApplication, then create `new DesignerWorkbench(workspaceService, codeService, trustedPreviewFactory)`. All three host services are optional. Omitted services yield explicit desktop-only messages rather than fake implementations.

`EditorPane`, `PropertyInspector`, `TimelineEditor` and `DesignSurface` are independently instantiable controls. Attach a `DesignerSession`; subscribe to their status/preview callbacks. The initial visual theme uses static workbench colors and needs additional theme-resource extraction for comprehensive rebranding.

## XamlX

ProDesigner.XamlX embeds the upstream XamlX source internally at revision `7ef6aef496ab6e8dcf3df04bef697be49db37c04`. This avoids leaking unstable compiler implementation types into the public package API. XamlXSemanticService validates through the real parser after applying the size, DTD and nesting policies of ProDesigner.Xaml. It does not pretend that syntax validation is Avalonia type resolution.

## Roslyn and MSBuild

Initialize `WorkspaceBootstrap.Initialize()` before any MSBuild assembly is loaded, then create the workspace through `WorkspaceBootstrap.Create()`. The service rejects untrusted evaluation. RoslynCodeService accepts a Compilation for member discovery and returns a new Solution for symbol rename; callers must preview/apply/persist those changes themselves. Do not write a returned Solution to disk without the host's normal user-confirmation and conflict policies.

## Distribution

Each src project is independently packable. Do not distribute private signing material or repository tokens. The release workflow builds a source archive, desktop distributions and library packages; it pushes to NuGet only when `NUGET_API_KEY` exists in the publishing environment. Application hosts are not NuGet libraries.
