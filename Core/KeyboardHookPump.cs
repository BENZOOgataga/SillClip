using System.Runtime.InteropServices;

namespace WindowSill.ClipboardHistory.Core;

/// <summary>Owns the keyboard hook and its message loop independently of XAML.</summary>
internal sealed class KeyboardHookPump
{
    internal delegate nint KeyboardCallback(int code, nuint message, nint data);
    private const uint Quit = 0x0012;
    private readonly KeyboardCallback _callback;
    private readonly Action _initialize;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private Session? _session;

    internal KeyboardHookPump(KeyboardCallback callback, Action initialize)
    {
        _callback = callback;
        _initialize = initialize;
    }

    internal bool IsActive => Volatile.Read(ref _session) is { Enabled: true, Handle: not 0 };

    internal async Task<bool> StartAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (IsActive) return true;
            if (_session is not null) await _session.Finished.Task.ConfigureAwait(false);
            var session = new Session();
            _session = session;
            var thread = new Thread(() => Run(session)) { IsBackground = true, Name = "Clipboard Win+V hook" };
            thread.Start();
            return await session.Started.Task.ConfigureAwait(false);
        }
        finally { _lifecycle.Release(); }
    }

    internal async Task StopAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            var session = _session;
            if (session is null) return;
            session.Enabled = false;
            if (!session.Finished.Task.IsCompleted)
                PostThreadMessage(session.ThreadId, Quit, 0, 0);
            await session.Finished.Task.ConfigureAwait(false);
            _session = null;
        }
        finally { _lifecycle.Release(); }
    }

    private void Run(Session session)
    {
        try
        {
            // Creating the queue before publishing Started makes StopAsync safe.
            PeekMessage(out _, 0, 0, 0, 0);
            session.ThreadId = GetCurrentThreadId();
            _initialize();
            session.Handle = SetWindowsHookEx(13, _callback, 0, 0);
            session.Enabled = session.Handle != 0;
            session.Started.TrySetResult(session.Enabled);
            if (!session.Enabled) return;
            while (GetMessage(out var message, 0, 0, 0) > 0)
            {
                TranslateMessage(in message);
                DispatchMessage(in message);
            }
        }
        catch (Exception ex) { session.Started.TrySetException(ex); }
        finally
        {
            session.Enabled = false;
            if (session.Handle != 0) UnhookWindowsHookEx(session.Handle);
            session.Handle = 0;
            session.Finished.TrySetResult();
        }
    }

    private sealed class Session
    {
        internal readonly TaskCompletionSource<bool> Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal volatile bool Enabled;
        internal nint Handle;
        internal uint ThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        internal nint Window;
        internal uint Id;
        internal nuint WParam;
        internal nint LParam;
        internal uint Time;
        internal int X;
        internal int Y;
        internal uint Private;
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int type, KeyboardCallback callback, nint module, uint threadId);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint handle);
    [DllImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out Message message, nint window, uint min, uint max, uint remove);
    [DllImport("user32.dll", EntryPoint = "GetMessageW")]
    private static extern int GetMessage(out Message message, nint window, uint min, uint max);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(in Message message);
    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static extern nint DispatchMessage(in Message message);
    [DllImport("user32.dll", EntryPoint = "PostThreadMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint threadId, uint message, nuint wParam, nint lParam);
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
