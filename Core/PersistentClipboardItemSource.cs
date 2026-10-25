using Windows.ApplicationModel.DataTransfer;

namespace WindowSill.ClipboardHistory.Core;

/// <summary>
/// Clipboard source backed by the extension's encrypted persistent history.
/// </summary>
internal sealed class PersistentClipboardItemSource : IClipboardItemSource
{
    private readonly Func<DataPackage> _rebuild;

    internal PersistentClipboardItemSource(
        PinnedClipboardItem model,
        Func<DataPackage> rebuild)
    {
        Model = model;
        _rebuild = rebuild;
    }

    internal PinnedClipboardItem Model { get; }

    public string Id => Model.Id;

    public bool IsPinned => false;

    public DataPackageView Data => _rebuild().GetView();

    public void SetAsClipboardContent() => Clipboard.SetContent(_rebuild());

    public void DeleteFromHistory() { } // The view model awaits archive deletion by signature.
}
