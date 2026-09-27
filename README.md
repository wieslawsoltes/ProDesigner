<div align="center">

# ◈ ProDesigner
### Your next great interface starts here.

**Composable visual authoring for Avalonia UI.**

[![Build](https://github.com/wieslawsoltes/ProDesigner/actions/workflows/ci.yml/badge.svg)](https://github.com/wieslawsoltes/ProDesigner/actions/workflows/ci.yml)
![.NET](https://img.shields.io/badge/.NET-10-7C6BC4)
![Avalonia](https://img.shields.io/badge/Avalonia-12.1.3-9D8BE0)
![License](https://img.shields.io/badge/license-MIT-77AE98)

[Open browser designer](https://wieslawsoltes.github.io/ProDesigner/) · [Downloads](https://github.com/wieslawsoltes/ProDesigner/releases) · [Getting started](docs/getting-started.md) · [Architecture](docs/architecture.md) · [Capability ledger](docs/capabilities.md)

</div>

---

ProDesigner brings visual editing and source authoring into one Avalonia workbench. Arrange real controls on device artboards, inspect properties, edit XAML without losing comments and formatting, navigate between views, create animation tracks, and recover your workspace after restarting. Desktop and browser hosts share C# editing libraries and real Avalonia rendering—not an HTML approximation.

> **0.2.0-alpha.1.** This is a functional alpha, not a claim of complete WPF Designer, Blend, Figma or Xcode parity. The [capability ledger](docs/capabilities.md) identifies usable workflows, restrictions and remaining work. Browser previews use an explicit built-in-control catalog; project evaluation and arbitrary runtime XAML execution are desktop-only.

## Design, code and project context

| Area | Implemented workflows |
| --- | --- |
| Visual authoring | Device/theme artboards, Canvas dragging, eight resize handles, multi-selection movement, aspect constraints, snapping/guides, alignment, distribution and gesture cancellation. |
| Source preservation | Source-spanned XAML tree, targeted property/structural transactions, undo/redo, stale-edit checks, namespace-aware fragment import, namescope-aware supported reference rewrites. Invalid typing retains the last valid preview. |
| Compiler services | Real pinned XamlX parser; Roslyn project type/property/event diagnostics, C# diagnostics and event generation; loaded-project member completion/inspection and reusable semantic rename APIs. |
| Workspace navigation | Multiple views, virtualized/collapsible layer outline, filtered solution view list, XAML import/export and target-framework choice for project preview. |
| Trusted project preview | SDK restore/build and dependency resolution; supervised child process for compiled custom controls/code-behind, revision-matched IPC, live XAML updates and cancellation/timeout termination. |
| Design systems and paint | Resource/style/ControlTheme authoring, source-based template composition, RGBA color controls, linear-gradient stops and editable JSON sample data. |
| Vector editing | Editable M/L/C/Q/Z paths with endpoint and Bézier-handle manipulation. Unsupported arc/shorthand/Boolean operations are not silently approximated. |
| Animation | Named scalar/color tracks, playback, scrub, looping, timeline undo/redo, custom splines, representable XAML import and tested continuous-easing export. |
| Recovery | Versioned/checksummed complete workspaces; desktop/browser two-generation journals; saved baselines, intermediate XAML, code-behind, timelines, sample data and profiles; optimistic conflict-aware atomic desktop saves. |

Use **Design** in the toolbar for resources, styles, templates, paint and data. Insert **Path** from Assets and open **Path** to edit its geometry. Use the command palette for workspace files, recovery, isolated preview and its Stop command.

## Run the desktop application

Install the .NET 10 SDK:

```sh
git clone --recurse-submodules https://github.com/wieslawsoltes/ProDesigner.git
cd ProDesigner
dotnet restore ProDesigner.slnx
dotnet run --project apps/ProDesigner.Desktop
```

An existing clone needs `git submodule update --init --recursive`. Windows, macOS and Linux use Avalonia's desktop backend. The SDK is also required to evaluate/build a trusted solution, even when running a self-contained desktop distribution.

## Browser development

```sh
dotnet workload install wasm-tools
dotnet run --project apps/ProDesigner.Browser
```

For a static bundle, run `dotnet publish apps/ProDesigner.Browser -c Release -o artifacts/browser` and serve its generated `wwwroot` over HTTP/HTTPS. Threading is disabled, so Pages does not require cross-origin-isolation headers. Browser journals stay in origin-local storage; quota failures are reported. Export `.prodesigner` files for portable backups.

## Thirteen reusable packages

| Package | Responsibility |
| --- | --- |
| `ProDesigner.Core` | Diagnostics, spans, edits, preview profiles and host contracts. |
| `ProDesigner.Xaml` | Lossless source model, namespace/namescope helpers, reference-safe editing and fragment import. |
| `ProDesigner.Design` | Sessions, history, selection, resize/layout geometry and toolbox. |
| `ProDesigner.Animation` | Scalar/color tracks, interpolation, splines and XAML import/export. |
| `ProDesigner.Authoring` | Resources, styles, themes, gradient models and editable path geometry. |
| `ProDesigner.Persistence` | Workspace codec, snapshots, recovery and atomic/conflict-aware files. |
| `ProDesigner.PreviewProtocol` | Bounded named-pipe transport and supervised preview processes. |
| `ProDesigner.XamlX` | Actual pinned upstream XamlX parser integration. |
| `ProDesigner.Roslyn` | Project-bound XAML diagnostics, C# generation and semantic services. |
| `ProDesigner.Workspaces` | Trusted MSBuild solution loading, dependencies and SDK builds. |
| `ProDesigner.Runtime` | Trusted Avalonia runtime XAML and compiled project controls. |
| `ProDesigner.Avalonia` | Safe real-control preview, artboards and source-linked design surface. |
| `ProDesigner.Workbench` | Embeddable editor, inspector, timeline, vector tools and workspace UI. |

Portable editing libraries do not depend on application hosts. See [embedding](docs/embedding.md) for independent use and optional host-service injection.

```csharp
using ProDesigner.Design;

var session = new DesignerSession(xaml);
var button = session.Tree.Elements.First(n => n.DisplayName == "SaveButton");
session.Select(button.Id);
session.SetProperty("Width", "160");
var editedXaml = session.Source; // Targeted edit; surrounding source stays intact.
session.Undo();
```

Use `session.Apply(label, edits, expectedVersion)` for atomic tools. Resolve syntax nodes again after a transaction: IDs are structural, with source-span selection reconciliation, not a fully incremental immutable compiler graph.

## Build, test and release

```sh
dotnet build ProDesigner.slnx -c Release
dotnet test tests/ProDesigner.Tests -c Release
dotnet pack src/ProDesigner.Authoring -c Release
```

The 128-test suite includes real SDK/MSBuild graphs, separately launched preview workers, malformed-source recovery, pointer operations, namespace/reference preservation, runtime-valid authored brushes/themes, animation fidelity, checksummed snapshots and external-file conflicts. Playwright tests the actual WebAssembly workbench, including mouse dragging and recovery across a page reload. See [testing](docs/testing.md).

CI builds/tests on Windows, macOS and Linux and packs all 13 libraries. A version change in `Directory.Build.props` merged into `main`, a matching version tag, or a manual release run builds release packages, self-contained desktop bundles and a tested browser archive. GitHub release downloads include SHA-256 checksums; prerelease versions are marked accordingly. Existing release assets are not overwritten. NuGet.org publishing is conditional on `NUGET_API_KEY`; downloadable `.nupkg` files do not imply registry publication. No signing or notarization is currently performed.

## Trust and remaining boundaries

The safe preview creates only known built-in controls. Opening/evaluating a project and running its XAML require affirmative trust. Desktop runtime execution is in a separate supervised process, but that process has the user's OS permissions: **crash containment is not a security sandbox**. Recovery never restores a trust grant or executes a project. See [SECURITY.md](SECURITY.md).

Remaining major areas include complete application-resource initialization, embedded process-rendered artboards, incremental AST/preview updates, comprehensive cross-file XAML/C# refactoring, full visual template/state authoring, advanced vector operations, Figma-style components/variants/constraints, complete animation semantics, multiplayer and production accessibility/performance qualification. Workspace files preserve documents and timelines, not all undo stacks or the full evaluated solution graph. The [ledger](docs/capabilities.md) is the acceptance contract, not a statement of parity already achieved.

## Contributing and licensing

Read [CONTRIBUTING.md](CONTRIBUTING.md). Preserve user source, keep libraries independently consumable, report unsupported behavior and add round-trip/undo/UI tests for new operations.

MIT. XamlX is pinned to its upstream MIT source; dependencies retain their respective licenses. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). ProDesigner is independent and is not affiliated with the designer products used as comparison targets.
