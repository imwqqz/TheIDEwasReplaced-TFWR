# Architecture

How the mod hooks into The Farmer Was Replaced (game 0.1, Unity 6000.0.43f1, Mono, Core.dll).
Everything lives in the BepInEx plugin; no game files are modified.

## Data flow

```
Saver.Load - postfix: scan Saves/<save>/**, open folder windows, build leaf - folder map
      - 
      - 
Workspace.codeWindows  (key = file name WITHOUT folder, e.g. "hay")
      - 
     - edit in game - CaptureCodeFiles - WriteCodeFiles prefix: write each window to
     -                                          <save>/<mapped folder>/<name>.py, prune map,
     -                                          then vanilla root cleanup
      - 
     - edit on disk - FileSystemWatcher (IncludeSubdirectories) - OnFileChanged
     -                    (name - leaf via GetFileNameWithoutExtension) - ApplyCodeChanges
      - 
     - import hay - ImportNode.Execute: codeWindows.TryGetValue("hay")
```

The game itself only ever enumerates the save's **top level** (`Saver.Load`,
`WriteCodeFiles` cleanup, the watcher). Subfolders are invisible to it without the patches.

## Patches

| Method | Type | What it does |
|---|---|---|
| `Saver.Load(MainSim)` | postfix | Clears/rebuilds the map, opens one window per subfolder `.py` (leaf-name key), restores position/size/scroll/minimised/dock from `save.json`, warns on name conflicts. |
| `Saver.WriteCodeFiles(code, builtins, savePath)` | prefix | If the map is empty - vanilla runs. Else writes each window to its mapped path with the game's atomic writer, prunes stale map entries, keeps `__builtins__.py` and root-cleanup semantics, skips the original. Any exception - log + run the vanilla writer. |
| `Saver.StartFileWatcher()` | postfix | Sets `IncludeSubdirectories = true` on the private watcher. |
| `Workspace.RenameWindow(window, name)` | postfix (with `__state`) | Carries the folder mapping across an in-game rename. |
| `Workspace.Scroll(float)` | prefix | Returns false while the mouse is over IDE chrome, so the game camera does not zoom under the UI. |

All patch bodies are wrapped in try/catch with `ManualLogSource` logging.

## Identity and collisions

- Identity = `Path.GetFileNameWithoutExtension`, so `utils/hay.py` becomes window `hay`.
  This keeps `import hay`, `hay.func()`, the import autocomplete, `save.json` keys and watcher
  matching working with **no** parser/import changes.
- Consequence: leaf names must be unique per save (case-insensitive). On load a root file wins
  over a same-named folder file; the loser is skipped, logged, and shown in the panel status.
- The panel checks disk + open windows before create/rename/move and refuses duplicates.

## Map lifecycle

`FolderMap` (thread-safe, `lock`):

- `Reset(saveDir)` at the start of every `Saver.Load` (save-slot switches cannot leak entries).
- `SetFolder` from load scan and from panel create/move; `Rename` on window rename.
- `PruneTo(code.Keys)` inside the write prefix - entries for windows that are not being saved are
  dropped, so a later root file with the same name can never be written into an old folder.
- `PathFor(leaf, saveDir)` - root path or `<save>/<folder>/<name>.py`.

`Scan`/`ListFolders` walk directories with a stack and prune hidden (`.venv`, `.git`,  - ) and
invalid folders **before** descending, so a Python venv inside a save costs nothing.

## Save/load concurrency

The game serialises writes through a `Task` (`Saver.QueueSnapshot` - `Task.Run`). The map is
therefore locked on every access; the panel only mutates it on the main thread. Panel file
operations are ordered: wait until `!Saver.IsSaving` - close the window - drop the map entry  - 
file operation.

## Editor

`CodeEditor` is a custom TMP editor, not the game's window:

- `TMP_InputField` (multiline) with its text rendered transparent so the caret and selection stay
  native; a `TMP_Text` overlay behind it draws `CodeUtilities.SyntaxColor2(text, theme)`.
- The overlay rect is copied from the input's text rect every frame, so wrapping and scrolling
  stay aligned; the editor scrolls with the mouse wheel.
- Text is pushed into the hidden `CodeWindow` 250 ms after typing stops, then `CodeWindow.Parse()`
  runs; a parse failure is reported through the window's private `errorString` into the status bar.
- External changes (file watcher) are polled every 0.4 s and applied when the editor is unfocused.
- The font is read from the game's `CodeWindow` prefab via reflection (fallback: TMP default).

## IDE window and window replacement

- `IdePanel` is a floating screen-space window: title bar drag, bottom-right grip resize,
  position/size persisted under `[Window]` in `tfwr.folders.cfg`.
- Regions (title, explorer, editor, terminal, status, separator, tool strip) come from
  `IdeLayout.Compute(...)`, a pure function with unit tests; the explorer side (`Right`/`Left`) and
  the strip width are inputs, so the config applies without a relaunch.
