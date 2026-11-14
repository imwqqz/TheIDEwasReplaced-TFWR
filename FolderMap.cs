using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;

namespace TFWRFolders
{
    public sealed class ScannedFile
    {
        public string Leaf;
        public string Folder;
        public string Path;
    }

    // Comment: one glob per line, '#' starts a comment, no negation.
    public sealed class IgnoreRules
    {
        private readonly List<Regex> patterns = new List<Regex>();

        public int Count { get { return patterns.Count; } }

        public static IgnoreRules Parse(IEnumerable<string> lines)
        {
            var rules = new IgnoreRules();
            if (lines == null) return rules;
            foreach (var raw in lines)
            {
                if (raw == null) continue;
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                line = line.Replace('\\', '/');
                if (line.StartsWith("!", StringComparison.Ordinal)) continue; // Comment: no negation
                if (line.StartsWith("./", StringComparison.Ordinal)) line = line.Substring(2);
                if (line.StartsWith("/", StringComparison.Ordinal)) line = line.TrimStart('/');
                if (line.EndsWith("/", StringComparison.Ordinal)) line = line.TrimEnd('/');
                if (line.Length == 0) continue;

                string regex = "^" + GlobToRegex(line) + "(/.*)?$";
                try { rules.patterns.Add(new Regex(regex, RegexOptions.IgnoreCase)); } catch { }
            }
            return rules;
        }

        public static IgnoreRules Load(string saveDir)
        {
            try
            {
                if (string.IsNullOrEmpty(saveDir)) return new IgnoreRules();
                string path = Path.Combine(saveDir, ".farmignore");
                if (!File.Exists(path)) return new IgnoreRules();
                return Parse(File.ReadAllLines(path));
            }
            catch { return new IgnoreRules(); }
        }

        public bool IsIgnored(string relPath, bool isDir)
        {
            if (patterns.Count == 0 || string.IsNullOrEmpty(relPath)) return false;
            string path = relPath.Replace('\\', '/').Trim('/');
            string name = path;
            int slash = path.LastIndexOf('/');
            if (slash >= 0) name = path.Substring(slash + 1);

            foreach (var regex in patterns)
            {
                if (regex.IsMatch(path)) return true;
                if (regex.IsMatch(name)) return true;
            }
            return false;
        }

