# Changelog

## 0.2.0-alpha.1

- Add reusable Authoring, Persistence and PreviewProtocol packages (13 libraries total).
- Move desktop project preview to a supervised child process, with current-user named-pipe IPC, timeout termination, live XAML updates and explicit trust.
- Add versioned/checksummed complete workspaces, local two-generation desktop/browser recovery and conflict-aware atomic desktop XAML saves.
- Add namespace/namescope-aware editing, token-aware Binding/Reference rewrites, imported namespace hoisting and selection reconciliation through source edits.
- Add resource/style/ControlTheme and template-composition tools, RGBA/gradient brushes, JSON sample data, and editable line/quadratic/cubic Path geometry.
- Add scalar/custom-spline and hexadecimal color animation import, color tracks, timeline undo/redo and persistence.
- Virtualize/collapse the layer outline, retain a solution view list, and connect loaded-project members to editor completion/property inspection.
- Expand unit, actual worker-process, Avalonia headless and browser recovery/interaction regression coverage.

This release does not claim full professional-designer parity. See docs/capabilities.md for current boundaries.
