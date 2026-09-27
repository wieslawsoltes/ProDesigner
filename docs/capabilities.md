# Capability and parity ledger

This ledger is part of the product contract. **Implemented** means code exists with a usable path; it does not imply exhaustive compatibility or production qualification. **Restricted** describes intentionally bounded behavior. **Pending** means no complete implementation is claimed.

| Area | Status | Boundary |
| --- | --- | --- |
| Source-preserving XAML properties | Implemented | Targeted spans preserve surrounding source. The tree is reparsed for each transaction. |
| Source/visual synchronization | Implemented | One session model; invalid text retains the last valid preview. Preview rebuilding is debounced, not fully incremental. |
| Source undo/redo | Implemented, bounded | Source history includes last-valid syntax snapshots. Workspace exports retain up to 20 undo and 20 redo entries per document. |
| Structural editing | Implemented, restricted | Insert/delete/duplicate/reparent, namescope-aware supported reference rewrites and root namespace hoisting. Arbitrary cross-template reference migration is not complete. |
| XamlX integration | Implemented, restricted | Actual pinned parser and markup-extension syntax validation. Not all Avalonia compiler transforms run in safe mode. |
| Roslyn/project analysis | Implemented, restricted | C# diagnostics/generation, project-bound XAML types/properties/events and member completion. Full binding-data-context resolution is incomplete. |
| Solution/project loading | Implemented, desktop | Trusted MSBuild evaluation, graph/metadata, diagnostics, cancellable restore/build, target-framework selection and virtualized view navigation. Package-management UI remains pending. |
| Dependency resolution | Restricted | Resolves dependencies available to the selected MSBuild/SDK environment; missing SDKs, packages and incompatibilities are reported. |
| Device/theme artboards | Implemented | Actual Avalonia controls at multiple dimensions/themes. Localization and DPI preview matrices remain pending. |
| Selection and outline | Implemented | Virtualized/collapsible/filterable outline, source mapping, multi-selection and span-based selection reconciliation. Drag-to-reparent outline UI remains pending. |
| Drag/resize/arrangement | Implemented, restricted | Canvas movement, eight handles, aspect constraints, snapping, alignment/distribution and cancellation. Non-Canvas positioning obeys Avalonia layout. |
| Responsive anchors | Implemented, restricted | Start/center/end/stretch conversion to ordinary single-cell Grid properties; measured Canvas conversion. Not a general solver or complete Figma auto layout. |
| Code editing | Implemented, restricted | AvaloniaEdit, highlighting, find/replace, catalog/project-member completion. No comprehensive XAML language server or project-semantic binding completion. |
| Inspector and paint | Implemented, restricted | Scalar/attached/project properties, RGBA, linear-gradient and supported geometry tools. Not an exhaustive custom type-editor catalog. |
| Resources/styles/themes | Restricted authoring | Source-preserving upserts and source-based template composition. Complete visual template/state authoring remains pending. |
| Linked components | Implemented, restricted | Captured XAML definitions, named variants, instance overrides, scoped names and cross-view propagation with conflict checks. Nested linked graphs, structural overrides and variant-axis matrices remain pending. |
| Component-group undo | Implemented, session-local | All affected sources and metadata are preflighted; explicit grouped undo/redo consumes the matching source-history entries. Group metadata history is not persisted across sessions. |
| Prototype flows | Implemented, restricted | Validated connections, flow map, back stack, overlays, string variables/equality conditions and pointer/key/delay input. Interactive safe real-control player; no network/project script execution. |
| Prototype fidelity | Restricted | Fixed 960×620 player profile, no animated transitions, scroll actions, graph drag-wiring or shareable hosted sessions. |
| Project-aware rename | Implemented, desktop, restricted | Actual Roslyn C# rename plus bound XAML tags, class/data-type directives, direct members, property elements and code-behind handlers. Preview/apply/undo UI and journaled files. Nested/generic/overloaded cases, arbitrary selectors/bindings and component-metadata synchronization remain pending. |
| File transactions | Implemented, bounded | All-file preflight, durable before/after snapshots, per-file atomic replacement, encoding preservation and conflict-aware rollback. Not atomic visibility across multiple paths; uncooperative external-writer races remain. |
| Runtime custom controls | Implemented, desktop | Trusted SDK build, assembly resolution, parameterless code-behind roots and real runtime XAML in a supervised worker. Full application initialization and C# hot reload remain pending. |
| Process-separated preview | Implemented | Bounded IPC, revision matching, liveness checks and cancellation/timeouts. Separate worker window, not an embedded remote renderer or OS sandbox. |
| Animation | Implemented, restricted | Scalar/color tracks, playback/scrub, timeline history, custom splines, workspace persistence and representable XAML import/export. Full transforms, choreography and all Avalonia playback options remain incomplete. |
| Vector geometry | Restricted | M/L/C/Q/Z absolute/relative editing, points and Bézier handles. Arc/shorthand support, path Boolean operations and a complete vector toolkit remain pending. |
| Workspace/recovery | Implemented, bounded | Schema-2 checked snapshots, stable document identities, components/prototypes, source/timeline data and two-generation desktop/browser recovery. Schema 1 remains readable; full evaluated solution and grouped/refactor UI histories do not persist. |
| Refactoring journal recovery | Library API | A receipt can be reopened with an explicit path allowlist and conflict checks. Automatic startup review/recovery UI and project-rename redo are not implemented. |
| Collaboration/comments | Pending | No multiplayer service, presence, remote synchronization or shared review backend. |
| Accessibility/performance | Partial qualification | Native control accessibility/automation labels, headless/browser regressions and existing virtualized views. Comprehensive platform audit and large-project budgets remain pending. |
| Delivery | Implemented workflow | Fifteen packable libraries, desktop/browser builds, PR tests, protected main-only Pages and versioned releases. NuGet requires a configured secret; signing/notarization are not implemented. |
| Full WPF/Blend/Figma/Xcode parity | Not complete | The comparison is an acceptance target, not the delivered status. |

## Next acceptance milestones

**Compiler and rendering fidelity:** complete application resources, project types in embedded process-rendered artboards, durable compiler/runtime object identities, incremental parsing and preview deltas, comprehensive XamlX transforms and binding analysis.

**Authoring depth:** visual template/state editors, nested linked components, richer responsive layout, complete vector geometry and booleans, faithful animation semantics and a richer prototype transition system.

**Cross-file integrity:** semantic handling of remaining XAML references and component metadata, project/file rename, recovery UI for interrupted refactors, unified cross-file undo/redo persistence and explicit concurrency policies for external editors.

**Qualification and distribution:** representative large solutions, cancellation/failure injection, browser/platform accessibility audits, memory/frame-time budgets, signed desktop packaging, collaboration architecture and security review.

See [the v0.3 workflow and embedding guide](components-prototypes-refactoring.md) for the actual commands, persisted data and boundaries of each new subsystem.
