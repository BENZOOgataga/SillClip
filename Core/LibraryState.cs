namespace WindowSill.ClipboardHistory.Core;

internal sealed record CaptureExclusion(DateTimeOffset Start, DateTimeOffset End);

/// <summary>Small encrypted metadata journal, independent from clipboard payload files.</summary>
internal sealed class LibraryState
{
    public int Version { get; set; } = 1;
    public List<CaptureExclusion> CaptureExclusions { get; set; } = [];
    public bool PresentationMode { get; set; }
    public List<string> Collections { get; set; } = [];
    public Dictionary<string, List<string>> Memberships { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, DateTimeOffset> Deleted { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, DateTimeOffset> Restored { get; set; } = new(StringComparer.Ordinal);
    public DateTimeOffset ClearedAt { get; set; } = DateTimeOffset.MinValue;

    internal bool AllowsCapture(DateTimeOffset copiedAt)
        => copiedAt > ClearedAt && !CaptureExclusions.Any(range => copiedAt >= range.Start && copiedAt < range.End);

    internal bool AllowsItem(string signature, DateTimeOffset copiedAt)
        => (copiedAt > ClearedAt || Restored.TryGetValue(signature, out var restoredAt) && restoredAt == copiedAt)
            && (!Deleted.TryGetValue(signature, out var deletedAt) || copiedAt > deletedAt);

    internal bool AllowsPinnedItem(string signature, DateTimeOffset copiedAt)
        => !Deleted.TryGetValue(signature, out var deletedAt) || copiedAt > deletedAt;

    internal DateTimeOffset? PausedUntil(DateTimeOffset now)
        => CaptureExclusions.LastOrDefault(range => now >= range.Start && now < range.End)?.End;

    internal void Pause(DateTimeOffset now, TimeSpan? duration)
    {
        Resume(now);
        CaptureExclusions.Add(new(now, duration.HasValue ? now.Add(duration.Value) : DateTimeOffset.MaxValue));
    }

    internal void Resume(DateTimeOffset now)
    {
        for (int i = 0; i < CaptureExclusions.Count; i++)
            if (CaptureExclusions[i].Start <= now && CaptureExclusions[i].End > now)
                CaptureExclusions[i] = CaptureExclusions[i] with { End = now };
    }

    internal static string ValidateCollectionName(string value)
    {
        string name = value.Trim();
        if (name.Length is < 1 or > 80 || name.Any(char.IsControl))
            throw new ArgumentException("Collection names must contain 1 to 80 printable characters.");
        return name;
    }
}
