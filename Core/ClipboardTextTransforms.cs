using System.Text.Json;
using System.Text.RegularExpressions;

namespace WindowSill.ClipboardHistory.Core;

internal enum ClipboardTextTransform { Original, PlainText, SingleLine, FormattedJson, CleanUrl }

internal static partial class ClipboardTextTransforms
{
    internal static string Apply(string text, ClipboardTextTransform transform) => transform switch
    {
        ClipboardTextTransform.Original or ClipboardTextTransform.PlainText => text,
        ClipboardTextTransform.SingleLine => Whitespace().Replace(text, " ").Trim(),
        ClipboardTextTransform.FormattedJson => FormatJson(text),
        ClipboardTextTransform.CleanUrl => CleanUrl(text),
        _ => throw new ArgumentOutOfRangeException(nameof(transform))
    };

    private static string FormatJson(string text)
    {
        using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 64 });
        return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string CleanUrl(string text)
    {
        if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new FormatException("Select an HTTP or HTTPS URL.");
        // Preserve the original encoding, fragment, and non-tracking parameters.
        string original = text.Trim();
        int hash = original.IndexOf('#');
        string fragment = hash >= 0 ? original[hash..] : string.Empty;
        string withoutFragment = hash >= 0 ? original[..hash] : original;
        int question = withoutFragment.IndexOf('?');
        if (question < 0) return original;
        var parameters = withoutFragment[(question + 1)..].Split('&').Where(part =>
        {
            string key = Uri.UnescapeDataString(part.Split('=', 2)[0]).ToLowerInvariant();
            return !key.StartsWith("utm_", StringComparison.Ordinal)
                && key is not ("fbclid" or "gclid" or "dclid" or "msclkid" or "mc_cid" or "mc_eid");
        }).ToArray();
        return withoutFragment[..question] + (parameters.Length > 0 ? "?" + string.Join('&', parameters) : string.Empty) + fragment;
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)] private static partial Regex Whitespace();
}
