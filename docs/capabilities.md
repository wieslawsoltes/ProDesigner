# Capability and parity ledger

This ledger is part of the product contract. **Implemented** means code exists with a usable path; it does not imply exhaustive compatibility or production qualification. **Restricted** describes intentionally bounded behavior. **Pending** means no complete implementation is claimed.

| Area | Status | Boundary |
| --- | --- | --- |
| Source-preserving XAML properties | Implemented | Immutable public source snapshots. Existing attribute-value edits use isolated XML validation and shifted spans without a full XML scan; structural/namespace changes fall back to full parsing. |
| Source/visual synchronization | Implemented | Invalid text retains the last valid preview. Supported scalar deltas reuse real safe-preview controls; structural/resource/name/binding changes rebuild. The compiler pipeline is not fully incremental. |
| Source undo/redo | Implemented, bounded | Source history includes last-valid syntax snapshots. Workspace exports retain up to 20 undo and 20 redo entries per document. |
| Structural editing | Implemented, restricted | Insert/delete/duplicate/reparent, namescope-aware supported reference rewrites and root namespace hoisting. Arbitrary cross-template reference migration is not complete. |
| XamlX integration | Implemented, restricted | Actual pinned parser and markup-extension syntax validation. Not all Avalonia compiler transforms run in safe mode. |
| Roslyn/project analysis | Implemented, restricted | C# diagnostics/generation, project-bound XAML types/properties/events and member completion. Full binding-data-context resolution is incomplete. |
| Solution/project loading | Implemented, desktop | Trusted MSBuild evaluation, graph/metadata, diagnostics, cancellable restore/build, target-framework selection and virtualized view navigation. Package-management UI remains pending. |
| Dependency resolution | Restricted | Resolves dependencies available to the selected MSBuild/SDK environment; missing SDKs, packages and incompatibilities are reported. |
| Device/theme artboards | Implemented | Actual Avalonia controls at multiple dimensions/themes. Localization and DPI preview matrices remain pending. |
| Selection and outline | Implemented | Virtualized/collapsible/filterable outline, source mapping, multi-selection and span-based selection reconciliation. Process-local identity is reconciled for mapped edits and unique named moves. Drag-to-reparent outline UI and full immutable compiler identities remain pending. |
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
| Process-separated preview | Implemented | Bounded IPC, revision matching, liveness checks and cancellation/timeouts. Embedded bounded PNG/source-map artboard with ordered pointer/key/text input and source/sequence checks. No project object is loaded by the host. Not a shared-GPU renderer or OS permissions sandbox. |
| Animation | Implemented, restricted | Scalar/color tracks, playback/scrub, timeline history, custom splines, workspace persistence and representable XAML import/export. Full transforms, choreography and all Avalonia playback options remain incomplete. |
| Vector geometry | Restricted | All SVG command families, relative/implicit commands, F0/F1 fill rules, arc editing, exact supported subdivision/affine transforms, bounded flattening, Boolean operations and stroke outlining. Skia path operations are single precision; compound-path topology editing and an exhaustive vector UX remain pending. |
| Workspace/recovery | Implemented, bounded | Schema-2 checked snapshots, stable document identities, components/prototypes, source/timeline data and two-generation desktop/browser recovery. Schema 1 remains readable; full evaluated solution and grouped/refactor UI histories do not persist. |
| Refactoring journal recovery | Implemented, desktop | Startup metadata-only discovery, explicit approved-target review, before/after views, conflict-aware restore and project-rename redo. Replay creates a new journal; grouped UI receipt history remains session-local. |
| Collaboration/comments | Pending | No multiplayer service, presence, remote synchronization or shared review backend. |
| Accessibility/performance | Partial qualification | Native control accessibility/automation labels, headless/browser regressions and existing virtualized views. Comprehensive platform audit and large-project budgets remain pending. |
| Delivery | Implemented workflow | Sixteen packable libraries, desktop/browser builds, PR tests, protected main-only Pages and versioned releases. NuGet requires a configured secret; signing/notarization are not implemented. |
| Full WPF/Blend/Figma/Xcode parity | Not complete | The comparison is an acceptance target, not the delivered status. |

## Next acceptance milestones

**Compiler and rendering fidelity:** complete application resources, exhaustive runtime/template object correspondence, compiler-level immutable structural sharing, incremental structural parsing and comprehensive XamlX transforms/binding analysis. The delivered attribute fast path and scalar control deltas cover a deliberately narrower subset.

**Authoring depth:** visual template/state editors, nested linked components, richer responsive layout, compound vector topology and interaction depth, faithful animation semantics and a richer prototype transition system.

**Cross-file integrity:** semantic handling of remaining XAML references and component metadata, project/file rename, unified cross-file undo/redo persistence and explicit concurrency policies for external editors.

**Qualification and distribution:** representative large solutions, cancellation/failure injection, browser/platform accessibility audits, memory/frame-time budgets, signed desktop packaging, collaboration architecture and security review.

See [the v0.3 workflow and embedding guide](components-prototypes-refactoring.md) for the actual commands, persisted data and boundaries of each new subsystem.

The [0.4 implementation guide](embedded-preview-vector-recovery.md) specifies the embedded preview, vector numeric and refactoring recovery boundaries.
