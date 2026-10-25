namespace WindowSill.ClipboardHistory.Core;

internal static class HistoryRetention
{
    internal static PinnedClipboardItem[] Expired(IEnumerable<PinnedClipboardItem> items, int maximum,
        IReadOnlySet<string> pinnedSignatures)
        => maximum <= 0 ? [] : items
            .Where(item => !pinnedSignatures.Contains(item.ContentSignature))
            .OrderByDescending(item => item.PinnedAt)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .Skip(maximum).ToArray();
}
