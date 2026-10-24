using Windows.ApplicationModel.DataTransfer;

namespace WindowSill.ClipboardHistory.Core;

/// <summary>
/// Clipboard source backed by the extension's encrypted persistent history.
/// </summary>
internal sealed class PersistentClipboardItemSource : IClipboardItemSource
{
    private readonly Func<DataPackage> _rebuild;
    private readonly Action<string> _delete;

    internal PersistentClipboardItemSource(
        PinnedClipboardItem model,
        Func<DataPackage> rebuild,
        Action<string> delete)
    {
        Model = model;
        _rebuild = rebuild;
        _delete = delete;
        Data = rebuild().GetView();
    }

    internal PinnedClipboardItem Model { get; }

    public string Id => Model.Id;

    public bool IsPinned => false;

    public DataPackageView Data { get; }

    public void SetAsClipboardContent() => Clipboard.SetContent(_rebuild());

    public void DeleteFromHistory() => _delete(Model.Id);
}
