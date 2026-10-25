namespace WindowSill.ClipboardHistory.Core;

/// <summary>Runs one refresh at a time and keeps one pending refresh for a burst of notifications.</summary>
internal sealed class CoalescingAsyncAction
{
    private readonly object _gate = new();
    private readonly Action<Exception> _onError;
    private Func<Task>? _pending;
    private Task? _running;

    internal CoalescingAsyncAction(Action<Exception> onError) => _onError = onError;

    internal Task RequestAsync(Func<Task> action)
    {
        lock (_gate)
        {
            _pending = action;
            return _running ??= Task.Run(DrainAsync);
        }
    }

    internal void CancelPending() { lock (_gate) _pending = null; }

    private async Task DrainAsync()
    {
        while (true)
        {
            Func<Task>? action;
            lock (_gate)
            {
                action = _pending;
                _pending = null;
                if (action is null) { _running = null; return; }
            }
            try { await action().ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _onError(ex); }
        }
    }
}
