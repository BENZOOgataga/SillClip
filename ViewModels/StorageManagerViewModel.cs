using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WindowSill.ClipboardHistory.Services;
using WindowSill.ClipboardHistory.UI;

namespace WindowSill.ClipboardHistory.ViewModels;

internal sealed record StorageRow(StorageEntry Entry, string Label)
{
    public string Size => FormatBytes(Entry.Bytes);
    public string Date => Entry.Item.CapturedAt.LocalDateTime.ToString("g");
    public bool IsPinned => Entry.Item.Source.IsPinned;
    public string PinLabel => IsPinned ? WorkspaceStrings.Get("WorkspacePinnedItems") : string.Empty;
    internal static string FormatBytes(long bytes) => bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):N1} GiB"
        : bytes >= 1L << 20 ? $"{bytes / (double)(1L << 20):N1} MiB" : $"{bytes / 1024.0:N1} KiB";
}

internal sealed partial class StorageManagerViewModel(WorkspaceViewModel owner) : ObservableObject
{
    private StorageOverview? _overview;
    public ObservableCollection<StorageRow> Entries { get; } = [];
    [ObservableProperty] public partial StorageRow? SelectedEntry { get; set; }
    [ObservableProperty] public partial string OverviewText { get; private set; } = string.Empty;
    [ObservableProperty] public partial DateTimeOffset? FromDate { get; set; }
    [ObservableProperty] public partial DateTimeOffset? ThroughDate { get; set; }
    [ObservableProperty] public partial bool ImagesOnly { get; set; } = true;
    [ObservableProperty] public partial bool IncludePins { get; set; }

    [RelayCommand]
    private Task RefreshAsync() => owner.RunAsync("StorageRefresh", RefreshCoreAsync);

    private async Task RefreshCoreAsync(CancellationToken cancellationToken)
    {
        _overview = await Task.Run(() => owner.Service.GetStorageAsync(cancellationToken), cancellationToken);
        var bySignature = owner.History.AllItems.GroupBy(item => item.ContentSignature).ToDictionary(group => group.Key, group => group.First());
        Entries.Clear();
        foreach (var entry in _overview.Entries)
            Entries.Add(new(entry, bySignature.TryGetValue(entry.Item.ContentSignature, out var viewModel) ? viewModel.Summary : entry.Item.DataType.ToString()));
        OverviewText = WorkspaceStrings.Format("WorkspaceStorageSummary", _overview.Entries.Count, StorageRow.FormatBytes(_overview.TotalBytes));
    }

    [RelayCommand]
    private Task DeleteSelectedAsync() => owner.RunAsync("StorageDelete", async cancellationToken =>
    {
        var entry = SelectedEntry ?? throw new ArgumentException("Choose an entry.");
        if (entry.IsPinned && !IncludePins) throw new ArgumentException("Enable Include pinned items first.");
        if (!await owner.Interaction.ConfirmAsync(WorkspaceStrings.Get("WorkspaceDeleteEntries"), WorkspaceStrings.Format("WorkspaceDeleteCount", 1), cancellationToken)) return;
        await Task.Run(() => owner.Service.DeleteAsync([entry.Entry.Item], cancellationToken), cancellationToken);
        await RefreshCoreAsync(cancellationToken);
    });

    [RelayCommand]
    private Task CleanRangeAsync() => owner.RunAsync("StorageCleanRange", async cancellationToken =>
    {
        if (FromDate is null || ThroughDate is null || FromDate.Value.Date > ThroughDate.Value.Date)
            throw new ArgumentException("Choose a valid date range.");
        var overview = await Task.Run(() => owner.Service.GetStorageAsync(cancellationToken), cancellationToken);
        var targets = overview.Entries.Where(entry => (IncludePins || !entry.Item.Source.IsPinned)
            && (!ImagesOnly || entry.Item.DataType == DetectedClipboardDataType.Image)
            && entry.Item.CapturedAt.LocalDateTime.Date >= FromDate.Value.Date
            && entry.Item.CapturedAt.LocalDateTime.Date <= ThroughDate.Value.Date).Select(entry => entry.Item).ToArray();
        if (targets.Length == 0) { owner.Status = WorkspaceStrings.Get("WorkspaceNoCleanupMatches"); return; }
        if (!await owner.Interaction.ConfirmAsync(WorkspaceStrings.Get("WorkspaceDeleteEntries"), WorkspaceStrings.Format("WorkspaceDeleteCount", targets.Length), cancellationToken)) return;
        await Task.Run(() => owner.Service.DeleteAsync(targets, cancellationToken), cancellationToken);
        await RefreshCoreAsync(cancellationToken);
    });
}
