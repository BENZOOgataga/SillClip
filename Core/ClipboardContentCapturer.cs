using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;

namespace WindowSill.ClipboardHistory.Core;

/// <summary>
/// Extracts the content of a clipboard item into a persistable <see cref="PinnedClipboardItem"/>
/// and computes the canonical content signature used to deduplicate the live history list
/// against pinned items. All work is intended to run on a background thread.
/// </summary>
internal static class ClipboardContentCapturer
{
    /// <summary>
    /// Captures the content of the given clipboard data into a new pinned item, including
    /// any image bytes (held in memory on the returned item).
    /// </summary>
    internal static async Task<PinnedClipboardItem> CaptureAsync(DataPackageView data, DetectedClipboardDataType dataType)
    {
        var item = new PinnedClipboardItem
        {
            DataType = dataType,
            PinnedAt = DateTimeOffset.UtcNow,
        };

        IReadOnlyList<string> formats = data.AvailableFormats;

        if (formats.Contains(StandardDataFormats.Text))
        {
            item.Text = await data.GetTextAsync();
        }
        if (formats.Contains(StandardDataFormats.Html)) item.Html = await data.GetHtmlFormatAsync();
        if (formats.Contains(StandardDataFormats.Rtf)) item.Rtf = await data.GetRtfAsync();

        switch (dataType)
        {
            case DetectedClipboardDataType.Uri:
                item.Uri = item.Text;
                if (string.IsNullOrEmpty(item.Uri) && formats.Contains(StandardDataFormats.WebLink))
                {
                    item.Uri = (await data.GetWebLinkAsync()).ToString();
                }
                if (string.IsNullOrEmpty(item.Uri) && formats.Contains(StandardDataFormats.Uri))
                    item.Uri = (await data.GetUriAsync()).ToString();
                break;

            case DetectedClipboardDataType.ApplicationLink:
                item.ApplicationLink = (await data.GetApplicationLinkAsync()).ToString();
                break;
            case DetectedClipboardDataType.UserActivity:
                item.UserActivity = await data.GetDataAsync(StandardDataFormats.UserActivityJsonArray) as string;
                break;

            case DetectedClipboardDataType.Color:
                item.Color = item.Text;
                break;

            case DetectedClipboardDataType.File:
                item.FilePaths = await GetFilePathsAsync(data);
                break;

            case DetectedClipboardDataType.Image:
                item.ImageBytes = await GetImageBytesAsync(data);
                item.HasImage = item.ImageBytes is { Length: > 0 };
                if (!item.HasImage) throw new NotSupportedException("The image has no readable bitmap payload.");
                break;
        }

        item.ContentSignature = ClipboardSignature.Compute(item);
        return item;
    }

    /// <summary>
    /// Computes the canonical content signature for a live clipboard item so it can be
    /// matched against pinned items. Mirrors the signature stored on pinned items.
    /// </summary>
    internal static async Task<string> ComputeSignatureAsync(DataPackageView data, DetectedClipboardDataType dataType)
    {
        return (await CaptureAsync(data, dataType)).ContentSignature;
    }

    private static async Task<string[]?> GetFilePathsAsync(DataPackageView data)
    {
        if (!data.AvailableFormats.Contains(StandardDataFormats.StorageItems))
        {
            return null;
        }

        IReadOnlyList<IStorageItem> items = await data.GetStorageItemsAsync();
        return [.. items.Select(i => i.Path).Where(p => !string.IsNullOrEmpty(p))];
    }

    private static async Task<byte[]?> GetImageBytesAsync(DataPackageView data)
    {
        if (!data.AvailableFormats.Contains(StandardDataFormats.Bitmap))
        {
            return null;
        }

        RandomAccessStreamReference streamRef = await data.GetBitmapAsync();
        using IRandomAccessStreamWithContentType stream = await streamRef.OpenReadAsync();
        using var memory = new MemoryStream();
        using (Stream input = stream.AsStreamForRead())
        {
            await input.CopyToAsync(memory);
        }

        return memory.ToArray();
    }
}
