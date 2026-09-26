# Security model

## Untrusted documents

The safe preview parses bounded XML with DTD processing prohibited and no XML resolver. It checks a maximum source length and nesting depth before passing source to the XamlX parser. PreviewBuilder instantiates a fixed catalog of built-in controls, not arbitrary type names, converters, events or markup-extension constructors. It does not load external images or download resources.

XAML text is still untrusted input. Large numeric layout values, unusual Unicode and malformed constructs require continued fuzzing and resource-budget hardening. The initial release is not a certified sandbox. Report failures with a minimal non-sensitive sample.

## Trusted workspace evaluation

MSBuild evaluation can execute imported tasks/targets. The desktop UI therefore requires an affirmative trust action before opening a .sln, .slnx or .csproj through MSBuildWorkspace. This is permission to evaluate that workspace for the current action, not a claim that its code is safe. Open XAML files alone for non-executing inspection.

## Runtime preview

The desktop runtime preview invokes Avalonia's runtime XAML loader after a separate warning. It runs inside the desktop process and can execute XAML constructors/extensions. It is **not process-isolated**. Do not use it with untrusted files. The browser does not expose this capability. Building and loading a user's custom-control assemblies is not implemented in the initial host.

## Local data

File import/export uses user-selected files. There is no telemetry, collaboration server, remote upload or credential store in the application. The WebAssembly runtime's downloaded application resources are static site assets. The browser automation bridge exposes local document commands to scripts on the same origin; do not host unrelated untrusted scripts with the app.

## Automation

CI pull requests use read-only repository permissions. Publishing jobs need only contents write (releases) or Pages/id-token permissions (Pages). NuGet credentials are referenced only by the release job. Fork PRs do not receive release secrets. No workflow extracts or prints tokens.

Use the repository's private security reporting feature where available. Do not put credentials, customer solutions, private package URLs or sensitive local paths in public issue reports.
