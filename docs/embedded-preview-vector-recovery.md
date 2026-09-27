# Embedded preview, vector operations and recovery — 0.4

## Source and process boundaries

Choose **Trusted runtime preview** in the desktop command palette and affirm the trust prompt. The host can restore/build the selected project and starts a supervised headless Avalonia worker. The worker owns project assemblies, runtime XAML, code-behind instances, application controls and the Skia renderer. The designer receives pixels and source-linked geometry and shows them in an embedded artboard. **No custom project control object crosses the process boundary.**

The artboard offers the configured viewport profiles, interaction mode, a return to safe artboards, and Stop. It currently displays one selected runtime profile at a time; the ordinary built-in safe preview can still show multiple profiles simultaneously. Reading XAML/workspaces does not grant runtime trust. Child processes retain the user's permissions and are not an OS sandbox.

### Wire and rendering contract

`IRenderedExternalPreview` extends the existing host service with RenderAsync and ResetInputAsync. Protocol version 2 uses the existing current-user named-pipe framing and process supervision. A frame contains:

- the SHA-256 hash of the exact rendered source and a strictly increasing frame sequence;
- logical dimensions, pixel scale and theme choice;
- PNG pixels whose IHDR dimensions are verified before host bitmap decoding;
- source IDs and bounds for the root and unambiguous named root-namescope controls.

The viewport is limited to 4096 logical units per axis, scale 0.25–3 and four megapixels. PNG payloads are capped at eight MiB; the enclosing wire frame retains its independent limit. There are at most 10,000 mapped nodes and 128 input events per batch. Only consecutive pointer moves coalesce; down/up/key events retain order. Overflow requires an input reset instead of silently losing a release.

Pointer/button/wheel, physical key names, text and focus/capture resets are synthesized in the worker. A reset can be delivered independently of a source revision, including when a tab is hidden or source is invalid. Every capture and input batch otherwise requires a matching source hash. Stale/mismatched frames cannot drive visual edits. Failed runtime source updates leave the previous valid runtime root available; the host dims obsolete pixels and disables edits until a current frame arrives.

The workbench polls at a nominal 100 ms interval with at most one capture request in flight. It is not a hard realtime or 60/120 Hz compositor guarantee. `RenderTargetBitmap` captures through Skia to PNG; no shared GPU texture, dirty-tile streaming, video encoder or platform-specific compositor handle is claimed. The capture rate and binary budgets intentionally prioritize bounded behavior over maximum video throughput.

### Runtime authoring boundaries

Named runtime controls can be selected using source-linked bounds. Canvas movement and eight-handle size edits write ordinary targeted XAML transactions with the source version captured at gesture start. Explicit transformed elements/ancestors are refused rather than incorrectly converting their axis-aligned world bounds into layout coordinates. Full unnamed/template-generated control correspondence, multi-selection runtime group transforms, automatic user Application resource initialization and remote native-window integration remain incomplete. Safe artboards continue to provide their own richer supported multi-selection workflows.

```csharp
// A host opts into rendered preview while retaining process ownership and cancellation.
IRenderedExternalPreview preview = await StartTrustedPreviewAsync(request, cancellationToken);
var frame = await preview.RenderAsync(new PreviewViewport(960, 620, 1.5),
    RenderedPreviewFrame.HashSource(source), cancellationToken: cancellationToken);
frame.Validate();
renderedSurface.Attach(session);
renderedSurface.Present(frame);
// Present refuses mismatched source and out-of-order frames. Dispose the surface/preview with its host.
```

`StartTrustedPreviewAsync` is the embedding host's factory, not a built-in static method. The desktop's ExternalPreviewSession is the reference implementation.

## Path command and numeric model

`ProDesigner.Authoring.VectorPathModel` parses M/L/H/V/C/S/Q/T/A/Z and relative variants, repeated/implicit segments, scientific notation, compact arc flags, SVG radius correction and Avalonia F0/F1 prefixes. H/V become Line, S becomes Cubic with an explicit reflected control, and T becomes Quadratic; Arc remains an analytic endpoint-parameterized segment. Canonical output preserves geometry rather than original path token spelling. Surrounding XAML is preserved by the source transaction engine.

`VectorPathOperations` provides evaluation, subdivision and affine transforms. Cubic/quadratic subdivision uses de Casteljau; arc subdivision uses the ellipse center/sweep. Elliptical arcs transform through the affine ellipse matrix; reflection reverses sweep. Singular projections of a nondegenerate ellipse are explicitly refused because an endpoint-only line would discard the projected trace. Flatten explicitly before a singular projection. Radii correction uses log-space ratios for extreme eccentricity; finite coordinate/radius/segment/depth budgets are enforced.

