using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace TFWRFolders
{
    // Comment: pure completion helpers; the editor only renders and applies the result.
    public static class Completion
    {
        private static readonly Regex DefLine =
            new Regex(@"(?:^|\n)[ \t]*(?:def|class)[ \t]+([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);

        private static readonly Regex MemberLine =
            new Regex(@"(?:^|\n)[ \t]+([A-Za-z_][A-Za-z0-9_]*)[ \t]*[:=]", RegexOptions.Compiled);

        private static readonly Regex GlobalLine =
            new Regex(@"(?:^|\n)([A-Za-z_][A-Za-z0-9_]*)[ \t]*=", RegexOptions.Compiled);

        public static bool IsNameChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_';
        }

        public static string WordAt(string text, int caret, out int start)
        {
            text = text ?? "";
            caret = TextEdit.Clamp(caret, text);
            int index = caret;
            while (index > 0 && IsNameChar(text[index - 1])) index--;
            start = index;
            return text.Substring(index, caret - index);
        }

        public static List<string> Match(IEnumerable<string> names, string prefix, int max)
        {
            prefix = prefix ?? "";
            var begins = new List<string>();
            var contains = new List<string>();
            foreach (var name in names)
            {
                if (string.IsNullOrEmpty(name)) continue;
                if (prefix.Length == 0 || name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    begins.Add(name);
                else if (name.IndexOf(prefix, StringComparison.OrdinalIgnoreCase) >= 0)
                    contains.Add(name);
            }
            begins.Sort(StringComparer.OrdinalIgnoreCase);
            contains.Sort(StringComparer.OrdinalIgnoreCase);

            var result = new List<string>();
            for (int i = 0; i < begins.Count && result.Count < max; i++) result.Add(begins[i]);
            for (int i = 0; i < contains.Count && result.Count < max; i++) result.Add(contains[i]);
            return result;
        }

        public static string Apply(string text, int caret, string replacement, out int newCaret)
        {
            text = text ?? "";
            replacement = replacement ?? "";
            int start;
            WordAt(text, caret, out start);
            caret = TextEdit.Clamp(caret, text);
            newCaret = start + replacement.Length;
            return text.Substring(0, start) + replacement + text.Substring(caret);
        }

        public static void AddNames(string source, HashSet<string> names)
        {
            if (string.IsNullOrEmpty(source) || names == null) return;
            AddMatches(DefLine, source, names);
            AddMatches(MemberLine, source, names);
            AddMatches(GlobalLine, source, names);
        }

        private static void AddMatches(Regex pattern, string source, HashSet<string> names)
        {
            foreach (Match match in pattern.Matches(source))
            {
                string name = match.Groups[1].Value;
                if (name.Length > 0) names.Add(name);
            }
        }
    }
}
