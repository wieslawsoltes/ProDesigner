# Capability and parity ledger

This ledger is part of the product contract. **Implemented** means code exists with a usable path; it does not imply exhaustive compatibility or production qualification. **Restricted** describes intentionally bounded behavior. **Pending** means no complete implementation is claimed.

| Area | Status | Boundary |
| --- | --- | --- |
| Source-preserving XAML properties | Implemented | Targeted spans preserve surrounding text; full-document reparse. |
| Source/visual synchronization | Implemented | One session model; invalid text retains last valid preview. |
| Undo/redo and stale edit guard | Implemented | Bounded source snapshots; not cross-file transactional undo. |
| Structural insert/delete/duplicate/reparent APIs | Implemented | Property-element semantics, namescope/reference remapping and identity reconciliation need expansion. |
| XamlX integration | Implemented, restricted | Actual pinned parser and markup-extension syntax; no full Avalonia/project type binding in safe mode. |
| Roslyn editing engine | Implemented, restricted | Syntax diagnostics, event generation, project-bound XAML types/properties/events, member/rename APIs; cross-file rename UI pending. |
| Solution/project loading | Implemented, desktop | Trusted MSBuild evaluation, graph/metadata, compilation diagnostics; explicit cancellable runtime-preview restore/build; no package-management UI. |
| Dependency resolution | Restricted | MSBuild-resolvable local project environment; missing SDKs/packages reported, not fabricated. |
| Device/theme artboards | Implemented | Real control rendering and multiple widths/themes; localization/DPI matrix pending. |
| Selection and outline | Implemented | Shift selection, outline, source mapping; non-virtualized tree, no collapse/reparent drag UI yet. |
| Dragging and resizing | Restricted | Canvas dragging, multi-selection moves, eight resize handles, aspect constraint, cancellation and pointer regression tests. Automatic layout still controls non-Canvas positioning. |
| Alignment/distribution/snapping | Implemented, restricted | Same-Canvas siblings; no full Figma constraint model. |
| Auto layout editing | Restricted | Avalonia layout attributes; not Figma auto-layout/constraint conversion. |
| Code editor | Implemented, restricted | AvaloniaEdit, highlighting, find/replace, catalog completion; no full XAML language server. |
| Property inspector | Implemented, restricted | Text-based scalar/attached properties; rich brush/gradient/geometry editors pending. |
| Bindings/resources | Restricted | Text editing, local scalar resources, simple sample-data binding in safe preview. |
| Custom controls and code-behind preview | Implemented, desktop | Explicit trusted SDK restore/build, assembly resolver, parameterless code-behind root and real runtime XAML; process isolation, application resources and code hot-reload remain pending. |
| Templates, themes, styles and visual states | Pending authoring | Source retained; runtime preview may render them; dedicated visual editors pending. |
| Animation timeline | Implemented, restricted | Scalar named-target tracks, playback/scrub, key insertion/deletion; no persisted timeline project model yet. |
| Animation XAML export | Restricted | Continuous scalar easing exported with tested KeySpline fidelity; Step export is explicitly rejected. Import remains pending. |
| Vector/path editing and booleans | Pending | Basic Rectangle/Ellipse insertion only. |
| Components, variants and design systems | Pending | Not equivalent to Figma components/variants. |
| Prototype links and interaction flows | Pending | Interactive built-in controls only; no flow graph. |
| Project-aware refactoring | Pending integration | Roslyn semantic API exists; XAML/C# symbol links and atomic multi-file commits not implemented. |
| Remote/process-isolated preview | Pending | Current desktop runtime preview requires trust and is in-process. |
| Collaboration, comments, multiplayer | Pending | No server or synchronization service. |
| Recovery and persistence | Restricted | XAML import/export; no crash journal, autosaved workspace or external file-conflict handling yet. |
| Accessibility and automation | Partial | Native control accessibility/command labels, headless/browser tests; comprehensive audit pending. |
| Package/release automation | Implemented workflow | Registry tokens, tags and Pages enablement are deployment prerequisites. |
| Full WPF/Blend/Figma/Xcode parity | Pending | A long-term acceptance target, not the status of this initial PR. |

## Next large implementation milestones

### Compiler-backed project host

Persist an evaluated, virtualized solution model; add cancellable restore/build, target-framework selection, assembly dependency resolution and a supervised preview process. Keep execution permission separate from read-only metadata. Reconcile source nodes with runtime objects and project symbols without relying on structural paths alone.

### Fidelity and safe editing

Expand property/content semantics; preserve namescope and ElementName references through clone/reparent; add namespace-aware symbol rename, resource/theme/template editing, validated collection operations, all-direction transforms and correct cancelable gestures.

### Professional tooling

Rich property controls, vector geometry, grid/constraint tooling, sample-data authoring, responsive variants, animation import and faithful easing/transform/color export, timeline persistence, full keyboard/focus behavior and multi-file undo.

### Qualification

Representative real-world Avalonia solutions, semantic round-trip fixtures, accessibility audits, pointer interaction tests, memory and responsiveness budgets, package API compatibility checks, signing and crash recovery.