```csharp
var path = VectorPathModel.Parse("F1 M10 30 C30 0 80 0 90 30 S150 60 170 30 A40 25 20 0 1 240 60 Z");
VectorPathOperations.Split(path, index: 1, t: 0.5);
var transformed = VectorPathOperations.Transform(path, new VectorTransform(1, .2, 0, 1, 10, 20));
string avaloniaData = transformed.ToData();
var contours = VectorPathOperations.Flatten(transformed, tolerance: 0.25);
```

The model uses double precision and G17 serialization. The flatten tolerance uses adaptive quarter/midpoint checks and a depth/point budget; it is not a formal exact Hausdorff bound for every degenerate input.

## Boolean geometry and stroke outlining

The independent `ProDesigner.Geometry` library uses Skia path operations for Union, Intersect, Difference and XOR, filled-region tests, and stroke-to-fill outlining. It supports explicit cap/join/miter choices. Skia uses single-precision path coordinates; these operations are not exact-arithmetic CAD Booleans. Native results may canonicalize arcs/conics to curves. Fill rules are carried into generated Avalonia data.

The Design tool header and command palette expose union, intersection, subtract, XOR and stroke outline. Select sibling Path elements in one Canvas. Operations convert their local placement into common parent coordinates, retain the first operand's style/name, and replace only the required source spans. Removing another named operand is refused when supported external references point to it. Linked instances, nontrivial layout stretches/margins/transforms and dashed stroke expansion are refused by the workbench adapter; the geometry library can be embedded with a host-specific normalization policy.

The vector editor adds an arc tool, segment selection, midpoint subdivision and radius/rotation/flag editing alongside point/Bézier handles. Compound path topology editing, node type conversion, pen-tool gestures, mesh deformation and a comprehensive Figma vector interaction model are not yet complete.

## Journal review, recovery and redo

Desktop startup now scans journal metadata, not project target contents. An interrupted Prepared journal can surface a status notification. **Refactoring journals / recovery** lists receipts and target paths; selecting Review explicitly authorizes reading those paths. Before/after views and per-file states distinguish Original, Applied, Conflict and Missing. Restore is disabled until every file matches an approved transaction state; the operation rechecks before changing anything. No journal or workspace grants project execution trust.

`JournaledFileTransaction.ReplayAsync` reopens a Reverted receipt and creates a new checked transaction. Exact byte hashes detect encoding-only external modifications as well as text changes. The UI exposes Redo project rename and preserves the distinction between unsaved buffers and saved disk baselines. A fresh Apply invalidates the session redo stack. After restart, the durable journal review can restore a receipt independently of the session-local UI history.

```csharp
var store = new JournaledFileTransaction(journalDirectory);
var summaries = await store.ListAsync(cancellationToken: cancellationToken);
// Present summary paths for explicit approval before reading target contents.
var review = await store.ReviewAsync(receipt, approvedPaths, cancellationToken);
if (review.CanRestore) await store.RevertAsync(receipt, approvedPaths, cancellationToken);
if (review.CanReplay) receipt = await store.ReplayAsync(receipt, approvedPaths, cancellationToken);
```

The journal is a corruption-checked local recovery record, not a signed authority. Per-file replacement is atomic; multi-path visibility is not. Uncooperative external writers can still race a last check. Symlink paths are refused, and Windows/Unix filesystem semantics remain subject to the documented host constraints.

## Incremental source and safe preview behavior

Syntax snapshots no longer expose mutable child/attribute lists. Each syntax node has a process-local Identity distinct from its structural source-path Id. Existing attribute-value edits validate in a bounded isolated XML attribute reader, shift source spans and retain identities without rescanning the entire document. Namespace/XML directives, structural edits, quote-breaking replacements or unsupported cases fall back to a complete parse. A full parse reconciles surviving mapped nodes and unique root-scope names conservatively; it does not guess ambiguous template names.

This is not an immutable green/red compiler tree with structural sharing: unchanged metadata is currently copied, and identity does not survive an unrelated fresh parse or application restart. Existing workspace document IDs remain the persisted identity used by components and prototypes.

Safe previews compare complete source equivalence around changed attributes. Existing supported scalar values can be applied to retained actual control instances, preserving unrelated interactive state. Names/classes/resources/bindings/property removal/structure, changed profiles/sample data and animation-reset requirements cause a full rebuild. Failed scalar application falls back to the normal diagnostic-building path rather than leaving an advertised partial success. Full XamlX transformation/binding and runtime project updates are not incrementally compiled.

`DesignerSession.LastSyntaxUpdate`, `DesignSurface.LastRefresh`, `FullBuildCount` and `PropertyDeltaCount` expose which path was used. They are measured operation counters, not a claim of a specific frame-time improvement on arbitrary solutions.
