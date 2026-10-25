using System.Runtime.InteropServices;
using WindowSill.ClipboardHistory.Core;

int cases = 0;
foreach (double scale in new[] { 1.0, 1.25, 1.5, 2.0 })
foreach (bool hasCaret in new[] { false, true })
foreach (uint moveFlags in new uint[] { 0x14, 0x414 })
{
    nint window = CreateWindowEx(0, "STATIC", "Hidden placement probe", 0x80000000, 0, 0, 32, 32, 0, 0, 0, 0);
    if (window == 0) throw new Exception("Probe window creation failed.");
    try
    {
        var area = new ScreenRect(-1920, 0, 1920, 1080);
        ScreenRect? caret = hasCaret ? new ScreenRect(-1800, 200, 2, 20) : null;
        var context = new PopupInvocationContext(0, caret, area, scale);
        using (var placement = new PopupOpeningPlacement(window, context, 360, 400))
        {
            // Simulate the SDK moving its hidden popup beside the bar.
            SetWindowPos(window, 0, 50, 60, 360, 400, moveFlags);
            GetWindowRect(window, out var actual);
            int width = (int)Math.Round(360 * scale), height = (int)Math.Round(400 * scale);
            var expected = PopupPlacement.Automatic(caret?.X, caret?.Y, caret?.Height ?? 0,
                width, height, area.X, area.Y, area.Width, area.Height, (int)Math.Round(8 * scale));
            if (actual.Left != expected.X || actual.Top != expected.Y || actual.Right - actual.Left != width
                || actual.Bottom - actual.Top != height || IsWindowVisible(window)) throw new Exception("Pre-show placement mismatch.");
        }
        SetWindowPos(window, 0, 20, 30, 40, 50, 0x14);
        GetWindowRect(window, out var released);
        if (released.Left != 20 || released.Top != 30) throw new Exception("Placement callback remained installed.");
        cases++;
    }
    finally { DestroyWindow(window); }
}
Console.WriteLine($"{cases} native hidden-window placement cases passed at 100%, 125%, 150%, and 200% scale. No window was shown and no keyboard input was generated.");

[DllImport("user32.dll", EntryPoint="CreateWindowExW", CharSet=CharSet.Unicode)] static extern nint CreateWindowEx(uint exStyle, string cls, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
[DllImport("user32.dll")] static extern bool DestroyWindow(nint window);
[DllImport("user32.dll")] static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
[DllImport("user32.dll")] static extern bool GetWindowRect(nint window, out Rect rect);
[DllImport("user32.dll")] static extern bool IsWindowVisible(nint window);
[StructLayout(LayoutKind.Sequential)] struct Rect { internal int Left, Top, Right, Bottom; }
