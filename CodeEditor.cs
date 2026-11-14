using System;
using System.Collections.Generic;
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
        private float nextConflictLog;
        private DateTime rawWriteTime;
        private string lastPushed = "";

        private RectTransform completeBox;
        private readonly List<GameObject> completeRows = new List<GameObject>();
        private readonly List<UnityEngine.UI.Text> completeTexts = new List<UnityEngine.UI.Text>();
        private readonly List<UnityEngine.UI.Image> completeImages = new List<UnityEngine.UI.Image>();
        private readonly List<string> completeOptions = new List<string>();
        private HashSet<string> completeNames;
        private string completeNamesKey = "";
        private int completeIndex;
        private bool completeOpen;

        private const float CompletionWidth = 240f;
        private const float CompletionRowHeight = 18f;
        private const int CompletionMaxRows = 8;

        private static readonly string[] Keywords =
        {
            "if", "elif", "else", "for", "while", "def", "return", "break", "continue", "pass",
            "and", "or", "not", "in", "import", "from", "as", "global", "True", "False", "None",
            "print", "quick_print", "range", "len", "str", "min", "max", "abs", "random",
            "num_items", "num_unlocked", "get_pos_x", "get_pos_y", "get_entity_type",
            "get_ground_type", "can_harvest", "can_move", "get_water", "get_time", "get_tick_count"
        };

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
            CloseCompletion();
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
            CloseCompletion();
            rawMode = true;
            rawPath = path;
            bound = null;
            owner.ShowParseError("");
            editing = false;
            caret = 0;
            rawWriteTime = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            string text = "";
            try { if (File.Exists(path)) text = File.ReadAllText(path); } catch { }
            Apply(text);
            scroll = 0f;
        }

        internal void Clear()
        {
            CloseCompletion();
            bound = null;
            rawMode = false;
            rawPath = null;
            editing = false;
            caret = 0;
            Apply("");
            owner.ShowParseError("");
        }

        private void DebugLog(string message)
        {
            if (Plugin.DebugLogs != null && Plugin.DebugLogs.Value) Plugin.Log?.LogInfo(message);
        }

        internal void Focus()
        {
            if (input == null) return;
            editing = true;
            caret = TextEdit.Clamp(caret, Text);
            if (caret == 0 && Text.Length > 0) caret = Text.Length;
            DrawCaret();
            DebugLog("[Folders] editor click caret=" + caret);
        }

        internal void CancelEditing()
        {
            editing = false;
            CloseCompletion();
            if (caretImage != null) caretImage.color = new Color(caretColor.r, caretColor.g, caretColor.b, 0f);
        }

        internal void Blur()
        {
            CancelEditing();
        }

        private void OnClick(Vector2 screen)
        {
            if (inputText == null) return;
            CloseCompletion();
            editing = true;
            caret = CaretFromScreen(screen);
            DrawCaret();
            DebugLog("[Folders] editor click caret=" + caret);
        }

        // Comment: click maps to an insertion point, not just a character, so the line end is reachable.
        private int CaretFromScreen(Vector2 screen)
        {
            string text = Text;
            var info = inputText.textInfo;
            if (info == null || info.characterCount == 0) return 0;
            try
            {
                int hit = TMP_TextUtilities.FindIntersectingCharacter(inputText, screen, null, true);
                if (hit >= 0 && hit < info.characterCount)
                    return CharacterCaret(info.characterInfo[hit], screen);

                int line = TMP_TextUtilities.FindNearestLine(inputText, screen, null);
                if (line >= 0 && line < info.lineCount)
                {
                    var li = info.lineInfo[line];
                    int first = Mathf.Clamp(li.firstCharacterIndex, 0, info.characterCount - 1);
                    int last = Mathf.Clamp(li.lastCharacterIndex, 0, info.characterCount - 1);
                    int startIndex = Mathf.Clamp(info.characterInfo[first].index, 0, text.Length);
                    int lineEnd = text.IndexOf('\n', startIndex);
                    if (lineEnd < 0) lineEnd = text.Length;

                    if (screen.x >= ScreenX(info.characterInfo[last].topRight)) return lineEnd;

                    int near = TMP_TextUtilities.FindNearestCharacterOnLine(inputText, screen, line, null, true);
                    if (near >= 0 && near < info.characterCount)
                        return CharacterCaret(info.characterInfo[near], screen);
                    return startIndex;
                }

                int any = TMP_TextUtilities.FindNearestCharacter(inputText, screen, null, true);
                if (any >= 0 && any < info.characterCount)
                    return Mathf.Clamp(info.characterInfo[any].index, 0, text.Length);
            }
            catch { }
            return text.Length;
        }

        private int CharacterCaret(TMP_CharacterInfo ch, Vector2 screen)
        {
            float middle = (ScreenX(ch.bottomLeft) + ScreenX(ch.topRight)) * 0.5f;
            int index = ch.index + (screen.x > middle ? 1 : 0);
            return Mathf.Clamp(index, 0, Text.Length);
        }

        private float ScreenX(Vector3 local)
        {
            return RectTransformUtility.WorldToScreenPoint(null, inputText.transform.TransformPoint(local)).x;
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
            lastPushed = input.text ?? "";
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
                lastPushed = text;
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
                lastPushed = input.text ?? "";
                rawWriteTime = File.GetLastWriteTimeUtc(rawPath);
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
            else if (completeOpen) CloseCompletion();

            if (pushAt > 0f && Time.unscaledTime >= pushAt) Push();

            if (!rawMode && bound != null && Time.unscaledTime >= nextPoll)
            {
                nextPoll = Time.unscaledTime + 0.4f;
                string windowText = bound.CodeInput.text ?? "";
                if (windowText != (input.text ?? ""))
                {
                    if (!editing) Apply(windowText);
                    else if (pushAt < 0f && (input.text ?? "") == lastPushed) Apply(windowText);
                    else if (Time.unscaledTime >= nextConflictLog)
                    {
                        nextConflictLog = Time.unscaledTime + 3f;
                        Plugin.Log?.LogWarning("[Folders] external edit ignored while you are typing");
                    }
                }
            }

            if (rawMode && !string.IsNullOrEmpty(rawPath) && Time.unscaledTime >= nextPoll)
            {
                nextPoll = Time.unscaledTime + 0.4f;
                try
                {
                    DateTime stamp = File.Exists(rawPath) ? File.GetLastWriteTimeUtc(rawPath) : DateTime.MinValue;
                    if (stamp != rawWriteTime)
                    {
                        if (pushAt < 0f && (input.text ?? "") == lastPushed)
                        {
                            rawWriteTime = stamp;
                            Apply(File.Exists(rawPath) ? File.ReadAllText(rawPath) : "");
                        }
                        else if (Time.unscaledTime >= nextConflictLog)
                        {
                            nextConflictLog = Time.unscaledTime + 3f;
                            Plugin.Log?.LogWarning("[Folders] external .farmignore edit ignored while you are typing");
                        }
                    }
                }
                catch { }
            }

            if (inputText != null && inputText.color.a != 0f)
                inputText.color = new Color(1f, 1f, 1f, 0f);

            SyncOverlay();
            UpdateHeight();
            DrawCaret();
            EnsureCaretVisible();
            if (completeOpen) PositionCompletion();
            HandleWheel();
        }

        private void EnsureCaretVisible()
        {
            if (!editing || caretRect == null || viewport == null) return;
            try
            {
                Vector3 world = caretRect.TransformPoint(new Vector3(0f, caretRect.sizeDelta.y, 0f));
                Vector2 screen = RectTransformUtility.WorldToScreenPoint(null, world);
                Vector2 local;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport, screen, null, out local)) return;
                Rect vp = viewport.rect;
                float margin = caretRect.sizeDelta.y + 8f;
                if (local.y < vp.yMin + margin)
                {
                    scroll -= (vp.yMin + margin - local.y);
                    ClampScroll();
                }
                else if (local.y > vp.yMax - 8f)
                {
                    scroll += (local.y - (vp.yMax - 8f));
                    ClampScroll();
                }
            }
            catch { }
        }

        private void HandleKeys()
        {
            string text = input.text ?? "";
            int pos = TextEdit.Clamp(caret, text);
            bool changed = false;

            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (ctrl && Input.GetKeyDown(KeyCode.Space))
            {
                ShowCompletion(true);
                return;
            }

            if (completeOpen)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) { CloseCompletion(); return; }
                if (Input.GetKeyDown(KeyCode.DownArrow)) { MoveCompletion(1); return; }
                if (Input.GetKeyDown(KeyCode.UpArrow)) { MoveCompletion(-1); return; }
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Tab))
                {
                    CommitCompletion();
                    return;
                }
            }

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
            if (changed)
            {
                SetText(text);
                RefreshCompletion();
            }
            else if (Time.unscaledTime >= nextKeyLog) nextKeyLog = Time.unscaledTime + 0.3f;
        }

        // ---------------- completion ----------------

        private void RefreshCompletion()
        {
            ShowCompletion(false);
        }

        private void ShowCompletion(bool force)
        {
            if (!editing || inputText == null) { CloseCompletion(); return; }
            int start;
            string word = Completion.WordAt(Text, caret, out start);
            if (!force && word.Length < 2) { CloseCompletion(); return; }
            var options = Completion.Match(Names(), word, CompletionMaxRows);
            if (options.Count == 0) { CloseCompletion(); return; }

            if (completeOpen && SameOptions(options))
            {
                PositionCompletion();
                return;
            }

            EnsureCompletionUI();
            completeOptions.Clear();
            completeOptions.AddRange(options);
            completeIndex = 0;

            for (int i = 0; i < completeRows.Count; i++)
            {
                bool used = i < options.Count;
                if (completeRows[i].activeSelf != used) completeRows[i].SetActive(used);
                if (used) completeTexts[i].text = options[i];
            }

            completeBox.sizeDelta = new Vector2(CompletionWidth, 4f + options.Count * CompletionRowHeight);
            completeBox.SetAsLastSibling();
            completeBox.gameObject.SetActive(true);
            completeOpen = true;
            HighlightCompletion();
            PositionCompletion();
        }

        private bool SameOptions(List<string> options)
        {
            if (completeOptions.Count != options.Count) return false;
            for (int i = 0; i < options.Count; i++)
                if (!string.Equals(options[i], completeOptions[i], StringComparison.Ordinal)) return false;
            return true;
        }

        private void EnsureCompletionUI()
        {
            if (completeBox != null) return;
            completeBox = MakeRect(root, "Completion");
            completeBox.anchorMin = Vector2.zero;
            completeBox.anchorMax = Vector2.zero;
            completeBox.pivot = Vector2.zero;
            var bg = completeBox.gameObject.AddComponent<Image>();
            bg.color = new Color(0.16f, 0.17f, 0.19f, 0.98f);
            var swallow = completeBox.gameObject.AddComponent<Button>();
            swallow.transition = Selectable.Transition.None;
            var layout = completeBox.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 0f;
            layout.padding = new RectOffset(2, 2, 2, 2);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            for (int i = 0; i < CompletionMaxRows; i++)
            {
                var go = new GameObject("Option", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
                go.transform.SetParent(completeBox, false);
                var element = go.GetComponent<LayoutElement>();
                element.preferredHeight = CompletionRowHeight;
                element.minHeight = CompletionRowHeight;
                var image = go.GetComponent<Image>();
                image.color = new Color(1f, 1f, 1f, 0.02f);
                var button = go.GetComponent<Button>();
                button.targetGraphic = image;
                int index = i;
                button.onClick.AddListener(delegate { PickCompletion(index); });

                var labelGo = new GameObject("Label", typeof(RectTransform), typeof(UnityEngine.UI.Text));
                labelGo.transform.SetParent(go.transform, false);
                var label = labelGo.GetComponent<UnityEngine.UI.Text>();
                label.font = IdePanel.UiFont;
                label.fontSize = 11;
                label.color = new Color(0.87f, 0.88f, 0.90f, 1f);
                label.alignment = TextAnchor.MiddleLeft;
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
                label.raycastTarget = false;
                var lr = (RectTransform)labelGo.transform;
                lr.anchorMin = Vector2.zero;
                lr.anchorMax = Vector2.one;
                lr.offsetMin = new Vector2(6f, 0f);
                lr.offsetMax = new Vector2(-4f, 0f);

                completeRows.Add(go);
                completeTexts.Add(label);
                completeImages.Add(image);
            }
            completeBox.gameObject.SetActive(false);
        }

        private void HighlightCompletion()
        {
            for (int i = 0; i < completeImages.Count; i++)
            {
                bool selected = i == completeIndex && i < completeOptions.Count;
                completeImages[i].color = selected
                    ? new Color(0.33f, 0.45f, 0.38f, 1f)
                    : new Color(1f, 1f, 1f, 0.02f);
            }
        }

        private void PositionCompletion()
        {
            if (completeBox == null || caretRect == null || root == null) return;
            try
            {
                Vector3 world = caretRect.TransformPoint(new Vector3(0f, -4f, 0f));
                Vector2 screen = RectTransformUtility.WorldToScreenPoint(null, world);
                Vector2 local;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, null, out local)) return;
                Rect area = root.rect;
                float x = local.x - area.xMin;
                float y = local.y - area.yMin;
                float width = completeBox.sizeDelta.x;
                float height = completeBox.sizeDelta.y;
                x = Mathf.Clamp(x, 2f, Mathf.Max(2f, area.width - width - 2f));
                float below = y - height;
                y = below >= 2f ? below : y + 20f;
                y = Mathf.Clamp(y, 2f, Mathf.Max(2f, area.height - height - 2f));
                completeBox.anchoredPosition = new Vector2(x, y);
            }
            catch { }
        }

        private void MoveCompletion(int delta)
        {
            if (completeOptions.Count == 0) return;
            completeIndex = Mathf.Clamp(completeIndex + delta, 0, completeOptions.Count - 1);
            HighlightCompletion();
        }

        private void PickCompletion(int index)
        {
            if (!completeOpen || index < 0 || index >= completeOptions.Count) return;
            completeIndex = index;
            CommitCompletion();
        }

        private void CommitCompletion()
        {
            if (!completeOpen || completeOptions.Count == 0) { CloseCompletion(); return; }
            int index = Mathf.Clamp(completeIndex, 0, completeOptions.Count - 1);
            int pos;
            string text = Completion.Apply(Text, caret, completeOptions[index], out pos);
            caret = TextEdit.Clamp(pos, text);
            CloseCompletion();
            SetText(text);
        }

        private void CloseCompletion()
        {
            completeOpen = false;
            if (completeBox != null) completeBox.gameObject.SetActive(false);
        }

        private HashSet<string> Names()
        {
            string key = "";
            try
            {
                int count = bound != null && bound.tokenizedIdentifiers != null ? bound.tokenizedIdentifiers.Count : 0;
                key = IdePanel.SaveDir() + "|" + (bound != null ? bound.fileName : (rawPath ?? "")) + "|" + count;
            }
            catch { }
            if (completeNames != null && key == completeNamesKey) return completeNames;

            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < Keywords.Length; i++) set.Add(Keywords[i]);
            try
            {
                string stub = Path.Combine(IdePanel.SaveDir(), "__builtins__.py");
                if (File.Exists(stub)) Completion.AddNames(File.ReadAllText(stub), set);
            }
            catch { }
            try
            {
                if (bound != null && bound.tokenizedIdentifiers != null)
                    foreach (var identifier in bound.tokenizedIdentifiers) set.Add(identifier);
            }
            catch { }

            completeNames = set;
            completeNamesKey = key;
            return set;
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
                caretRect.anchoredPosition = new Vector2(0f, Mathf.Max(0f, inputText.rectTransform.rect.height - 16f));
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

            caretRect.anchoredPosition = CaretAnchor(position);
            caretRect.sizeDelta = new Vector2(1.5f, Mathf.Max(8f, lineHeight));
            BlinkCaret();
        }

        // Comment: TMP reports caret coords around the text centre; the caret rect is corner-anchored.
        private Vector2 CaretAnchor(Vector2 local)
        {
            Rect rect = inputText.rectTransform.rect;
            float x, y;
            TextEdit.CaretAnchor(local.x, local.y, rect.width, rect.height, out x, out y);
            return new Vector2(x, y);
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
