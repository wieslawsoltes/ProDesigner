# Security model

## Untrusted documents

The safe preview parses bounded XML with DTD processing prohibited and no XML resolver. It checks maximum source length and nesting depth before the XamlX parser. PreviewBuilder instantiates an explicit catalog of built-in controls, not arbitrary types, converters, events or markup-extension constructors. It does not load external images or download resources.

XAML remains untrusted input. Numeric layout extremes, unusual Unicode and malformed constructs require continued fuzzing and resource-budget hardening. An alpha build is not a certified sandbox. Report failures with minimal non-sensitive samples.

## Trusted workspace evaluation

MSBuild evaluation can execute imported tasks/targets. The desktop requires affirmative trust before opening .sln/.slnx/.csproj through MSBuildWorkspace. This permits evaluation for the action, not a declaration that project code is safe. Open individual XAML files for non-executing inspection.

## Runtime preview

The default desktop runtime preview runs in a supervised child process after explicit trust. SDK restore/build has bounded logs, cancellation and timeouts. IPC uses a random per-session, current-user-only named pipe with length-prefixed JSON frames capped at 16 MiB. Application stdout/stderr is drained separately and is never parsed as protocol data. Blocked/crashed workers can be terminated without loading preview constructors into the workbench. In-process RuntimePreviewEngine remains available to hosts deliberately choosing it.

**Process separation is not an OS permissions sandbox.** The worker/build retain the user's filesystem and network permissions and can execute constructors, extensions, converters, code-behind and imported build targets. Collectible load contexts are lifecycle aids, not security boundaries. The browser has neither project evaluation nor runtime project execution.

## Components and prototypes

Component definitions are source data. Their safe previews use the same catalog as ordinary documents. Generated instance names and supported references are remapped without interpreting application code. Propagation refuses manually edited source conflicts; detaching retains the XAML. Component import does not grant runtime trust.

Prototype operations are a bounded fixed set: navigate/back, overlays and string variables. Conditions perform string equality and delays use a monotonic host clock. There is no script evaluation, URL navigation, process launch, remote upload or arbitrary extension execution in the prototype player. Same-origin application scripts can invoke the exposed automation bridge; do not co-host untrusted scripts.

## Workspace recovery

Workspace envelopes have SHA-256 integrity checks and a source-generated versioned schema. Checksums detect corruption, not authorship. Import/recovery never restore trust grants or automatically load assemblies. Source/code-behind, undo snapshots, component definitions, sample data and local project paths can be present; treat `.prodesigner` files and journals as sensitive project data.

Desktop workspace recovery keeps two generations using same-directory replacement. XAML saves compare an expected content hash and use an advisory lock for cooperating writers. This is optimistic concurrency, not filesystem-wide compare-and-swap against arbitrary applications. Browser quota failures are surfaced. Changes since the last checkpoint can be lost during termination or power failure.

## Cross-file refactoring and durable journals

Preparation uses Roslyn symbols and targeted supported XAML references without modifying files. The desktop UI requires a reviewed plan before Apply. It validates open buffers and the on-disk source from which the plan was prepared. File application records both original and replacement bytes before starting replacements, preserving supported BOM encodings and Unix permissions. Journals use owner-only file permissions on Unix; on Windows they inherit the enclosing user profile ACL. Encryption at rest is not implemented.

Each file replacement is atomic; the collection of replacements is **not** an atomic filesystem-wide operation. External processes can observe intermediate results or race the final content check. A failure triggers rollback only while current file hashes still match the transaction. Later external edits are not intentionally overwritten. Conflicting rollback errors preserve the journal for manual/API recovery rather than fabricating a successful rollback.

Recovery APIs require a receipt from the configured journal root, an explicit allowed-path set and valid before/after checksums. Relative paths and symlink traversal are rejected. The UI's last-rename history remains session-local. Startup discovery now reads only journal metadata; target reads require an explicit Review action. Before/after review and conflict-aware recovery are available, and redo creates a new journal with exact-byte preconditions. A host should inspect and present journal targets before authorizing recovery. Checksummed journal files are not a trusted command source.

## Local data and automation

File access uses user-selected projects/documents. Recovery remains local. There is no telemetry, collaboration server, credential store or automatic project upload. The WebAssembly runtime downloads only static application assets.

PR CI uses read-only repository permissions. Release publishing needs contents write; Pages uses pages/id-token permissions and the existing protected environment. NuGet credentials are referenced only by the release publishing job; fork PRs do not receive them. No workflow extracts or prints secrets. The temporary source-integration workflows used during development are removed from the delivered branch.

Use private security reporting where available. Never include credentials, customer solutions, sensitive local paths or private package tokens in public issue reports.

## Embedded pixels, native geometry and incremental source validation

The embedded renderer accepts bounded PNG frames from the already trusted preview worker. The frame includes an exact source hash, ordered sequence, viewport and named-control map. Validate checks PNG signature/IHDR and the negotiated four-megapixel maximum before image decoding. PNG bytes are limited to eight MiB, node count to 10,000 and input batches to 128. Stale geometry cannot target newer source. Input reset works independently of source state, including hidden views. This does not make a malicious trusted project harmless: its worker already has the user's privileges.

Vector Boolean and stroke-outline operations invoke the bundled native Skia library over bounded, validated path models. Their single-precision topology is not an exact-arithmetic guarantee; native-library security updates remain part of dependency maintenance.

Incremental existing-attribute validation parses an isolated XML attribute and checks its exact shape. Quote-breaking, namespace, XML-directive and structural edits fall back to full document parsing. Tests compare full and incremental spans across randomized source edits. The fast path does not bypass DTD/size/encoding validation and never evaluates a markup extension.
