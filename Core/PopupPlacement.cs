namespace WindowSill.ClipboardHistory.Core;

internal readonly record struct ScreenPoint(int X, int Y);
internal readonly record struct ScreenRect(int X, int Y, int Width, int Height);

internal static class PopupPlacement
{
    internal static ScreenRect? CaretOnPointerMonitor(ScreenRect? caret, ScreenRect monitor)
        => caret is { Width: > 0, Height: > 0 } rect
        && rect.X >= monitor.X && (long)rect.X < (long)monitor.X + monitor.Width
        && rect.Y >= monitor.Y && (long)rect.Y < (long)monitor.Y + monitor.Height
            ? rect : null;

    internal static (int X, int Y) Automatic(int? caretX, int? caretY, int caretHeight,
        int width, int height, int workX, int workY, int workWidth, int workHeight, int margin)
    {
        int x = caretX ?? workX + workWidth - width - margin;
        int y = caretY.HasValue ? caretY.Value + caretHeight + margin : workY + workHeight - height - margin;
        if (caretY.HasValue && y + height > workY + workHeight - margin) y = caretY.Value - height - margin;
        return Clamp(x, y, width, height, workX + margin, workY + margin,
            Math.Max(0, workWidth - margin * 2), Math.Max(0, workHeight - margin * 2));
    }

    internal static (int X, int Y) Clamp(int x, int y, int width, int height,
        int workX, int workY, int workWidth, int workHeight)
        => (Math.Clamp(x, workX, workX + Math.Max(0, workWidth - width)),
            Math.Clamp(y, workY, workY + Math.Max(0, workHeight - height)));
}
