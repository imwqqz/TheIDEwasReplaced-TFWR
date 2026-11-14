using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TFWRFolders
{
    // Comment: drag and drop for explorer rows.
    internal sealed partial class IdePanel
    {
        private void BeginPress(string leaf, string folder, Vector2 screen, PointerEventData eventData)
        {
            if (string.IsNullOrEmpty(leaf) || suppressed) return;
            pressActive = true;
            pressLeaf = leaf;
            pressFolder = folder ?? "";
            pressPos = screen;
            pressEvent = eventData;
            if (contextMenu != null) CloseMenu();
            DebugLog("[Folders] row press " + leaf);
        }

        private void HandlePointer()
        {
            Vector2 mouse = Input.mousePosition;
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                CancelPress();
                return;
            }
            if (Input.GetMouseButtonUp(0))
            {
                EndPress(mouse);
                return;
            }
            if (!dragActive && (mouse - pressPos).sqrMagnitude > 100f) StartDrag(mouse);
            if (dragActive) UpdateRowDrag(mouse);
        }

        private void StartDrag(Vector2 screen)
        {
            if (dragActive || string.IsNullOrEmpty(pressLeaf)) return;
            dragActive = true;
            dragLeaf = pressLeaf;
            dragFolder = pressFolder;
            if (pressEvent != null) pressEvent.eligibleForClick = false;
            CloseMenu();

            dragGhost = new GameObject("DragGhost", typeof(RectTransform), typeof(Image));
            dragGhost.transform.SetParent(canvas.transform, false);
            var rt = (RectTransform)dragGhost.transform;
            SetRect(rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(150f, 20f), Vector2.zero);
            dragGhost.GetComponent<Image>().color = statusCol;
            dragGhostText = MakeText(dragGhost.transform, dragLeaf, 11, fg, TextAnchor.MiddleCenter, false);
            var tr = (RectTransform)dragGhostText.transform;
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(4f, 0f);
            tr.offsetMax = new Vector2(-4f, 0f);
            DebugLog("[Folders] drag start " + dragLeaf);
            UpdateRowDrag(screen);
        }

        private void EndPress(Vector2 screen)
        {
            pressActive = false;
            pressEvent = null;
            if (dragActive)
            {
                EndRowDrag(screen);
            }
            else
            {
                CleanupDrag();
            }
        }

        private void UpdateRowDrag(Vector2 screen)
        {
            if (!dragActive) return;
            PositionGhost(screen);

            RowInfo target = FolderRowFor(FindRowUnder(screen, 2f));

            if (hoverRow != null && hoverRow != target && hoverRow.image != null)
                hoverRow.image.color = hoverRow.baseColor;
            hoverRow = target;
            if (hoverRow != null && hoverRow.image != null)
                hoverRow.image.color = Color.Lerp(hoverRow.baseColor, accent, 0.45f);
        }

        // Comment: the folder that receives a drop, or null when the row refuses it.
        private RowInfo FolderRowFor(RowInfo row)
        {
            if (row == null) return null;
            string folder = DragTarget.FolderFor(row.folder, row.isFolder, row.leaf);
            if (folder == null) return null;
            if (row.isFolder) return row;
            if (folder.Length == 0) return null;
            foreach (var candidate in rowInfos)
            {
                if (candidate.isFolder && candidate.folder != null && string.Equals(candidate.folder, folder, StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }
            return null;
        }

        private void EndRowDrag(Vector2 screen)
        {
            if (!dragActive) return;
            pressActive = false;
            string leaf = dragLeaf;
            string from = dragFolder;

            RowInfo target = FindRowUnder(screen, 6f);
            string dropFolder = target == null ? null : DragTarget.FolderFor(target.folder, target.isFolder, target.leaf);
            bool overEditor = editorRect != null && RectTransformUtility.RectangleContainsScreenPoint(editorRect, screen, null);
            bool overTree = treePanel != null && RectTransformUtility.RectangleContainsScreenPoint(treePanel, screen, null);

            DebugLog("[Folders] drag end " + leaf + " -> " + (overEditor ? "editor" : (dropFolder != null ? "folder '" + dropFolder + "'" : (overTree && target == null ? "root" : "none"))));

            CleanupDrag();

            if (overEditor)
            {
                ignoreSelected = false;
                selectedLeaf = leaf;
                selectedFolder = from;
                EnsureEditor(leaf, from);
                status = "opened " + leaf;
                Refresh();
                return;
            }

            if (dropFolder != null)
            {
                MoveFile(leaf, from, dropFolder);
            }
            else if (overTree && target == null)
            {
                MoveFile(leaf, from, "");
            }
            Refresh();
        }

        private void CancelPress()
        {
            pressActive = false;
            pressEvent = null;
            if (dragActive)
            {
                CleanupDrag();
                Refresh();
            }
        }

        private void CleanupDrag()
        {
            dragActive = false;
            if (dragGhost != null)
            {
                DestroyImmediate(dragGhost);
                dragGhost = null;
                dragGhostText = null;
            }
            if (hoverRow != null && hoverRow.image != null) hoverRow.image.color = hoverRow.baseColor;
            hoverRow = null;
        }

        private void PositionGhost(Vector2 screen)
        {
            if (dragGhost == null) return;
            var canvasRt = (RectTransform)canvas.transform;
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRt, screen, null, out local)) return;
            ((RectTransform)dragGhost.transform).anchoredPosition = local + new Vector2(12f, -12f);
        }

        private RowInfo FindRowUnder(Vector2 screen, float pad = 0f)
        {
            foreach (var row in rowInfos)
            {
                if (row == null || row.rect == null) continue;
                Vector2 local;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(row.rect, screen, null, out local)) continue;
                Rect bounds = row.rect.rect;
                if (DragTarget.Contains(local.x, local.y, bounds.xMin, bounds.xMax, bounds.yMin, bounds.yMax, pad)) return row;
            }
            return null;
        }
    }
}
