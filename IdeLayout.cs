using System;

namespace TFWRFolders
{
    // Comment: coords are window-local, origin bottom-left.
    public struct IdeRegions
    {
        public float Width, Height;
        public float TitleX, TitleY, TitleW, TitleH;
        public float TreeX, TreeY, TreeW, TreeH;
        public float EditorX, EditorY, EditorW, EditorH;
        public float TerminalX, TerminalY, TerminalW, TerminalH;
        public float StatusX, StatusY, StatusW, StatusH;
        public float SepX, SepY, SepW, SepH;
        public float HSepX, HSepY, HSepW, HSepH;
        public bool ExplorerRight;
    }

    public static class IdeLayout
    {
        public const float TitleHeight = 26f;
        public const float StatusHeight = 18f;
        public const float Separator = 1f;
        public const float CollapsedTreeWidth = 22f;

        public static IdeRegions Compute(float width, float height, bool explorerRight, float treeWidth, float terminalHeight)
        {
            var r = new IdeRegions { Width = width, Height = height, ExplorerRight = explorerRight };

            float w = Math.Max(320f, width);
            float h = Math.Max(240f, height);
            treeWidth = Clamp(treeWidth, CollapsedTreeWidth, w * 0.6f);
            terminalHeight = Clamp(terminalHeight, 0f, h * 0.6f);

            r.TitleX = 0f; r.TitleY = h - TitleHeight; r.TitleW = w; r.TitleH = TitleHeight;
            r.StatusX = 0f; r.StatusY = 0f; r.StatusW = w; r.StatusH = StatusHeight;
            r.TerminalX = 0f; r.TerminalY = StatusHeight; r.TerminalW = w; r.TerminalH = terminalHeight;

            float bodyBottom = StatusHeight + terminalHeight + Separator;
            float bodyTop = r.TitleY - Separator;
            float bodyH = Math.Max(10f, bodyTop - bodyBottom);

            r.HSepX = 0f;
            r.HSepY = bodyBottom - Separator;
            r.HSepW = w;
            r.HSepH = Separator;

            r.TreeY = bodyBottom;
            r.TreeH = bodyH;
            r.EditorY = bodyBottom;
            r.EditorH = bodyH;

            float treeW = Math.Min(treeWidth, Math.Max(CollapsedTreeWidth, w - 120f));
            if (explorerRight)
            {
                r.TreeX = w - treeW;
                r.TreeW = treeW;
                r.SepX = r.TreeX - Separator;
                r.SepY = bodyBottom;
                r.SepW = Separator;
                r.SepH = bodyH;
                r.EditorX = 0f;
                r.EditorW = Math.Max(10f, r.TreeX - Separator);
            }
            else
            {
                r.TreeX = 0f;
                r.TreeW = treeW;
                r.SepX = treeW;
                r.SepY = bodyBottom;
                r.SepW = Separator;
                r.SepH = bodyH;
                r.EditorX = treeW + Separator;
                r.EditorW = Math.Max(10f, w - r.EditorX);
            }
            return r;
        }

        private static float Clamp(float value, float min, float max)
        {
            return value < min ? min : (value > max ? max : value);
        }
    }
}
