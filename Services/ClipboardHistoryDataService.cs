using System.Collections.Concurrent;
using System.ComponentModel.Composition;
using Microsoft.Extensions.Logging;
using Windows.ApplicationModel.DataTransfer;
using WindowSill.API;
using WindowSill.ClipboardHistory.Core;
using WindowSill.ClipboardHistory.Utils;

namespace WindowSill.ClipboardHistory.Services;

/// <summary>
/// Singleton service that centralizes clipboard history management and data type detection.
/// Computes <see cref="DetectedClipboardDataType"/> once per clipboard item on a background thread,
/// then notifies subscribers when the cache is updated.
/// </summary>
[Export(typeof(ClipboardHistoryDataService))]
internal sealed class ClipboardHistoryDataService : ILiveClipboardHistory
{
    private readonly DisposableSemaphore _semaphore = new();
    private readonly ILogger _logger;
    private readonly LibraryStateService _library;
    private readonly ConcurrentDictionary<string, DetectedClipboardDataType> _dataTypeCache = new();
    private readonly ConcurrentDictionary<string, string> _signatureCache = new();
    private readonly ConcurrentDictionary<string, LiveClipboardItemSource> _sourceCache = new();

    private IReadOnlyList<ClipboardItemData> _cachedItems = [];
    private int _subscriberCount;

    [ImportingConstructor]
    public ClipboardHistoryDataService(LibraryStateService library)
    {
        _logger = this.Log();
        _library = library;
    }

    /// <summary>
    /// Raised when the clipboard history data has been updated.
    /// Subscribers should refresh their UI with the new data from <see cref="GetCachedItems"/>.
    /// </summary>
    public event EventHandler? DataUpdated;

    /// <summary>
    /// Gets whether clipboard history is enabled in Windows.
    /// </summary>
    public bool IsHistoryEnabled => Clipboard.IsHistoryEnabled();

    /// <summary>
    /// Gets the current cached clipboard items with their detected data types.
    /// </summary>
    public IReadOnlyList<ClipboardItemData> GetCachedItems() => Volatile.Read(ref _cachedItems);

    public Task DeleteBySignatureAsync(string signature)
        => ThreadHelper.RunOnUIThreadAsync(() =>
        {
            foreach (var item in GetCachedItems().Where(item => item.ContentSignature == signature))
                item.Source.DeleteFromHistory();
        });

    public Task ClearAsync() => ThreadHelper.RunOnUIThreadAsync(() =>
    {
        if (Clipboard.IsHistoryEnabled() && !Clipboard.ClearHistory())
            throw new InvalidOperationException("Windows clipboard history could not be cleared.");
    });

    /// <summary>
    /// Subscribes to clipboard events. Call this when a consumer activates.
    /// The service only listens to clipboard events when there's at least one subscriber.
    /// </summary>
    public void Subscribe()
    {
        if (Interlocked.Increment(ref _subscriberCount) == 1)
        {
            Clipboard.ContentChanged += Clipboard_ContentChanged;
            Clipboard.HistoryChanged += Clipboard_HistoryChanged;
            Clipboard.HistoryEnabledChanged += Clipboard_HistoryEnabledChanged;
        }
    }

    /// <summary>
    /// Unsubscribes from clipboard events. Call this when a consumer deactivates.
    /// </summary>
    public void Unsubscribe()
    {
        if (Interlocked.Decrement(ref _subscriberCount) == 0)
        {
            Clipboard.ContentChanged -= Clipboard_ContentChanged;
            Clipboard.HistoryChanged -= Clipboard_HistoryChanged;
            Clipboard.HistoryEnabledChanged -= Clipboard_HistoryEnabledChanged;
        }
    }

