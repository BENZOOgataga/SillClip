using Windows.ApplicationModel.DataTransfer;

namespace WindowSill.ClipboardHistory.Core;

/// <summary>
/// A clipboard item source backed by a live Windows clipboard history item.
/// </summary>
internal sealed class LiveClipboardItemSource : IClipboardItemSource
{
    private readonly ClipboardHistoryItem _item;
    private readonly DataPackageView _data;
    private readonly string _id;

    internal LiveClipboardItemSource(ClipboardHistoryItem item, DataPackageView data)
    {
        _item = item;
        _data = data;
        _id = item.Id;
    }

    /// <summary>
    /// Gets the underlying Windows clipboard history item.
    /// </summary>
    internal ClipboardHistoryItem Item => _item;

    /// <inheritdoc />
    public string Id => _id;

    /// <inheritdoc />
    public bool IsPinned => false;

    /// <inheritdoc />
    public DataPackageView Data => _data;

    /// <inheritdoc />
    public void SetAsClipboardContent() => Clipboard.SetHistoryItemAsContent(_item);

    /// <inheritdoc />
    public void DeleteFromHistory() => Clipboard.DeleteItemFromHistory(_item);
}
