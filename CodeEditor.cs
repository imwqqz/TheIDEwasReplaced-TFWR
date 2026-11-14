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

        private CodeWindow bound;
        private bool rawMode;
        private string rawPath;
        private bool syncing;
        private float pushAt = -1f;
        private float nextPoll;
        private float nextFontTry;
        private float scroll;
        private string colored = "";
        private bool fontReady;

        internal int Caret { get { return input != null ? input.stringPosition : 0; } }
        internal string Text { get { return input != null ? (input.text ?? "") : ""; } }
        internal bool HasFocus { get { return input != null && input.isFocused; } }

        internal CodeEditor(IdePanel owner, RectTransform parent)
        {
            this.owner = owner;

            root = MakeRect(parent, "CodeEditor");
            Stretch(root);
            background = root.gameObject.AddComponent<Image>();
            background.color = new Color(0.09f, 0.10f, 0.12f, 1f);
            var focusClick = root.gameObject.AddComponent<FocusOnClick>();
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
            input.restoreOriginalTextOnEscape = false;
            input.onFocusSelectAll = false;
            input.scrollSensitivity = 0f;
            input.customCaretColor = true;
            input.interactable = true;
            input.readOnly = false;

            // Comment: Image gives the field something to click.
            var hit = inputGo.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0.01f);
            input.targetGraphic = hit;

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

            input.pointSize = 13f;
            overlay.fontSize = 13f;

            input.onValueChanged.AddListener(OnValueChanged);
        }

        private sealed class FocusOnClick : MonoBehaviour, IPointerClickHandler
        {
            public CodeEditor editor;

            public void OnPointerClick(PointerEventData eventData)
            {
                if (editor != null) editor.Focus();
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
            if (input != null)
            {
                input.caretColor = text;
                input.selectionColor = new Color(accent.r, accent.g, accent.b, 0.30f);
            }
        }

        internal void Bind(CodeWindow cw)
        {
            Flush();
            EnsureFont();
            rawMode = false;
            rawPath = null;
            bound = cw;
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
            Apply("");
            owner.ShowParseError("");
        }

        internal void Focus()
        {
            if (input == null) return;
            input.interactable = true;
            input.readOnly = false;
            var es = EventSystem.current;
            if (es != null) es.SetSelectedGameObject(input.gameObject);
            input.ActivateInputField();
        }

        internal void Blur()
        {
            if (input == null) return;
            input.DeactivateInputField();
            var es = EventSystem.current;
            if (es != null && es.currentSelectedGameObject == input.gameObject)
                es.SetSelectedGameObject(null);
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
            syncing = true;
            input.text = text ?? "";
            syncing = false;
            Colorize(text ?? "");
        }

        private void OnValueChanged(string value)
        {
            if (syncing) return;
            Colorize(value ?? "");
            pushAt = Time.unscaledTime + (rawMode ? 0.6f : 0.25f);
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

            if (pushAt > 0f && Time.unscaledTime >= pushAt) Push();

            if (!rawMode && bound != null && Time.unscaledTime >= nextPoll)
            {
                nextPoll = Time.unscaledTime + 0.4f;
                string windowText = bound.CodeInput.text ?? "";
                if (!input.isFocused && windowText != (input.text ?? "")) Apply(windowText);
            }

            if (inputText != null && inputText.color.a != 0f)
                inputText.color = new Color(1f, 1f, 1f, 0f);

            UpdateHeight();
            HandleWheel();
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
                ClampScroll();
            }
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
            content.anchoredPosition = new Vector2(0f, -scroll);
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
