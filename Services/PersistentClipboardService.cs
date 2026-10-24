using System.ComponentModel.Composition;
using Windows.Storage;
using Windows.Storage.Streams;
using WindowSill.API;
using WindowSill.ClipboardHistory.Core;

namespace WindowSill.ClipboardHistory.Services;

/// <summary>
/// Owns the DPAPI-encrypted clipboard archive used after Windows evicts an item.
/// </summary>
[Export(typeof(PersistentClipboardService))]
internal sealed class PersistentClipboardService
{
    private readonly PinnedClipboardStore _store;
    private readonly DisposableSemaphore _semaphore = new();
    private readonly List<ClipboardItemData> _items = [];
    private readonly Dictionary<string, ClipboardItemData> _itemsBySignature = new(StringComparer.Ordinal);
    private bool _loaded;

    [ImportingConstructor]
    public PersistentClipboardService(IPluginInfo pluginInfo)
    {
        _store = new PinnedClipboardStore(pluginInfo.GetPluginDataFolder(), "History");
    }

    public event EventHandler? HistoryChanged;

    public IReadOnlyList<ClipboardItemData> GetItems()
        => [.. _items.OrderByDescending(GetCapturedAt)];

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        using IDisposable _ = await _semaphore.WaitAsync(cancellationToken);
        if (_loaded)
        {
            return;
        }

        foreach (PinnedClipboardItem model in await _store.LoadAllAsync(cancellationToken))
        {
            ClipboardItemData data = await CreateDataAsync(model);
            _items.Add(data);
            _itemsBySignature[model.ContentSignature] = data;
        }

        _loaded = true;
    }

    public async Task CaptureAsync(
        IReadOnlyList<ClipboardItemData> liveItems,
        int maximumItemCount,
        IReadOnlySet<string> pinnedSignatures,
        CancellationToken cancellationToken = default)
    {
        using (await _semaphore.WaitAsync(cancellationToken))
        {
            foreach (ClipboardItemData item in liveItems.Reverse())
            {
                if (string.IsNullOrEmpty(item.ContentSignature)
                    || _itemsBySignature.ContainsKey(item.ContentSignature))
                {
                    continue;
                }

                PinnedClipboardItem model = await ClipboardContentCapturer.CaptureAsync(item.Source.Data, item.DataType);
                if (string.IsNullOrEmpty(model.ContentSignature))
                {
                    continue;
                }

                await _store.SaveAsync(model, cancellationToken);
                ClipboardItemData storedData = await CreateDataAsync(model);
                _items.Add(storedData);
                _itemsBySignature[model.ContentSignature] = storedData;
            }

            ClipboardItemData[] unpinnedItems = _items
                .Where(item => !pinnedSignatures.Contains(item.ContentSignature))
                .OrderByDescending(GetCapturedAt)
                .ToArray();

            if (maximumItemCount > 0 && unpinnedItems.Length > maximumItemCount)
            {
                ClipboardItemData[] expired = unpinnedItems
                    .Skip(maximumItemCount)
                    .ToArray();

                foreach (ClipboardItemData item in expired)
                {
                    await DeleteCoreAsync(item, cancellationToken);
                }
            }
        }
    }

    public async Task DeleteBySignatureAsync(string signature, CancellationToken cancellationToken = default)
    {
        bool changed = false;
        using (await _semaphore.WaitAsync(cancellationToken))
        {
            if (_itemsBySignature.TryGetValue(signature, out ClipboardItemData? item))
            {
                await DeleteCoreAsync(item, cancellationToken);
                changed = true;
            }
        }

        if (changed)
        {
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        bool changed = false;
        using (await _semaphore.WaitAsync(cancellationToken))
        {
            ClipboardItemData? item = _items.FirstOrDefault(i => i.Source.Id == id);
            if (item is not null)
            {
                await DeleteCoreAsync(item, cancellationToken);
                changed = true;
            }
        }

        if (changed)
        {
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        using (await _semaphore.WaitAsync(cancellationToken))
        {
            await _store.DeleteAllAsync(cancellationToken);
            _items.Clear();
            _itemsBySignature.Clear();
        }

        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task DeleteCoreAsync(ClipboardItemData item, CancellationToken cancellationToken)
    {
        _items.Remove(item);
        _itemsBySignature.Remove(item.ContentSignature);
        await _store.DeleteAsync(item.Source.Id, cancellationToken);
    }

    private async Task<ClipboardItemData> CreateDataAsync(PinnedClipboardItem model)
    {
        RandomAccessStreamReference? imageReference =
            await PinnedClipboardRebuilder.CreateImageReferenceAsync(model.ImageBytes);
        IReadOnlyList<IStorageItem>? storageItems =
            await PinnedClipboardRebuilder.ResolveStorageItemsAsync(model.FilePaths);

        var source = new PersistentClipboardItemSource(
            model,
            () => PinnedClipboardRebuilder.Build(model, imageReference, storageItems),
            id => DeleteAsync(id).Forget());

        return new ClipboardItemData(source, model.DataType, model.ContentSignature);
    }

    private static DateTimeOffset GetCapturedAt(ClipboardItemData item)
        => ((PersistentClipboardItemSource)item.Source).Model.PinnedAt;
}
