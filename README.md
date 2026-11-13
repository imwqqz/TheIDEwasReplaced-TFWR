# TFWR Folders

BepInEx mod for The Farmer Was Replaced: real subfolders for your scripts, plus an in-game IDE
(file tree, framed editor, live console).

## What it does

- Loads `Saves/<save>/**/*.py` (any depth, hidden folders skipped) into code windows at load.
- Saves every open window back to its own folder — root scripts stay root, folder scripts stay put.
- Adds a floating IDE window (toggle **F10**) in a VS Code-like layout: our own syntax-coloured
  editor in the middle, a Project explorer you can put left or right, a collapsible Terminal and
  a status bar. While it is open it is the only visible editor — the game's windows are hidden
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

1. Load a save and press **F10** — the IDE opens.
2. **New folder** → `utils`.
3. Select a root script in the tree, click **Move**, enter `utils`, OK.
4. Click **Save**. The `.py` file is now on disk in `utils\`; the window name (and `import`) did
   not change.

## Usage

- **F10** — open/close the IDE.
- **Drag the title bar** to move the window; **drag the bottom-right corner** to resize.
  Position and size are saved in `tfwr.folders.cfg` and restored next launch.
- **Editor** — our own editor with syntax colours; pick scripts in the Project explorer.
  Run/Stop and Save act on the selected script. The game's code windows are never shown while
  the IDE is open; they remain only as run/save backends.
- **Project explorer** — collapsible, on the right by default (`Explorer side` config). Click a
  file to open it in the editor area; footer buttons: New folder, Refresh, Rename, Move, Delete.
- **Terminal** — bottom panel with `Terminal · Local` header, live output and Clear; toggle with
  the top-bar **Terminal** button or the panel chevron.
- **Run / Stop** — runs or stops the selected script (same as the window's run button).
- **Save** — full game save (progress + scripts).
- The terminal shows the game's real `print` / `quick_print` / error output, including `In:` traces.
- The IDE hides itself while the game's Escape menu is open and returns when you close the menu.

## Rules

- **A script's identity is its file name without folder.** `utils/hay.py` is the window `hay`,
  so `import hay`, `from hay import x` and `hay.func()` keep working unchanged.
- **Names must be unique across the whole save** (root + folders, case-insensitive). That is what
  makes `import` unambiguous.
- Root scripts behave exactly as vanilla — including the game's rule that closing a root window
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
Explorer side = Right

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

- `docs/` — the folder and everything below it.
- `*.md` — by file name, at any depth.
- `farm/**` — anything under `farm`.
- Hidden folders (`.venv`, `.idea`, …) are always skipped; no pattern needed.
- Ignored files are **not deleted** — they are only hidden from the IDE and not loaded as scripts.
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
Folder files simply become invisible to the game again — they are **not** deleted. Move them back
to the save root for the vanilla layout.

## Notes

- The editor is our own (TMP input + syntax-coloured overlay) bound to a hidden game window used
  only for parsing, running and saving. The game's editor windows are never displayed while the
  IDE is open.
- All IDE surfaces are opaque; the only translucent part is the dimmed backdrop of a rename/delete
  dialog.
- The IDE hides while the Escape menu is open (and logs it) and comes back when the menu closes.
- Editor scroll is mouse-wheel only; there is no scrollbar or line-number gutter (the status bar
  shows `Ln/Col`).
- The IDE chrome uses Unity's built-in UI font; the editor uses the game's code font (FiraCode)
  when it can be read from the CodeWindow prefab.
- Saving while the IDE is open stores the hidden windows as usual; no window positions are
  changed by the IDE.
