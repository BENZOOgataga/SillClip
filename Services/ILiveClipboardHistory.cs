using WindowSill.ClipboardHistory.Core;

namespace WindowSill.ClipboardHistory.Services;

/// <summary>Access to the native history, separate from storage and backup operations.</summary>
internal interface ILiveClipboardHistory
{
    IReadOnlyList<ClipboardItemData> GetCachedItems();
    Task DeleteBySignatureAsync(string signature);
    Task ClearAsync();
}
