<div align="center">

# ◈ ProDesigner
### Your next great interface starts here.

**A composable visual authoring environment for Avalonia UI.**

[![Build](https://github.com/wieslawsoltes/ProDesigner/actions/workflows/ci.yml/badge.svg)](https://github.com/wieslawsoltes/ProDesigner/actions/workflows/ci.yml)
![.NET](https://img.shields.io/badge/.NET-10-7C6BC4)
![Avalonia](https://img.shields.io/badge/Avalonia-12.1.3-9D8BE0)
![License](https://img.shields.io/badge/license-MIT-77AE98)

[Getting started](docs/getting-started.md) · [Architecture](docs/architecture.md) · [Capabilities](docs/capabilities.md) · [Embedding](docs/embedding.md) · [Security](SECURITY.md)

</div>

---

ProDesigner brings visual editing and source authoring into one Avalonia workbench. Arrange real controls on device artboards, inspect their properties, edit XAML without losing its comments and formatting, navigate between views, and build numeric animation tracks. The desktop and browser hosts share the same C# workbench and Avalonia rendering—not an HTML approximation.

> **Status: 0.2.0-alpha.1.** This is an initial, functional implementation, not a claim of complete WPF Designer, Blend, Figma, or Xcode parity. The [capability matrix](docs/capabilities.md) distinguishes implemented workflows, restricted behavior, and remaining engineering work. The GitHub Pages build uses safe catalog previews; full MSBuild and arbitrary runtime XAML execution are desktop-only.

## Design and code, together

| Workspace | Implemented behavior |
| --- | --- |
| Visual authoring | Multi-artboard preview, selection, Canvas dragging, eight-handle resize, grid/object snapping, guides, marquee selection, keyboard nudging, alignment and distribution commands. |
| Source model | Source-spanned XML/XAML concrete syntax tree, targeted changes, exact-source undo/redo, optimistic revision checks, atomic structural edits, preserved unknown syntax and comments. |
| Language services | Real pinned XamlX parser integration, markup-extension syntax validation, Roslyn C# syntax diagnostics and event-handler generation; project-bound XAML type/property/event diagnostics, semantic member discovery and rename APIs in the reusable Roslyn package. |
| Inspector and editor | Multi-selection property editing, attached properties, arbitrary attribute input, binding/resource text, AvaloniaEdit syntax coloring, line numbers, find/replace, catalog completion, code-to-selection synchronization. |
| Navigation | Multiple open views with independent history, sample view navigator, desktop XAML/solution/project file pickers, browser XAML import/export, device/theme profiles. |
| Animation | Named-target numeric tracks, sorted/replaced keyframes, scrubbing, playback, looping, easing evaluation, cubic-Bezier solver, tested continuous-easing Avalonia Style/Animation spline export. |
| Project services | Explicit-trust MSBuildWorkspace loading, project-reference and metadata resolution through MSBuild, compilation diagnostics, source discovery, cancellable SDK restore/build, raw project/package inventory. |
| Delivery | Thirteen packable libraries, desktop and WebAssembly hosts, cross-platform build/test jobs, browser interaction tests, release and Pages publishing workflows. |

## New in 0.2

The initial PR is merged, and the actual Avalonia browser application is deployed through GitHub Pages. This iteration adds a supervised preview worker (live XAML updates, failure containment and cancellation), a virtualized/collapsible outline and persistent solution view list, and project-member completion/inspection.

Use **Design** in the toolbar for resource, style, ControlTheme, template, RGBA/gradient and sample-data tools. Insert a **Path** from Assets and open **Path** to drag line/Bézier endpoints and control points. The source editor retains unsupported geometry rather than approximating it.

Use the command palette for **Save complete workspace**, **Open saved workspace**, **Recovery journal** and **Stop isolated preview**. `.prodesigner` files include all views, exact intermediate XAML, saved baselines, companion C#, scalar/color animation tracks, selection, sample data and device profiles. Desktop checkpoints use an atomic two-generation journal; browser checkpoints use origin-local storage and report quota failures. Recovery never grants permission to execute a project.

The timeline now imports representable named-target XAML animations, preserves custom splines, supports hexadecimal color tracks and has separate undo/redo. Unsupported selectors, repeat/delay/direction options, discontinuous easing and complex setters are reported instead of silently reinterpreted.

## Run the desktop application

Install the .NET 10 SDK, then:

```sh
git clone --recurse-submodules https://github.com/wieslawsoltes/ProDesigner.git
cd ProDesigner
dotnet restore ProDesigner.slnx
dotnet run --project apps/ProDesigner.Desktop
```

The pinned XamlX submodule is required. For an existing clone, run `git submodule update --init --recursive`. Windows, macOS, and Linux use Avalonia's desktop platform backend. The .NET SDK is also required for trusted solution evaluation.

## Run the browser application

```sh
dotnet workload install wasm-tools
dotnet run --project apps/ProDesigner.Browser
```

For a static distribution:

```sh
dotnet publish apps/ProDesigner.Browser -c Release -o artifacts/browser
# Serve the generated wwwroot directory over HTTP or HTTPS, not file://.
```

Browser builds do not include Roslyn/MSBuild or execute custom XAML constructors. They use the same source model, XamlX parser, editor, timeline, and safe built-in Avalonia control preview. Threading is disabled so the site does not depend on cross-origin isolation headers.

## A toolbox of reusable libraries

| Package | Responsibility |
| --- | --- |
| `ProDesigner.Authoring` | Source-preserving resources, styles, themes, gradients and vector geometry. |
| `ProDesigner.Persistence` | Versioned workspace snapshots, checksums, recovery journals and conflict-aware atomic files. |
| `ProDesigner.PreviewProtocol` | Bounded named-pipe IPC and supervised preview processes. |
| `ProDesigner.Core` | Diagnostics, spans, edits, preview profiles, host-service contracts. |
| `ProDesigner.Xaml` | Lossless syntax model and structural/property editing operations. |
| `ProDesigner.Design` | Document sessions, history, selection, layout geometry and control catalog. |
| `ProDesigner.Animation` | Tracks, keyframes, interpolation, cubic-Bezier evaluation and XAML export. |
| `ProDesigner.XamlX` | Pinned upstream XamlX parser integration with non-executing validation. |
| `ProDesigner.Roslyn` | C# syntax, handler generation, project-bound XAML diagnostics, semantic members and workspace symbol rename. |
| `ProDesigner.Runtime` | Explicitly trusted runtime XAML, compiled custom controls, code-behind roots and assembly-dependency resolution. |
| `ProDesigner.Workspaces` | Trusted MSBuild solution/project loading and dependency diagnostics. |
| `ProDesigner.Avalonia` | Real-control safe preview, source maps, artboards, interactive design surface. |
| `ProDesigner.Workbench` | Embeddable workbench, code editor, property inspector and timeline controls. |

Build NuGet archives with `dotnet pack src/ProDesigner.Xaml -c Release`. CI packs every library. Public registry publishing occurs only when a release tag is pushed and the repository's `NUGET_API_KEY` secret is configured; no registry publication is implied by a successful build.

## A source-preserving edit in a few lines

```csharp
using ProDesigner.Design;

var session = new DesignerSession(xaml);
var button = session.Tree.Elements.First(n => n.Get("x:Name") == "SaveButton");
session.Select(button.Id);
session.SetProperty("Width", "160");
// Only the affected attribute changes; surrounding source remains untouched.
var editedXaml = session.Source;
session.Undo();
```

Use `session.Apply(label, edits, expectedVersion)` for atomic tools. Invalid text typed into the editor is retained as the active source; visual edits pause and the last valid tree/preview remains available. Preview interpretation never rewrites unsupported markup.

## Build and test

```sh
dotnet build ProDesigner.slnx -c Release
dotnet test tests/ProDesigner.Tests -c Release
```

The test suite covers lexical edge cases, trivia preservation, stale and overlapping edits, undo/redo, malformed documents, DTD rejection, layout geometry, keyframe evaluation, Roslyn generation, project inspection, and Avalonia headless workbench behavior. Playwright loads the actual WebAssembly application and exercises the public UI-command bridge; screenshots and traces are retained as CI artifacts. These checks do not substitute for platform accessibility audits, complex-project interoperability testing, or professional-designer parity acceptance.

## Safety and trust

Reading XAML is not the same as executing XAML. Safe preview instantiates only an explicit catalog of built-in Avalonia controls. MSBuild evaluation and runtime previews require separate affirmative actions. The desktop runtime preview uses a **supervised separate process**, and can restore/build and load the selected project only after the explicit runtime-preview trust action. The child has the same OS permissions as the user; process separation is crash containment, **not an OS security sandbox**. See [SECURITY.md](SECURITY.md).

## Roadmap and contribution

Professional parity is tracked explicitly in [docs/capabilities.md](docs/capabilities.md): full project/application resource initialization, comprehensive template/style/resource visual editing, robust cross-file XAML/C# refactoring, advanced vector tools, constraints/components/variants, complete animation semantics, cross-file refactoring, components/variants/constraints, multiplayer collaboration and accessibility qualification remain active work.

Read [CONTRIBUTING.md](CONTRIBUTING.md) before opening a PR. Keep services independent of the workbench, make unsupported behavior visible, and include source round-trip and UI regression tests with every new editor operation.

## License and acknowledgements

MIT. XamlX is included at a pinned upstream revision under its MIT license. Avalonia, AvaloniaEdit, Roslyn, MSBuild Locator, xUnit and Playwright retain their respective licenses. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
