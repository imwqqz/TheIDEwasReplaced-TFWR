namespace TFWRFolders
{
    // Comment: padded hit test shared by the drop code and its tests.
    public static class DragTarget
    {
        public static bool Contains(float x, float y, float xMin, float xMax, float yMin, float yMax, float pad)
        {
            return x >= xMin - pad && x <= xMax + pad
                && y >= yMin - pad && y <= yMax + pad;
        }

        // folder row -> itself; file row -> its parent folder; null leaf (.farmignore) -> cancel
        public static string FolderFor(string rowFolder, bool isFolder, string leaf)
        {
            if (isFolder) return rowFolder ?? "";
            if (string.IsNullOrEmpty(leaf)) return null;
            return rowFolder ?? "";
        }
    }
}
