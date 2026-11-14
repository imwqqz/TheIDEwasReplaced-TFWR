# Testing checklist

Manual test for one game launch. Use a throwaway save; never test on a save you care about.

## Prerequisites

- BepInEx installed, game **closed** before copying the DLL.
- Plugin in `BepInEx\plugins\TFWRFolders\TFWRFolders.dll`.

## Prepare a throwaway save

Close the game, then (PowerShell, from the save folder):

```powershell
$root = "$env:LOCALAPPDATA\..\LocalLow\TheFarmerWasReplaced\TheFarmerWasReplaced"
$src  = "$root\Saves\the-farmer-was-replaced-main"   # any save you have
$dst  = "$root\Saves\foldertest"
New-Item -ItemType Directory -Force -Path "$dst\test" | Out-Null
Copy-Item "$src\save.json" $dst -Force
Copy-Item "$src\__builtins__.py" $dst -Force
Copy-Item "$src\main.py" $dst -Force
Copy-Item "$src\hay.py" "$dst\test\hay.py" -Force
```

Then create two small files (UTF-8, no BOM):

```powershell
$utf8 = New-Object System.Text.UTF8Encoding($false)
[IO.File]::WriteAllText("$dst\test\helper.py", "def add(a, b):`n    return a + b`n", $utf8)
[IO.File]::WriteAllText("$dst\usefolder.py", "import helper`n`nquick_print('helper says ' + str(helper.add(2, 3)))`n", $utf8)
```

## Checklist

| # | Step | Expected |
|---|---|---|
| 1 | Launch (fresh start) | BepInEx console appears; `LogOutput.log` has `[Folders] Harmony patches applied.` and the build line. |
| 2 | Load `foldertest`, press **F10** | IDE opens: title bar, Project tree with `main`, `usefolder`, `[-] test (2)`, built-in editor area, Terminal, status bar. |
| 3 | Click `helper` in the tree | Content appears in the built-in editor with syntax colours; caret and typing work. |
| 4 | Run `usefolder.py` | Console shows `helper says 5`; `output.txt` has the same line. |
| 5 | Save (top bar or Ctrl+S) | `test\hay.py` and `test\helper.py` still exist in `test\`; root files unchanged. |
| 6 | Edit `test\helper.py` externally, wait ~1 s | Editor content updates when the editor is not focused (file watcher). |
| 7 | Select `hay` → Rename → `hay2` | File `test\hay2.py` on disk; editor title/selection follows; import name changes accordingly. |
| 8 | Select `hay2` → Move → `other` | File at `test\other\hay2.py`; editor unchanged. |
| 9 | Close the `hay2` window, save | `test\other\hay2.py` still on disk (folder files persist). |
| 10 | Create a root file with an existing leaf name (e.g. copy `test\helper.py` to root) | Next load: conflict popup, red status line, folder copy skipped; log lists it. |
| 11 | Drag the IDE by its title bar, resize from the bottom-right corner | Window and editor area move/resize. Position/size survive a F10 close/reopen and a relaunch. |
| 12 | Type in the editor, then Run | The edited text runs; status `Ln/Col` tracks the caret. |
| 13 | Set `Explorer side = Left` in `tfwr.folders.cfg`, press F10 twice | Explorer moves to the left without a relaunch. |
| 14 | Add `docs/` to `Saves/foldertest/.farmignore` | The `docs` folder disappears from the tree; its scripts are not loaded; files stay on disk. |
| 15 | Press Escape while the IDE is open, then close the menu | IDE hides while the menu is up (log line) and returns afterwards. |
| 16 | Check that no game code window is visible while the IDE is open | All code/docs windows are hidden; closing the IDE (F10) restores them. |
| 17 | Right-click a file row, then the terminal | File list shows New file, New folder, Rename, Move, Delete, Refresh; terminal list shows Copy output and Clear. |
| 18 | Use the toolbar icons | Run/stop toggle, save, terminal, new file, new folder, collapse all, delete, menu, hide; no text buttons. |
| 19 | Click in the code and type; roll the wheel over the editor | Caret appears, typing works, code scrolls, and the map does not zoom. |
| 20 | Drag a file row onto a folder, onto the editor, then onto an empty tree spot | File moves on disk, editor opens it, file returns to root. Escape cancels. |
| 21 | Add `docs/` to `.farmignore` in the editor | Tree drops the folder after the save; the file is written on disk. |
| 22 | Open `.farmignore`, then click the `×` icon | The file is deleted after the confirm dialog. |
| 23 | While typing, press F10 (close), then type in the game | The caret leaves the editor; camera keys work again. |
| 24 | Scroll with the wheel outside the IDE window | The map zooms; scrolling over the IDE never zooms it. |
| 25 | Drag inside the Project list | The list does not scroll; the wheel does. |
| 17 | F10 to hide IDE | Normal canvas layout is intact; windows still open. |

## Log lines that prove success

```
[Info   :TFWR Folders] [Folders] Harmony patches applied.
[Info   :TFWR Folders] [Folders] game=0.1 engine=6000.0.43f1 exe=6000.0.43.9905963 core=...
[Info   :TFWR Folders] [Folders] loaded save 'foldertest', folder scripts: 2, conflicts: 0
```

Warnings about `skipped: a root file with the same name is loaded first` mean a name conflict —
expected in step 10, not otherwise.

## If something fails

1. Copy `BepInEx\LogOutput.log` and note the last `[Folders]` lines.
2. Restore the save from the pre-test backup (`scratch\mod-backups\` or `scratch\backups\`).
3. Roll back the plugin: delete `BepInEx\plugins\TFWRFolders\TFWRFolders.dll`.

## Cleanup

Delete `Saves\foldertest` when done.
