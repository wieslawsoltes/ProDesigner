# Components, prototypes, responsive layout and project refactoring

This guide describes the implemented v0.3 workflows. It is not a statement of complete Figma or Visual Studio parity.

## Linked components and variants

Select a visual subtree and open **Design → Components** or the **Components and variants** command. Enter a definition name and capture it. A definition contains the original XAML, a stable component ID, a monotonically increasing revision and named variants. Capturing includes the subtree's inherited namespace context; the source is not serialized through a lossy XML DOM.

Insert an instance into a supported Canvas, Grid, StackPanel, WrapPanel or DockPanel. The generated subtree uses unique control names, remaps supported internal name references and materializes as ordinary Avalonia XAML. The document root owns imported namespace declarations. The workspace stores the exact inserted subtree as the instance's conflict baseline plus its namespace context.

Use **Edit component** to revise the template source. Variants contain property overrides in this format:

```text
$root.Background=#8570D8
$root.Opacity=0.85
Title.Text=Featured
```

`$root` addresses the component root. Other targets must uniquely name an element in the root namescope. Attached properties are supported, for example `$root.Canvas.Left=32`. Namespace directives and identity properties cannot be overridden.

Instance overrides take precedence over variant overrides, which take precedence over the definition. Definition changes propagate to linked instances across all open views. Every instance is checked before any source changes: a missing, ambiguous or manually edited instance aborts the group. **Detach** preserves the XAML and removes the link. Direct source edits are not automatically interpreted as overrides; this prevents undocumented source loss.

**Undo grouped workspace edit** and **Redo grouped workspace edit** preflight each affected source and metadata state before consuming the corresponding document history entries. Later independent source edits must be undone first. Ordinary source undo has a different scope. Grouped transaction history is session-local; definitions, links, variants, overrides and bounded source history persist in workspace files.

Restrictions: instances belong to a view's root namescope, not a nested template namescope. Definitions are expanded XAML, not generated UserControl classes. Nested linked-component graphs, arbitrary structural overrides, variant-axis matrices and automatic hand-edit reconciliation remain unimplemented.

### Library example

```csharp
using ProDesigner.Core;
using ProDesigner.DesignSystems;
using ProDesigner.Xaml;

var tree = XamlSyntaxTree.Parse(source);
var selected = tree.Elements.First(n => XamlNames.Name(n) == "Card");
var definition = ComponentEngine.Capture("Card", tree, selected);
var instance = ComponentEngine.CreateInstance(definition, "view-id", "CardInstance",
    overrides: [new ComponentOverride("$root", "Width", "280")]);

// Standalone materialization: namespace-complete ordinary XAML.
string fragment = instance.Baseline;

// A workbench host inserts with FragmentImporter and records the exact resulting baseline.
// Updates must include both the fragment edit and any newly required root namespace edits.
ComponentUpdate update = ComponentEngine.PrepareUpdate(definition, linkedInstance, documentTree);
var updatedSource = EditApplication.Apply(documentTree.Source,
    (update.NamespaceEdits ?? []).Concat([update.Edit]));
```

The last example assumes `linkedInstance` is the host's stored instance, not a newly created uninserted record. Never substitute a fresh baseline to bypass conflict detection.

## Responsive constraints

Open **Constraints** for a selected control in a measured single-cell Grid. Start and End preserve edge distances, Center preserves the center offset, and Stretch preserves both edge distances. The operation emits standard `HorizontalAlignment`, `VerticalAlignment`, `Margin`, size and minimum-size properties. No runtime dependency on ProDesigner is introduced.

**Convert selected Canvas to Grid** measures its children, captures their size/position and rewrites only layout-related source. Unknown measurements, unsupported property-element containers or existing transforms are refused rather than guessed. This is not a general constraint solver and does not automatically transform a complex multi-row Grid or reproduce all Figma auto-layout behavior.

```csharp
var constraint = new LayoutConstraint(
    ReferenceWidth: 960,
    ReferenceHeight: 620,
    Bounds: new LayoutBox(24, 32, 280, 160),
    Horizontal: AxisAnchor.Stretch,
    Vertical: AxisAnchor.Start);

LayoutBox resized = ResponsiveLayout.Resolve(constraint, width: 1280, height: 720);
```

## Executable prototypes

Open **Prototype connections and flow map**. Choose source view/control, trigger, action and destination. Click and pointer-entry triggers require a unique `Name` or `x:Name` in that view's root namescope. Keyboard and delayed triggers belong to the visible view. SetVariable updates prototype-only string variables; optional equality conditions select applicable transitions. Conditional links take precedence over an unconditional fallback for the same event.

