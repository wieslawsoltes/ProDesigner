<div align="center">

# ◈ ProDesigner
### Your next great interface starts here.

**Composable, source-preserving visual authoring for Avalonia UI.**

[![Build](https://github.com/wieslawsoltes/ProDesigner/actions/workflows/ci.yml/badge.svg)](https://github.com/wieslawsoltes/ProDesigner/actions/workflows/ci.yml)
![.NET](https://img.shields.io/badge/.NET-10-7C6BC4)
![Avalonia](https://img.shields.io/badge/Avalonia-12.1.3-9D8BE0)
![License](https://img.shields.io/badge/license-MIT-77AE98)

[Browser designer](https://wieslawsoltes.github.io/ProDesigner/) · [Downloads](https://github.com/wieslawsoltes/ProDesigner/releases) · [Getting started](docs/getting-started.md) · [Architecture](docs/architecture.md) · [Capability ledger](docs/capabilities.md)

</div>

---

ProDesigner brings visual editing, real Avalonia rendering and compiler-aware source authoring into one workbench. Arrange controls on device artboards, build linked components, prototype navigation, inspect properties, edit XAML without losing its comments, and refactor project symbols across C# and XAML. Desktop and browser share reusable C# libraries—not an HTML imitation of Avalonia.

> **0.3.0-alpha.1.** This is a functional alpha, not complete WPF Designer, Blend, Figma or Xcode parity. The [capability ledger](docs/capabilities.md) distinguishes usable workflows, restricted semantics and outstanding work. Browser previews use an explicit built-in-control catalog; project evaluation, semantic C# refactoring and arbitrary runtime XAML execution are desktop-only.

## Design, source and project context

| Area | Implemented workflows |
| --- | --- |
| Visual authoring | Device/theme artboards, Canvas dragging, eight resize handles, multi-selection movement, aspect constraints, snapping/guides, alignment, distribution and gesture cancellation. |
| Source preservation | Source-spanned XAML tree, targeted property/structural transactions, bounded undo/redo, stale-edit checks, namespace-aware fragment import and supported namescope/reference rewrites. Invalid typing retains the last valid preview. |
| Linked components | Capture a subtree, create named variants, insert linked instances, apply explicit instance overrides and propagate changes across views. Source conflicts abort an update before any view is changed; grouped workspace undo/redo keeps source and component metadata together. |
| Responsive constraints | Start/center/end/stretch anchors generate ordinary Avalonia Grid properties. Measured Canvas children can be converted to a single-cell Grid without replacing their content. No designer runtime is needed by exported XAML. |
| Prototyping | Validated view connections, a flow map, navigation/back, overlays, variables/conditions, click/pointer/key/delay triggers and an interactive real-control player. Prototype actions do not rewrite design source. |
| Compiler services | Real pinned XamlX parser; Roslyn project type/property/event diagnostics, C# diagnostics and handler generation; loaded-project member completion and inspection. |
| Project refactoring | Preview Roslyn symbol changes alongside targeted XAML changes. Apply reviewed multi-file renames using file/buffer conflict checks and a durable before/after journal; undo restores prior disk bytes and unsaved buffers separately. |
| Project preview | Trusted SDK restore/build and dependency resolution; supervised child process for compiled controls/code-behind, revision-matched IPC, live XAML updates and cancellation/timeout termination. |
| Paint, themes and geometry | Resource/style/ControlTheme authoring, source-based template composition, RGBA controls, linear gradients, sample data and point/handle editing of supported M/L/C/Q/Z paths. |
| Animation | Named scalar/color tracks, playback, scrub, looping, timeline undo/redo, custom splines, representable XAML import and tested continuous-easing export. |
| Workspace recovery | Checksummed schema-2 workspaces with stable view identities, linked components, prototype graphs, source undo/redo and last-valid syntax snapshots. Schema-1 files remain readable. Desktop/browser recovery uses two generations. |

Use **Design** for resources, styles, templates, paint and data. Its header opens **Components**, **Prototype** and **Constraints**. Use **Ctrl/Cmd+K** for these tools, project rename, grouped undo/redo, workspace files, recovery and isolated previews. See the [components, prototypes and refactoring guide](docs/components-prototypes-refactoring.md) for complete workflows and restrictions.

## Run the desktop application

Install the .NET 10 SDK:

```sh
git clone --recurse-submodules https://github.com/wieslawsoltes/ProDesigner.git
cd ProDesigner
dotnet restore ProDesigner.slnx
dotnet run --project apps/ProDesigner.Desktop
```

An existing clone needs `git submodule update --init --recursive`. The desktop runs on Windows, macOS and Linux. The SDK remains necessary to evaluate/build a trusted solution, even with a self-contained application distribution.

## Browser development

```sh
dotnet workload install wasm-tools
dotnet run --project apps/ProDesigner.Browser
```

For a static bundle, publish `apps/ProDesigner.Browser` in Release and serve its generated `wwwroot` over HTTP/HTTPS. Threading is disabled, so Pages needs no cross-origin-isolation headers. Browser recovery stays in origin-local storage; quota failures are reported. Export `.prodesigner` files for portable backups. A feature branch is not a public deployment: the protected Pages workflow publishes only merged `main` builds.

## Fifteen reusable packages

| Package | Responsibility |
| --- | --- |
| `ProDesigner.Core` | Diagnostics, spans, edits, preview profiles, history/transaction and host-service contracts. |
| `ProDesigner.Xaml` | Lossless source model, namespace/namescope helpers, supported reference rewriting and fragment import. |
| `ProDesigner.Design` | Sessions, persistent source history, selection, resize/layout geometry and toolbox. |
| `ProDesigner.DesignSystems` | Linked definitions/variants/overrides, materialization, conflict detection and responsive anchor conversion. |
| `ProDesigner.Prototyping` | Validated navigation graphs and deterministic host-independent prototype execution. |
| `ProDesigner.Animation` | Scalar/color tracks, interpolation, splines and XAML import/export. |
| `ProDesigner.Authoring` | Resources, styles, themes, gradient models and editable path geometry. |
| `ProDesigner.Persistence` | Workspace codec, snapshots, recovery, conflict-aware saves and journaled multi-file replacement. |
| `ProDesigner.PreviewProtocol` | Bounded named-pipe transport and supervised preview processes. |
| `ProDesigner.XamlX` | Actual pinned upstream XamlX parser integration. |
| `ProDesigner.Roslyn` | Project-bound XAML analysis, C# services and cross-language rename planning. |
| `ProDesigner.Workspaces` | Trusted MSBuild loading, dependency/SDK services and project refactoring snapshots. |
| `ProDesigner.Runtime` | Trusted Avalonia runtime XAML and compiled project controls. |
| `ProDesigner.Avalonia` | Safe real-control preview, artboards and source-linked design surface. |
| `ProDesigner.Workbench` | Embeddable editor, inspector, timeline, vector/component/prototype tools and project UI. |

Portable editing libraries do not depend on application hosts. See [embedding](docs/embedding.md) and the [v0.3 integration guide](docs/components-prototypes-refactoring.md).

```csharp
using ProDesigner.Design;
using ProDesigner.DesignSystems;

var session = new DesignerSession(xaml);
var card = session.Tree.Elements.First(n => n.DisplayName == "RevenueCard");
session.Select(card.Id);
session.SetProperty("Width", "240");

var component = ComponentEngine.Capture("Revenue card", session.Tree, session.Primary!);
var instance = ComponentEngine.CreateInstance(component, "dashboard-view", "Revenue1",
    overrides: [new ComponentOverride("$root", "Opacity", "0.85")]);
// instance.Baseline contains ordinary, namespace-complete Avalonia XAML.
// Workbench insertion hoists namespaces and stores the exact inserted conflict baseline.

var history = session.CaptureHistory();
session.Undo();
```

Use `session.Apply(label, edits, expectedVersion)` for atomic source tools. Syntax nodes must be resolved again after edits: node IDs are structural, not an incremental immutable compiler graph. The workbench's stable **document IDs** are a separate persistence identity.

## Build, test and release

```sh
dotnet build ProDesigner.slnx -c Release
dotnet test tests/ProDesigner.Tests -c Release
dotnet pack src/ProDesigner.DesignSystems -c Release
```

Tests include real SDK/MSBuild graphs, separately launched preview workers, source recovery, pointer operations, namescope preservation, runtime-valid authored brushes/themes, animation fidelity, linked-component propagation, variant precedence, grouped undo, actual Roslyn rename plans and failure-injected journal rollback. Playwright exercises the real WebAssembly workbench, including component overrides, workspace round-trips and a mouse-driven prototype navigation flow. Current results are recorded in the PR/Actions run; the existence of tests is not a claim that an unvalidated revision passed.

CI builds/tests on Windows, macOS and Linux and packs all 15 libraries. A version change in `Directory.Build.props` merged into `main`, a matching tag, or a manual release run builds packages, self-contained desktop bundles and a tested browser archive. Releases include SHA-256 checksums and existing assets are not overwritten. NuGet.org publishing requires `NUGET_API_KEY`; downloadable `.nupkg` archives do not imply registry publication. Signing and notarization are not implemented.

## Trust and engineering boundaries

The safe preview creates only known controls. Project evaluation and runtime execution need affirmative trust. The supervised preview worker has the user's OS permissions: **process separation is not a security sandbox**. Recovery never restores project trust.

File refactoring preflights all paths and writes a durable journal. Each replacement is atomic, but ordinary filesystems do not provide atomic visibility across several paths. Cooperating external editors are necessary to eliminate the final check/replace race; later edits are rejected rather than knowingly overwritten. Undo source history, component-group history and project-file journals have distinct ownership and retention rules.

Remaining areas include embedded process-rendered artboards, complete application-resource initialization, incremental compiler/preview updates, full visual template/state editing, arbitrary vector operations, nested component/variant systems, complete project-bound binding refactoring, exhaustive animation semantics, multiplayer, and production accessibility/performance qualification. Source undo stacks are persisted in bounded form; grouped component transactions, project-rename UI history and the full evaluated solution are not restored across sessions. See [SECURITY.md](SECURITY.md) and the [capability ledger](docs/capabilities.md).

## Contributing and licensing

Read [CONTRIBUTING.md](CONTRIBUTING.md). Preserve user source, keep libraries independently consumable, make unsupported behavior visible and add round-trip/undo/UI tests with new operations.

MIT. XamlX is pinned to its upstream MIT source; dependencies retain their licenses. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). ProDesigner is independent of the designer products used as comparison targets.