        private static string GlobToRegex(string pattern)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < pattern.Length; i++)
            {
                char c = pattern[i];
                if (c == '*')
                {
                    if (i + 1 < pattern.Length && pattern[i + 1] == '*')
                    {
                        sb.Append(".*");
                        i++;
                    }
                    else
                    {
                        sb.Append("[^/]*");
                    }
                }
                else if (c == '?')
                {
                    sb.Append("[^/]");
                }
                else if ("\\.+()[]{}^$|".IndexOf(c) >= 0)
                {
                    sb.Append('\\').Append(c);
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }

    // Comment: lock because the save writer runs on a task while the UI reads the map.
    public static class FolderMap
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, string> folders =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static string SaveDir { get; private set; } = "";

        public static bool HasAny
        {
            get { lock (Gate) { return folders.Count > 0; } }
        }

        public static void Reset(string saveDir)
        {
            lock (Gate)
            {
                folders.Clear();
                SaveDir = saveDir ?? "";
            }
        }

        public static bool TryGetFolder(string leaf, out string folder)
        {
            lock (Gate) { return folders.TryGetValue(leaf, out folder); }
        }

        public static void SetFolder(string leaf, string folder)
        {
            if (string.IsNullOrEmpty(leaf)) return;
            folder = NormalizeFolder(folder);
            lock (Gate)
            {
                if (folder.Length == 0) folders.Remove(leaf);
                else folders[leaf] = folder;
            }
        }

        public static void Remove(string leaf)
        {
            lock (Gate) { folders.Remove(leaf); }
        }

        public static void Rename(string oldLeaf, string newLeaf)
        {
            if (string.IsNullOrEmpty(oldLeaf) || string.IsNullOrEmpty(newLeaf) || oldLeaf == newLeaf) return;
            lock (Gate)
            {
                string folder;
                if (folders.TryGetValue(oldLeaf, out folder))
                {
                    folders.Remove(oldLeaf);
                    folders[newLeaf] = folder;
                }
            }
        }

        public static void PruneTo(IEnumerable<string> leaves)
        {
            var keep = new HashSet<string>(leaves ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            lock (Gate)
            {
                var remove = folders.Keys.Where(k => !keep.Contains(k)).ToList();
                foreach (var k in remove) folders.Remove(k);
            }
        }

        public static string NormalizeFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return "";
            var parts = folder.Replace('\\', '/').Split('/');
            var clean = new List<string>();
            foreach (var p in parts)
            {
                var t = p.Trim();
                if (t.Length > 0) clean.Add(t);
            }
            return string.Join("/", clean);
        }

        public static bool IsValidFolder(string folder, Func<string, bool> isValidSegment)
        {
            var norm = NormalizeFolder(folder);
            if (norm.Length == 0) return true;
            foreach (var seg in norm.Split('/'))
            {
                if (seg == "." || seg == "..") return false;
                if (isValidSegment == null || !isValidSegment(seg)) return false;
            }
            return true;
        }

        public static string PathFor(string leaf, string saveDir)
        {
            string folder = null;
            lock (Gate) { folders.TryGetValue(leaf, out folder); }
            string dir = saveDir ?? SaveDir ?? "";
            if (!string.IsNullOrEmpty(folder))
                return Path.Combine(dir, folder.Replace('/', Path.DirectorySeparatorChar), leaf + ".py");
            return Path.Combine(dir, leaf + ".py");
        }

        public static bool TryMoveFile(string saveDir, string leaf, string fromFolder, string toFolder, out string message)
        {
            message = "";
            try
            {
                string oldPath = BuildPath(saveDir, fromFolder, leaf);
                string newPath = BuildPath(saveDir, toFolder, leaf);
                if (string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
                {
                    message = "already in that folder";
                    return false;
                }
                if (File.Exists(newPath))
                {
                    message = "target already has '" + leaf + ".py'";
                    return false;
                }
                if (File.Exists(oldPath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(newPath));
                    File.Move(oldPath, newPath);
                }
                return true;
            }
            catch (Exception e)
            {
                message = e.Message;
                return false;
            }
        }

        private static string BuildPath(string saveDir, string folder, string leaf)
        {
            folder = NormalizeFolder(folder);
            return folder.Length == 0
                ? Path.Combine(saveDir, leaf + ".py")
                : Path.Combine(saveDir, folder.Replace('/', Path.DirectorySeparatorChar), leaf + ".py");
        }

        // Comment: hidden folders are skipped while walking, so a .venv inside a save costs nothing.
        public static List<ScannedFile> Scan(string saveDir, Func<string, bool> isValidSegment)
        {
            return Scan(saveDir, isValidSegment, null);
        }

        public static List<ScannedFile> Scan(string saveDir, Func<string, bool> isValidSegment, IgnoreRules ignore)
        {
            var list = new List<ScannedFile>();
            if (string.IsNullOrEmpty(saveDir) || !Directory.Exists(saveDir)) return list;

            string root = saveDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            foreach (var item in Walk(root, isValidSegment, ignore))
            {
                string folder = item.Key;
                foreach (var file in item.Value)
                {
                    string leaf = Path.GetFileNameWithoutExtension(file);
                    if (string.Equals(leaf, "__builtins__", StringComparison.OrdinalIgnoreCase)) continue;
                    if (folder.Length == 0) continue;
                    list.Add(new ScannedFile { Leaf = leaf, Folder = folder, Path = file });
                }
            }

            list.Sort((a, b) =>
            {
                int c = string.Compare(a.Folder, b.Folder, StringComparison.OrdinalIgnoreCase);
                return c != 0 ? c : string.Compare(a.Leaf, b.Leaf, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        public static List<string> ListFolders(string saveDir, Func<string, bool> isValidSegment)
        {
            return ListFolders(saveDir, isValidSegment, null);
        }

        public static List<string> ListFolders(string saveDir, Func<string, bool> isValidSegment, IgnoreRules ignore)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(saveDir) || !Directory.Exists(saveDir)) return list;
            string root = saveDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            foreach (var item in Walk(root, isValidSegment, ignore))
            {
                if (item.Key.Length > 0) list.Add(item.Key);
            }
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        private static IEnumerable<KeyValuePair<string, List<string>>> Walk(string root, Func<string, bool> isValidSegment, IgnoreRules ignore)
        {
            var stack = new Stack<KeyValuePair<string, string>>();
            stack.Push(new KeyValuePair<string, string>(root, ""));
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                var files = new List<string>();
                try
                {
                    foreach (var file in Directory.EnumerateFiles(current.Key, "*.py", SearchOption.TopDirectoryOnly))
                    {
                        string name = Path.GetFileName(file);
                        string rel = current.Value.Length == 0 ? name : current.Value + "/" + name;
                        if (ignore != null && ignore.IsIgnored(rel, false)) continue;
                        files.Add(file);
                    }
                }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }

                yield return new KeyValuePair<string, List<string>>(current.Value, files);

                try
                {
                    foreach (var sub in Directory.EnumerateDirectories(current.Key, "*", SearchOption.TopDirectoryOnly))
                    {
                        string name = Path.GetFileName(sub);
                        if (name.StartsWith(".", StringComparison.Ordinal)) continue;
                        if (isValidSegment != null && !isValidSegment(name)) continue;
                        string rel = current.Value.Length == 0 ? name : current.Value + "/" + name;
                        if (ignore != null && ignore.IsIgnored(rel, true)) continue;
                        stack.Push(new KeyValuePair<string, string>(sub, rel));
                    }
                }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
            }
        }
    }
}
