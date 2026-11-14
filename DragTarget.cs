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
    }
}
