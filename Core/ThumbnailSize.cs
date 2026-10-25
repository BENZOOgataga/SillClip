namespace WindowSill.ClipboardHistory.Core;

internal static class ThumbnailSize
{
    internal static (uint Width, uint Height) Fit(uint width, uint height, uint maximum)
    {
        if (width == 0 || height == 0 || maximum == 0) throw new ArgumentOutOfRangeException(nameof(width));
        double scale = Math.Min(1, (double)maximum / Math.Max(width, height));
        return (Math.Max(1, (uint)(width * scale)), Math.Max(1, (uint)(height * scale)));
    }
}
