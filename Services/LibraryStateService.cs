using System.ComponentModel.Composition;
using System.Text.Json;
using WindowSill.API;
using WindowSill.ClipboardHistory.Core;
using Path = System.IO.Path;

namespace WindowSill.ClipboardHistory.Services;

[Export(typeof(LibraryStateService))]
internal sealed class LibraryStateService : IDisposable
{
    private readonly EncryptedJsonFile<LibraryState> _file;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Timer _timer;
    private LibraryState _snapshot = new() { PresentationMode = true, CaptureExclusions = [new(DateTimeOffset.MinValue, DateTimeOffset.MaxValue)] };
    private bool _loaded, _wasPaused;
    internal Exception? LoadError { get; private set; }
    internal event EventHandler? Changed;

    [ImportingConstructor]
    public LibraryStateService(IPluginInfo pluginInfo)
        : this(ClipboardDataMigration.ForkFolder(pluginInfo.GetPluginDataFolder())) { }

    internal LibraryStateService(string folder)
    {
        _file = new(Path.Combine(folder, "library-state.dat"));
        _timer = new Timer(_ =>
        {
            bool paused = PausedUntil is not null;
            if (paused != _wasPaused) { _wasPaused = paused; Changed?.Invoke(this, EventArgs.Empty); }
        }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    internal bool PresentationMode => Volatile.Read(ref _snapshot).PresentationMode;
    internal DateTimeOffset? PausedUntil => Volatile.Read(ref _snapshot).PausedUntil(DateTimeOffset.UtcNow);
    internal IReadOnlyList<string> Collections => Volatile.Read(ref _snapshot).Collections.ToArray();
    internal bool AllowsCapture(DateTimeOffset copiedAt) => _loaded && LoadError is null && Volatile.Read(ref _snapshot).AllowsCapture(copiedAt);
    internal bool AllowsItem(string signature, DateTimeOffset copiedAt) => _loaded && LoadError is null && Volatile.Read(ref _snapshot).AllowsItem(signature, copiedAt);
    internal bool AllowsPinnedItem(string signature, DateTimeOffset copiedAt) => _loaded && LoadError is null && Volatile.Read(ref _snapshot).AllowsPinnedItem(signature, copiedAt);
    internal IReadOnlyList<string> CollectionsFor(string signature)
        => Volatile.Read(ref _snapshot).Memberships.TryGetValue(signature, out var names) ? names.ToArray() : [];

    internal async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_loaded) return;
            var state = await Task.Run(() => _file.ReadAsync(cancellationToken), cancellationToken).ConfigureAwait(false) ?? new();
            if (state.Version != 1 || state.Collections is null || state.Memberships is null || state.Deleted is null || state.Restored is null
                || state.CaptureExclusions is null || state.CaptureExclusions.Any(range => range is null || range.End < range.Start))
                throw new InvalidDataException("Unsupported library metadata.");
            foreach (string name in state.Collections) LibraryState.ValidateCollectionName(name);
            if (state.Memberships.Values.Any(names => names is null)) throw new InvalidDataException("Invalid collection metadata.");
            Volatile.Write(ref _snapshot, state);
            _loaded = true;
            _wasPaused = PausedUntil is not null;
            _timer.Change(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) { LoadError = ex; throw; }
        finally { _gate.Release(); }
    }

    internal Task PauseAsync(TimeSpan? duration)
        => MutateAsync(state => state.Pause(DateTimeOffset.UtcNow, duration), protectImmediately: true);
    internal Task ResumeAsync() => MutateAsync(state => state.Resume(DateTimeOffset.UtcNow));
    internal Task SetPresentationAsync(bool enabled)
        => MutateAsync(state => state.PresentationMode = enabled, protectImmediately: enabled);

    internal Task CreateCollectionAsync(string value)
    {
        string name = LibraryState.ValidateCollectionName(value);
        return MutateAsync(state => { if (!state.Collections.Contains(name, StringComparer.OrdinalIgnoreCase)) state.Collections.Add(name); });
    }

    internal Task RemoveCollectionAsync(string name) => MutateAsync(state =>
    {
        state.Collections.RemoveAll(value => string.Equals(value, name, StringComparison.OrdinalIgnoreCase));
        foreach (var names in state.Memberships.Values) names.RemoveAll(value => string.Equals(value, name, StringComparison.OrdinalIgnoreCase));
    });

    internal Task AssignAsync(IEnumerable<string> signatures, string collection, bool remove)
    {
        string[] keys = signatures.Distinct(StringComparer.Ordinal).ToArray();
        return MutateAsync(state =>
        {
            string name = state.Collections.FirstOrDefault(value => string.Equals(value, collection, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException("Choose an existing collection.");
            foreach (string key in keys)
            {
                if (!state.Memberships.TryGetValue(key, out var names)) state.Memberships[key] = names = [];
                if (remove) names.RemoveAll(value => string.Equals(value, name, StringComparison.OrdinalIgnoreCase));
                else if (!names.Contains(name, StringComparer.OrdinalIgnoreCase)) names.Add(name);
            }
        });
    }

    internal Task MarkDeletedAsync(IEnumerable<string> signatures) => MutateAsync(state =>
    {
        var now = DateTimeOffset.UtcNow;
        foreach (string signature in signatures) { state.Deleted[signature] = now; state.Memberships.Remove(signature); state.Restored.Remove(signature); }
    });

    internal Task ClearUnpinnedAsync() => MutateAsync(state => { state.ClearedAt = DateTimeOffset.UtcNow; state.Restored.Clear(); });

    internal Task MergeImportedCollectionsAsync(IReadOnlyDictionary<string, (DateTimeOffset CapturedAt, string[] Collections)> memberships) => MutateAsync(state =>
    {
        foreach (var (signature, imported) in memberships)
        {
            state.Deleted.Remove(signature);
            state.Restored[signature] = imported.CapturedAt;
            if (!state.Memberships.TryGetValue(signature, out var names)) state.Memberships[signature] = names = [];
            foreach (string importedName in imported.Collections)
            {
                string name = LibraryState.ValidateCollectionName(importedName);
                string canonical = state.Collections.FirstOrDefault(existing => string.Equals(existing, name, StringComparison.OrdinalIgnoreCase)) ?? name;
                if (!state.Collections.Contains(canonical, StringComparer.OrdinalIgnoreCase)) state.Collections.Add(canonical);
                if (!names.Contains(canonical, StringComparer.OrdinalIgnoreCase)) names.Add(canonical);
            }
        }
    });

    private async Task MutateAsync(Action<LibraryState> change, bool protectImmediately = false)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_loaded || LoadError is not null) throw new InvalidOperationException("Library metadata is unavailable. Capture remains paused.");
            // Published snapshots are never modified; readers can safely use them without taking this gate.
            var next = await Task.Run(() => JsonSerializer.Deserialize<LibraryState>(JsonSerializer.Serialize(_snapshot, JsonSerializerOptions.Web), JsonSerializerOptions.Web)!).ConfigureAwait(false);
            change(next);
            if (protectImmediately) { Volatile.Write(ref _snapshot, next); Changed?.Invoke(this, EventArgs.Empty); }
            await Task.Run(() => _file.WriteAsync(next)).ConfigureAwait(false);
            Volatile.Write(ref _snapshot, next);
            _wasPaused = PausedUntil is not null;
        }
        finally { _gate.Release(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => _timer.Dispose();
}
