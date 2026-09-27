# Security model

## Untrusted documents

The safe preview parses bounded XML with DTD processing prohibited and no XML resolver. It checks a maximum source length and nesting depth before passing source to the XamlX parser. PreviewBuilder instantiates a fixed catalog of built-in controls, not arbitrary type names, converters, events or markup-extension constructors. It does not load external images or download resources.

XAML text is still untrusted input. Large numeric layout values, unusual Unicode and malformed constructs require continued fuzzing and resource-budget hardening. The initial release is not a certified sandbox. Report failures with a minimal non-sensitive sample.

## Trusted workspace evaluation

MSBuild evaluation can execute imported tasks/targets. The desktop UI therefore requires an affirmative trust action before opening a .sln, .slnx or .csproj through MSBuildWorkspace. This is permission to evaluate that workspace for the current action, not a claim that its code is safe. Open XAML files alone for non-executing inspection.

## Runtime preview

The default desktop runtime preview runs in a supervised child process after an explicit trust action. Build/restore also runs SDK subprocesses with bounded logs, cancellation and timeout termination. IPC uses a per-session random, current-user-only named pipe with length-prefixed JSON frames capped at 16 MiB; application stdout/stderr is drained separately and never parsed as protocol data. A blocked/crashed worker can be terminated without executing preview constructors inside the workbench process. In-process RuntimePreviewEngine remains a reusable library API for hosts that deliberately choose it.

**Process isolation is not an OS permissions sandbox.** The child and project build have the user's filesystem/network privileges and can execute arbitrary constructors, markup extensions, converters, code-behind and imported MSBuild targets. Do not trust unknown projects. Collectible assembly contexts are lifecycle aids, not security boundaries. The browser has neither the runtime worker nor project-code execution.

## Recovery and file writes

Workspace envelopes have SHA-256 integrity checks and a versioned source-generated JSON schema. Checksums detect corruption; they do not authenticate a workspace author. Workspace import and recovery never restore a trust grant or automatically load assemblies. Source text, code-behind and document/project paths may be present in the local journal; treat `.prodesigner` files as sensitive project data.

Desktop recovery uses two generations and atomic same-directory replacement. Desktop XAML saves compare an expected content hash and use a stable advisory lock for cooperating designer writers. This is optimistic conflict detection, not a filesystem-wide compare-and-swap against arbitrary applications that ignore the lock. Browser local-storage quota errors are surfaced. A power failure or browser termination can lose changes since the last checkpoint. The browser exposes workspace import/export and local editing commands to same-origin scripts; do not co-host untrusted scripts.

## Local data

File import/export uses user-selected files. Recovery snapshots remain local. There is no telemetry, collaboration server, remote upload or credential store in the application. The WebAssembly runtime's downloaded application resources are static site assets. The browser automation bridge exposes local document commands to scripts on the same origin; do not host unrelated untrusted scripts with the app.

## Automation

CI pull requests use read-only repository permissions. Publishing jobs need only contents write (releases) or Pages/id-token permissions (Pages). NuGet credentials are referenced only by the release job. Fork PRs do not receive release secrets. No workflow extracts or prints tokens.

Use the repository's private security reporting feature where available. Do not put credentials, customer solutions, private package URLs or sensitive local paths in public issue reports.
