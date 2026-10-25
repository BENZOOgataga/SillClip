namespace WindowSill.ClipboardHistory.Core;

internal static class PopupNavigation
{
    internal static int Next(int current, int count, int direction)
        => count <= 0 ? -1 : current < 0 ? 0 : Math.Clamp(current + direction, 0, count - 1);
}
