using System.Runtime.InteropServices;
using CommunityToolkit.WinUI.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using WindowSill.API;

namespace WindowSill.ClipboardHistory.Utils;

internal static partial class DataHelper
{
    private static readonly SemaphoreSlim BitmapGate = new(2, 2);
    internal static async Task<(BitmapImage? Bitmap, uint Width, uint Height)> GetBitmapAsync(DataPackageView dataPackageView)
    {
        await BitmapGate.WaitAsync();
        try
        {
            RandomAccessStreamReference randomAccessStreamReference = await dataPackageView.GetBitmapAsync();
            using IRandomAccessStreamWithContentType randomAccessStreamWithContentType = await randomAccessStreamReference.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(randomAccessStreamWithContentType);
            uint width = decoder.PixelWidth;
            uint height = decoder.PixelHeight;
            double scale = Math.Min(1, 256.0 / Math.Max(width, height));
            randomAccessStreamWithContentType.Seek(0);
            var bitmap = new BitmapImage
            {
                DecodePixelType = DecodePixelType.Physical,
                DecodePixelWidth = Math.Max(1, (int)(width * scale)),
                DecodePixelHeight = Math.Max(1, (int)(height * scale))
            };
            await bitmap.SetSourceAsync(randomAccessStreamWithContentType);
            return (bitmap, width, height);
        }
        catch (COMException)
        {
            return (null, 0, 0);
        }
        catch (Exception ex)
        {
            typeof(DataHelper).Log().LogError(ex, "Error while retrieving bitmap from data package view.");
            return (null, 0, 0);
        }
        finally { BitmapGate.Release(); }
    }

    internal static (SolidColorBrush background, SolidColorBrush foreground) GetBackgroundAndForegroundBrushes(string colorString)
    {
        if (!colorString.StartsWith('#'))
        {
            colorString = "#" + colorString;
        }

        var color = colorString.ToColor();
        var background = new SolidColorBrush(color);

        // Check the brightness of the color to determine whether the text must be black or white.
        if ((int)Math.Sqrt(color.R * color.R * 0.241 + color.G * color.G * 0.691 + color.B * color.B * 0.068) > 130)
        {
            return (background, new SolidColorBrush(Colors.Black));
        }
        else
        {
            return (background, new SolidColorBrush(Colors.White));
        }
    }

    internal static async Task<DetectedClipboardDataType> GetDetectedClipboardDataTypeAsync(ClipboardHistoryItem item)
    {
        if (item.Content.AvailableFormats.Contains(StandardDataFormats.StorageItems))
        {
            return DetectedClipboardDataType.File;
        }
        else if (item.Content.AvailableFormats.Contains(StandardDataFormats.Bitmap))
        {
            return DetectedClipboardDataType.Image;
        }
        else if (item.Content.AvailableFormats.Contains("DeviceIndependentBitmap") ||
                 item.Content.AvailableFormats.Contains("DeviceIndependentBitmapV5") ||
                 item.Content.AvailableFormats.Contains("TaggedImageFileFormat") ||
                 item.Content.AvailableFormats.Contains("EnhancedMetafile"))
        {
            return DetectedClipboardDataType.Image;
        }
        else if (item.Content.AvailableFormats.Contains(StandardDataFormats.Rtf))
        {
            return DetectedClipboardDataType.Rtf;
        }
        else if (item.Content.AvailableFormats.Contains(StandardDataFormats.ApplicationLink))
        {
            return DetectedClipboardDataType.ApplicationLink;
        }
        else if (item.Content.AvailableFormats.Contains(StandardDataFormats.UserActivityJsonArray))
        {
            return DetectedClipboardDataType.UserActivity;
        }
        else if (item.Content.AvailableFormats.Contains(StandardDataFormats.WebLink)
            || item.Content.AvailableFormats.Contains(StandardDataFormats.Uri))
        {
            return DetectedClipboardDataType.Uri;
        }
        else if (item.Content.AvailableFormats.Contains(StandardDataFormats.Text))
        {
            string text = await item.Content.GetTextAsync();
            if (IsHexColor(text))
            {
                return DetectedClipboardDataType.Color;
            }
            else if (IsUri(text))
            {
                return DetectedClipboardDataType.Uri;
            }

            return DetectedClipboardDataType.Text;
        }
        else if (item.Content.AvailableFormats.Contains(StandardDataFormats.Html))
        {
            if (item.Content.AvailableFormats.Contains(StandardDataFormats.Text))
            {
                string text = await item.Content.GetTextAsync();
                if (IsUri(text))
                {
                    return DetectedClipboardDataType.Uri;
                }
            }

            return DetectedClipboardDataType.Html;
        }
        else if (item.Content.AvailableFormats.Contains("AnsiText") ||
                 item.Content.AvailableFormats.Contains("OEMText") ||
                 item.Content.AvailableFormats.Contains("TEXT"))
        {
            return DetectedClipboardDataType.Text;
        }

        // Log unknown formats for debugging and future enhancement
        LogUnknownFormats(item.Content.AvailableFormats);
        return DetectedClipboardDataType.Unknown;
    }

    private static bool IsHexColor(string text)
    {
        return IsValidHexColor(text);
    }

    private static bool IsValidHexColor(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 20)
        {
            return false;
        }

        // Remove optional '#' prefix
        string hex = text.StartsWith('#') ? text[1..] : text;

        // Check valid lengths (3, 6, or 8 characters)
        if (hex.Length != 3 && hex.Length != 6 && hex.Length != 8)
        {
            return false;
        }

        // Check if all characters are valid hex digits
        foreach (char c in hex)
        {
            if (!IsHexDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsHexDigit(char c)
    {
        return (c >= '0' && c <= '9') ||
           (c >= 'A' && c <= 'F') ||
           (c >= 'a' && c <= 'f');
    }

    private static bool IsUri(string text)
    {
        return Uri.TryCreate(text, UriKind.Absolute, out Uri? uriResult)
            && uriResult is not null
            && (uriResult.Scheme == Uri.UriSchemeHttp
            || uriResult.Scheme == Uri.UriSchemeHttps
            || uriResult.Scheme == Uri.UriSchemeFtp
            || uriResult.Scheme == Uri.UriSchemeMailto);
    }

    private static void LogUnknownFormats(IReadOnlyList<string> availableFormats)
    {
        string formatsString = string.Join(", ", availableFormats);
        typeof(DataHelper).Log().LogWarning("Unknown clipboard data formats detected: {Formats}", formatsString);
    }
}
