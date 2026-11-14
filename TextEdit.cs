using System;
using System.Collections.Generic;

namespace TFWRFolders
{
    // Comment: pure caret and text operations; the editor only renders the result.
    public static class TextEdit
    {
        public static int Clamp(int caret, string text)
        {
            text = text ?? "";
            return caret < 0 ? 0 : (caret > text.Length ? text.Length : caret);
        }

        public static string Insert(string text, int caret, string insert, out int newCaret)
        {
            text = text ?? "";
            insert = insert ?? "";
            caret = Clamp(caret, text);
            newCaret = caret + insert.Length;
            return text.Insert(caret, insert);
        }

        public static string Backspace(string text, ref int caret)
        {
            text = text ?? "";
            caret = Clamp(caret, text);
            if (caret == 0) return text;
            caret--;
            return text.Remove(caret, 1);
        }

        public static string Delete(string text, ref int caret)
        {
            text = text ?? "";
            caret = Clamp(caret, text);
            if (caret >= text.Length) return text;
            return text.Remove(caret, 1);
        }

        public static int LineStart(string text, int caret)
        {
            text = text ?? "";
            caret = Clamp(caret, text);
            if (caret == 0) return 0;
            int index = text.LastIndexOf('\n', caret - 1);
            return index < 0 ? 0 : index + 1;
        }

        public static int LineEnd(string text, int caret)
        {
            text = text ?? "";
            caret = Clamp(caret, text);
            int index = text.IndexOf('\n', caret);
            return index < 0 ? text.Length : index;
        }

        public static int MoveLine(string text, int caret, int delta)
        {
            text = text ?? "";
            caret = Clamp(caret, text);

            var starts = new List<int> { 0 };
            for (int i = 0; i < text.Length; i++)
                if (text[i] == '\n') starts.Add(i + 1);

            int line = 0;
            while (line + 1 < starts.Count && starts[line + 1] <= caret) line++;
            int column = caret - starts[line];
            int target = Math.Max(0, Math.Min(starts.Count - 1, line + delta));
            int start = starts[target];
            int end = target + 1 < starts.Count ? starts[target + 1] - 1 : text.Length;
            return Math.Min(start + column, end);
        }
    }
}
