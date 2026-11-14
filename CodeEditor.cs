using System;
using System.IO;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TFWRFolders
{
    // Comment: the editor is the face; the hidden game window still parses, runs and saves.
    internal sealed class CodeEditor
    {
        private readonly IdePanel owner;
        private readonly RectTransform root;
        private readonly RectTransform viewport;
        private readonly RectTransform content;
        private readonly Image background;
        private readonly TMP_InputField input;
        private readonly TMP_Text inputText;
        private readonly TMP_Text overlay;
        private readonly Image caretImage;
        private readonly RectTransform caretRect;

        private CodeWindow bound;
        private bool rawMode;
        private string rawPath;
        private float pushAt = -1f;
        private float nextPoll;
        private float nextFontTry;
        private float scroll;
        private string colored = "";
        private bool fontReady;
        private bool editing;
        private int caret;
        private Color caretColor = Color.white;
        private float nextKeyLog;

        internal int Caret { get { return caret; } }
        internal string Text { get { return input != null ? (input.text ?? "") : ""; } }
        internal bool IsEditing { get { return editing; } }

        internal CodeEditor(IdePanel owner, RectTransform parent)
        {
            this.owner = owner;

            root = MakeRect(parent, "CodeEditor");
            Stretch(root);
            background = root.gameObject.AddComponent<Image>();
            background.color = new Color(0.09f, 0.10f, 0.12f, 1f);
            var focusClick = root.gameObject.AddComponent<ClickFocus>();
            focusClick.editor = this;

            viewport = MakeRect(root, "Viewport");
            Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();
            var vpImage = viewport.gameObject.AddComponent<Image>();
            vpImage.color = new Color(0f, 0f, 0f, 0.01f);

            content = MakeRect(viewport, "Content");
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = new Vector2(0f, 60f);
            content.anchoredPosition = Vector2.zero;

            var inputGo = new GameObject("Input", typeof(RectTransform));
            inputGo.transform.SetParent(content, false);
            var irt = (RectTransform)inputGo.transform;
            irt.anchorMin = new Vector2(0f, 0f);
            irt.anchorMax = new Vector2(1f, 1f);
            irt.offsetMin = new Vector2(12f, 10f);
            irt.offsetMax = new Vector2(-12f, -8f);

            input = inputGo.AddComponent<TMP_InputField>();
            input.lineType = TMP_InputField.LineType.MultiLineNewline;
            input.richText = false;
            input.scrollSensitivity = 0f;
            input.transition = Selectable.Transition.None;
            input.interactable = false;

            // Comment: Image gives the field something to click.
            var hit = inputGo.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0.01f);
            input.targetGraphic = hit;

            var inputClick = inputGo.AddComponent<ClickFocus>();
            inputClick.editor = this;

            // Comment: overlay sits under the input text so the caret stays visible.
            var overlayGo = new GameObject("Overlay", typeof(RectTransform), typeof(TextMeshProUGUI));
            overlayGo.transform.SetParent(inputGo.transform, false);
            overlay = overlayGo.GetComponent<TextMeshProUGUI>();
            overlay.richText = true;
            overlay.raycastTarget = false;
            overlay.alignment = TextAlignmentOptions.TopLeft;
            overlay.color = new Color(0.87f, 0.88f, 0.90f, 1f);
            Stretch((RectTransform)overlay.transform);

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(inputGo.transform, false);
            inputText = textGo.GetComponent<TextMeshProUGUI>();
            inputText.alignment = TextAlignmentOptions.TopLeft;
            inputText.raycastTarget = false;
            inputText.richText = false;
            inputText.color = new Color(1f, 1f, 1f, 0f);
            Stretch((RectTransform)inputText.transform);
            input.textComponent = inputText;

            var caretGo = new GameObject("Caret", typeof(RectTransform), typeof(Image));
            caretGo.transform.SetParent(inputText.transform, false);
            caretRect = (RectTransform)caretGo.transform;
            caretRect.anchorMin = Vector2.zero;
            caretRect.anchorMax = Vector2.zero;
            caretRect.pivot = Vector2.zero;
            caretRect.sizeDelta = new Vector2(1.5f, 16f);
            caretImage = caretGo.GetComponent<Image>();
            caretImage.raycastTarget = false;
            caretImage.color = new Color(1f, 1f, 1f, 0f);

            input.pointSize = 13f;
            overlay.fontSize = 13f;
        }

        private sealed class ClickFocus : MonoBehaviour, IPointerClickHandler
        {
            public CodeEditor editor;

            public void OnPointerClick(PointerEventData eventData)
            {
                if (editor != null) editor.OnClick(eventData.position);
            }
        }

        private void EnsureFont()
        {
            if (fontReady || Time.unscaledTime < nextFontTry) return;
            try
            {
                var font = FindFont();
                if (font != null)
                {
                    input.fontAsset = font;
                    overlay.font = font;
                    input.pointSize = 13f;
                    overlay.fontSize = 13f;
                    fontReady = true;
                }
                else
                {
                    nextFontTry = Time.unscaledTime + 1f;
                }
            }
            catch { nextFontTry = Time.unscaledTime + 1f; }
        }

        // Comment: borrow the game's code font; TMP default if that fails.
        private static TMP_FontAsset FindFont()
        {
            try
            {
                var ws = MainSim.Inst != null ? MainSim.Inst.workspace : null;
                var prefabField = typeof(Workspace).GetField("codeWinPrefab", BindingFlags.NonPublic | BindingFlags.Instance);
                var prefab = prefabField != null ? prefabField.GetValue(ws) as CodeWindow : null;
                if (prefab != null)
                {
                    var textField = typeof(CodeWindow).GetField("codeText", BindingFlags.NonPublic | BindingFlags.Instance);
                    var text = textField != null ? textField.GetValue(prefab) as TMP_Text : null;
                    if (text != null && text.font != null) return text.font;
                }
            }
            catch { }
            try { return TMP_Settings.defaultFontAsset; } catch { return null; }
        }

        internal void SetColors(Color editorBg, Color text, Color accent)
        {
            if (background != null) background.color = editorBg;
            if (overlay != null) overlay.color = text;
            if (inputText != null) inputText.color = new Color(1f, 1f, 1f, 0f);
            caretColor = text;
            if (caretImage != null) caretImage.color = new Color(text.r, text.g, text.b, 0f);
        }

        internal void Bind(CodeWindow cw)
        {
            Flush();
            EnsureFont();
            rawMode = false;
            rawPath = null;
            bound = cw;
            editing = false;
            caret = 0;
            Apply(cw != null ? cw.CodeInput.text : "");
            scroll = 0f;
        }

        internal void BindFile(string path)
        {
            Flush();
            EnsureFont();
            rawMode = true;
            rawPath = path;
            bound = null;
            owner.ShowParseError("");
            editing = false;
            caret = 0;
            string text = "";
            try { if (File.Exists(path)) text = File.ReadAllText(path); } catch { }
            Apply(text);
            scroll = 0f;
        }

        internal void Clear()
        {
            bound = null;
            rawMode = false;
            rawPath = null;
            editing = false;
            caret = 0;
            Apply("");
            owner.ShowParseError("");
        }

        internal void Focus()
        {
            if (input == null) return;
            editing = true;
            caret = TextEdit.Clamp(caret, Text);
            if (caret == 0 && Text.Length > 0) caret = Text.Length;
            DrawCaret();
            Plugin.Log?.LogInfo("[Folders] editor click caret=" + caret);
        }

        internal void CancelEditing()
        {
            editing = false;
            if (caretImage != null) caretImage.color = new Color(caretColor.r, caretColor.g, caretColor.b, 0f);
        }

        internal void Blur()
        {
            CancelEditing();
        }

        private void OnClick(Vector2 screen)
        {
            if (inputText == null) return;
            editing = true;
            string text = Text;
            int index = -1;
            try { index = TMP_TextUtilities.FindIntersectingCharacter(inputText, screen, null, true); }
            catch { }
            if (index < 0) caret = text.Length;
            else caret = TextEdit.Clamp(inputText.textInfo.characterInfo[index].index, text);
            DrawCaret();
            Plugin.Log?.LogInfo("[Folders] editor click caret=" + caret);
        }

        internal void Flush()
        {
            if (bound != null && pushAt > 0f) Push();
            if (rawMode && pushAt > 0f) Push();
        }

        internal void SaveNow()
        {
            if (rawMode) Push();
            else if (bound != null && pushAt > 0f) Push();
        }

        private void Apply(string text)
        {
            input.text = text ?? "";
            Colorize(text ?? "");
            DrawCaret();
        }

        private void SetText(string text)
        {
            input.text = text ?? "";
            Colorize(text ?? "");
            pushAt = Time.unscaledTime + (rawMode ? 0.6f : 0.25f);
            DrawCaret();
        }

        private void Colorize(string text)
        {
            if (rawMode)
            {
                overlay.richText = false;
                colored = text ?? "";
                overlay.text = colored;
                return;
            }

            try
            {
                var theme = ThemeManager.Inst != null ? ThemeManager.Inst.Theme : null;
                if (theme != null)
                {
                    overlay.richText = true;
                    colored = CodeUtilities.SyntaxColor2(text ?? "", theme);
                    overlay.text = colored;
                }
                else
                {
                    overlay.richText = false;
                    colored = text ?? "";
                    overlay.text = colored;
                }
            }
            catch
            {
                overlay.richText = false;
                colored = text ?? "";
                overlay.text = colored;
            }
        }

        private void Push()
        {
            pushAt = -1f;
            if (rawMode)
            {
                WriteRaw();
                return;
            }
            if (bound == null) return;
            try
            {
                string text = input.text ?? "";
                if (bound.CodeInput.text != text) bound.CodeInput.text = text;
                Node node = bound.Parse();
                if (node == null)
                {
                    owner.ShowParseError(ReadError());
                }
                else
                {
                    owner.ShowParseError("");
                }
            }
            catch (Exception e)
            {
                Plugin.Log?.LogWarning("[Folders] editor push failed: " + e.Message);
            }
        }

        private void WriteRaw()
        {
            if (string.IsNullOrEmpty(rawPath)) return;
            string temp = null;
            try
            {
                string dir = Path.GetDirectoryName(rawPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                temp = rawPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temp, input.text ?? "", new UTF8Encoding(false));
                if (File.Exists(rawPath)) File.Replace(temp, rawPath, null);
                else File.Move(temp, rawPath);
                temp = null;
                owner.RawSaved(rawPath);
            }
            catch (Exception e)
            {
                Plugin.Log?.LogWarning("[Folders] ignore file save failed: " + e.Message);
            }
            finally
            {
                try { if (temp != null && File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }

        private string ReadError()
        {
            try
            {
                var field = typeof(CodeWindow).GetField("errorString", BindingFlags.NonPublic | BindingFlags.Instance);
                var value = field != null ? field.GetValue(bound) as string : null;
                return string.IsNullOrEmpty(value) ? "syntax error" : value;
            }
            catch { return "syntax error"; }
        }

        internal void Tick()
        {
            if (input == null) return;
            if (!fontReady) EnsureFont();
            if (editing) HandleKeys();

            if (pushAt > 0f && Time.unscaledTime >= pushAt) Push();

            if (!rawMode && bound != null && Time.unscaledTime >= nextPoll)
            {
                nextPoll = Time.unscaledTime + 0.4f;
                string windowText = bound.CodeInput.text ?? "";
                if (!editing && windowText != (input.text ?? "")) Apply(windowText);
            }

            if (inputText != null && inputText.color.a != 0f)
                inputText.color = new Color(1f, 1f, 1f, 0f);

            SyncOverlay();
            UpdateHeight();
            DrawCaret();
            HandleWheel();
        }

        private void HandleKeys()
        {
            string text = input.text ?? "";
            int pos = TextEdit.Clamp(caret, text);
            bool changed = false;

            string typed = Input.inputString ?? "";
            for (int i = 0; i < typed.Length; i++)
            {
                char c = typed[i];
                if (c == '\b')
                {
                    text = TextEdit.Backspace(text, ref pos);
                    changed = true;
                }
                else if (c == '\n' || c == '\r')
                {
                    string indent = IndentAt(text, pos);
                    text = TextEdit.Insert(text, pos, "\n" + indent, out pos);
                    changed = true;
                }
                else if (c == '\t')
                {
                    text = TextEdit.Insert(text, pos, "    ", out pos);
                    changed = true;
                }
                else if (c >= 32 && c != 127)
                {
                    text = TextEdit.Insert(text, pos, c.ToString(), out pos);
                    changed = true;
                }
            }

            if (Input.GetKeyDown(KeyCode.Delete))
            {
                text = TextEdit.Delete(text, ref pos);
                changed = true;
            }
            if (Input.GetKeyDown(KeyCode.LeftArrow)) pos = Mathf.Max(0, pos - 1);
            if (Input.GetKeyDown(KeyCode.RightArrow)) pos = Mathf.Min(text.Length, pos + 1);
            if (Input.GetKeyDown(KeyCode.Home)) pos = TextEdit.LineStart(text, pos);
            if (Input.GetKeyDown(KeyCode.End)) pos = TextEdit.LineEnd(text, pos);
            if (Input.GetKeyDown(KeyCode.UpArrow)) pos = TextEdit.MoveLine(text, pos, -1);
            if (Input.GetKeyDown(KeyCode.DownArrow)) pos = TextEdit.MoveLine(text, pos, 1);

            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (ctrl && Input.GetKeyDown(KeyCode.V))
            {
                string clip = GUIUtility.systemCopyBuffer;
                if (!string.IsNullOrEmpty(clip))
                {
                    clip = clip.Replace("\r", "");
                    text = TextEdit.Insert(text, pos, clip, out pos);
                    changed = true;
                }
            }

            caret = TextEdit.Clamp(pos, text);
            if (changed) SetText(text);
            else if (Time.unscaledTime >= nextKeyLog) nextKeyLog = Time.unscaledTime + 0.3f;
        }

        private static string IndentAt(string text, int pos)
        {
            int start = TextEdit.LineStart(text, pos);
            int i = start;
            while (i < text.Length && (text[i] == ' ' || text[i] == '\t')) i++;
            if (i <= pos) return text.Substring(start, i - start);
            return text.Substring(start, Mathf.Max(0, pos - start));
        }

        private void DrawCaret()
        {
            if (caretImage == null || inputText == null) return;
            if (!editing)
            {
                caretImage.color = new Color(caretColor.r, caretColor.g, caretColor.b, 0f);
                return;
            }

            string text = input.text ?? "";
            if (inputText.textInfo == null || inputText.textInfo.characterCount == 0)
            {
                caretRect.anchoredPosition = new Vector2(0f, 0f);
                caretRect.sizeDelta = new Vector2(1.5f, 16f);
                BlinkCaret();
                return;
            }

            int target = TextEdit.Clamp(caret, text);
            var info = inputText.textInfo;
            int ci = -1;
            for (int i = 0; i < info.characterCount; i++)
            {
                if (info.characterInfo[i].index >= target)
                {
                    ci = i;
                    break;
                }
            }

            Vector2 position;
            float lineHeight;
            if (ci < 0)
            {
                var last = info.characterInfo[info.characterCount - 1];
                position = last.bottomRight;
                lineHeight = info.lineInfo[last.lineNumber].lineHeight;
            }
            else
            {
                var ch = info.characterInfo[ci];
                position = ch.index == target ? ch.bottomLeft : ch.bottomRight;
                lineHeight = info.lineInfo[ch.lineNumber].lineHeight;
            }

            caretRect.anchoredPosition = position;
            caretRect.sizeDelta = new Vector2(1.5f, Mathf.Max(8f, lineHeight));
            BlinkCaret();
        }

        private void BlinkCaret()
        {
            float alpha = (Mathf.Repeat(Time.unscaledTime, 1f) < 0.55f) ? 1f : 0f;
            caretImage.color = new Color(caretColor.r, caretColor.g, caretColor.b, alpha);
        }

        private void SyncOverlay()
        {
            if (inputText == null || overlay == null) return;
            var src = (RectTransform)inputText.transform;
            var dst = (RectTransform)overlay.transform;
            if (dst.anchoredPosition != src.anchoredPosition) dst.anchoredPosition = src.anchoredPosition;
            if (dst.rect.size != src.rect.size) dst.sizeDelta = src.rect.size;
        }

        private void UpdateHeight()
        {
            float width = content.rect.width - 24f;
            if (width < 40f) return;

            float preferred;
            try { preferred = overlay.GetPreferredValues(colored, width, 0f).y; }
            catch { preferred = 60f; }

            float height = Mathf.Max(viewport.rect.height, preferred + 26f);
            if (Mathf.Abs(content.sizeDelta.y - height) > 0.5f)
            {
                content.sizeDelta = new Vector2(0f, height);
            }
            ClampScroll();
        }

        private void HandleWheel()
        {
            try
            {
                if (!RectTransformUtility.RectangleContainsScreenPoint(viewport, Input.mousePosition, null)) return;
                float wheel = Input.mouseScrollDelta.y;
                if (Mathf.Abs(wheel) < 0.01f) return;
                scroll -= wheel * 60f;
                ClampScroll();
            }
            catch { }
        }

        private void ClampScroll()
        {
            float max = Mathf.Max(0f, content.rect.height - viewport.rect.height);
            scroll = Mathf.Clamp(scroll, 0f, max);
            content.anchoredPosition = new Vector2(0f, scroll);
        }

        private static RectTransform MakeRect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
