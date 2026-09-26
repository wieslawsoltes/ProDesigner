# Testing and release checklist

Run `dotnet test tests/ProDesigner.Tests -c Release` for core and Avalonia headless tests. The solution intentionally excludes the browser application so desktop contributors do not need the wasm-tools workload. Browser builds are independently published and tested in CI.

To run Playwright locally, publish the browser host and copy the generated wwwroot contents into artifacts/site, then run `npm install` and `npx playwright install chromium` under tests/browser followed by `npm test`. The configured static server serves the actual managed WebAssembly application.

Tests must cover both directions of every editing feature: source → tree/preview and visual operation → exact source. Verify undo, redo, malformed intermediate source, comments/trivia, namespace aliases, attached properties, bindings, expected-version failure, and non-destructive handling of unsupported syntax. For new runtime features, add headless control assertions and browser checks where supported.

Before a release: require cross-platform CI and browser checks; inspect screenshots; review the capability ledger; inspect package contents; exercise a sample solution containing project and NuGet references; verify Pages boot from its repository subpath; confirm no secrets or local files are packaged. Current automation does not provide code signing, notarization, a security sandbox, or proof of full professional designer parity.

## Validated initial implementation

The expanded implementation has 76 passing tests, including actual SDK builds, an MSBuild project-reference graph, project-bound Roslyn XAML diagnostics, runtime loading of a compiled custom control and code-behind root, pointer dragging/resizing/cancellation, and exported spline comparisons against Avalonia's evaluator. The local Release solution build completed with zero warnings and errors. GitHub Actions separately validates Windows, macOS, Linux and the actual WebAssembly workbench; consult the PR's latest run for current remote results.

GitHub Pages deployment is restricted to `main`. Feature branches build and test the website but do not bypass the repository's protected Pages environment. Merge the reviewed PR to `main` to activate the configured publishing path. The initial implementation has no public NuGet publication or signed desktop release until the release workflow is invoked with the required credentials.
