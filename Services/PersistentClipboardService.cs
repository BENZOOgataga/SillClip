using System.ComponentModel.Composition;
using Microsoft.Extensions.Logging;
using WindowSill.API;
using WindowSill.ClipboardHistory.Core;

namespace WindowSill.ClipboardHistory.Services;

[Export(typeof(PersistentClipboardService))]
internal sealed class PersistentClipboardService
{
    private readonly PinnedClipboardStore _store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, ClipboardItemData> _items = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _deleted = new(StringComparer.Ordinal);
    private readonly ILogger _logger;
    private readonly string _dataFolder;
    private ClipboardItemData[] _snapshot = [];
    private DateTimeOffset _clearedAt;
    private bool _loaded;

    [ImportingConstructor]
    public PersistentClipboardService(IPluginInfo pluginInfo)
        : this(ClipboardDataMigration.ForkFolder(pluginInfo.GetPluginDataFolder()), typeof(PersistentClipboardService).Log()) { }

    internal PersistentClipboardService(string dataFolder, ILogger logger)
    {
        _logger = logger;
        _dataFolder = dataFolder;
        _store = new PinnedClipboardStore(_dataFolder, "History");
    }

    public event EventHandler? HistoryChanged;
    public IReadOnlyList<ClipboardItemData> GetItems() => Volatile.Read(ref _snapshot);
    internal PinnedClipboardStore Store => _store;

    internal async Task<bool> ImportAsync(PinnedClipboardItem model, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_items.ContainsKey(model.ContentSignature)) return false;
            try { await _store.SaveAsync(model, cancellationToken); }
            catch { await _store.DeleteAsync(model.Id, CancellationToken.None); throw; }
            model.ImageBytes = null;
            _items.Add(model.ContentSignature, CreateData(model));
            Publish();
            return true;
        }
        finally { _gate.Release(); }
    }

    internal void NotifyChanged() => HistoryChanged?.Invoke(this, EventArgs.Empty);

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_loaded) return;
            await Task.Run(() => ClipboardDataMigration.Prepare(_dataFolder), cancellationToken);
            foreach (PinnedClipboardItem model in await _store.LoadAllAsync(cancellationToken))
            {
                try
                {
                    model.ContentSignature = ClipboardSignature.Compute(model);
                    _items[model.ContentSignature] = CreateData(model);
                }
                catch (Exception ex) { _logger.LogWarning(ex, "An archived item could not be restored."); }
            }
            _loaded = true;
            Publish();
        }
        finally { _gate.Release(); }
    }

    public async Task CaptureAsync(IReadOnlyList<ClipboardItemData> liveItems, int maximumItemCount,
        IReadOnlySet<string> pinnedSignatures, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            foreach (ClipboardItemData item in liveItems.OrderBy(item => item.CapturedAt))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(item.ContentSignature) || item.CapturedAt <= _clearedAt
                    || (_deleted.TryGetValue(item.ContentSignature, out var deletedAt) && item.CapturedAt <= deletedAt))
                    continue;
                try
                {
                    if (_items.TryGetValue(item.ContentSignature, out var existing))
                    {
                        if (item.CapturedAt <= existing.CapturedAt) continue;
                        var existingModel = ((PersistentClipboardItemSource)existing.Source).Model;
                        existingModel.PinnedAt = item.CapturedAt;
                        await _store.SaveAsync(existingModel, cancellationToken);
                        _items[item.ContentSignature] = existing with { CapturedAt = item.CapturedAt };
                        continue;
                    }
                    PinnedClipboardItem model = await ClipboardContentCapturer.CaptureAsync(item.Source.Data, item.DataType);
                    model.PinnedAt = item.CapturedAt;
                    if (string.IsNullOrEmpty(model.ContentSignature)) continue;
                    await _store.SaveAsync(model, cancellationToken);
                    model.ImageBytes = null;
                    _items[model.ContentSignature] = CreateData(model);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex) { _logger.LogWarning(ex, "A clipboard item could not be archived. Other items remain available."); }
            }

            var models = _items.Values.Select(item => ((PersistentClipboardItemSource)item.Source).Model);
            foreach (var expired in HistoryRetention.Expired(models, maximumItemCount, pinnedSignatures))
            {
                try
                {
                    await _store.DeleteAsync(expired.Id, cancellationToken);
                    _items.Remove(expired.ContentSignature);
                }
                catch (IOException ex) { _logger.LogWarning(ex, "An expired archive file could not be removed."); }
            }
            Publish();
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteBySignatureAsync(string signature, CancellationToken cancellationToken = default)
        => await DeleteManyAsync([signature], cancellationToken);

    internal async Task DeleteManyAsync(IEnumerable<string> signatures, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        var failures = new List<Exception>();
        try
        {
            foreach (string signature in signatures)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _deleted[signature] = DateTimeOffset.UtcNow;
                if (!_items.TryGetValue(signature, out var item)) continue;
                try { await _store.DeleteAsync(item.Source.Id, cancellationToken); _items.Remove(signature); }
                catch (IOException ex) { failures.Add(ex); }
            }
            Publish();
        }
        finally { _gate.Release(); }
        HistoryChanged?.Invoke(this, EventArgs.Empty);
        if (failures.Count > 0) throw new AggregateException("Some archive files could not be deleted.", failures);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _clearedAt = DateTimeOffset.UtcNow;
            await _store.DeleteAllAsync(cancellationToken);
            _items.Clear();
            Publish();
        }
        finally { _gate.Release(); }
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    private ClipboardItemData CreateData(PinnedClipboardItem model)
    {
        var source = new PersistentClipboardItemSource(model,
            () => PinnedClipboardRebuilder.BuildDeferred(model, () => _store.ReadImageAsync(model.Id)));
        return new ClipboardItemData(source, model.DataType, model.ContentSignature) { CapturedAt = model.PinnedAt };
    }

    private void Publish() => Volatile.Write(ref _snapshot, _items.Values.OrderByDescending(item => item.CapturedAt).ToArray());
}
