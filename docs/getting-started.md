# Getting started

## First design session

Launch the desktop or browser host. The workspace opens Dashboard, SignIn and Settings samples. The left sidebar switches between Layers and Assets. Choose a view in the navigator or tab strip; each view owns its source and undo history. The center hosts real Avalonia device artboards. The right inspector edits the selected element. The lower panel contains XAML, Animation, Problems and History tabs; drag the splitters to adjust the workspace.

Select `RevenueCard` in the outline to try dragging a Canvas child. Drag its lower-right handle to resize. Movement snaps to an eight-unit grid and nearby object edges/centers. Arrow keys move one unit; Shift+arrow moves ten. Automatic-layout children are not silently converted to absolute positioning: use Margin, Grid.Row/Column, alignment, or the parent layout properties instead.

The Assets palette inserts controls into the selected or nearest supported layout container. Inserted controls receive a unique x:Name. Undo restores the original source. Shift-click adds/removes a selection, and dragging empty artboard space can select intersecting Canvas children. Alignment and distribution require siblings in one Canvas.

## Editing XAML

Type in the source editor. Syntax checks and preview refresh are debounced. Comments, whitespace, attribute ordering and quote style survive property edits. A malformed document stays in the editor; the last valid preview remains visible and visual tools refuse changes until the error is repaired. The Problems tab contains source and preview diagnostics.

Ctrl/Cmd+F opens find/replace. Ctrl/Cmd+Space opens catalog completion. Clicking an element in the source selects the innermost syntax node; canvas selection navigates to that element when the editor is not focused. Completion is a catalog-based helper, not full project-semantic XAML IntelliSense.

## Files and solutions

Open accepts multiple .axaml/.xaml files. Save uses the host file picker and exports the active XAML document. Browser file access is limited to files chosen by the user. Companion C# code is saved separately from its editor. Animation tracks are session-local until exported into XAML; importing existing animation XAML back into tracks is not implemented yet.

On desktop, Solution accepts .sln, .slnx and .csproj. Confirm workspace trust before MSBuild evaluation. A local SDK and restored/evaluable project environment are needed. Failed metadata or project resolution is surfaced through diagnostics; the app does not claim to resolve missing proprietary SDKs or dependencies it cannot access. The initial solution explorer lists discovered views; it is not yet a persistent, virtualized IDE project tree.

Use the command palette's trusted runtime preview only for trusted XAML. This standalone mode can render more Avalonia features through its runtime loader, but does not automatically compile and load the solution's custom controls or code-behind.

## Animation

Select a named control. In Animation, choose a numeric property, enter a value and add a key at the playhead. Click or drag the timeline ruler to scrub; add another key at a later time. Play uses the selected duration; Loop repeats. Right-click near a key to remove it. Export XAML inserts Style/Animation markup into the current root's Styles property.

The first version exports scalar key values and cues; it does not yet serialize per-segment easing faithfully. Avoid assuming that exported playback matches non-linear preview easing. Existing animation import, color/transform tracks and advanced choreography are tracked in the capability matrix.

## Keyboard reference

| Shortcut | Action |
| --- | --- |
| Ctrl/Cmd+K | Command palette |
| Ctrl/Cmd+O | Open XAML files |
| Ctrl/Cmd+S | Save active XAML |
| Ctrl/Cmd+Z | Undo |
| Ctrl/Cmd+Shift+Z / Ctrl/Cmd+Y | Redo |
| Ctrl/Cmd+D | Duplicate selected visual roots |
| Ctrl/Cmd+C / V | Internal designer copy/paste outside text editors |
| Delete / Backspace | Delete visual selection outside editors |
| Arrow / Shift+Arrow | Nudge Canvas children by 1 / 10 units |
| Ctrl/Cmd+wheel | Zoom |
| Escape | Close dialog / leave interactive mode |

The inspector, editor, and desktop file picker retain their text-entry behavior. Not every shortcut from Figma, Blend, Visual Studio or Xcode is implemented.
