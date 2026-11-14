using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace TFWRFolders
{
    // Comment: reflection for members the game keeps private.
    internal static class GameAccess
    {
        internal static readonly MethodInfo WriteAtomicallyMethod =
            typeof(Saver).GetMethod("WriteTextAtomically", BindingFlags.NonPublic | BindingFlags.Static);

        internal static readonly FieldInfo LastWrittenField =
            typeof(Saver).GetField("lastWrittenCode", BindingFlags.NonPublic | BindingFlags.Static);

        internal static readonly FieldInfo WatcherField =
            typeof(Saver).GetField("watcher", BindingFlags.NonPublic | BindingFlags.Static);

        internal static readonly ConcurrentDictionary<string, string> LastWritten =
            LastWrittenField != null ? LastWrittenField.GetValue(null) as ConcurrentDictionary<string, string> : null;

        internal static void WriteAtomically(FileInfo file, string content)
        {
            if (WriteAtomicallyMethod != null)
            {
                try { WriteAtomicallyMethod.Invoke(null, new object[] { file, content }); }
                catch (TargetInvocationException e) { throw e.InnerException ?? e; }
            }
            else
            {
                file.Directory.Create();
                File.WriteAllText(file.FullName, content, new UTF8Encoding(false));
            }
            if (LastWritten != null) LastWritten[file.FullName] = content;
        }
    }

    // Comment: state shared by the save/load/write patches.
    internal static class FolderSync
    {
        internal static readonly List<string> Conflicts = new List<string>();

        internal static void OnLoaded(MainSim mainSim)
        {
            try { OnLoadedCore(mainSim); }
            catch (Exception e) { Plugin.Log?.LogError("[Folders] Load postfix failed: " + e); }
        }

        private static void OnLoadedCore(MainSim mainSim)
        {
            Conflicts.Clear();
            var ws = mainSim != null ? mainSim.workspace : null;
            if (ws == null) return;

            string saveName = OptionHolder.GetString("activeSave", "Save0");
            string dir = Saver.GetPathOfSaveDirectory(saveName);
            FolderMap.Reset(dir);
            IdePanel.OnSaveLoaded();

            var sg = ReadSaveGame(saveName);
            var positions = PairsToDict(sg != null ? sg.openFilePositions : null);
            var sizes = PairsToDict(sg != null ? sg.openFileSizes : null);
            var scrolls = PairsToDict(sg != null ? sg.openFileScrollPositions : null);
            var minimized = new HashSet<string>(sg != null && sg.minimizedFiles != null ? sg.minimizedFiles : new List<string>(), StringComparer.OrdinalIgnoreCase);

            var rootLeaves = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(dir))
            {
                foreach (var f in Directory.EnumerateFiles(dir, "*.py", SearchOption.TopDirectoryOnly))
                    rootLeaves.Add(Path.GetFileNameWithoutExtension(f));
            }

            var chosen = new Dictionary<string, ScannedFile>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in FolderMap.Scan(dir, Saver.IsValidPortableFileName))
            {
                if (chosen.ContainsKey(f.Leaf))
                {
                    Conflicts.Add(Rel(f.Folder, f.Leaf) + " skipped: the name '" + f.Leaf + "' is used by another file");
                    continue;
                }
                chosen[f.Leaf] = f;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var cw in ws.codeWindows.Values)
            {
                if (string.IsNullOrEmpty(cw.fileName)) continue;
                seen.Add(cw.fileName);
                ScannedFile match;
                if (!rootLeaves.Contains(cw.fileName) && chosen.TryGetValue(cw.fileName, out match))
                    FolderMap.SetFolder(match.Leaf, match.Folder);
            }

            var opened = new List<string>();
            foreach (var f in chosen.Values)
            {
                if (seen.Contains(f.Leaf))
                {
                    if (rootLeaves.Contains(f.Leaf))
                        Conflicts.Add(Rel(f.Folder, f.Leaf) + " skipped: a root file with the same name is loaded first");
                    continue;
                }
                try
                {
                    string code = File.ReadAllText(f.Path).Replace("\r", "");
                    Vector2 pos;
                    positions.TryGetValue(f.Leaf, out pos);
                    Vector2 size;
                    sizes.TryGetValue(f.Leaf, out size);
                    ws.OpenCodeWindow(f.Leaf, code, pos + ws.farmPosition.anchoredPosition, size);
                    FolderMap.SetFolder(f.Leaf, f.Folder);
                    opened.Add(f.Leaf);
                    seen.Add(f.Leaf);
                }
                catch (Exception e)
                {
                    Conflicts.Add(Rel(f.Folder, f.Leaf) + " could not be opened: " + e.Message);
                }
            }

            foreach (var leaf in opened)
            {
                Window win;
                if (!ws.TryGetOpenWindow(leaf, out win)) continue;
                float scroll;
                if (scrolls.TryGetValue(leaf, out scroll))
                {
                    CodeWindow cw;
                    if (win.TryGetComponent<CodeWindow>(out cw))
                        ((RectTransform)cw.CodeInput.transform).anchoredPosition += new Vector2(0f, scroll);
                }
                if (minimized.Contains(leaf)) win.SetMinmized(true);
            }

            if (sg != null && sg.dockedFiles != null)
            {
                foreach (var pair in sg.dockedFiles)
                {
                    if (string.IsNullOrEmpty(pair.key) || string.IsNullOrEmpty(pair.value)) continue;
                    if (!opened.Contains(pair.key) && !opened.Contains(pair.value)) continue;
                    Window a, b;
                    if (ws.TryGetOpenWindow(pair.key, out a) && ws.TryGetOpenWindow(pair.value, out b)) a.DockOnto(b);
                }
            }

            IdePanel.MarkDirty();
            if (Conflicts.Count > 0)
            {
                foreach (var c in Conflicts) Plugin.Log?.LogWarning("[Folders] " + c);
                ShowConflictPopup(mainSim);
                IdePanel.ShowWithConflicts();
            }
            Plugin.Log?.LogInfo("[Folders] loaded save '" + saveName + "', folder scripts: " + opened.Count + ", conflicts: " + Conflicts.Count);
        }

        private static string Rel(string folder, string leaf)
        {
            return folder.Length == 0 ? leaf : folder + "/" + leaf;
        }

        private static SaveGame ReadSaveGame(string saveName)
        {
            try
            {
                string path = Saver.GetPathOfSaveFile(saveName);
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return null;
                var sg = JsonUtility.FromJson<SaveGame>(json);
                if (sg != null) sg.Normalize();
                return sg;
            }
            catch (Exception e)
            {
                Plugin.Log?.LogWarning("[Folders] could not read save.json: " + e.Message);
                return null;
            }
        }

        private static Dictionary<string, T> PairsToDict<T>(List<Pair<string, T>> pairs)
        {
            var dict = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
            if (pairs == null) return dict;
            foreach (var pair in pairs)
                if (!string.IsNullOrEmpty(pair.key)) dict[pair.key] = pair.value;
            return dict;
        }

        private static void ShowConflictPopup(MainSim mainSim)        {
            try
            {
                WarningPopup popup = mainSim != null ? mainSim.warningPopup : null;
                if (popup == null) return;
                var lines = Conflicts.Take(4).ToList();
                string text = "TFWR Folders: " + Conflicts.Count + " script name conflict(s).\n" + string.Join("\n", lines.ToArray());
                if (Conflicts.Count > lines.Count) text += "\n(...see BepInEx log)";
                var buttons = new List<WarningPopup.ButtonData>
                {
                    new WarningPopup.ButtonData("ok", popup.Close)
                };
                popup.ShowPopup(text, buttons);
            }
            catch (Exception e)
            {
                Plugin.Log?.LogWarning("[Folders] conflict popup failed: " + e.Message);
            }
        }
    }

    [HarmonyPatch(typeof(Saver), "Load")]
    internal static class Saver_Load_Patch
    {
        private static void Postfix(MainSim mainSim)
        {
            try { FolderSync.OnLoaded(mainSim); }
            catch (Exception e) { Plugin.Log?.LogError("[Folders] Load patch failed: " + e); }
        }
    }

    [HarmonyPatch(typeof(Saver), "WriteCodeFiles")]
    internal static class Saver_WriteCodeFiles_Patch
    {
        private static bool Prefix(Dictionary<string, string> code, string builtins, string savePath)
        {
            if (!FolderMap.HasAny || code == null) return true;

            try
            {
                FolderMap.PruneTo(code.Keys);

                foreach (var kv in code.ToList())
                {
                    var file = new FileInfo(FolderMap.PathFor(kv.Key, savePath));
                    GameAccess.WriteAtomically(file, kv.Value);
                }

                var builtinsFile = new FileInfo(Path.Combine(savePath, "__builtins__.py"));
                if (builtins != null) GameAccess.WriteAtomically(builtinsFile, builtins);

                // Comment: same as vanilla: delete untracked root .py files.
                foreach (var f in Directory.GetFiles(savePath))
                {
                    if (Path.GetExtension(f) == ".py"
                        && !code.ContainsKey(Path.GetFileNameWithoutExtension(f))
                        && !f.EndsWith("__builtins__.py", StringComparison.Ordinal))
                    {
                        File.Delete(f);
                    }
                }
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log?.LogError("[Folders] write failed, falling back to vanilla writer (folder files may be stale): " + e);
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(Saver), "Save")]
    internal static class Saver_Save_Patch
    {
        // Comment: pending keystrokes reach the hidden window before the game captures it.
        private static void Prefix()
        {
            IdePanel.FlushEditor();
        }
    }

    [HarmonyPatch(typeof(Saver), "SaveCode")]
    internal static class Saver_SaveCode_Patch
    {
        private static void Prefix()
        {
            IdePanel.FlushEditor();
        }
    }

    [HarmonyPatch(typeof(Saver), "StartFileWatcher")]
    internal static class Saver_StartFileWatcher_Patch
    {
        private static void Postfix()
        {
            try
            {
                var watcher = GameAccess.WatcherField != null ? GameAccess.WatcherField.GetValue(null) as FileSystemWatcher : null;
                if (watcher != null) watcher.IncludeSubdirectories = true;
            }
            catch (Exception e) { Plugin.Log?.LogWarning("[Folders] watcher patch failed: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(Workspace), "RenameWindow")]
    internal static class Workspace_RenameWindow_Patch
    {
        private static void Prefix(Window window, ref string __state)
        {
            try { __state = window != null ? window.windowName : null; }
            catch { }
        }

        private static void Postfix(Window window, string newWindowName, string __state)
        {
            try
            {
                if (!string.IsNullOrEmpty(__state) && !string.IsNullOrEmpty(newWindowName))
                    FolderMap.Rename(__state, newWindowName);
            }
            catch (Exception e) { Plugin.Log?.LogWarning("[Folders] rename mapping failed: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(Workspace), "Scroll")]
    internal static class Workspace_Scroll_Patch
    {
        private static bool Prefix()
        {
            return !IdePanel.IsPointerOverWindow;
        }
    }
}
