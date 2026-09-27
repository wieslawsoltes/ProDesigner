# Platform validation and deployment architecture

The desktop CI matrix retains the same complete suite on Windows x64, Linux x64 and macOS Intel (`macos-15-intel`). It includes actual SDK/MSBuild graphs, file transactions, runtime previews, pointer input and modal keyboard behavior. The actual browser application is published and exercised independently in Chromium.

Apple Silicon runner jobs using `macos-latest` and `macos-26`, followed by `macos-15-intel`, remained queued during v0.3 development. The macOS test job is retained with no tests removed or skipped. Consult the final PR/main run for its real status: queued or cancelled is not a pass. An earlier macOS run exposed a test dispatcher-initialization race; the affected KeySpline tests now use AvaloniaTheory so framework objects are created on the UI dispatcher.

## Packaging is distinct from platform execution

The release matrix produces `win-x64`, `linux-x64`, `osx-x64` and `osx-arm64` self-contained managed/JIT distributions. Both macOS packages are cross-published by the .NET SDK on a Linux host using their platform-specific apphost, runtime and NuGet native assets. This is not Native AOT and does not involve compilation of Apple-specific native source. A successful publish verifies packaging, **not native macOS or Apple Silicon execution**. This release does not infer macOS runtime validation from Windows/Linux/headless/browser results.

macOS downloads include this qualification note. Signing, notarization, native-platform accessibility checks and representative real-solution performance acceptance remain separate outstanding tasks. Native macOS validation can be run from the same source:

```sh
git submodule update --init --recursive
dotnet build ProDesigner.slnx -c Release
dotnet test tests/ProDesigner.Tests -c Release --no-build
```

The main/PR macOS job remains independent of the portable release-package builder. No workflow weakens the protected Pages environment or restores execution trust from imported workspace data. Exact tested commits and platform outcomes are recorded in PR/release validation, not implied by the existence of downloadable packages.
