namespace WindowSill.ClipboardHistory.Core;

internal sealed class WinVShortcutState
{
    private bool _captured;
    private bool _pending;

    internal (bool Suppress, bool MaskMenu, bool Open) Process(uint key, bool down, bool up,
        bool windowsDown, bool otherModifierDown, bool otherWindowsDown)
    {
        if (up && key is 0x5B or 0x5C && _pending && !otherWindowsDown)
        {
            _pending = false;
            return (false, false, true);
        }
        if (key != 0x56) return default;
        if (down && _captured) return (true, false, false);
        if (down && windowsDown && !otherModifierDown)
        {
            _captured = true;
            _pending = true;
            return (true, true, false);
        }
        if (up && _captured)
        {
            _captured = false;
            return (true, false, false);
        }
        return default;
    }

    internal void Reset() { _captured = false; _pending = false; }
}
