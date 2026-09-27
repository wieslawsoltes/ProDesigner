# Platform validation and deployment architecture

The automatic desktop CI matrix runs the same complete suite on Windows x64, Linux x64 and macOS Intel (`macos-15-intel`). It includes actual SDK/MSBuild graph tests, file transactions, runtime previews, pointer input and modal keyboard behavior. The browser is built and exercised separately in Chromium.

Apple Silicon runner jobs using `macos-latest` and then `macos-26` remained queued during v0.3 development. The macOS test suite was moved to the supported standard Intel host; tests were not removed or marked skipped. Consult the final PR run for the actual result rather than interpreting an earlier queued/cancelled run as a pass.

The release matrix produces `win-x64`, `linux-x64`, `osx-x64` and `osx-arm64` self-contained managed/JIT distributions. The ARM64 apphost and runtime assets are cross-published with the .NET SDK on the Intel macOS host; this is not Native AOT. A successful ARM64 publish is not evidence of native Apple Silicon runtime testing. This distinction remains part of the release qualification record.

Contributors can run the complete suite directly on an Apple Silicon development machine:

```sh
git submodule update --init --recursive
dotnet build ProDesigner.slnx -c Release
dotnet test tests/ProDesigner.Tests -c Release --no-build
```

Signing, notarization, complete native-platform accessibility checks and large real-solution performance acceptance are separate outstanding qualification tasks. No workflow weakens the protected Pages environment or grants project execution trust from imported workspace data.
