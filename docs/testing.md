# Testing and release checklist

Run `dotnet test tests/ProDesigner.Tests -c Release` for core and Avalonia headless tests. The solution intentionally excludes the browser application so desktop contributors do not need the wasm-tools workload. Browser builds are independently published and tested in CI.

To run Playwright locally, publish the browser host and copy the generated wwwroot contents into artifacts/site, then run `npm install` and `npx playwright install chromium` under tests/browser followed by `npm test`. The configured static server serves the actual managed WebAssembly application.

Tests must cover both directions of every editing feature: source → tree/preview and visual operation → exact source. Verify undo, redo, malformed intermediate source, comments/trivia, namespace aliases, attached properties, bindings, expected-version failure, and non-destructive handling of unsupported syntax. For new runtime features, add headless control assertions and browser checks where supported.

Before a release: require cross-platform CI and browser checks; inspect screenshots; review the capability ledger; inspect package contents; exercise a sample solution containing project and NuGet references; verify Pages boot from its repository subpath; confirm no secrets or local files are packaged. Current automation does not provide code signing, notarization, a security sandbox, or proof of full professional designer parity.

## Validated initial implementation

The expanded implementation has 126 passing tests, including actual SDK builds, an MSBuild project-reference graph, project-bound Roslyn XAML diagnostics, runtime loading of a compiled custom control and code-behind root, pointer dragging/resizing/cancellation, and exported spline comparisons against Avalonia's evaluator. The local Release solution build completed with zero warnings and errors. GitHub Actions separately validates Windows, macOS, Linux and the actual WebAssembly workbench; consult the PR's latest run for current remote results.

GitHub Pages deployment is restricted to `main`; PR #1 was merged and its deployment succeeded. Feature branches build and test the website but do not bypass the repository's protected Pages environment. Merging reviewed feature PRs activates the configured publishing path. The initial implementation has no public NuGet publication or signed desktop release until the release workflow is invoked with the required credentials.

## 0.2 regression suite

The expanded suite adds token-aware/namescope tests, namespace hoisting validated by Avalonia's real runtime loader, style/resource preservation, gradient rendering, vector point editing, scalar/color animation round-tripping, corruption recovery and stale-file conflict tests. Process tests start the actual desktop executable in a headless worker mode, verify independent PIDs, render/update real controls, reject malformed XAML without losing the worker, and observe worker termination without terminating the test host.

Browser tests cover the new authoring/path dialogs, complete-workspace export/import and recovery across a real page reload. CI screenshots and traces accompany the actual WebAssembly build; static HTML alone does not count as browser validation. Do not equate this suite with full WPF/Blend/Figma/Xcode parity.
