using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TFWRFolders
{
    // Comment: our editor is the UI; hidden game windows remain the run/save backend.
    internal sealed partial class IdePanel : MonoBehaviour
    {
        internal static bool IsOpen
        {
            get { return instance != null && instance.canvas != null && instance.canvas.enabled; }
        }

        internal static bool IsPointerOverWindow
        {
            get
            {
                return instance != null && instance.canvas != null && instance.canvas.enabled
                    && instance.windowRect != null
                    && RectTransformUtility.RectangleContainsScreenPoint(instance.windowRect, Input.mousePosition, null);
            }
        }

        private const float GripSize = 16f;

        private static IdePanel instance;
        private static bool dirty = true;
        private static bool forceOpen;
        private static Font uiFont;

        private Canvas canvas;
        private RectTransform windowRect;
        private Image windowBg;
        private RectTransform titleBar;
        private Image titleBarBg;
        private Text titleText;
        private RectTransform toolStrip;
        private Image toolStripBg;
        private Text runText;
        private RectTransform treePanel;
        private Image treeBg;
        private RectTransform treeHeader;
        private Text treeTitle;
        private RectTransform treeScrollRect;
        private RectTransform treeContent;
        private RectTransform editorRect;
        private CodeEditor editor;
        private RectTransform consolePanel;
        private Image consoleBg;
        private RectTransform consoleScrollRect;
        private ScrollRect consoleScroll;
        private Text consoleText;
        private RectTransform statusBar;
        private Image statusBg;
        private Text statusLeft;
        private Text statusRight;
        private Text parseErrorText;
        private RectTransform hSep;
        private Image hSepImg;
        private Image gripImg;
        private GameObject modal;
        private GameObject contextMenu;
        private GameObject dragGhost;
        private Text dragGhostText;

        private Button deleteIcon;
        private Button collapseBtn;
        private Button terminalCollapseBtn;
        private Text collapseText;

        private readonly List<Button> themedButtons = new List<Button>();
        private readonly HashSet<string> collapsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<Window> touched = new HashSet<Window>();
        private readonly List<string> lastFolders = new List<string>();
        private readonly List<RowInfo> rowInfos = new List<RowInfo>();

        private string selectedLeaf;
        private string selectedFolder = "";
        private string status = "";
        private float nextRefresh;
        private float nextConsole;
        private string lastConsole;
        private string outputCache = "";
        private DateTime outputStamp = DateTime.MinValue;
        private bool consoleVisible = true;
        private bool explorerCollapsed;
        private bool rectLoaded;
        private bool suppressed;
        private bool wasVisibleBeforeMenu;
        private bool ignoreSelected;
        private bool pressActive;
        private string pressLeaf;
        private string pressFolder;
        private Vector2 pressPos;
        private PointerEventData pressEvent;
        private bool dragActive;
        private string dragLeaf;
        private string dragFolder;
        private RowInfo hoverRow;
        private InputBoss inputBoss;
        private bool bossDisabledByUs;

        private Vector2 windowPos;
        private Vector2 windowSize;

        private static readonly FieldInfo LoggerField =
            typeof(MainSim).GetField("logger", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly Color FallbackBg = new Color(0.11f, 0.12f, 0.14f, 1f);
        private static readonly Color FallbackFg = new Color(0.87f, 0.88f, 0.90f, 1f);
        private static readonly Color FallbackMuted = new Color(0.66f, 0.68f, 0.72f, 1f);
        private static readonly Color FallbackAccent = new Color(0.55f, 0.72f, 0.60f, 1f);
        private static readonly Color FallbackWarn = new Color(0.95f, 0.55f, 0.45f, 1f);

        private Color bg = FallbackBg;
        private Color fg = FallbackFg;
        private Color muted = FallbackMuted;
        private Color accent = FallbackAccent;
        private Color warn = FallbackWarn;
        private Color titleCol = FallbackBg;
        private Color treeCol = FallbackBg;
        private Color consoleCol = FallbackBg;
        private Color statusCol = FallbackBg;
        private Color sepCol = FallbackBg;
        private Color btnCol = FallbackBg;

        private sealed class DragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
        {
            public IdePanel owner;
            public bool resize;

            public void OnBeginDrag(PointerEventData eventData) { }

            public void OnDrag(PointerEventData eventData)
            {
                if (owner != null) owner.OnWindowDrag(eventData.delta, resize);
            }

            public void OnEndDrag(PointerEventData eventData)
            {
                if (owner != null) owner.SaveWindowRect();
            }
        }

        private sealed class RowPress : MonoBehaviour, IPointerDownHandler
        {
            public IdePanel owner;
            public string leaf;
            public string folder;

            public void OnPointerDown(PointerEventData eventData)
            {
                if (eventData.button == PointerEventData.InputButton.Left && owner != null)
                    owner.BeginPress(leaf, folder, eventData.position, eventData);
            }
        }

        private sealed class RightClick : MonoBehaviour, IPointerClickHandler
        {
            public Action<Vector2> onClick;

            public void OnPointerClick(PointerEventData eventData)
            {
                if (eventData.button == PointerEventData.InputButton.Right && onClick != null)
                    onClick(eventData.position);
            }
        }

        private sealed class NoDrag : MonoBehaviour, IBeginDragHandler
        {
            public void OnBeginDrag(PointerEventData eventData) { }
        }

        private sealed class RowInfo
        {
            public RectTransform rect;
            public string folder;
            public string leaf;
            public bool isFolder;
            public Image image;
            public Color baseColor;
        }

        private sealed class MenuItem
        {
            public string label;
            public bool enabled;
            public Action action;

            public MenuItem(string label, bool enabled, Action action)
            {
                this.label = label;
                this.enabled = enabled;
                this.action = action;
            }
        }

        internal static void MarkDirty() { dirty = true; }
        internal static void ShowWithConflicts() { forceOpen = true; }

        internal static void OnSaveLoaded()
        {
            if (instance == null) return;
            instance.touched.Clear();
            instance.selectedLeaf = null;
            instance.selectedFolder = "";
            instance.ignoreSelected = false;
            if (instance.editor != null) instance.editor.Clear();
            dirty = true;
        }

        private void Awake()
        {
            instance = this;
            windowSize = new Vector2(Mathf.Max(520f, Plugin.WindowWidth.Value), Mathf.Max(360f, Plugin.WindowHeight.Value));
            windowPos = new Vector2(Plugin.WindowX.Value, Plugin.WindowY.Value);
            CreateUI();
            canvas.enabled = false;
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
            if (canvas != null) canvas.enabled = false;
        }

        private void Update()
        {
            try
            {
                bool inGame = MainSim.Inst != null && MainSim.Inst.workspace != null;
                if (!inGame)
                {
                    if (canvas != null) canvas.enabled = false;
                    return;
                }

                // Comment: the game menu wins; F10 is ignored while it is open.
                bool menuOpen = false;
                try
                {
                    var menu = MainSim.Inst.menu;
                    menuOpen = menu != null && menu.gameObject.activeInHierarchy;
                }
                catch { }

                if (menuOpen != suppressed)
                {
                    suppressed = menuOpen;
                    if (menuOpen)
                    {
                        wasVisibleBeforeMenu = canvas.enabled;
                        if (canvas.enabled) canvas.enabled = false;
                        CloseModal();
                        CloseMenu();
                        if (editor != null)
                        {
                            editor.Flush();
                            editor.Blur();
                        }
                        CancelPress();
                        ReleaseInputBoss();
                        Plugin.Log?.LogInfo("[Folders] IDE hidden for the in-game menu");
                    }
                    else
                    {
                        if (wasVisibleBeforeMenu) canvas.enabled = true;
                        Plugin.Log?.LogInfo("[Folders] IDE restored after the in-game menu");
                    }
                }

                if (suppressed)
                {
                    return;
                }

                if (forceOpen)
                {
                    forceOpen = false;
                    SetOpen(true);
                }

                if (Plugin.ToggleKey.Value.IsDown()) SetOpen(!canvas.enabled);

                if (!canvas.enabled)
                {
                    if (editor != null && editor.IsEditing) editor.Blur();
                    ReleaseInputBoss();
                    return;
                }

                if (pressActive || dragActive) HandlePointer();
                else if (contextMenu != null && Input.GetKeyDown(KeyCode.Escape)) CloseMenu();

                if (editor != null && editor.IsEditing && Input.GetMouseButtonDown(0)
                    && !RectTransformUtility.RectangleContainsScreenPoint(editorRect, Input.mousePosition, null))
                    editor.CancelEditing();

                if (!rectLoaded) ClampWindow();

                UpdateInputBoss(editor != null && editor.IsEditing);
                HideAll();
                if (editor != null) editor.Tick();
                UpdateRunIcon();

                if (!dragActive && (dirty || Time.unscaledTime >= nextRefresh))
                {
                    dirty = false;
                    nextRefresh = Time.unscaledTime + 1.5f;
                    Refresh();
                }

                if (consoleVisible && Time.unscaledTime >= nextConsole)
                {
                    nextConsole = Time.unscaledTime + 0.4f;
                    UpdateConsole(false);
                }

                UpdateStatusBar();
            }
            catch (Exception e)
            {
                if (editor == null || !editor.IsEditing) ReleaseInputBoss();
                Plugin.Log?.LogError("[Folders] panel update: " + e);
            }
        }

        private void SetOpen(bool open)
        {
            if (suppressed || open == canvas.enabled) return;
            canvas.enabled = open;

            if (open)
            {
                rectLoaded = false;
                ClampWindow();
                Layout();
                EnsureEditorCreated();
                dirty = true;
                nextRefresh = 0f;
                UpdateConsole(true);
                HideAll();
                if (selectedLeaf != null) EnsureEditor(selectedLeaf, selectedFolder);
            }
            else
            {
                if (editor != null)
                {
                    editor.Flush();
                    editor.Blur();
                }
                CancelPress();
                CloseMenu();
                ReleaseInputBoss();
                RestoreAll();
            }
        }

        private void UpdateInputBoss(bool typing)
        {
            if (inputBoss == null)
            {
                try { inputBoss = FindAnyObjectByType<InputBoss>(); }
                catch { }
            }
            if (inputBoss == null) return;

            if (typing && inputBoss.enabled)
            {
                inputBoss.enabled = false;
                bossDisabledByUs = true;
            }
            else if (!typing && bossDisabledByUs)
            {
                inputBoss.enabled = true;
                bossDisabledByUs = false;
            }
        }

        private void ReleaseInputBoss()
        {
            if (!bossDisabledByUs) return;
            try { if (inputBoss != null) inputBoss.enabled = true; } catch { }
            bossDisabledByUs = false;
        }

        private void UpdateRunIcon()
        {
            if (runText == null) return;
            bool running = false;
            var ws = MainSim.Inst != null ? MainSim.Inst.workspace : null;
            Window win;
            if (ws != null && selectedLeaf != null && ws.TryGetOpenWindow(selectedLeaf, out win))
            {
                CodeWindow cw;
                if (win.TryGetComponent<CodeWindow>(out cw)) running = cw.isExecuting;
            }
            runText.text = running ? "\u25A0" : "\u25B6";
        }

        private void HideAll()
        {
            var ws = MainSim.Inst != null ? MainSim.Inst.workspace : null;
            if (ws == null) return;
            foreach (var win in ws.OpenWindows)
            {
                if (win == null) continue;
                if (win.GetComponent<CodeWindow>() == null && win.GetComponent<DocsWindow>() == null) continue;
                if (win.gameObject.activeSelf) Hide(win);
            }
        }

        private void Hide(Window win)
        {
            if (win == null) return;
            touched.Add(win);
            win.gameObject.SetActive(false);
        }

        private void RestoreAll()
        {
            try
            {
                foreach (var win in touched)
                {
                    if (win == null) continue;
                    win.gameObject.SetActive(true);
                }
            }
            catch (Exception e) { Plugin.Log?.LogWarning("[Folders] restore failed: " + e.Message); }
            touched.Clear();
        }

        private CodeWindow EnsureBackend(string leaf, string folder)
        {
            var ws = MainSim.Inst != null ? MainSim.Inst.workspace : null;
            if (ws == null) return null;
            Window win;
            if (!ws.TryGetOpenWindow(leaf, out win))
            {
                string path = PathFor(leaf, folder);
                string code = File.Exists(path) ? File.ReadAllText(path).Replace("\r", "") : "";
                ws.OpenCodeWindow(leaf, code, ws.farmPosition.anchoredPosition + new Vector2(300f, -150f));
                FolderMap.SetFolder(leaf, folder);
                if (!ws.TryGetOpenWindow(leaf, out win)) return null;
            }
            touched.Add(win);
            win.gameObject.SetActive(false);
            CodeWindow cw;
            return win.TryGetComponent<CodeWindow>(out cw) ? cw : null;
        }

        private void EnsureEditor(string leaf, string folder)
        {
            EnsureEditorCreated();
            var cw = EnsureBackend(leaf, folder);
            if (cw != null && editor != null)
            {
                editor.Bind(cw);
                editor.Focus();
            }
        }

        private void EnsureEditorCreated()
        {
            if (editor != null || editorRect == null || MainSim.Inst == null) return;
            editor = new CodeEditor(this, editorRect);
            editor.SetColors(Shade(bg, 0.70f), fg, accent);
        }

        private static void DebugLog(string message)
        {
            if (Plugin.DebugLogs != null && Plugin.DebugLogs.Value) Plugin.Log?.LogInfo(message);
        }

        internal void ShowParseError(string message)
        {
            if (parseErrorText == null) return;
            parseErrorText.text = string.IsNullOrEmpty(message) ? "" : "syntax: " + message;
            parseErrorText.color = string.IsNullOrEmpty(message) ? muted : warn;
        }

        internal void RawSaved(string path)
        {
            status = "saved " + Path.GetFileName(path);
            dirty = true;
        }

        // ---------------- window drag / resize ----------------

        private void OnWindowDrag(Vector2 delta, bool resize)
        {
            if (canvas == null) return;
            float scale = Mathf.Max(0.0001f, canvas.scaleFactor);
            Vector2 d = delta / scale;

            if (!resize)
            {
                windowPos += d;
                ClampWindow();
                return;
            }

            windowSize += new Vector2(d.x, -d.y);
            windowPos += new Vector2(d.x * 0.5f, d.y * 0.5f);
            ClampWindow();
            Layout();
        }

        private void ClampWindow()
        {
            try
            {
                var canvasRt = (RectTransform)canvas.transform;
                Vector2 size = canvasRt.rect.size;
                if (size.x < 100f || size.y < 100f) return;

                windowSize.x = Mathf.Clamp(windowSize.x, 520f, Mathf.Max(520f, size.x));
                windowSize.y = Mathf.Clamp(windowSize.y, 360f, Mathf.Max(360f, size.y));
                float mx = Mathf.Max(0f, (size.x - windowSize.x) * 0.5f);
                float my = Mathf.Max(0f, (size.y - windowSize.y) * 0.5f);
                windowPos.x = Mathf.Clamp(windowPos.x, -mx, mx);
                windowPos.y = Mathf.Clamp(windowPos.y, -my, my);

                windowRect.sizeDelta = windowSize;
                windowRect.anchoredPosition = windowPos;
                rectLoaded = true;
            }
            catch { }
        }

        private void SaveWindowRect()
        {
            try
            {
                Plugin.WindowX.Value = windowPos.x;
                Plugin.WindowY.Value = windowPos.y;
                Plugin.WindowWidth.Value = windowSize.x;
                Plugin.WindowHeight.Value = windowSize.y;
                Plugin.ConfigFile.Save();
                Plugin.Log?.LogInfo("[Folders] window saved at " + windowPos + " size " + windowSize);
            }
            catch (Exception e) { Plugin.Log?.LogWarning("[Folders] save window rect failed: " + e.Message); }
        }

        // ---------------- UI construction ----------------

        private void CreateUI()
        {
            var canvasGo = new GameObject("TFWRFolders_Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5000;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1200f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            windowRect = MakeRect(canvasGo.transform, "Window");
            SetRect(windowRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), windowSize, windowPos);
            windowBg = windowRect.gameObject.AddComponent<Image>();
            windowBg.color = FallbackBg;

            titleBar = MakeRect(windowRect, "TitleBar");
            titleBarBg = titleBar.gameObject.AddComponent<Image>();
            titleBarBg.color = FallbackBg;
            titleBar.gameObject.AddComponent<DragHandle>().owner = this;

            titleText = MakeText(titleBar, "TFWR Folders", 13, FallbackFg, TextAnchor.MiddleLeft, false);
            var titleRt = (RectTransform)titleText.transform;
            titleRt.anchorMin = Vector2.zero;
            titleRt.anchorMax = Vector2.one;
            titleRt.offsetMin = new Vector2(10f, 0f);
            titleRt.offsetMax = new Vector2(-18f, 0f);
            titleText.horizontalOverflow = HorizontalWrapMode.Wrap;
            titleText.verticalOverflow = VerticalWrapMode.Truncate;

            toolStrip = MakeRect(windowRect, "ToolStrip");
            toolStripBg = toolStrip.gameObject.AddComponent<Image>();
            toolStripBg.color = FallbackBg;
            var stripLayout = toolStrip.gameObject.AddComponent<VerticalLayoutGroup>();
            stripLayout.spacing = 2f;
            stripLayout.padding = new RectOffset(2, 2, 6, 2);
            stripLayout.childControlWidth = true;
            stripLayout.childControlHeight = true;
            stripLayout.childForceExpandWidth = true;
            stripLayout.childForceExpandHeight = false;
            stripLayout.childAlignment = TextAnchor.UpperCenter;

            var runButton = MakeIcon(toolStrip, "\u25B6", RunSelected);
            runText = runButton.GetComponentInChildren<Text>();
            themedButtons.Add(runButton);
            themedButtons.Add(MakeIcon(toolStrip, "\u2193", SaveCode));
            themedButtons.Add(MakeIcon(toolStrip, ">_", ToggleConsole));
            themedButtons.Add(MakeIcon(toolStrip, "_", delegate { SetOpen(false); }));

            treePanel = MakeRect(windowRect, "Tree");
            treeBg = treePanel.gameObject.AddComponent<Image>();
            treeBg.color = FallbackBg;
            var treeHit = treePanel.gameObject.AddComponent<RightClick>();
            treeHit.onClick = ShowTreeMenu;

            treeHeader = MakeRect(treePanel, "Header");
            SetRect(treeHeader, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 22f), Vector2.zero);
            treeTitle = MakeText(treeHeader, "Project", 11, FallbackMuted, TextAnchor.MiddleLeft, false);
            SetRect((RectTransform)treeTitle.transform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0f, 0.5f), new Vector2(-132f, 0f), new Vector2(8f, 0f));

            var treeIcons = MakeRect(treeHeader, "Icons");
            SetRect(treeIcons, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(100f, 0f), new Vector2(-20f, 0f));
            var iconsLayout = treeIcons.gameObject.AddComponent<HorizontalLayoutGroup>();
            iconsLayout.spacing = 0f;
            iconsLayout.childControlWidth = true;
            iconsLayout.childControlHeight = true;
            iconsLayout.childForceExpandWidth = false;
            iconsLayout.childForceExpandHeight = true;
            iconsLayout.childAlignment = TextAnchor.MiddleCenter;

            themedButtons.Add(MakeIcon(treeIcons, "+", NewFile));
            themedButtons.Add(MakeIcon(treeIcons, "\u229E", NewFolder));
            themedButtons.Add(MakeIcon(treeIcons, "\u21D5", ToggleAllCollapsed));
            deleteIcon = MakeIcon(treeIcons, "\u00D7", DeleteSelected);
            themedButtons.Add(deleteIcon);
            themedButtons.Add(MakeIcon(treeIcons, "\u22EE", delegate { ShowTreeMenu(Input.mousePosition); }));

            collapseBtn = MakeButton(treePanel, "<", ToggleExplorer, 10);
            SetRect((RectTransform)collapseBtn.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(18f, 18f), new Vector2(-2f, -1f));
            collapseText = collapseBtn.GetComponentInChildren<Text>();
            themedButtons.Add(collapseBtn);

            treeScrollRect = MakeRect(treePanel, "Scroll");
            SetRect(treeScrollRect, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var treeScroll = treeScrollRect.gameObject.AddComponent<ScrollRect>();
            treeScroll.horizontal = false;
            treeScroll.vertical = true;
            treeScroll.movementType = ScrollRect.MovementType.Clamped;
            treeScroll.scrollSensitivity = 25f;

            var treeViewport = MakeRect(treeScrollRect, "Viewport");
            SetRect(treeViewport, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            treeViewport.gameObject.AddComponent<RectMask2D>();
            treeViewport.gameObject.AddComponent<NoDrag>();
            var treeViewportImage = treeViewport.gameObject.AddComponent<Image>();
            treeViewportImage.color = new Color(0f, 0f, 0f, 0.01f);

            treeContent = MakeRect(treeViewport, "Content");
            SetRect(treeContent, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 0f), Vector2.zero);
            var treeLayout = treeContent.gameObject.AddComponent<VerticalLayoutGroup>();
            treeLayout.spacing = 2f;
            treeLayout.padding = new RectOffset(4, 4, 4, 4);
            treeLayout.childControlWidth = true;
            treeLayout.childControlHeight = true;
            treeLayout.childForceExpandWidth = true;
            treeLayout.childForceExpandHeight = false;
            var treeFitter = treeContent.gameObject.AddComponent<ContentSizeFitter>();
            treeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            treeScroll.viewport = treeViewport;
            treeScroll.content = treeContent;

            editorRect = MakeRect(windowRect, "Editor");

            consolePanel = MakeRect(windowRect, "Terminal");
            consoleBg = consolePanel.gameObject.AddComponent<Image>();
            consoleBg.color = FallbackBg;
            var consoleHit = consolePanel.gameObject.AddComponent<RightClick>();
            consoleHit.onClick = ShowTerminalMenu;

            var consoleTitle = MakeText(consolePanel, "Terminal", 11, FallbackMuted, TextAnchor.MiddleLeft, false);
            SetRect((RectTransform)consoleTitle.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(120f, 18f), new Vector2(8f, -2f));

            var clearBtn = MakeButton(consolePanel, "Clear", ClearConsole, 10);
            SetRect((RectTransform)clearBtn.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(56f, 18f), new Vector2(-24f, -2f));
            themedButtons.Add(clearBtn);
            terminalCollapseBtn = MakeButton(consolePanel, "v", ToggleConsole, 10);
            SetRect((RectTransform)terminalCollapseBtn.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(18f, 18f), new Vector2(-2f, -2f));
            themedButtons.Add(terminalCollapseBtn);

            consoleScrollRect = MakeRect(consolePanel, "Scroll");
            consoleScroll = consoleScrollRect.gameObject.AddComponent<ScrollRect>();
            consoleScroll.horizontal = false;
            consoleScroll.vertical = true;
            consoleScroll.movementType = ScrollRect.MovementType.Clamped;
            consoleScroll.scrollSensitivity = 30f;

            var consoleViewport = MakeRect(consoleScrollRect, "Viewport");
            SetRect(consoleViewport, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            consoleViewport.gameObject.AddComponent<RectMask2D>();
            var consoleViewportImage = consoleViewport.gameObject.AddComponent<Image>();
            consoleViewportImage.color = new Color(0f, 0f, 0f, 0.01f);

            var consoleContent = MakeRect(consoleViewport, "Content");
            SetRect(consoleContent, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 0f), Vector2.zero);
            consoleText = consoleContent.gameObject.AddComponent<Text>();
            consoleText.font = UiFont;
            consoleText.fontSize = 11;
            consoleText.color = FallbackMuted;
            consoleText.alignment = TextAnchor.UpperLeft;
            consoleText.horizontalOverflow = HorizontalWrapMode.Wrap;
            consoleText.verticalOverflow = VerticalWrapMode.Overflow;
            consoleText.supportRichText = false;
            consoleText.raycastTarget = false;
            var consoleFitter = consoleContent.gameObject.AddComponent<ContentSizeFitter>();
            consoleFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            consoleScroll.viewport = consoleViewport;
            consoleScroll.content = consoleContent;

            statusBar = MakeRect(windowRect, "Status");
            statusBg = statusBar.gameObject.AddComponent<Image>();
            statusBg.color = FallbackBg;

            statusLeft = MakeText(statusBar, "", 10, FallbackMuted, TextAnchor.MiddleLeft, false);
            SetRect((RectTransform)statusLeft.transform, new Vector2(0f, 0f), new Vector2(0.55f, 1f), new Vector2(0f, 0.5f), new Vector2(-6f, 0f), new Vector2(6f, 0f));
            parseErrorText = MakeText(statusBar, "", 10, FallbackWarn, TextAnchor.MiddleCenter, false);
            SetRect((RectTransform)parseErrorText.transform, new Vector2(0.55f, 0f), new Vector2(0.85f, 1f), new Vector2(0.5f, 0.5f), new Vector2(-6f, 0f), new Vector2(0f, 0f));
            statusRight = MakeText(statusBar, "", 10, FallbackMuted, TextAnchor.MiddleRight, false);
            SetRect((RectTransform)statusRight.transform, new Vector2(0.85f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(-6f, 0f), new Vector2(-6f, 0f));

            hSep = MakeRect(windowRect, "SepH");
            hSepImg = hSep.gameObject.AddComponent<Image>();
            hSepImg.raycastTarget = false;

            var grip = new GameObject("Grip", typeof(RectTransform), typeof(Image));
            grip.transform.SetParent(windowRect, false);
            SetRect((RectTransform)grip.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(GripSize, GripSize), Vector2.zero);
            gripImg = grip.GetComponent<Image>();
            gripImg.color = FallbackBg;
            var gripDrag = grip.AddComponent<DragHandle>();
            gripDrag.owner = this;
            gripDrag.resize = true;
        }

        private static RectTransform MakeRect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static void SetRect(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 size, Vector2 anchored)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.sizeDelta = size;
            rt.anchoredPosition = anchored;
        }

        private static void ApplyRegion(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, y);
        }

        internal static Font UiFont
        {
            get
            {
                if (uiFont == null)
                {
                    try { uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
                    if (uiFont == null) { try { uiFont = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { } }
                }
                return uiFont;
            }
        }

        private Text MakeText(Transform parent, string text, int size, Color color, TextAnchor anchor, bool wrap)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = UiFont;
            t.text = text ?? "";
            t.fontSize = size;
            t.color = color;
            t.alignment = anchor;
            t.horizontalOverflow = wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.supportRichText = false;
            return t;
        }

        private Button MakeButton(Transform parent, string label, Action onClick, int fontSize)
        {
            var go = new GameObject("Btn_" + label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = Color.white;
            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            var layout = go.AddComponent<LayoutElement>();
            layout.preferredHeight = 22f;
            layout.flexibleWidth = 1f;

            var text = MakeText(go.transform, label, fontSize, fg, TextAnchor.MiddleCenter, false);
            var tr = (RectTransform)text.transform;
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(2f, 0f);
            tr.offsetMax = new Vector2(-2f, 0f);

            btn.onClick.AddListener(() => { try { onClick(); } catch (Exception e) { Fail(e); } });
            StyleButton(btn);
            return btn;
        }

        private Button MakeIcon(Transform parent, string glyph, Action onClick)
        {
            var button = MakeButton(parent, glyph, onClick, 11);
            var layout = button.GetComponent<LayoutElement>();
            if (layout != null)
            {
                layout.flexibleWidth = 0f;
                layout.preferredWidth = 20f;
                layout.preferredHeight = 18f;
            }
            return button;
        }

        private void StyleButton(Button button)
        {
            if (button == null) return;
            var img = button.targetGraphic as Image;
            if (img != null) img.color = Color.white;

            var colors = button.colors;
            colors.normalColor = btnCol;
            colors.highlightedColor = Color.Lerp(btnCol, fg, 0.14f);
            colors.pressedColor = Color.Lerp(btnCol, fg, 0.24f);
            colors.selectedColor = btnCol;
            colors.disabledColor = Shade(btnCol, 0.6f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            var text = button.GetComponentInChildren<Text>();
            if (text != null) text.color = fg;
        }

        // ---------------- layout / theme ----------------

        private void Layout()
        {
            bool right = !string.Equals(Plugin.ExplorerSide.Value, "Left", StringComparison.OrdinalIgnoreCase);
            float treeW = explorerCollapsed ? IdeLayout.CollapsedTreeWidth : Plugin.TreeWidth.Value;
            float termH = consoleVisible ? Plugin.ConsoleHeight.Value : 0f;
            float stripW = explorerCollapsed ? 0f : IdeLayout.ToolStripWidth;

            var r = IdeLayout.Compute(windowSize.x, windowSize.y, right, treeW, termH, stripW);
            windowRect.sizeDelta = windowSize;

            ApplyRegion(titleBar, r.TitleX, r.TitleY, r.TitleW, r.TitleH);
            ApplyRegion(treePanel, r.TreeX, r.TreeY, r.TreeW, r.TreeH);
            ApplyRegion(toolStrip, r.StripX, r.StripY, r.StripW, r.StripH);
            ApplyRegion(editorRect, r.EditorX, r.EditorY, r.EditorW, r.EditorH);
            ApplyRegion(consolePanel, r.TerminalX, r.TerminalY, r.TerminalW, r.TerminalH);
            ApplyRegion(statusBar, r.StatusX, r.StatusY, r.StatusW, r.StatusH);
            ApplyRegion(hSep, r.HSepX, r.HSepY, r.HSepW, r.HSepH);

            bool showConsole = consoleVisible && termH > 1f;
            consolePanel.gameObject.SetActive(showConsole);
            hSep.gameObject.SetActive(showConsole);

            treeHeader.gameObject.SetActive(!explorerCollapsed);
            treeScrollRect.gameObject.SetActive(!explorerCollapsed);
            toolStrip.gameObject.SetActive(!explorerCollapsed);
            collapseText.text = explorerCollapsed ? ">" : "<";

            treeScrollRect.offsetMin = new Vector2(4f, 4f);
            treeScrollRect.offsetMax = new Vector2(-4f, -22f);

            consoleScrollRect.offsetMin = new Vector2(4f, 4f);
            consoleScrollRect.offsetMax = new Vector2(-4f, -22f);
        }

        private void ApplyTheme()
        {
            bg = FallbackBg;
            fg = FallbackFg;
            muted = FallbackMuted;
            accent = FallbackAccent;
            warn = FallbackWarn;

            try
            {
                var theme = ThemeManager.Inst != null ? ThemeManager.Inst.Theme : null;
                if (theme != null && theme.ui != null)
                {
                    var frame = theme.ui.WindowFrameColor;
                    bg = new Color(frame.r / 255f, frame.g / 255f, frame.b / 255f, 1f);
                    if (theme.ui.text != null)
                    {
                        var fgc = theme.ui.text.TextColor;
                        fg = new Color(fgc.r / 255f, fgc.g / 255f, fgc.b / 255f, 1f);
                    }
                    if (theme.ui.button != null)
                    {
                        var ac = theme.ui.button.NormalColor;
                        accent = new Color(ac.r / 255f, ac.g / 255f, ac.b / 255f, 1f);
                    }
                    muted = Color.Lerp(fg, bg, 0.35f);
                }
            }
            catch { }

            titleCol = Shade(bg, 1.07f);
            treeCol = Shade(bg, 0.92f);
            consoleCol = Shade(bg, 0.86f);
            statusCol = Shade(bg, 1.03f);
            sepCol = Color.Lerp(bg, fg, 0.22f);
            btnCol = Color.Lerp(bg, accent, 0.35f);
            btnCol.a = 1f;

            titleBarBg.color = titleCol;
            windowBg.color = bg;
            toolStripBg.color = titleCol;
            treeBg.color = treeCol;
            consoleBg.color = consoleCol;
            statusBg.color = statusCol;
            hSepImg.color = sepCol;
            gripImg.color = sepCol;

            titleText.color = fg;
            treeTitle.color = muted;
            statusLeft.color = muted;
            parseErrorText.color = warn;
            statusRight.color = muted;
            consoleText.color = muted;

            foreach (var b in themedButtons) StyleButton(b);
            if (editor != null) editor.SetColors(Shade(bg, 0.70f), fg, accent);
        }

        private static Color Shade(Color c, float factor)
        {
            return new Color(Mathf.Clamp01(c.r * factor), Mathf.Clamp01(c.g * factor), Mathf.Clamp01(c.b * factor), 1f);
        }

        // ---------------- tree ----------------

        private void ClearRows()
        {
            for (int i = treeContent.childCount - 1; i >= 0; i--)
            {
                var child = treeContent.GetChild(i).gameObject;
                child.transform.SetParent(null, false);
                DestroyImmediate(child);
            }
            rowInfos.Clear();
        }

        private void Refresh()
        {
            if (canvas == null) return;
            ApplyTheme();
            Layout();
            ClearRows();

            string dir = SaveDir();
            var ignore = IgnoreRules.Load(dir);
            var rootLeaves = new List<string>();
            var folderNames = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            var filesByFolder = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            lastFolders.Clear();

            try
            {
                if (Directory.Exists(dir))
                {
                    if (File.Exists(Path.Combine(dir, ".farmignore")))
                    {
                        AddRow(".farmignore", null, "", 0, false, ignoreSelected,
                            OpenIgnoreFile,
                            pos => { ignoreSelected = true; selectedLeaf = null; selectedFolder = ""; ShowTreeMenu(pos); });
                    }

                    foreach (var f in Directory.EnumerateFiles(dir, "*.py", SearchOption.TopDirectoryOnly))
                    {
                        var leaf = Path.GetFileNameWithoutExtension(f);
                        if (string.Equals(leaf, "__builtins__", StringComparison.OrdinalIgnoreCase)) continue;
                        if (ignore.IsIgnored(leaf + ".py", false)) continue;
                        rootLeaves.Add(leaf);
                    }

                    foreach (var rel in FolderMap.ListFolders(dir, Saver.IsValidPortableFileName, ignore))
                    {
                        folderNames.Add(rel);
                        lastFolders.Add(rel);
                    }

                    foreach (var f in FolderMap.Scan(dir, Saver.IsValidPortableFileName, ignore))
                    {
                        List<string> list;
                        if (!filesByFolder.TryGetValue(f.Folder, out list)) filesByFolder[f.Folder] = list = new List<string>();
                        if (!list.Contains(f.Leaf)) list.Add(f.Leaf);
                    }
                }
            }
            catch (Exception e) { status = "!error: " + e.Message; }

            rootLeaves.Sort(StringComparer.OrdinalIgnoreCase);

            foreach (var leaf in rootLeaves)
            {
                var l = leaf;
                AddRow(l, l, "", 0, false, selectedLeaf == l && selectedFolder.Length == 0 && !ignoreSelected,
                    () => { ignoreSelected = false; selectedLeaf = l; selectedFolder = ""; EnsureEditor(l, ""); Refresh(); },
                    pos => { ignoreSelected = false; selectedLeaf = l; selectedFolder = ""; ShowTreeMenu(pos); });
            }

            foreach (var folder in folderNames)
            {
                bool hidden = false;
                var parts = folder.Split('/');
                for (int i = 1; i < parts.Length; i++)
                {
                    var ancestor = string.Join("/", parts.Take(i).ToArray());
                    if (collapsed.Contains(ancestor)) { hidden = true; break; }
                }
                if (hidden) continue;

                int depth = parts.Length - 1;
                bool isCollapsed = collapsed.Contains(folder);
                int count = filesByFolder.ContainsKey(folder) ? filesByFolder[folder].Count : 0;
                var label = (isCollapsed ? "[+] " : "[-] ") + parts[parts.Length - 1] + "  (" + count + ")";
                var fo = folder;
                AddRow(label, null, fo, depth, true, selectedFolder == fo && string.IsNullOrEmpty(selectedLeaf) && !ignoreSelected,
                    () =>
                    {
                        if (editor != null) editor.CancelEditing();
                        ignoreSelected = false;
                        if (collapsed.Contains(fo)) collapsed.Remove(fo); else collapsed.Add(fo);
                        selectedLeaf = null;
                        selectedFolder = fo;
                        Refresh();
                    },
                    pos => { ignoreSelected = false; selectedLeaf = null; selectedFolder = fo; ShowTreeMenu(pos); });

                if (isCollapsed) continue;
                List<string> leaves;
                if (!filesByFolder.TryGetValue(fo, out leaves)) continue;
                leaves.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (var leaf in leaves)
                {
                    var l = leaf;
                    AddRow(l, l, fo, depth + 1, false, selectedLeaf == l && selectedFolder == fo && !ignoreSelected,
                        () => { ignoreSelected = false; selectedLeaf = l; selectedFolder = fo; EnsureEditor(l, fo); Refresh(); },
                        pos => { ignoreSelected = false; selectedLeaf = l; selectedFolder = fo; ShowTreeMenu(pos); });
                }
            }

            if (string.IsNullOrEmpty(status) && FolderSync.Conflicts.Count > 0)
                status = "!" + FolderSync.Conflicts.Count + " name conflict(s): " + FolderSync.Conflicts[0];

            if (!string.IsNullOrEmpty(selectedLeaf))
            {
                bool present = rootLeaves.Any(l => string.Equals(l, selectedLeaf, StringComparison.OrdinalIgnoreCase))
                    || filesByFolder.Values.Any(list => list.Any(l => string.Equals(l, selectedLeaf, StringComparison.OrdinalIgnoreCase)));
                if (!present && !ignoreSelected)
                {
                    var hiddenLeaf = selectedLeaf;
                    var hiddenFolder = selectedFolder;
                    AddRow(hiddenLeaf + "  (ignored)", hiddenLeaf, hiddenFolder, 0, false, true,
                        () => { ignoreSelected = false; selectedLeaf = hiddenLeaf; selectedFolder = hiddenFolder; EnsureEditor(hiddenLeaf, hiddenFolder); Refresh(); },
                        pos => { ignoreSelected = false; selectedLeaf = hiddenLeaf; selectedFolder = hiddenFolder; ShowTreeMenu(pos); },
                        true);
                }
            }

            if (deleteIcon != null) deleteIcon.interactable = selectedLeaf != null || ignoreSelected;
        }

        private void AddRow(string label, string leaf, string folder, int depth, bool isFolder, bool selected, Action onClick, Action<Vector2> onContext = null, bool mutedRow = false)
        {
            var go = new GameObject("Row", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(treeContent, false);
            var layout = go.GetComponent<LayoutElement>();
            layout.preferredHeight = 22f;
            layout.minHeight = 22f;

            var img = go.GetComponent<Image>();
            var baseColor = selected ? Color.Lerp(treeCol, accent, 0.35f) : treeCol;
            img.color = baseColor;

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => { try { onClick(); } catch (Exception e) { Fail(e); } });

            if (onContext != null)
            {
                var rightClick = go.AddComponent<RightClick>();
                rightClick.onClick = onContext;
            }

            if (!isFolder && !string.IsNullOrEmpty(leaf))
            {
                var press = go.AddComponent<RowPress>();
                press.owner = this;
                press.leaf = leaf;
                press.folder = folder ?? "";
            }

            rowInfos.Add(new RowInfo
            {
                rect = (RectTransform)go.transform,
                folder = folder,
                leaf = leaf,
                isFolder = isFolder,
                image = img,
                baseColor = baseColor
            });

            var text = MakeText(go.transform, label, 12, mutedRow ? muted : (isFolder ? accent : fg), TextAnchor.MiddleLeft, false);
            var tr = (RectTransform)text.transform;
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(6f + depth * 12f, 0f);
            tr.offsetMax = new Vector2(-6f, 0f);
        }

        private void ToggleExplorer()
        {
            explorerCollapsed = !explorerCollapsed;
            Layout();
        }

        private void ToggleAllCollapsed()
        {
            if (collapsed.Count == 0)
            {
                foreach (var folder in lastFolders) collapsed.Add(folder);
            }
            else
            {
                collapsed.Clear();
            }
            Refresh();
        }

        private void OpenIgnoreFile()
        {
            EnsureEditorCreated();
            string path = Path.Combine(SaveDir(), ".farmignore");
            try
            {
                if (!File.Exists(path)) File.WriteAllText(path, "# one glob per line: docs/  *.md  farm/**\n");
            }
            catch (Exception e) { Fail(e); return; }
            ignoreSelected = true;
            selectedLeaf = null;
            selectedFolder = "";
            if (editor != null)
            {
                editor.BindFile(path);
                editor.Focus();
            }
            status = "editing .farmignore";
            dirty = true;
            Refresh();
        }

        // ---------------- drag and drop ----------------

        private bool MoveFile(string leaf, string from, string to)
        {
            if (string.IsNullOrEmpty(leaf)) return false;
            to = FolderMap.NormalizeFolder(to);
            if (string.Equals(from ?? "", to, StringComparison.OrdinalIgnoreCase)) return false;
            if (!FolderMap.IsValidFolder(to, Saver.IsValidPortableFileName))
            {
                status = "!invalid folder path";
                return false;
            }

            string rel = (string.IsNullOrEmpty(from) ? "" : from + "/") + leaf;
            if (LeafTaken(leaf, rel, leaf))
            {
                status = "!a file with that name exists in the target folder";
                return false;
            }

            string message;
            if (!FolderMap.TryMoveFile(SaveDir(), leaf, from ?? "", to, out message))
            {
                status = "!" + message;
                return false;
            }

            FolderMap.SetFolder(leaf, to);
            if (string.Equals(selectedLeaf, leaf, StringComparison.OrdinalIgnoreCase)) selectedFolder = to;
            status = "moved " + leaf + " to " + (to.Length == 0 ? "(root)" : to);
            return true;
        }

        // ---------------- context menus ----------------

        private void ShowTreeMenu(Vector2 screenPos)
        {
            bool hasIgnore = File.Exists(Path.Combine(SaveDir(), ".farmignore"));
            var items = new List<MenuItem>
            {
                new MenuItem("New file", true, NewFile),
                new MenuItem("New folder", true, NewFolder),
                new MenuItem("Rename", selectedLeaf != null, RenameSelected),
                new MenuItem("Move", selectedLeaf != null, MoveSelected),
                new MenuItem(ignoreSelected ? "Delete .farmignore" : "Delete", selectedLeaf != null || ignoreSelected, DeleteSelected),
                new MenuItem(hasIgnore ? "Edit .farmignore" : "New .farmignore", true, OpenIgnoreFile),
                new MenuItem("Refresh", true, delegate { dirty = true; Refresh(); })
            };
            ShowMenu(screenPos, items);
        }

        private void ShowTerminalMenu(Vector2 screenPos)
        {
            var items = new List<MenuItem>
            {
                new MenuItem("Copy output", true, delegate
                {
                    var logger = GetLogger();
                    GUIUtility.systemCopyBuffer = logger != null ? (logger.GetOutputString() ?? "") : (consoleText != null ? consoleText.text : "");
                    status = "output copied";
                }),
                new MenuItem("Clear", true, ClearConsole)
            };
            ShowMenu(screenPos, items);
        }

        private void ShowMenu(Vector2 screenPos, List<MenuItem> items)
        {
            CloseMenu();
            if (items == null || items.Count == 0) return;

            contextMenu = new GameObject("ContextMenu", typeof(RectTransform), typeof(Image), typeof(Button));
            contextMenu.transform.SetParent(canvas.transform, false);
            var rootRt = (RectTransform)contextMenu.transform;
            SetRect(rootRt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;
            var blocker = contextMenu.GetComponent<Image>();
            blocker.color = new Color(0f, 0f, 0f, 0.01f);
            var blockerButton = contextMenu.GetComponent<Button>();
            blockerButton.transition = Selectable.Transition.None;
            blockerButton.onClick.AddListener(CloseMenu);

            float width = 180f;
            float height = 8f + items.Count * 22f;

            var panelGo = new GameObject("List", typeof(RectTransform), typeof(Image));
            panelGo.transform.SetParent(contextMenu.transform, false);
            var panelRt = (RectTransform)panelGo.transform;
            SetRect(panelRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 1f), new Vector2(width, height), Vector2.zero);
            panelGo.GetComponent<Image>().color = statusCol;

            var layout = panelGo.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 2f;
            layout.padding = new RectOffset(4, 4, 4, 4);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            foreach (var item in items)
            {
                var menuItem = item;
                var button = MakeButton(panelGo.transform, menuItem.label, delegate
                {
                    CloseMenu();
                    if (menuItem.action != null) menuItem.action();
                }, 11);
                var le = button.GetComponent<LayoutElement>();
                if (le != null) le.preferredHeight = 20f;
                button.interactable = menuItem.enabled;
            }

            var canvasRt = (RectTransform)canvas.transform;
            Rect cr = canvasRt.rect;
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRt, screenPos, null, out local)) return;
            Vector2 pos = local + new Vector2(6f, -6f);
            pos.x = Mathf.Clamp(pos.x, -cr.width * 0.5f, cr.width * 0.5f - width);
            pos.y = Mathf.Clamp(pos.y, -cr.height * 0.5f + height, cr.height * 0.5f);
            panelRt.anchoredPosition = pos;
        }

        private void CloseMenu()
        {
            if (contextMenu != null)
            {
                DestroyImmediate(contextMenu);
                contextMenu = null;
            }
        }

        private void UpdateStatusBar()
        {
            string save = "";
            try { save = OptionHolder.GetString("activeSave", "Save0"); } catch { }
            string selected = ignoreSelected
                ? ".farmignore"
                : (selectedLeaf == null
                    ? "(no script selected)"
                    : (selectedFolder.Length > 0 ? selectedFolder + "/" + selectedLeaf : selectedLeaf));

            string pos = "";
            if (editor != null && (selectedLeaf != null || ignoreSelected))
            {
                int caret = Mathf.Clamp(editor.Caret, 0, editor.Text.Length);
                string text = editor.Text;
                int line = 1, col = 1;
                for (int i = 0; i < caret; i++)
                {
                    if (text[i] == '\n') { line++; col = 1; }
                    else col++;
                }
                pos = "Ln " + line + ", Col " + col;
            }

            statusLeft.text = save + " - " + selected + (status.Length > 0 ? " - " + status : "");
            statusRight.text = (pos.Length > 0 ? pos + " - " : "") + "plugin " + Plugin.PluginVersion;
        }

        // ---------------- console ----------------

        private Logger GetLogger()
        {
            try
            {
                if (LoggerField == null || MainSim.Inst == null) return null;
                return LoggerField.GetValue(MainSim.Inst) as Logger;
            }
            catch { return null; }
        }

        private void UpdateConsole(bool force)
        {
            if (!consoleVisible && !force) return;
            string loggerText = "";
            try
            {
                var logger = GetLogger();
                if (logger != null) loggerText = logger.GetOutputString() ?? "";
            }
            catch { }

            // Comment: quick_print lands in output.txt, not the logger list; show both.
            string printed = ReadPrintedOutput();
            string text = printed;
            if (loggerText.Length > 0) text = text.Length > 0 ? text + "\n" + loggerText : loggerText;

            if (!force && text == lastConsole) return;
            lastConsole = text;
            if (text.Length > 16000) text = "..." + text.Substring(text.Length - 16000);
            consoleText.text = text;
            Canvas.ForceUpdateCanvases();
            if (consoleScroll != null) consoleScroll.verticalNormalizedPosition = 0f;
        }

        private static string OutputFilePath()
        {
            try { return Path.Combine(Helper.persistentDataPath, "output.txt"); }
            catch { return Path.Combine(SaveDir(), "output.txt"); }
        }

        private string ReadPrintedOutput()
        {
            try
            {
                string path = OutputFilePath();
                if (!File.Exists(path)) return "";
                DateTime stamp = File.GetLastWriteTimeUtc(path);
                if (stamp == outputStamp) return outputCache;

                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    long start = Math.Max(0L, stream.Length - 16000L);
                    stream.Seek(start, SeekOrigin.Begin);
                    var buffer = new byte[stream.Length - start];
                    int read = stream.Read(buffer, 0, buffer.Length);
                    outputCache = System.Text.Encoding.UTF8.GetString(buffer, 0, read);
                }
                outputStamp = stamp;
                return outputCache;
            }
            catch { return ""; }
        }

        private void ClearConsole()
        {
            try
            {
                var logger = GetLogger();
                if (logger != null) logger.Clear();
            }
            catch (Exception e) { Plugin.Log?.LogWarning("[Folders] clear console: " + e.Message); }
            try
            {
                File.WriteAllText(OutputFilePath(), "");
                outputCache = "";
                outputStamp = DateTime.MinValue;
            }
            catch (Exception e) { Plugin.Log?.LogWarning("[Folders] clear output file: " + e.Message); }
            lastConsole = null;
            UpdateConsole(true);
        }

        private void ToggleConsole()
        {
            consoleVisible = !consoleVisible;
            Layout();
            if (consoleVisible) UpdateConsole(true);
            if (terminalCollapseBtn != null)
            {
                var t = terminalCollapseBtn.GetComponentInChildren<Text>();
                if (t != null) t.text = consoleVisible ? "v" : ">";
            }
        }

        // ---------------- actions ----------------

        internal static string SaveDir()
        {
            try { return Saver.GetPathOfSaveDirectory(OptionHolder.GetString("activeSave", "Save0")); }
            catch { return Helper.persistentDataPath + "/Saves/Save0"; }
        }

        private string PathFor(string leaf, string folder)
        {
            string dir = SaveDir();
            return folder.Length == 0
                ? Path.Combine(dir, leaf + ".py")
                : Path.Combine(dir, folder.Replace('/', Path.DirectorySeparatorChar), leaf + ".py");
        }

        private bool Busy()
        {
            if (Saver.IsSaving)
            {
                status = "!the game is saving, try again in a moment";
                return true;
            }
            return false;
        }

        private bool LeafTaken(string leaf, string exceptRel, string ignoreWindowLeaf = null)
        {
            string dir = SaveDir();
            try
            {
                if (FolderMap.HasRootLeaf(dir, leaf, exceptRel)) return true;
                var ignore = IgnoreRules.Load(dir);
                foreach (var f in FolderMap.Scan(dir, Saver.IsValidPortableFileName, ignore))
                {
                    var rel = f.Folder + "/" + f.Leaf;
                    if (string.Equals(f.Leaf, leaf, StringComparison.OrdinalIgnoreCase) && !string.Equals(rel, exceptRel, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch { }

            var ws = MainSim.Inst != null ? MainSim.Inst.workspace : null;
            if (ws != null)
            {
                foreach (var cw in ws.codeWindows.Values)
                {
                    if (string.Equals(cw.fileName, leaf, StringComparison.OrdinalIgnoreCase)
                        && (ignoreWindowLeaf == null || !string.Equals(cw.fileName, ignoreWindowLeaf, StringComparison.OrdinalIgnoreCase)))
                        return true;
                }
            }
            return false;
        }

        private void RunSelected()
        {
            if (ignoreSelected)
            {
                status = "!no script selected";
                return;
            }
            var ws = MainSim.Inst != null ? MainSim.Inst.workspace : null;
            Window win;
            if (ws == null || selectedLeaf == null || !ws.TryGetOpenWindow(selectedLeaf, out win))
            {
                status = "!open a script first";
                Refresh();
                return;
            }
            if (editor != null) editor.Flush();
            CodeWindow cw;
            if (!win.TryGetComponent<CodeWindow>(out cw)) return;
            try
            {
                cw.PressExecuteOrStop();
                status = "run/stop: " + selectedLeaf;
            }
            catch (Exception e) { Fail(e); }
        }

        private void SaveCode()
        {
            try
            {
                if (ignoreSelected)
                {
                    if (editor != null) editor.SaveNow();
                    status = "saving .farmignore";
                    dirty = true;
                    Refresh();
                    return;
                }
                if (editor != null) editor.Flush();
                Saver.Save(MainSim.Inst);
                status = "saving...";
            }
            catch (Exception e) { Fail(e); }
        }

        private void NewFile()
        {
            if (Busy()) { Refresh(); return; }
            ShowPrompt("New script", "Folder (empty = root):", selectedLeaf == null ? selectedFolder : "", "Name:", "", (folderText, nameText) =>
            {
                try
                {
                    string folder = FolderMap.NormalizeFolder(folderText);
                    string name = (nameText ?? "").Trim();
                    if (!FolderMap.IsValidFolder(folder, Saver.IsValidPortableFileName)) { status = "!invalid folder path"; return; }
                    if (!Saver.IsValidCodeFileName(name)) { status = "!invalid script name"; return; }
                    if (LeafTaken(name, null)) { status = "!the name '" + name + "' is already used"; return; }

                    string path = PathFor(name, folder);
                    if (File.Exists(path)) { status = "!file already exists"; return; }
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllText(path, "");
                    FolderMap.SetFolder(name, folder);

                    var ws = MainSim.Inst.workspace;
                    if (!ws.IsWindowOpen(name))
                    {
                        float step = ws.codeWindows.Count;
                        ws.OpenCodeWindow(name, "", ws.farmPosition.anchoredPosition + new Vector2(300f + 30f * step, -150f - 30f * step));
                    }
                    FolderMap.SetFolder(name, folder);
                    ignoreSelected = false;
                    selectedLeaf = name;
                    selectedFolder = folder;
                    EnsureEditor(name, folder);
                    status = "created " + (folder.Length == 0 ? name : folder + "/" + name);
                    Refresh();
                }
                catch (Exception e) { Fail(e); }
            });
        }

        private void NewFolder()
        {
            if (Busy()) { Refresh(); return; }
            ShowPrompt("New folder", "Folder path (e.g. cactus/hay):", selectedFolder, null, null, (folderText, _) =>
            {
                try
                {
                    string folder = FolderMap.NormalizeFolder(folderText);
                    if (folder.Length == 0 || !FolderMap.IsValidFolder(folder, Saver.IsValidPortableFileName))
                    {
                        status = "!invalid folder path";
                        return;
                    }
                    Directory.CreateDirectory(Path.Combine(SaveDir(), folder.Replace('/', Path.DirectorySeparatorChar)));
                    selectedFolder = folder;
                    selectedLeaf = null;
                    status = "created folder " + folder;
                    Refresh();
                }
                catch (Exception e) { Fail(e); }
            });
        }

        private void RenameSelected()
        {
            if (Busy() || selectedLeaf == null) { status = "!select a file first"; Refresh(); return; }
            string oldLeaf = selectedLeaf;
            string folder = selectedFolder;
            ShowPrompt("Rename", "New name:", oldLeaf, null, null, (nameText, _) =>
            {
                try
                {
                    string name = (nameText ?? "").Trim();
                    if (!Saver.IsValidCodeFileName(name)) { status = "!invalid script name"; return; }
                    if (string.Equals(name, oldLeaf, StringComparison.OrdinalIgnoreCase)) return;

                    string oldRel = (folder.Length == 0 ? "" : folder + "/") + oldLeaf;
                    if (LeafTaken(name, oldRel)) { status = "!the name '" + name + "' is already used"; return; }

                    var ws = MainSim.Inst.workspace;
                    Window win;
                    if (ws.TryGetOpenWindow(oldLeaf, out win))
                    {
                        CodeWindow cw;
                        if (win.TryGetComponent<CodeWindow>(out cw)) cw.Rename(name);
                        Window renamed;
                        if (!ws.TryGetOpenWindow(name, out renamed)) { status = "!the game rejected that name"; return; }
                    }

                    string oldPath = PathFor(oldLeaf, folder);
                    string newPath = PathFor(name, folder);
                    FolderMap.SetFolder(name, folder);
                    FolderMap.Remove(oldLeaf);

                    Directory.CreateDirectory(Path.GetDirectoryName(newPath));
                    if (File.Exists(newPath) && !string.Equals(newPath, oldPath, StringComparison.OrdinalIgnoreCase)) File.Delete(newPath);
                    if (File.Exists(oldPath)) File.Move(oldPath, newPath);

                    ignoreSelected = false;
                    selectedLeaf = name;
                    if (editor != null) EnsureEditor(name, folder);
                    status = "renamed to " + name;
                    Refresh();
                }
                catch (Exception e) { Fail(e); }
            });
        }

        private void MoveSelected()
        {
            if (Busy() || selectedLeaf == null) { status = "!select a file first"; Refresh(); return; }
            string leaf = selectedLeaf;
            string from = selectedFolder;
            ShowPrompt("Move to folder", "Folder (empty = root):", from, null, null, (folderText, _) =>
            {
                try
                {
                    MoveFile(leaf, from, FolderMap.NormalizeFolder(folderText));
                    Refresh();
                }
                catch (Exception e) { Fail(e); }
            });
        }

        private void DeleteSelected()
        {
            if (Busy()) { Refresh(); return; }
            if (ignoreSelected)
            {
                ShowConfirm("Delete .farmignore?", "The ignore file is removed from disk.", delegate
                {
                    try
                    {
                        string path = Path.Combine(SaveDir(), ".farmignore");
                        if (File.Exists(path)) File.Delete(path);
                        ignoreSelected = false;
                        if (editor != null) editor.Clear();
                        status = "deleted .farmignore";
                        dirty = true;
                        Refresh();
                    }
                    catch (Exception e) { Fail(e); }
                });
                return;
            }
            if (selectedLeaf == null) { status = "!select a file first"; Refresh(); return; }

            string leaf = selectedLeaf;
            string folder = selectedFolder;
            string rel = (folder.Length == 0 ? "" : folder + "/") + leaf;
            ShowConfirm("Delete " + rel + "?", "The file is removed from disk. The game's save backups may still contain it.", delegate
            {
                try
                {
                    var ws = MainSim.Inst.workspace;
                    Window win;
                    if (ws.TryGetOpenWindow(leaf, out win)) win.Close();
                    FolderMap.Remove(leaf);
                    string path = PathFor(leaf, folder);
                    if (File.Exists(path)) File.Delete(path);
                    selectedLeaf = null;
                    if (editor != null) editor.Clear();
                    status = "deleted " + rel;
                    Refresh();
                }
                catch (Exception e) { Fail(e); }
            });
        }

        private void Fail(Exception e)
        {
            status = "!error: " + e.Message;
            Plugin.Log?.LogError("[Folders] " + e);
            Refresh();
        }

        // ---------------- modal ----------------

        private void CloseModal()
        {
            if (modal != null)
            {
                DestroyImmediate(modal);
                modal = null;
            }
        }

        private GameObject BeginModal(string title, float height)
        {
            CloseModal();
            modal = new GameObject("Modal", typeof(RectTransform), typeof(Image));
            modal.transform.SetParent(canvas.transform, false);
            var rt = (RectTransform)modal.transform;
            SetRect(rt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            modal.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);

            var box = new GameObject("Box", typeof(RectTransform), typeof(Image));
            box.transform.SetParent(modal.transform, false);
            var brt = (RectTransform)box.transform;
            SetRect(brt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(440f, height), Vector2.zero);
            box.GetComponent<Image>().color = statusCol;

            var t = MakeText(box.transform, title, 15, fg, TextAnchor.MiddleLeft, false);
            SetRect((RectTransform)t.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(-24f, 24f), new Vector2(0f, -10f));

            var buttons = MakeRect(box.transform, "Buttons");
            SetRect(buttons, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(-24f, 26f), new Vector2(0f, 10f));
            var layout = buttons.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            return box;
        }

        private InputField ModalField(Transform box, string label, string value, float y)
        {
            var lt = MakeText(box, label, 11, muted, TextAnchor.MiddleLeft, false);
            SetRect((RectTransform)lt.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(-24f, 16f), new Vector2(0f, y));

            var go = new GameObject("Input", typeof(RectTransform), typeof(Image), typeof(InputField));
            go.transform.SetParent(box, false);
            SetRect((RectTransform)go.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(-24f, 24f), new Vector2(0f, y - 18f));
            var img = go.GetComponent<Image>();
            img.color = Shade(statusCol, 0.82f);

            var input = go.GetComponent<InputField>();
            input.targetGraphic = img;
            input.lineType = InputField.LineType.SingleLine;

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(go.transform, false);
            var tr = (RectTransform)textGo.transform;
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(6f, 0f);
            tr.offsetMax = new Vector2(-6f, 0f);
            var text = textGo.GetComponent<Text>();
            text.font = UiFont;
            text.fontSize = 13;
            text.color = fg;
            text.supportRichText = false;
            text.alignment = TextAnchor.MiddleLeft;
            text.raycastTarget = false;
            input.textComponent = text;
            input.text = value ?? "";
            return input;
        }

        private void ModalButtons(Transform box, Action ok)
        {
            var row = box.Find("Buttons");
            MakeButton(row, "Cancel", CloseModal, 12);
            MakeButton(row, "OK", ok, 12);
        }

        private void ShowPrompt(string title, string label1, string value1, string label2, string value2, Action<string, string> ok)
        {
            bool two = label2 != null;
            var box = BeginModal(title, two ? 210f : 150f);
            var f1 = ModalField(box.transform, label1, value1, -40f);
            InputField f2 = two ? ModalField(box.transform, label2, value2, -92f) : null;

            ModalButtons(box.transform, () =>
            {
                string v1 = f1 != null ? f1.text : null;
                string v2 = f2 != null ? f2.text : null;
                CloseModal();
                try { ok(v1, v2); } catch (Exception e) { Fail(e); }
            });
        }

        private void ShowConfirm(string title, string body, Action ok)
        {
            var box = BeginModal(title, 150f);
            var t = MakeText(box.transform, body, 11, muted, TextAnchor.UpperLeft, true);
            SetRect((RectTransform)t.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(-24f, 40f), new Vector2(0f, -40f));

            ModalButtons(box.transform, () =>
            {
                CloseModal();
                try { ok(); } catch (Exception e) { Fail(e); }
            });
        }
    }
}
