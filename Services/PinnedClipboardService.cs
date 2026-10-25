using System.ComponentModel.Composition;
using Windows.Storage;
using Windows.Storage.Streams;
using WindowSill.API;
using WindowSill.ClipboardHistory.Core;

namespace WindowSill.ClipboardHistory.Services;

/// <summary>
/// Singleton service that owns the persisted pinned clipboard items. Pins survive Windows
/// clipboard history eviction and application restarts. All capture, encryption, and file
/// IO runs off the UI thread.
/// </summary>
[Export(typeof(PinnedClipboardService))]
internal sealed class PinnedClipboardService
{
    private readonly PinnedClipboardStore _store;
    private readonly string _dataFolder;
    private readonly DisposableSemaphore _semaphore = new();
    private readonly List<ClipboardItemData> _pinned = [];
    private readonly HashSet<string> _signatures = new(StringComparer.Ordinal);

    private bool _loaded;
    private ClipboardItemData[] _snapshot = [];

    [ImportingConstructor]
    public PinnedClipboardService(IPluginInfo pluginInfo)
    {
        _dataFolder = ClipboardDataMigration.ForkFolder(pluginInfo.GetPluginDataFolder());
        _store = new PinnedClipboardStore(_dataFolder);
    }

    /// <summary>
    /// Raised when the set of pinned items changes.
    /// </summary>
    public event EventHandler? PinsChanged;

    /// <summary>
    /// Gets a snapshot of the pinned items, newest first.
    /// </summary>
    public IReadOnlyList<ClipboardItemData> GetPinnedItems()
        => Volatile.Read(ref _snapshot);

    /// <summary>
    /// Gets whether an item with the given content signature is already pinned.
    /// </summary>
    public bool IsPinnedSignature(string signature) => GetPinnedItems().Any(item => item.ContentSignature == signature);

    /// <summary>
    /// Loads persisted pins into memory. Safe to call multiple times.
    /// </summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        using IDisposable _ = await _semaphore.WaitAsync(cancellationToken);
        if (_loaded)
        {
            return;
        }

        await Task.Run(() => ClipboardDataMigration.Prepare(_dataFolder), cancellationToken);
        List<PinnedClipboardItem> models = await _store.LoadAllAsync(cancellationToken);
        foreach (PinnedClipboardItem model in models)
        {
            model.ContentSignature = ClipboardSignature.Compute(model);
            ClipboardItemData data = await CreateDataAsync(model);
            _pinned.Add(data);
            _signatures.Add(model.ContentSignature);
        }

        _loaded = true;
        Publish();
    }

    /// <summary>
    /// Pins the given clipboard item. Returns <c>false</c> if the item is already pinned.
    /// </summary>
    public async Task<bool> PinAsync(ClipboardItemData itemData, CancellationToken cancellationToken = default)
    {
        using IDisposable _ = await _semaphore.WaitAsync(cancellationToken);

        if (_signatures.Contains(itemData.ContentSignature))
        {
            return false;
        }

        PinnedClipboardItem model = await ClipboardContentCapturer.CaptureAsync(itemData.Source.Data, itemData.DataType);
        await _store.SaveAsync(model, cancellationToken);
        model.ImageBytes = null;

        ClipboardItemData data = await CreateDataAsync(model);
        _pinned.Add(data);
        _signatures.Add(model.ContentSignature);

        Publish();
        PinsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Removes the pin with the given id.
    /// </summary>
    public async Task UnpinAsync(string id, CancellationToken cancellationToken = default)
    {
        using IDisposable _ = await _semaphore.WaitAsync(cancellationToken);

        int index = _pinned.FindIndex(p => string.Equals(p.Source.Id, id, StringComparison.Ordinal));
        if (index < 0)
        {
            return;
        }

        await _store.DeleteAsync(id, cancellationToken);
        _signatures.Remove(((PinnedClipboardItemSource)_pinned[index].Source).Model.ContentSignature);
        _pinned.RemoveAt(index);

        Publish();
        PinsChanged?.Invoke(this, EventArgs.Empty);
    }

    private Task<ClipboardItemData> CreateDataAsync(PinnedClipboardItem model)
    {
        var source = new PinnedClipboardItemSource(
            model,
            () => PinnedClipboardRebuilder.BuildDeferred(model, () => _store.ReadImageAsync(model.Id)));

        return Task.FromResult(new ClipboardItemData(source, model.DataType, model.ContentSignature) { CapturedAt = model.PinnedAt });
    }

    private void Publish() => Volatile.Write(ref _snapshot,
        _pinned.OrderByDescending(item => item.CapturedAt).ToArray());
}
