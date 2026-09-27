# Capability and parity ledger

This ledger is part of the product contract. **Implemented** means code exists with a usable path; it does not imply exhaustive compatibility or production qualification. **Restricted** describes intentionally bounded behavior. **Pending** means no complete implementation is claimed.

| Area | Status | Boundary |
| --- | --- | --- |
| Source-preserving XAML properties | Implemented | Targeted spans preserve surrounding text; full-document reparse. |
| Source/visual synchronization | Implemented | One session model; invalid text retains last valid preview. |
| Undo/redo and stale edit guard | Implemented | Bounded source snapshots; not cross-file transactional undo. |
| Structural insert/delete/duplicate/reparent APIs | Implemented | Template namescope-aware rename/duplication, token-aware supported Binding/Reference rewrites and root namespace hoisting. Arbitrary cross-template/cross-file reference migration remains restricted. |
| XamlX integration | Implemented, restricted | Actual pinned parser and markup-extension syntax; no full Avalonia/project type binding in safe mode. |
| Roslyn editing engine | Implemented, restricted | Syntax diagnostics, event generation, project-bound XAML types/properties/events, member/rename APIs; cross-file rename UI pending. |
| Solution/project loading | Implemented, desktop | Trusted MSBuild evaluation, graph/metadata, compilation diagnostics; explicit cancellable runtime-preview restore/build; target-framework choice, persistent virtualized view list and project-member completion. No package-management UI. |
| Dependency resolution | Restricted | MSBuild-resolvable local project environment; missing SDKs/packages reported, not fabricated. |
| Device/theme artboards | Implemented | Real control rendering and multiple widths/themes; localization/DPI matrix pending. |
| Selection and outline | Implemented | Shift selection, outline, source mapping; virtualized flattened outline with expand/collapse and filtering. Drag-to-reparent UI remains pending. |
| Dragging and resizing | Restricted | Canvas dragging, multi-selection moves, eight resize handles, aspect constraint, cancellation and pointer regression tests. Automatic layout still controls non-Canvas positioning. |
| Alignment/distribution/snapping | Implemented, restricted | Same-Canvas siblings; no full Figma constraint model. |
| Auto layout editing | Restricted | Avalonia layout attributes; not Figma auto-layout/constraint conversion. |
| Code editor | Implemented, restricted | AvaloniaEdit, highlighting, find/replace, catalog and loaded-project member completion; no complete XAML language server. |
| Property inspector | Implemented, restricted | Scalar/attached/project properties plus RGBA, gradient and supported Bézier geometry tools. Not an exhaustive type-editor catalog. |
| Bindings/resources | Restricted | Text editing, resource and ControlTheme authoring, editable JSON sample data and scalar bindings in safe preview. |
| Custom controls and code-behind preview | Implemented, desktop | Explicit trusted SDK restore/build, assembly resolver, parameterless code-behind root and real runtime XAML; supervised process isolation and live XAML updates. Full application resources and C# hot reload remain pending. |
| Templates, themes, styles and visual states | Restricted authoring | Resource/style/theme upserts and source-based template composition are usable. Full visual template/state editing remains pending. |
| Animation timeline | Implemented, restricted | Scalar named-target tracks, playback/scrub, key insertion/deletion; separate timeline undo/redo, color tracks and workspace persistence. |
| Animation XAML export | Restricted | Continuous scalar easing exported with tested KeySpline fidelity; Step export is explicitly rejected. Representable #Name scalar/hex-color animations and custom splines can be imported; unsupported options are diagnosed. |
| Vector/path editing and booleans | Restricted | Path insertion and point editing for M/L/C/Q/Z absolute/relative paths. Arc tools and Boolean operations remain pending. |
| Components, variants and design systems | Pending | Not equivalent to Figma components/variants. |
| Prototype links and interaction flows | Pending | Interactive built-in controls only; no flow graph. |
| Project-aware refactoring | Pending integration | Roslyn semantic API exists; XAML/C# symbol links and atomic multi-file commits not implemented. |
| Remote/process-isolated preview | Implemented process separation | Desktop preview runs in a supervised worker with bounded IPC, cancellation, liveness checks and timeout termination. This is not an OS sandbox or remote renderer. |
| Collaboration, comments, multiplayer | Pending | No server or synchronization service. |
| Recovery and persistence | Implemented, bounded | Checksummed workspace files, desktop/browser two-generation recovery, saved-baseline preservation and optimistic conflict-aware atomic desktop saves. External-editor races and multi-file atomic refactoring remain pending. |
| Accessibility and automation | Partial | Native control accessibility/command labels, headless/browser tests; comprehensive audit pending. |
| Package/release automation | Implemented workflow | Registry tokens, tags and Pages enablement are deployment prerequisites. |
| Full WPF/Blend/Figma/Xcode parity | Pending | A long-term acceptance target, not the status of this initial PR. |

## Next large implementation milestones

### Compiler-backed project host

Persist an evaluated, virtualized solution model; extend existing cancellable restore/build, target-framework selection and supervised preview services with complete application resource initialization and process-rendered embedded artboards. Keep execution permission separate from read-only metadata. Reconcile source nodes with runtime objects and project symbols without relying on structural paths alone.

### Fidelity and safe editing

Expand property/content semantics; extend existing namescope/reference-safe operations to complex cross-file cases; add transactional project symbol rename, resource/theme/template editing, validated collection operations, all-direction transforms and correct cancelable gestures.

### Professional tooling

Broaden rich property and vector controls, grid/constraint tooling, responsive variants, exhaustive animation semantics, full keyboard/focus behavior, cross-file undo and collaborative editing.

### Qualification

Representative real-world Avalonia solutions, semantic round-trip fixtures, accessibility audits, pointer interaction tests, memory and responsiveness budgets, package API compatibility checks, signing and crash recovery.

## Remaining acceptance boundaries after 0.2

The new tools are working implementations, not declarations of complete product parity. The preview is still a separate window rather than an embedded process-rendered surface. Workspace snapshots persist documents and timelines, but not the source undo stacks or complete solution evaluation state. The vector editor rejects arcs/shorthand SVG commands it cannot represent. Animation import supports exact named selectors with compatible duration/looping and scalar/hex-color values; it does not flatten unsupported playback options. Source IDs are still structural, with explicit span-based selection reconciliation for visual transactions rather than a full incremental immutable compiler tree.

Figma-grade components/variants/constraints, multiplayer/comments, arbitrary path Boolean tooling, complete visual template/state editors, comprehensive Avalonia compiler transforms and atomic cross-file C#/XAML refactoring are not complete. Production performance, platform accessibility, signing and notarization also need qualification.
