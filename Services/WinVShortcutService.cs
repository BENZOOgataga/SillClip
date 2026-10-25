using System.ComponentModel.Composition;
using System.Runtime.InteropServices;
using WindowSill.ClipboardHistory.Core;
using Microsoft.Extensions.Logging;
using WindowSill.API;

namespace WindowSill.ClipboardHistory.Services;

/// <summary>
/// Replaces the Windows clipboard panel shortcut while this extension is active.
/// </summary>
[Export(typeof(WinVShortcutService))]
internal sealed class WinVShortcutService : IDisposable
{
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
    private const ushort DummyVirtualKey = 0xFF;
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 2;

    private readonly KeyboardHookPump _pump;
    private readonly ILogger _logger;
    private long _generation;
    private readonly WinVShortcutState _shortcutState = new();

    [ImportingConstructor]
    public WinVShortcutService()
    {
        _logger = this.Log();
        _pump = new KeyboardHookPump(HookCallback, _shortcutState.Reset);
    }

    internal event EventHandler? ShortcutPressed;

    internal bool IsActive => _pump.IsActive;

    internal Task<bool> StartAsync() => _pump.StartAsync();

    internal Task StopAsync()
    {
        Interlocked.Increment(ref _generation);
        return _pump.StopAsync();
    }

    public void Dispose()
    {
        StopAsync().Forget();
    }

    private nint HookCallback(int code, nuint message, nint keyboardDataAddress)
    {
        try
        {
            if (code >= 0 && IsActive)
            {
                var keyboardData = Marshal.PtrToStructure<KeyboardData>(keyboardDataAddress);
                // Ordinary gameplay keys and injected input never enter shortcut handling.
                if ((keyboardData.Flags & 0x10) != 0 || keyboardData.VirtualKey is not (VkV or VkLeftWindows or VkRightWindows))
                    return CallNextHookEx(0, code, message, keyboardDataAddress);
                uint keyboardMessage = unchecked((uint)message);
                bool isKeyDown = keyboardMessage is WmKeyDown or WmSysKeyDown;
                bool isKeyUp = keyboardMessage is WmKeyUp or WmSysKeyUp;

                int otherWindowsKey = keyboardData.VirtualKey == VkLeftWindows ? VkRightWindows : VkLeftWindows;
                var decision = _shortcutState.Process(keyboardData.VirtualKey, isKeyDown, isKeyUp,
                    IsWindowsKeyDown(), IsAnotherModifierDown(), IsKeyDown(otherWindowsKey));
                if (decision.MaskMenu) SendMenuMask();
                if (decision.Open)
                {
                    long generation = Volatile.Read(ref _generation);
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        if (generation != Volatile.Read(ref _generation) || !IsActive) return;
                        try { ShortcutPressed?.Invoke(this, EventArgs.Empty); }
                        catch (Exception ex) { _logger.LogWarning(ex, "The clipboard shortcut could not be opened."); }
                    });
                }
                if (decision.Suppress) return new nint(1);
            }
        }
        catch
        {
            // Native hook callbacks must never allow managed exceptions to escape.
        }

        return CallNextHookEx(0, code, message, keyboardDataAddress);
    }

    private static bool IsWindowsKeyDown()
        => IsKeyDown(VkLeftWindows) || IsKeyDown(VkRightWindows);

    private static bool IsAnotherModifierDown()
        => IsKeyDown(VkShift) || IsKeyDown(VkControl) || IsKeyDown(VkMenu);

    private static bool IsKeyDown(int virtualKey)
        => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private static void SendMenuMask()
    {
        NativeInput[] inputs =
        [
            new() { Type = InputKeyboard, Data = new InputData
                { Keyboard = new KeyboardInput { VirtualKey = DummyVirtualKey } } },
            new() { Type = InputKeyboard, Data = new InputData
                { Keyboard = new KeyboardInput { VirtualKey = DummyVirtualKey, Flags = KeyEventKeyUp } } }
        ];
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeInput>());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput
    {
        internal uint Type;
        internal InputData Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputData
    {
        [FieldOffset(0)] internal KeyboardInput Keyboard;
        [FieldOffset(0)] internal MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        internal ushort VirtualKey;
        internal ushort ScanCode;
        internal uint Flags;
        internal uint Time;
        internal nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        internal int X;
        internal int Y;
        internal uint MouseData;
        internal uint Flags;
        internal uint Time;
        internal nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct KeyboardData
    {
        internal readonly uint VirtualKey;
        internal readonly uint ScanCode;
        internal readonly uint Flags;
        internal readonly uint Time;
        internal readonly nuint ExtraInfo;
    }

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(
        nint hookHandle,
        int code,
        nuint message,
        nint keyboardDataAddress);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, NativeInput[] inputs, int size);
}
