using System.ComponentModel.Composition;
using System.Runtime.InteropServices;

namespace WindowSill.ClipboardHistory.Services;

/// <summary>
/// Replaces the Windows clipboard panel shortcut while this extension is active.
/// </summary>
[Export(typeof(WinVShortcutService))]
internal sealed class WinVShortcutService : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const uint WmSysKeyDown = 0x0104;
    private const uint WmSysKeyUp = 0x0105;
    private const uint VkV = 0x56;
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkLeftWindows = 0x5B;
    private const int VkRightWindows = 0x5C;

    private readonly LowLevelKeyboardProc _hookCallback;
    private nint _hookHandle;
    private bool _vKeyDown;
    private bool _suppressVKeyUp;

    [ImportingConstructor]
    public WinVShortcutService()
    {
        _hookCallback = HookCallback;
    }

    internal event EventHandler? ShortcutPressed;

    internal bool IsActive => _hookHandle != nint.Zero;

    internal bool Start()
    {
        if (IsActive)
        {
            return true;
        }

        _hookHandle = SetWindowsHookEx(WhKeyboardLl, _hookCallback, nint.Zero, 0);
        return IsActive;
    }

    internal void Stop()
    {
        if (_hookHandle != nint.Zero)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = nint.Zero;
        }

        _vKeyDown = false;
        _suppressVKeyUp = false;
    }

    public void Dispose()
    {
        Stop();
    }

    private nint HookCallback(int code, nuint message, nint keyboardDataAddress)
    {
        try
        {
            if (code >= 0)
            {
                var keyboardData = Marshal.PtrToStructure<KeyboardData>(keyboardDataAddress);
                if (keyboardData.VirtualKey == VkV)
                {
                    uint keyboardMessage = unchecked((uint)message);
                    bool isKeyDown = keyboardMessage is WmKeyDown or WmSysKeyDown;
                    bool isKeyUp = keyboardMessage is WmKeyUp or WmSysKeyUp;

                    if (isKeyDown && IsWindowsKeyDown() && !IsAnotherModifierDown())
                    {
                        _suppressVKeyUp = true;
                        if (!_vKeyDown)
                        {
                            _vKeyDown = true;
                            ShortcutPressed?.Invoke(this, EventArgs.Empty);
                        }

                        return new nint(1);
                    }

                    if (isKeyUp)
                    {
                        _vKeyDown = false;
                        if (_suppressVKeyUp)
                        {
                            _suppressVKeyUp = false;
                            return new nint(1);
                        }
                    }
                }
            }
        }
        catch
        {
            // Native hook callbacks must never allow managed exceptions to escape.
        }

        return CallNextHookEx(_hookHandle, code, message, keyboardDataAddress);
    }

    private static bool IsWindowsKeyDown()
        => IsKeyDown(VkLeftWindows) || IsKeyDown(VkRightWindows);

    private static bool IsAnotherModifierDown()
        => IsKeyDown(VkShift) || IsKeyDown(VkControl) || IsKeyDown(VkMenu);

    private static bool IsKeyDown(int virtualKey)
        => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct KeyboardData
    {
        internal readonly uint VirtualKey;
        internal readonly uint ScanCode;
        internal readonly uint Flags;
        internal readonly uint Time;
        internal readonly nuint ExtraInfo;
    }

    private delegate nint LowLevelKeyboardProc(int code, nuint message, nint keyboardDataAddress);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(
        int hookType,
        LowLevelKeyboardProc hookCallback,
        nint moduleHandle,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hookHandle);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(
        nint hookHandle,
        int code,
        nuint message,
        nint keyboardDataAddress);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
}