Navigate pushes a bounded back stack. Back returns from the top overlay first, then navigation history. Overlays route input to the top view. Delay evaluation uses a monotonic clock and executes at most one transition per tick, avoiding immediate navigation loops. The validator reports missing endpoints, ambiguous dispatch and unreachable views.

**Run prototype** opens the safe real-control player. Button activation uses Avalonia's Click routed event, including keyboard activation; ordinary controls use left-button release. Prototype state does not change document source. Prototype variables are merged with sample data for safe binding previews. The player does not run project code, network operations or arbitrary scripts.

The flow map visualizes connections; it is not yet a drag-wired node editor. Transition animation, scrolling actions, richer data expressions, shared prototype links and remote collaboration remain outside this version. All prototype artboards currently use a fixed 960×620 reference profile in the runner; designer device artboards remain separate.

```csharp
var graph = new PrototypeGraph("sign-in", [
    new PrototypeLink("submit", "sign-in", "SignInButton",
        PrototypeTrigger.Click, PrototypeAction.Navigate, "settings")
], []);

var controls = new Dictionary<string, IReadOnlyCollection<string>>
{
    ["sign-in"] = ["SignInButton"],
    ["settings"] = []
};
var player = new PrototypePlayer(graph, controls);
player.Dispatch(PrototypeTrigger.Click, "SignInButton");
```

## Project-aware cross-file rename

This workflow is desktop-only. Open a trusted project/solution and open the relevant view from its solution explorer. Choose **Project-aware XAML / C# rename**. Enter the declaring type's metadata name, optionally a uniquely declared member, and the new identifier.

Preparation reads the project's current source files and overlays open unsaved buffers. Roslyn produces the C# rename, preserving comments and string literals. The XAML adapter binds type names to project/reference assembly symbols before editing element tags, property element owners, `x:Class`, `x:DataType`, supported explicit type extensions, directly bound properties and code-behind event names. Same-spelled properties on unrelated control types and plain text content are not renamed accidentally.

Preview each affected file in the before/after editor. Compilation errors introduced by rename block Apply. Unresolved data-context Binding paths and custom extensions produce review warnings instead of speculative string replacement. Workspace-only component definition references are not project source files and must be reviewed separately.

Apply requires the exact prepared plan and rechecks open buffers and current disk contents. A durable journal stores the original and replacement bytes. Supported UTF-8/16/32 BOMs, line endings and Unix file modes are retained. Each changed file is staged and flushed before atomic replacement. Partial failures attempt conflict-aware rollback. Existing symlink paths are refused.

**Undo last project rename** checks every file again. An external edit after refactoring prevents rollback before any file is changed. Original disk content and original unsaved editor buffers are separate snapshots; undo restores them separately. The UI retains a bounded session-local stack of rename receipts. Project rename redo and an automatic startup recovery UI for these receipts are not provided yet.

The reusable journal API can reopen a receipt after process interruption. Reverting requires an explicit allowed-path set supplied by the host, plus checksum verification. A journal is a recovery mechanism, not proof of atomic multi-path visibility: another process may observe intermediate replacements or race the final check. Do not advertise the operation as a filesystem-wide atomic transaction.

```csharp
var store = new JournaledFileTransaction(journalDirectory);
FileTransactionReceipt receipt = await store.ApplyAsync(
    plan.Files.Select(f => new FileTextChange(f.Path, f.DiskBefore, f.After)).ToArray(),
    cancellationToken);

await store.RevertAsync(receipt, plan.Files.Select(f => f.Path).ToArray(), cancellationToken);
```

Type/member restrictions: no nested/generic type renames, overloaded-method set rename, arbitrary XAML selector/binding rewrite, file-path/project-file rename or comprehensive third-party markup-extension semantics. These restrictions are explicit safeguards, not silent no-op implementations.

## Schema-2 workspace compatibility

Workspace document IDs are stable across save/recovery and connect component instances and prototype endpoints to the correct view. They are distinct from structural syntax-node IDs. Source history now stores the exact current text and the last valid syntax source needed to recover an invalid intermediate edit.

New exports use schema 2; schema-1 files remain readable and receive fresh document IDs when restored. The codec validates size limits, component links, history entries and checksums. The UI stores up to 20 undo and 20 redo entries per source document in each workspace snapshot. Grouped component operations, project-rename UI history and full MSBuild evaluation state are not restored. Browser local-storage quotas may be smaller than the codec's workspace size budget; export files are the portable backup path.
