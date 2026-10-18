# Studio workspace redesign

Original design branch: `design/studio-workspace`. The Studio workspace is the default editor on `main`.

## Visual direction

Keep the existing warm dark palette: #1B1916 canvas chrome, #26231E panels, #F3EDE4 primary text, #A89B8C secondary text and #CE8349 active controls. Use compact toolbars, squared dock panels, restrained separators, clear active tabs and consistent control heights. The interface stays in English.

## Workspace layout

1. Top: application menu and a compact command toolbar for import, open, save, add elements, build and upload. Package details and recovery stay in their relevant menus/panels.
2. Left: Project Explorer with tabs for layers, image/sequence assets and package settings. Keep layer ordering and selection synchronized with the canvas. Background is an explicit selectable entry.
3. Center: a document tab, canvas toolbar with fit/zoom controls, the 368x448 watch artboard, a quiet work surface and an information footer. Preserve the actual artboard coordinate system and existing drag/resize behavior.
4. Right: Inspector tabs for text, images/sequences and background. Group transform, appearance and data controls; use collapsible sections and a scrollable property area rather than a long combined sidebar.
5. Bottom: a resizable dock with Timeline and Output tabs. Timeline has a sequence picker, data/loop/frame preview modes, transport controls, time/frame readout, ruler/playhead, horizontal frame filmstrip and frame alignment/edit commands. Other sequences appear as selectable tracks. Output retains all existing severity colors.
6. Status bar: current selection, project name, canvas dimensions and preview state. Vertical and horizontal splitters resize the main panels.

## Animation behavior

Use the existing 200 ms preview clock and exported interval. The filmstrip represents each frame's effective duration; data-driven tracks explicitly label their value-based selection and keep their mappings. Loop playback is an inspection override; single-frame selection pauses and scrubs the selected sequence. Selecting or scrubbing must never alter X/Y or offsets. Frame movement/resizing remains attached to the existing canvas gestures. Reordering frames must move their source artwork and independent offsets together.

Add previous/next/first frame, restart/play/pause, a draggable playhead, timeline scale, keyboard stepping outside text fields and selection synchronization among explorer, inspector, filmstrip and track list. Do not introduce unsupported arbitrary clip durations or keyframe export semantics.

## Implementation

- Reorganize `editor/MainWindow.xaml`, preserving all named existing controls and handler contracts.
- Extend `editor/App.xaml` with shared dock/tab/list/menu/button styles in the existing palette.
- Add a `StudioWorkspace.cs` partial for shell commands, panel synchronization, zoom and timeline presentation.
- Add narrow hooks to selection, frame refresh, playback and busy-state handling in `MainWindow.xaml.cs`, `Layers.cs` and logging.
- Retain saved project and watch-side format compatibility. No runtime/builder or upload protocol changes.
- Mirror shared editor source to the local checkout, preserving portable path discovery in the Git branch.

## Verification

Compile Release, instantiate and render the actual WPF window at normal and minimum sizes, inspect screenshots, and exercise selection, zoom, splitters, frame scrubbing, playback, reorder/alignment and busy-state behavior with a native harness. Open existing projects and check save/reopen/export preserves positions, fonts, layers, images and mappings. Run relevant existing regression checks. Never close the user's running editor, modify their saved project or upload to the watch. No commit or push is implied by this task.


## Implementation and verification

The first implementation is complete: IDE shell, explorer and inspector tabs, resizable panels, artboard zoom, dark tool chrome, bottom Timeline/Output dock, frame ruler/playhead, keyboard transport, frame drag/reorder, independent frame alignment, horizontal timeline scale and playhead following. All 75 existing named UI controls were retained. Time loops and value-driven frames remain distinct; the saved watchface schema and firmware module were not changed.

Both local and portable Release builds passed without warnings or errors. A native WPF harness instantiated the actual window, rendered normal/minimum layouts, opened a copied Pip-Boy project and verified track/inspector selection, 1/64-frame timeline behavior, pause/resume, scrub/zoom geometry preservation, frame artwork/offset preservation on reorder, edit guards while building and save/reopen. The real project exported successfully, passed package preflight and preserved stack/registers during 500 mocked ARM ticks. Original projects and watch binaries were not overwritten; no watch upload was performed. Validation images remain local temporary artifacts, outside version control.

The implementation and its visual plan are maintained in the main editor. The original design branch is retained in Git history.
