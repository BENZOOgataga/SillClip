using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WindowSill.ClipboardHistory.Core;

internal static class ClipboardSignature
{
    internal static string Compute(PinnedClipboardItem item)
    {
        string canonical;
        if (item.DataType == DetectedClipboardDataType.Image)
        {
            if (item.ImageBytes is { Length: > 0 })
                item.ImageContentHash = Convert.ToHexString(SHA256.HashData(item.ImageBytes));
            if (string.IsNullOrEmpty(item.ImageContentHash)) return item.ContentSignature;
            canonical = "img:" + item.ImageContentHash;
        }
        else if (!string.IsNullOrEmpty(item.Html) || !string.IsNullOrEmpty(item.Rtf))
        {
            canonical = $"{item.DataType}:" + JsonSerializer.Serialize(new[] { item.Text, item.Uri, item.Color, item.Html, item.Rtf });
        }
        else
        {
            canonical = item.DataType switch
            {
                DetectedClipboardDataType.File => "file:" + string.Join('|', item.FilePaths ?? []),
                DetectedClipboardDataType.Uri => "uri:" + (item.Uri ?? string.Empty),
                DetectedClipboardDataType.Color => "color:" + (item.Color ?? string.Empty),
                DetectedClipboardDataType.Html or DetectedClipboardDataType.Rtf
                    => $"{item.DataType}:" + JsonSerializer.Serialize(new[] { item.Text, item.Html, item.Rtf }),
                DetectedClipboardDataType.ApplicationLink => "application:" + item.ApplicationLink,
                DetectedClipboardDataType.UserActivity => "activity:" + item.UserActivity,
                _ => $"{item.DataType}:{item.Text ?? string.Empty}"
            };
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
