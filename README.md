# TFWR Folders

BepInEx mod for The Farmer Was Replaced: real subfolders for your scripts, plus an in-game IDE
(file tree, framed editor, live console).

## What it does

- Loads `Saves/<save>/**/*.py` (any depth, hidden folders skipped) into code windows at load.
- Saves every open window back to its own folder - root scripts stay root, folder scripts stay put.
- Adds a floating IDE window (toggle **F10**) in a VS Code-like layout: our own syntax-coloured
  editor in the middle, a Project explorer you can put left or right, a collapsible Terminal and
  a status bar. While it is open it is the only visible editor - the game's windows are hidden
  and restored exactly when you close it. The chrome is opaque.
- Watches subfolders too, so external edits hot-reload into the matching window.

## Install

1. Install **BepInEx 5.4.23.5 x64** into the game folder if it is not there already
   (`BepInEx\core\BepInEx.dll`, `winhttp.dll`, `doorstop_config.ini` next to the game exe).
2. Copy `TFWRFolders.dll` to:

   ```
   The Farmer Was Replaced\BepInEx\plugins\TFWRFolders\TFWRFolders.dll
   ```

3. Start the game and check `BepInEx\LogOutput.log`:

   ```
   [TFWR Folders] [Folders] Harmony patches applied.
   [TFWR Folders] [Folders] game=0.1 engine=6000.0.43f1 ...
   ```

First-run verification steps: [docs/testing.md](docs/testing.md).

## Quick start

1. Load a save and press **F10** - the IDE opens.
2. **New folder** - `utils`.
3. Select a root script in the tree, click **Move**, enter `utils`, OK.
4. Click **Save**. The `.py` file is now on disk in `utils\`; the window name (and `import`) did
   not change.

## Usage

- **F10** - open/close the IDE.
- **Drag the title bar** to move the window; **drag the bottom-right corner** to resize.
  Position and size are saved in `tfwr.folders.cfg` and restored next launch.
- **Editor** - our own editor with syntax colours; click to place the caret and type (characters,
  Backspace/Delete, arrows, Home/End, Enter with indent, Tab, Ctrl+V). There is no text selection
  or IME input. The wheel scrolls the code; the map zooms only when the pointer is outside the
  IDE window.
- **Completion** - pops up as you type a word of two or more characters, or on **Ctrl+Space**.
  Up/Down move, Enter/Tab or a click inserts, Esc closes. Names come from the save's
  `__builtins__.py`, the identifiers already in the file and the language keywords.
- **Project explorer** - collapsible, on the right by default (`Explorer side` config). Its header
  holds the file icons: new file, new folder, collapse/expand all, delete and menu, right-aligned
  before the collapse chevron. A 24 px column inside the Project panel, under its header, holds
  run/stop, save, terminal and hide; it hides with the panel. Drag a file row onto a folder header,
  or onto any file inside that folder,
  to move it; drop it on the editor to open it, or on an empty spot of the tree to move it back
  to the root. Right-click any row for the same actions.
- **New file** - created in the root unless a folder row is selected; select a folder and the
  dialog is prefilled with it. A selected file counts as no folder.
- **`.farmignore`** appears in the explorer when present (or create it from the menu). It opens
  in the editor as plain text and saves back to the file; the tree re-reads the rules.
- **Terminal** - bottom panel with a `Terminal` header, live output and Clear; toggle from the
  toolbar or the panel chevron. Right-click it for Copy output and Clear.
- The terminal shows `quick_print` results from `output.txt` plus the game's log lines
  (`print`, errors and `In:` traces). Game errors are marked `[Error]`; warnings `[Warn]`.
- **Problems** - the `Problems` button switches the same panel to a list of script errors with
  file and line, taken from each open script's parse error and the game log. Clear empties both.
- The IDE hides itself while the game's Escape menu is open and returns when you close the menu.

## Rules

- **A script's identity is its file name without folder.** `utils/hay.py` is the window `hay`,
  so `import hay`, `from hay import x` and `hay.func()` keep working unchanged.
- **Names must be unique across the whole save** (root + folders, case-insensitive). That is what
  makes `import` unambiguous.
- Root scripts behave exactly as vanilla - including the game's rule that closing a root window
  and saving deletes that file.
- Folder scripts are **not** deleted when you close their window; the file stays on disk and
  reopens next session. Delete them with the panel.
- `__builtins__.py` is written by the game at the root and is never shown or touched.
- Window layout (position, size, dock, minimised) is restored from `save.json` like vanilla.

> [!WARNING]
> If two files share a name, the root file wins: the folder copy is skipped with a warning popup,
> a red status line, and details in `LogOutput.log`. Move the root file into the folder instead of
> copying it. The panel refuses to create, rename to, or move onto a taken name.

## Import

```python
import hay        # ok - window name
hay.run()         # ok

