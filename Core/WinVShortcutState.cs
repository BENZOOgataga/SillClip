namespace WindowSill.ClipboardHistory.Core;

internal sealed class WinVShortcutState
{
    private bool _captured;

    internal (bool Suppress, bool MaskMenu, bool Open) Process(uint key, bool down, bool up,
        bool windowsDown, bool otherModifierDown, bool otherWindowsDown)
    {
        if (key != 0x56) return default;
        if (down && _captured) return (true, false, false);
        if (down && windowsDown && !otherModifierDown)
        {
            _captured = true;
            return (true, true, true);
        }
        if (up && _captured)
        {
            _captured = false;
            return (true, false, false);
        }
        return default;
    }

    internal void Reset() { _captured = false; }
}
