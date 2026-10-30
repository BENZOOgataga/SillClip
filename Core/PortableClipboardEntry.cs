namespace WindowSill.ClipboardHistory.Core;

internal sealed class PortableClipboardEntry
{
    public PinnedClipboardItem Item { get; set; } = new();
    public bool IsPinned { get; set; }
    public string[] Collections { get; set; } = [];
    public byte[]? Image { get; set; }

    /// <summary>Validates archive data without resolving paths, activating links, or decoding images.</summary>
    internal bool NormalizeForImport(bool allowLocalFileReferences)
    {
        if (Item is null || !Enum.IsDefined(Item.DataType)) throw new InvalidDataException("Unknown clipboard item type.");
        if (Item.DataType == DetectedClipboardDataType.File)
        {
            if (!allowLocalFileReferences) return false;
            if (Item.FilePaths is not { Length: > 0 } || Item.FilePaths.Any(path =>
                string.IsNullOrWhiteSpace(path) || path.Length < 3 || !char.IsAsciiLetter(path[0])
                || path[1] != ':' || path[2] != '\\' || path.Any(char.IsControl)))
                throw new InvalidDataException("Only local drive file references can be restored.");
            // Restored paths are inert text. Import must never resolve mapped drives or reparse points.
            Item.Text = string.Join(Environment.NewLine, Item.FilePaths);
            Item.FilePaths = null;
            Item.DataType = DetectedClipboardDataType.Text;
        }
        else Item.FilePaths = null;

        if (Item.DataType is DetectedClipboardDataType.ApplicationLink or DetectedClipboardDataType.UserActivity)
        {
            Item.Text ??= Item.ApplicationLink ?? Item.UserActivity ?? string.Empty;
            Item.DataType = DetectedClipboardDataType.Text;
        }
        Item.ApplicationLink = null;
        Item.UserActivity = null;
        if (Item.DataType == DetectedClipboardDataType.Uri
            && (!Uri.TryCreate(Item.Uri, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")))
        {
            Item.Text ??= Item.Uri;
            Item.Uri = null;
            Item.DataType = DetectedClipboardDataType.Text;
        }
        if (Item.DataType != DetectedClipboardDataType.Uri) Item.Uri = null;
        if (Item.DataType != DetectedClipboardDataType.Html) Item.Html = null;
        if (Item.DataType != DetectedClipboardDataType.Rtf) Item.Rtf = null;
        Item.Id = Guid.NewGuid().ToString("N");
        Item.ImageBytes = Item.DataType == DetectedClipboardDataType.Image ? Image : null;
        Item.HasImage = Item.ImageBytes is { Length: > 0 };
        Item.ImageContentHash = null;
        if (Item.DataType == DetectedClipboardDataType.Image && !Item.HasImage)
            throw new InvalidDataException("An image entry has no payload.");
        Collections = (Collections ?? []).Select(LibraryState.ValidateCollectionName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        Item.ContentSignature = ClipboardSignature.Compute(Item);
        if (string.IsNullOrEmpty(Item.ContentSignature)) throw new InvalidDataException("An archive entry has no signature.");
        return true;
    }
}
