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
            // Clipboard providers can pump messages or block. Read and decode them off the XAML thread.
            var (thumbnail, width, height) = await Task.Run(async () =>
            {
                var reference = await dataPackageView.GetBitmapAsync();
                using var source = await reference.OpenReadAsync();
                var decoder = await BitmapDecoder.CreateAsync(source);
                uint width = decoder.PixelWidth;
                uint height = decoder.PixelHeight;
                var size = Core.ThumbnailSize.Fit(width, height, 256);
                var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                    new BitmapTransform { ScaledWidth = size.Width, ScaledHeight = size.Height },
                    ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
                var thumbnail = new InMemoryRandomAccessStream();
                try
                {
                    var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, thumbnail);
                    encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                        size.Width, size.Height, 96, 96, pixels.DetachPixelData());
                    await encoder.FlushAsync();
                    thumbnail.Seek(0);
                    return (thumbnail, width, height);
                }
                catch { thumbnail.Dispose(); throw; }
            });
            using var thumbnailStream = thumbnail;
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(thumbnailStream);
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

    internal static async Task<DetectedClipboardDataType> GetDetectedClipboardDataTypeAsync(DataPackageView content)
    {
        if (content.AvailableFormats.Contains(StandardDataFormats.StorageItems))
        {
            return DetectedClipboardDataType.File;
        }
        else if (content.AvailableFormats.Contains(StandardDataFormats.Bitmap))
        {
            return DetectedClipboardDataType.Image;
        }
        else if (content.AvailableFormats.Contains("DeviceIndependentBitmap") ||
                 content.AvailableFormats.Contains("DeviceIndependentBitmapV5") ||
                 content.AvailableFormats.Contains("TaggedImageFileFormat") ||
                 content.AvailableFormats.Contains("EnhancedMetafile"))
        {
            return DetectedClipboardDataType.Image;
        }
        else if (content.AvailableFormats.Contains(StandardDataFormats.Rtf))
        {
            return DetectedClipboardDataType.Rtf;
        }
        else if (content.AvailableFormats.Contains(StandardDataFormats.ApplicationLink))
        {
            return DetectedClipboardDataType.ApplicationLink;
        }
        else if (content.AvailableFormats.Contains(StandardDataFormats.UserActivityJsonArray))
        {
            return DetectedClipboardDataType.UserActivity;
        }
        else if (content.AvailableFormats.Contains(StandardDataFormats.WebLink)
            || content.AvailableFormats.Contains(StandardDataFormats.Uri))
        {
            return DetectedClipboardDataType.Uri;
        }
        else if (content.AvailableFormats.Contains(StandardDataFormats.Text))
        {
            string text = await content.GetTextAsync();
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
        else if (content.AvailableFormats.Contains(StandardDataFormats.Html))
        {
            if (content.AvailableFormats.Contains(StandardDataFormats.Text))
            {
                string text = await content.GetTextAsync();
                if (IsUri(text))
                {
                    return DetectedClipboardDataType.Uri;
                }
            }

            return DetectedClipboardDataType.Html;
        }
        else if (content.AvailableFormats.Contains("AnsiText") ||
                 content.AvailableFormats.Contains("OEMText") ||
                 content.AvailableFormats.Contains("TEXT"))
        {
            return DetectedClipboardDataType.Text;
        }

        // Log unknown formats for debugging and future enhancement
        LogUnknownFormats(content.AvailableFormats);
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
