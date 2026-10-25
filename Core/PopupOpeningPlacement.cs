using System.Runtime.InteropServices;

namespace WindowSill.ClipboardHistory.Core;

/// <summary>Applies final coordinates to the SDK popup's initial native move, before its first visible frame.</summary>
internal sealed class PopupOpeningPlacement : IDisposable
{
    private readonly SubclassCallback _callback;
    private readonly nint _window;
    private readonly PopupInvocationContext _context;
    private readonly int _width, _height;
    private bool _pending = true;
    private bool _installed;
    private bool _applying;

    private (int X, int Y) DesiredPosition()
    {
        var area = _context.WorkArea;
        var caret = _context.Caret;
        return PopupPlacement.Automatic(caret?.X, caret?.Y, caret?.Height ?? 0,
            _width, _height, area.X, area.Y, area.Width, area.Height, (int)Math.Round(8 * _context.Scale));
    }

    internal PopupOpeningPlacement(nint window, PopupInvocationContext context, double width, double height)
    {
        _window = window;
        _context = context;
        _width = (int)Math.Round(width * context.Scale);
        _height = (int)Math.Round(height * context.Scale);
        _callback = Callback;
        _installed = SetWindowSubclass(window, _callback, 0x435042, 0);
        if (!_installed) throw new InvalidOperationException("The popup placement callback could not be installed.");
    }

    private nint Callback(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        try
        {
            if (message == 0x0046 && _pending && !_applying && lParam != 0)
            {
                var position = Marshal.PtrToStructure<WindowPosition>(lParam);
                if ((position.Flags & 0x0080) == 0) // Do not interfere with hiding the popup.
                {
                    var point = DesiredPosition();
                    position.X = point.X; position.Y = point.Y;
                    position.Width = _width; position.Height = _height;
                    position.Flags &= ~3u; // Apply move and size in the same native operation.
                    Marshal.StructureToPtr(position, lParam, false);
                }
            }
            if (message == 0x0082) Dispose();
        }
        catch { /* Exceptions must not escape a native window procedure. */ }
        nint result = DefSubclassProc(window, message, wParam, lParam);
        try
        {
            if (message == 0x0047 && _pending && !_applying)
            {
                if (IsWindowVisible(window)) _pending = false;
                else
                {
                    // Some hosts skip WINDOWPOSCHANGING. Correct their hidden move before Show returns.
                    var point = DesiredPosition();
                    if (GetWindowRect(window, out var actual) && (actual.Left != point.X || actual.Top != point.Y
                        || actual.Right - actual.Left != _width || actual.Bottom - actual.Top != _height))
                    {
                        _applying = true;
                        try { SetWindowPos(window, 0, point.X, point.Y, _width, _height, 0x414); }
                        finally { _applying = false; }
                    }
                }
            }
        }
        catch { }
        return result;
    }

    public void Dispose()
    {
        if (_installed) RemoveWindowSubclass(_window, _callback, 0x435042);
        _installed = false;
        _pending = false;
    }

    private delegate nint SubclassCallback(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data);
    [StructLayout(LayoutKind.Sequential)] private struct WindowPosition
    {
        internal nint Window, InsertAfter;
        internal int X, Y, Width, Height;
        internal uint Flags;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { internal int Left, Top, Right, Bottom; }
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowSubclass(nint window, SubclassCallback callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RemoveWindowSubclass(nint window, SubclassCallback callback, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
}