import utils.hay  # not supported: the game's parser only accepts identifiers
```

Only plain names are importable. Autocomplete lists window names, i.e. the file names.

## Configuration

`BepInEx\config\tfwr.folders.cfg`:

```
[General]
Toggle key = F10
Tree width = 210
Console height = 170
Explorer side = Left
Debug logs = false

[Window]
X = 0
Y = 0
Width = 900
Height = 600
```

## Ignoring files (`.farmignore`)

Create `Saves/<save>/.farmignore` to keep non-script folders out of the IDE and out of the
folder scan (the game ignores dotfiles anyway):

```
# comments start with #
docs/
img/
*.md
farm/tmp_*
```

- `docs/` - the folder and everything below it.
- `*.md` - by file name, at any depth.
- `farm/**` - anything under `farm`.
- Hidden folders (`.venv`, `.idea`,  - ) are always skipped; no pattern needed.
- Ignored files are **not deleted** - they are only hidden from the IDE and not loaded as scripts.
- Root `.py` files are still loaded by the game; the tree hides them if a pattern matches.

## Backup

Close the game, then:

```powershell
$root = "$env:LOCALAPPDATA\..\LocalLow\TheFarmerWasReplaced\TheFarmerWasReplaced"
robocopy "$root\Saves" "$root\scratch\backups\Saves_pre_mod" /E /XD .venv .git __pycache__
```

> [!NOTE]
> The game also snapshots the whole save folder before every save
> (`Backup/<save>_backupN/`), and that copy is recursive, so it includes your folders.

## Architecture

```
TFWRFolders/
  Plugin.cs           BepInEx entry point, config, build-identity check
  Patches.cs          Harmony patches + save/load/watcher glue
  FolderMap.cs        pure logic: leaf-to-folder map, disk scan, .farmignore (tested)
  IdeLayout.cs        pure layout math for the window regions (tested)
  CodeEditor.cs       custom TMP editor (syntax overlay) bound to a hidden CodeWindow
  IdePanel.cs         uGUI IDE: editor, explorer, terminal, status bar
TFWRFolders.Tests/
  Program.cs          assert harness for FolderMap, IdeLayout and IgnoreRules
```

Save layout:

```
Saves/<save>/
  main.py             root script (vanilla behaviour)
  __builtins__.py     game stub, untouched
  utils/
    hay.py            folder script (window name: hay)
```

Deep dive: [docs/architecture.md](docs/architecture.md).

## Build

```
dotnet build TFWRFolders\TFWRFolders.csproj -c Release
dotnet run --project TFWRFolders.Tests\TFWRFolders.Tests.csproj -c Release
```

The `GameDir` MSBuild property defaults to the development Steam path; override with
`-p:GameDir="C:\path\to\The Farmer Was Replaced"`.

## Compatibility

Tested against: game **0.1** (`Application.version`), Unity **6000.0.43f1**, Steam build
**26462009** (2026-11-09), Core.dll SHA-256 `F0EFDA7C138DE8028B1769C121A4B5C17B6BB538AEAE5A67D1881E7C4EBD7D25`.

> [!NOTE]
> If a game update changes the patched methods, the mod logs a warning and saving falls back to
> the vanilla root-only writer. Folder files then stay stale but are not lost. Check
> `LogOutput.log` after game updates.

## Uninstall

Delete `BepInEx\plugins\TFWRFolders\TFWRFolders.dll` (and optionally the config file).
Folder files simply become invisible to the game again - they are **not** deleted. Move them back
to the save root for the vanilla layout.

## Notes

- The editor is our own (TMP input + syntax-coloured overlay) bound to a hidden game window used
  only for parsing, running and saving. The game's editor windows are never displayed while the
  IDE is open.
- All IDE surfaces are opaque; the only translucent part is the dimmed backdrop of a rename/delete
  dialog and of the context menu.
- The IDE hides while the Escape menu is open (and logs it) and comes back when the menu closes.
  An open context menu or drag closes with it.
- While the editor is active the game's input handler is disabled, so typing does not move the
  camera or trigger shortcuts. Clicking outside the editor restores it.
- The wheel over the IDE scrolls the code or the Project list; the map zooms only outside the
  IDE window.
- The Project list scrolls with the wheel only; dragging inside it does not scroll the list.
- Editor keys are handled directly (no EventSystem keyboard path). Text selection and IME are not
  supported yet.
- Dragging files from Windows Explorer into the game is not possible without a native plugin. Put
  the file in the save folder; the tree picks it up within 1.5 s.
- Editor scroll is mouse-wheel only; there is no scrollbar or line-number gutter (the status bar
  shows `Ln/Col`).
- The IDE chrome uses Unity's built-in UI font; the editor uses the game's code font (FiraCode)
  when it can be read from the CodeWindow prefab. Icon glyphs come from the same built-in font.
- `.farmignore` edits are written after a 600 ms pause and on F10, so closing the IDE right after
  typing does not lose them.
- Saving while the IDE is open stores the hidden windows as usual; no window positions are
  changed by the IDE.
