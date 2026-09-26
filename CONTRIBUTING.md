# Contributing

Use .NET 10 and initialize the pinned submodule. Submit substantial, coherent PRs with tests and documentation, but keep library boundaries narrow. Portable syntax/design code must not depend on UI assemblies or MSBuild. UI packages must obtain host capabilities through explicit contracts.

Preserve user source. Never regenerate an entire document for a single property edit. Reject invalid visual transactions before publishing them; retain malformed text edits without destructive fallback. Preserve unknown markup and report unsupported preview behavior.

Do not equate XML parsing with XamlX semantic binding, or syntax diagnostics with project compilation. Keep capability statements accurate. Add source round-trip, undo and regression tests for every editing operation. Update docs/capabilities.md whenever a boundary changes.

Do not execute workspace code or load arbitrary assemblies without an explicit trust action. Never hide dependency failures or capture credentials in logs. Follow SECURITY.md for reports.

CI builds desktop on Windows/macOS/Linux and tests the actual WebAssembly host. Keep changes formatting-consistent, nullable-aware, deterministic and independently packable. New public APIs need XML documentation and an embedding example.