- All surfaces are opaque: shades are derived by multiplying the theme frame colour.
- **Explorer header icons**: new file, new folder, collapse/expand all, delete, menu; right-click
  on a row opens the same actions as a context list; the icons sit on the header row before the
  collapse chevron. Run/stop, save, terminal and hide live in a 24 px column inside the Project
  panel, at its right edge below the header; it hides with the panel.
- **Drag and drop**: a file row can be dropped on a folder header or on any file row inside that
  folder (move), on the editor (open) or on an empty part of the tree (move to root). Dropping on
  `.farmignore` is refused. `DragTarget.FolderFor` maps the row under the pointer to the receiving
  folder; one `MoveFile` path is shared with the Move dialog and is covered by the
  `TryMoveFile` unit test. The tree refresh is deferred while dragging so rows do not disappear
  under the cursor.
- **New file** goes to the root unless a folder row is selected; the prompt is prefilled with the
  selected folder. A selected file counts as no folder.
- **Typing**: keys are read directly from `Input.inputString`, with keycodes for arrows, Delete,
  Home/End, Ctrl+V and Ctrl+Space; a custom caret is drawn from `TMP_Text` line info and kept in
  view. TMP reports caret coordinates around the text centre, so `TextEdit.CaretAnchor` adds the
  rect pivot offset before placing the caret. The game's `InputBoss` is disabled while the editor
  is active. No EventSystem keyboard path is used.
- **Completion**: `Completion` (pure, unit tested) finds the word before the caret, ranks prefix
  matches before contains matches and replaces the word. `CodeEditor` renders up to eight rows
  under the caret; Up/Down/Enter/Tab/Esc and clicks are handled before normal typing. Names come
  from the save's `__builtins__.py` (parsed lazily and cached per save/file), the bound window's
  `tokenizedIdentifiers` and a keyword list.
- **Editor sync**: while typing, an external watcher change is ignored with a warning instead of
  being overwritten; idle external changes are applied. `.farmignore` is reloaded on mtime change
  when it has no pending edit.
- **Scroll isolation**: `Workspace.Scroll` returns false only while the pointer is inside the IDE
  window, so the map still zooms outside it. The editor scrolls with the wheel; the Project list
  ignores drag-scrolling and uses the wheel only.
- **.farmignore editing**: the file is listed in the explorer and opened in a raw-text mode of
  `CodeEditor` (no hidden window, no parser). Writes happen after 600 ms, on file switch and on
  F10; the tree re-reads the rules afterwards.
- **Terminal** shows the tail of `output.txt` in the game's persistent data folder (where
  `quick_print` writes) followed by `Logger.GetOutputString()` (reflection on `MainSim.logger`),
  polled every 0.4 s while visible and reloaded only when the file timestamp changes. Logger lines
  starting `Error:` / `Warning:` are marked `[Error]` / `[Warn]`. Clear calls `Logger.Clear()` and
  truncates the file. Its header has no subtitle and right-click opens Copy output / Clear.
- **Problems**: the `Problems` button swaps the panel body for a list built from each open
  window's private `errorString` plus the logger's `Error:` lines; window errors carry
  `file:line` from `errorStartIndex`. It is rebuilt on the same 0.4 s poll.
- While open, `EnforceSingleEditor` hides every code window **and docs window**
  (`SetActive(false)`, tracked in `touched`); windows created later are hidden on the next frame.
  The selected script is bound to the editor through its hidden window.
- Closing restores every touched window. Nothing is closed, deleted or re-saved by this.
- **Menu suppression**: when `MainSim.menu` is active, the IDE canvas is hidden, open modals are
  closed and F10 is ignored; closing the menu restores the pre-menu visibility. Each transition
  is logged once.

## .farmignore

`IgnoreRules` (pure, tested) parses `Saves/<save>/.farmignore`:

- one pattern per line, `#` comments, blank lines ignored, `/` separators;
- `*` matches inside a segment, `**` any depth, `?` one character;
- a pattern also matches everything below a folder it matches; no negation;
- patterns are matched against the relative path and the base name (case-insensitive).

`FolderMap.Scan` / `ListFolders` take the rules and prune ignored folders before descending, so
ignored scripts are not loaded as windows. The IDE tree applies the same rules, including to root
files (which the game still loads itself).


## Console

`MainSim`'s private `logger` field is read via reflection; `Logger.GetOutputString()` and
`Logger.Clear()` are public. Polled every 0.4 s, tail-truncated to 16 000 chars.

## Version gate

At startup the plugin logs `Application.version`, Unity version, exe FileVersion and the SHA-256
of `Core.dll`. The tested combination is pinned in the README. A mismatch is a warning, never a
hard failure.

## Failure modes

| Situation | Result |
|---|---|
| Patch throws | Logged; the original game method runs where applicable (save falls back to vanilla). |
| Name conflict | Root wins, folder copy skipped; popup + panel status + log. |
| Folder write fails | Error logged, vanilla writer runs (a folder copy may also appear at the root - visible, not silent). |
| Window placement fails | Warning logged, window focused in place. |
| Game update changes a patched method | `Harmony.PatchAll` throws at startup; the mod logs and stays inactive for that method; classic save behaviour remains. |
| Uninstall | Folder files remain on disk, invisible to the game; root files unaffected. |
