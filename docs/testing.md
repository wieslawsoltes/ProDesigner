# Testing and release checklist

Run `dotnet test tests/ProDesigner.Tests -c Release` for core and Avalonia headless tests. The solution intentionally excludes the browser application so desktop contributors do not need the wasm-tools workload. Browser builds are independently published and tested in CI.

To run Playwright locally, publish the browser host and copy the generated wwwroot contents into artifacts/site, then run `npm install` and `npx playwright install chromium` under tests/browser followed by `npm test`. The configured static server serves the actual managed WebAssembly application.

Tests must cover both directions of every editing feature: source → tree/preview and visual operation → exact source. Verify undo, redo, malformed intermediate source, comments/trivia, namespace aliases, attached properties, bindings, expected-version failure, and non-destructive handling of unsupported syntax. For new runtime features, add headless control assertions and browser checks where supported.

Before a release: require cross-platform CI and browser checks; inspect screenshots; review the capability ledger; inspect package contents; exercise a sample solution containing project and NuGet references; verify Pages boot from its repository subpath; confirm no secrets or local files are packaged. Current automation does not provide code signing, notarization, a security sandbox, or proof of full professional designer parity.
