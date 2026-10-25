namespace WindowSill.ClipboardHistory.Core;

internal static class PopupPlacement
{
    internal static (int X, int Y) Clamp(int x, int y, int width, int height,
        int workX, int workY, int workWidth, int workHeight)
        => (Math.Clamp(x, workX, workX + Math.Max(0, workWidth - width)),
            Math.Clamp(y, workY, workY + Math.Max(0, workHeight - height)));
}
