# Validation and release checklist

## Local commands

```sh
dotnet restore ProDesigner.slnx
dotnet build ProDesigner.slnx -c Release --no-restore
dotnet test tests/ProDesigner.Tests -c Release --no-build
```

The desktop solution deliberately excludes the browser host, so a desktop-only contributor does not need the WASM workload. For browser tests, install `wasm-tools`, publish `apps/ProDesigner.Browser`, place the generated wwwroot content in `artifacts/site`, and run `npm install`, `npx playwright install chromium`, then `npm test` under `tests/browser`.

## Test layers

Pure-library tests cover syntax spans/trivia, malformed intermediate edits, namescopes, layout/resize/constraint geometry, component variants/overrides and source conflicts, prototype state transitions, scalar/color/spline animation and bounded workspace/history serialization.

Compiler/project tests exercise actual Roslyn renames, related and unrelated XAML types, C# comments/literals, code-behind events, real SDK builds, MSBuild project references, unsaved editor overlays and refreshed semantic snapshots. Journal tests inject a failure during a multi-file commit, verify rollback, preserve BOMs/CRLF bytes and refuse stale/external edits. They do not assert an impossible filesystem-wide atomic visibility guarantee.

Avalonia.Headless tests run real controls and pointer events: drag/resize/cancel, runtime-valid authored resources/brushes/themes, custom-control previews, component propagation/grouped history, workspace recovery and prototype button navigation. Framework objects such as KeySpline are constructed through AvaloniaFact/AvaloniaTheory on the shared dispatcher. Ordinary xUnit workers must not accidentally initialize Avalonia's global dispatcher first.

Playwright loads the real managed WebAssembly application, not a mocked DOM. Coverage includes visual pointer dragging, XAML edits, undo/redo, malformed-source recovery, reload recovery, geometry editing, linked component overrides and propagation, workspace schema round-trips, and a real mouse click navigating the prototype from SignIn to Settings and back. Screenshots and failure traces are attached to CI runs.

## Interpreting CI

The PR workflow builds/tests desktop on Windows, macOS and Linux, packs all 15 source libraries and publishes/tests the browser independently. Keep build, test and packaging outcomes separate. A successful package archive does not prove production API compatibility, rendering parity or public registry publication.

Exact counts and the tested commit belong in the final PR validation record, linked to its Actions run. Do not preserve a stale numeric badge when tests change. Do not claim local execution when a change was only validated by GitHub-hosted CI.

## Release acceptance

Require a green final-head PR build, inspect browser results/screenshots, and review the capability ledger. Test representative solutions, conflict paths and recovery. Verify that imported workspaces never restore trust and that prototype playback does not mutate source. Confirm package contents, licenses and no private data.

Pages deploys only merged `main` through the protected environment. Verify the public repository subpath boots the intended revision. Versioned releases package source, 15 libraries, desktop bundles, browser output and checksums. Confirm the publishing result separately from CI: NuGet push is conditional, signing/notarization are not implemented, and existing release downloads are not overwritten.

Remaining qualification includes large-solution frame/memory budgets, robust immutable/incremental identity, fuzzing, cancellation stress, full pointer/keyboard/platform coverage and accessibility audits. These are not replaced by the regression suite.