    /// <summary>
    /// Triggers a refresh of the clipboard history data.
    /// Data types are computed on a background thread, then <see cref="DataUpdated"/> is raised.
    /// </summary>
    public async Task RefreshAsync()
    {
        await Task.Run(async () =>
        {
            ThreadHelper.ThrowIfOnUIThread();

            using (await _semaphore.WaitAsync(CancellationToken.None).ConfigureAwait(false))
            {
                IReadOnlyList<ClipboardHistoryItem> clipboardItems = await GetClipboardHistoryItemsAsync();

                // Compute data types and signatures on background thread
                var itemDataList = new List<ClipboardItemData>(clipboardItems.Count);
                foreach (ClipboardHistoryItem item in clipboardItems)
                {
                    try
                    {
                        if (!_library.AllowsCapture(item.Timestamp)) continue;
                        // Resolve native history properties on this worker, never during XAML layout.
                        var source = _sourceCache.GetOrAdd(item.Id, _ => new LiveClipboardItemSource(item, item.Content));
                        DetectedClipboardDataType dataType = await GetOrComputeDataTypeAsync(item, source.Data);
                        string signature = await GetOrComputeSignatureAsync(item, source.Data, dataType);
                        if (!_library.AllowsItem(signature, item.Timestamp)) continue;
                        itemDataList.Add(new ClipboardItemData(source, dataType, signature) { CapturedAt = item.Timestamp });
                    }
                    catch (Exception ex) { _logger.LogWarning(ex, "A Windows clipboard entry is no longer available."); }
                }

                // Prune cache of items no longer in history
                PruneCache(clipboardItems);

                Volatile.Write(ref _cachedItems, itemDataList);
            }
        });
    }

    /// <summary>
    /// Clears the data type cache. Call this when settings change that affect how items are displayed
    /// (e.g., HidePasswords setting).
    /// </summary>
    public void ClearCache()
    {
        _dataTypeCache.Clear();
        _signatureCache.Clear();
    }

    private async Task<string> GetOrComputeSignatureAsync(ClipboardHistoryItem item, DataPackageView content, DetectedClipboardDataType dataType)
    {
        if (_signatureCache.TryGetValue(item.Id, out string? cached))
        {
            return cached;
        }

        try
        {
            string signature = await ClipboardContentCapturer.ComputeSignatureAsync(content, dataType);
            _signatureCache.TryAdd(item.Id, signature);
            return signature;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compute content signature for item {ItemId}.", item.Id);
            return string.Empty;
        }
    }

    private async Task<DetectedClipboardDataType> GetOrComputeDataTypeAsync(ClipboardHistoryItem item, DataPackageView content)
    {
        if (_dataTypeCache.TryGetValue(item.Id, out DetectedClipboardDataType cachedType))
        {
            return cachedType;
        }

        try
        {
            DetectedClipboardDataType dataType = await DataHelper.GetDetectedClipboardDataTypeAsync(content);
            _dataTypeCache.TryAdd(item.Id, dataType);
            return dataType;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to detect clipboard data type for item {ItemId}.", item.Id);
            return DetectedClipboardDataType.Unknown;
        }
    }

    private void PruneCache(IReadOnlyList<ClipboardHistoryItem> currentItems)
    {
        var currentIds = new HashSet<string>(currentItems.Select(i => i.Id));
        foreach (string cachedId in _dataTypeCache.Keys)
        {
            if (!currentIds.Contains(cachedId))
            {
                _dataTypeCache.TryRemove(cachedId, out _);
                _signatureCache.TryRemove(cachedId, out _);
            }
        }
        foreach (string cachedId in _sourceCache.Keys)
            if (!currentIds.Contains(cachedId)) _sourceCache.TryRemove(cachedId, out _);
        foreach (string cachedId in _signatureCache.Keys)
            if (!currentIds.Contains(cachedId)) _signatureCache.TryRemove(cachedId, out _);
    }

    private async Task<IReadOnlyList<ClipboardHistoryItem>> GetClipboardHistoryItemsAsync()
    {
        try
        {
            if (Clipboard.IsHistoryEnabled())
            {
                ClipboardHistoryItemsResult clipboardHistory = await Clipboard.GetHistoryItemsAsync();
                if (clipboardHistory.Status == ClipboardHistoryItemsResultStatus.Success)
                {
                    return clipboardHistory.Items.ToList();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get clipboard history items.");
        }

        return [];
    }

    private void Clipboard_HistoryEnabledChanged(object? sender, object e)
    {
        // Subscribers will refresh the shared cache.
        DataUpdated?.Invoke(this, EventArgs.Empty);
    }

    private void Clipboard_HistoryChanged(object? sender, ClipboardHistoryChangedEventArgs e)
    {
        // Subscribers will refresh the shared cache.
        DataUpdated?.Invoke(this, EventArgs.Empty);
    }

    private void Clipboard_ContentChanged(object? sender, object e)
    {
        // Subscribers will refresh the shared cache.
        DataUpdated?.Invoke(this, EventArgs.Empty);
    }
}
