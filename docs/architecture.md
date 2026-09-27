# Architecture

## Boundaries

```mermaid
flowchart TD
  Desktop[Desktop host] --> Workbench
  Browser[WebAssembly host] --> Workbench
  Workbench --> Preview[ProDesigner.Avalonia]
  Workbench --> XamlX[ProDesigner.XamlX]
  Preview --> Design[ProDesigner.Design]
  Preview --> Animation[ProDesigner.Animation]
  Design --> Xaml[ProDesigner.Xaml]
  Animation --> Xaml
  XamlX --> Xaml
  Xaml --> Core[ProDesigner.Core]
  Desktop --> Runtime[ProDesigner.Runtime]
  Runtime --> Xaml
  Desktop --> Roslyn[ProDesigner.Roslyn]
  Desktop --> Workspaces[ProDesigner.Workspaces]
  Roslyn --> Core
  Workspaces --> Core
```

No portable editing library depends on an application host. The browser excludes MSBuild and Roslyn. Its safe renderer and UI are real Avalonia controls compiled to WebAssembly.

## The two-tree design

XamlX is a compiler frontend, not a lossless document store. Serializing a semantic AST after every drag would destroy comments, trivia and potentially unsupported constructs. ProDesigner therefore keeps source text plus a concrete syntax tree containing element/attribute spans, and invokes the actual XamlX parser as a separate validation pass. The CST is currently reparsed per transaction; this is not yet an incremental green/red syntax tree or a project-bound semantic XAML graph.

A visual operation prepares TextEdit records. EditApplication checks bounds, overlap and optional expected old text before constructing a new source string. DesignerSession validates visual transactions before publication, increments the version, records undo and notifies the UI. Invalid source edits are allowed only through the text-edit path; they preserve the last valid syntax tree and disable visual transactions. Gesture commits carry the version captured on pointer-down, preventing a stale drag from overwriting intervening edits.

Node IDs are document-local structural paths. They are not durable object identities across arbitrary structural reordering. Name-aware identity reconciliation and cross-file symbol maps are future work. Plugins must re-resolve nodes after transactions and must not retain mutable node references across source versions.

## Preview boundary

PreviewBuilder creates only known Avalonia control types. It maps source node IDs to those controls and applies a bounded property set. It supports local scalar resource values and a small sample-data Binding lookup. Unsupported controls, properties, templates, styles and markup extensions generate diagnostics while their original source remains unchanged.

DesignSurface composes PreviewFrames, scrolling and zoom. Each frame overlays selection adorners and handles source-linked gestures. Automatic-layout controls retain their layout semantics. Free positioning, object guides, and alignment/distribution are restricted to Canvas. The preview is rebuilt after debounced source changes; selection overlays update independently.

Desktop trusted runtime preview uses Avalonia.Markup.Xaml.Loader, which executes XAML through Avalonia's XamlX pipeline. It is deliberately explicit and currently in-process. The desktop host now combines a cancellable SDK builder with the standalone runtime library to load project controls and code-behind roots. AssemblyDependencyResolver and a collectible load context resolve project dependencies while sharing the host’s Avalonia assemblies. This is not a sandbox or a guarantee of immediate unloading; global framework caches can retain references. Application-wide resources, incompatible Avalonia major versions, isolated processes and code hot-reload need further work.

## Roslyn and project resolution

RoslynCodeService provides syntax diagnostics, structural handler insertion, compilation member discovery and semantic rename returning a changed Solution. The UI exposes syntax diagnostics, handler generation and asynchronous project-bound XAML diagnostics. XamlCompilationService resolves concrete syntax nodes to Roslyn project/reference symbols and reports unknown types, properties, attached properties and event handlers without executing them. It is not the complete Avalonia XamlX transform pipeline. Cross-file rename application and transactional XAML/C# refactoring remain unintegrated.

WorkspaceBootstrap registers MSBuild before creating the workspace. SolutionWorkspace uses MSBuildWorkspace to load an evaluated solution and referenced projects and exposes compilation diagnostics. Its project inventory also reports raw XML framework/reference declarations; conditional and centrally managed values in that inventory are not advertised as fully evaluated metadata. Evaluation requires affirmative trust because imported targets can execute code. An explicit runtime-preview action can restore/build through DotNetProjectBuilder, using argument-list process invocation, bounded logs, cancellation and a timeout. Package-management UI and framework selection UI remain pending.

## Performance strategy

Editing and preview are debounced (180 ms for source input and 160 ms for preview scheduling). Selection does not reparse source. Drag previews mutate the preview controls and publish one source transaction on release. Compiler/project services are host-injected and kept out of the browser startup path. History is bounded to 200 snapshots. Size/depth limits protect the parser.

Current limitations are explicit: full-document parsing, full preview rebuilds, non-virtualized layer/property UI, snapshot history memory, synchronous XamlX validation and overlay layout work need profiling against large real applications. Parse duration in the status bar is measured; it is not a whole-frame performance claim.

## Test layers

1. Pure editing, geometry, animation and compiler service tests.
2. Avalonia.Headless integration tests over actual controls/workbench.
3. Playwright boot and command tests over the actual WebAssembly application, with screenshots and traces.

The test bridge calls the same workbench/session commands as the UI. It does not emulate the runtime. Full pointer/keyboard matrix testing, golden image comparison, screen-reader audits, memory/performance budgets and large-solution fixtures are still required for production qualification.

## 0.2 extension architecture

`ProDesigner.Authoring` builds targeted CST transactions for resources, selectors, themes and object-valued properties; its geometry model handles explicit Move/Line/Quadratic/Cubic/Close segments. `FragmentImporter` prepares imported fragments using their inherited namespace context and hoists declarations to the root required by Avalonia's runtime compiler. Conflicting aliases inside value tokens are rejected rather than rewritten indiscriminately. `MarkupReferences` tokenizes nested extensions and quoted arguments, restricting name rewrites to supported Binding/Reference syntax.

`ProDesigner.Persistence` serializes a checksummed, versioned workspace envelope using source-generated System.Text.Json metadata, preserving trimming compatibility in WebAssembly. Recovery stores implement a shared host contract. The desktop file store performs same-directory atomic replacement, optimistic content-hash checking and cooperative writer locking. Import constructs and validates all tabs before swapping the workbench document set.

`ProDesigner.PreviewProtocol` owns length-prefixed named-pipe framing and process supervision. The desktop executable starts a separate minimal Avalonia application in worker mode before initializing the normal workbench/MSBuild services. Workers receive already-authorized requests, load trusted project assemblies and send revision-matched success/error replies. A failed parse retains the old preview; a hung/crashed process is terminated. Live changes to the associated document are serialized and coalesced, while C# recompilation/relaunch still requires a new preview action. This is crash containment, not an OS sandbox.

The layer view uses a virtualized ListBox over flattened expand/collapse rows. Source-node lookup uses a dictionary, and offset lookup uses binary search followed by ancestor traversal. Visual transactions map selection through their text edits. Full immutable/incremental syntax identity and incremental preview diffing remain future work.
