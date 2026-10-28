using System.Runtime.InteropServices;

namespace WindowSill.ClipboardHistory.Core;

internal sealed record PopupInvocationContext(nint TargetWindow, ScreenRect? Caret, ScreenRect WorkArea, double Scale)
{
    private static int _accessibleProbeRunning;
    internal ScreenPoint? Pointer { get; init; }
    internal ScreenRect? Anchor => Caret ?? (Pointer is { } pointer ? new ScreenRect(pointer.X, pointer.Y, 1, 0) : null);
    internal uint TargetProcessId { get; init; }
    internal static nint ForegroundWindow => GetForegroundWindow();
    internal static uint ProcessIdOf(nint window) { GetWindowThreadProcessId(window, out uint id); return id; }
    internal static bool IsTargetActive(nint window, uint processId)
        => window != 0 && processId != 0 && GetForegroundWindow() == window && ProcessIdOf(window) == processId;
    internal static bool IsOwnWindowActive() => ProcessIdOf(GetForegroundWindow()) == Environment.ProcessId;
    internal static ScreenPoint CapturePointer()
    {
        if (!GetCursorPos(out var point)) throw new InvalidOperationException("The pointer position is unavailable.");
        return new(point.X, point.Y);
    }

    internal static async Task<PopupInvocationContext> CaptureAsync(nint window, ScreenPoint pointer)
    {
        // Choose the monitor from the invocation snapshot, before a caret provider can time out
        // or the host can change focus to one of its windows on another display.
        nint monitor = MonitorFromPoint(new Point { X = pointer.X, Y = pointer.Y }, 2);
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) throw new InvalidOperationException("The popup monitor is unavailable.");
        var gui = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        bool externalWindow = window != 0 && ProcessIdOf(window) != Environment.ProcessId;
        nint focus = externalWindow ? window : 0;
        ScreenRect? caret = null;
        if (externalWindow && GetGUIThreadInfo(GetWindowThreadProcessId(window, out _), ref gui))
        {
            focus = gui.Focus != 0 ? gui.Focus : window;
            if (gui.Caret != 0 && gui.CaretRect.Bottom > gui.CaretRect.Top)
            {
                var point = new Point { X = gui.CaretRect.Left, Y = gui.CaretRect.Top };
                if (ClientToScreen(gui.Caret, ref point))
                    caret = new ScreenRect(point.X, point.Y, Math.Max(1, gui.CaretRect.Right - gui.CaretRect.Left), gui.CaretRect.Bottom - gui.CaretRect.Top);
            }
        }
        if (caret is null && focus != 0 && Interlocked.CompareExchange(ref _accessibleProbeRunning, 1, 0) == 0)
        {
            // Custom editors may expose a caret through MSAA. Never wait for them on an input or UI thread.
            // A timed-out provider may still be blocked. Do not start more probes until it returns.
            try
            {
                caret = await Task.Run(() =>
                {
                    try { return ReadAccessibleCaret(focus); }
                    finally { Volatile.Write(ref _accessibleProbeRunning, 0); }
                }).WaitAsync(TimeSpan.FromMilliseconds(40)).ConfigureAwait(false);
            }
            catch (TimeoutException) { }
        }
        caret = PopupPlacement.CaretOnPointerMonitor(caret,
            new(info.Bounds.Left, info.Bounds.Top, info.Bounds.Right - info.Bounds.Left, info.Bounds.Bottom - info.Bounds.Top));
        double scale = GetDpiForMonitor(monitor, 0, out uint dpi, out _) == 0 && dpi > 0 ? dpi / 96.0 : 1;
        return new(window, caret, new(info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top), scale)
        { TargetProcessId = ProcessIdOf(window), Pointer = pointer };
    }

    private static ScreenRect? ReadAccessibleCaret(nint window)
    {
        object? accessible = null;
        try
        {
            var iid = new Guid("618736e0-3c3d-11cf-810c-00aa00389b71");
            if (AccessibleObjectFromWindow(window, 0xfffffff8, ref iid, out accessible) != 0 || accessible is not IAccessibleCaret caret) return null;
            caret.accLocation(out int x, out int y, out int width, out int height, 0);
            return height is > 0 and < 1000 && width is >= 0 and < 1000 ? new(x, y, Math.Max(1, width), height) : null;
        }
        catch (Exception) { return null; }
        finally { if (accessible is not null && Marshal.IsComObject(accessible)) Marshal.ReleaseComObject(accessible); }
    }

    [ComImport, Guid("618736e0-3c3d-11cf-810c-00aa00389b71"), InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    private interface IAccessibleCaret
    {
        [DispId(-5015)] void accLocation(out int x, out int y, out int width, out int height, [MarshalAs(UnmanagedType.Struct)] object child);
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point { internal int X; internal int Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { internal int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { internal uint Size; internal Rect Bounds, Work; internal uint Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct GuiThreadInfo
    {
        internal uint Size, Flags;
        internal nint Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        internal Rect CaretRect;
    }
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ClientToScreen(nint window, ref Point point);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern nint MonitorFromPoint(Point point, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(nint monitor, int type, out uint x, out uint y);
    [DllImport("oleacc.dll")] private static extern int AccessibleObjectFromWindow(nint window, uint id, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object? accessible);
}
