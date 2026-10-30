using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WindowSill.ClipboardHistory.Core;
using WindowSill.ClipboardHistory.Services;
using WindowSill.ClipboardHistory.UI;

namespace WindowSill.ClipboardHistory.ViewModels;

internal sealed partial class ComparisonViewModel(WorkspaceViewModel owner) : ObservableObject
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyCommand))]
    public partial string Diff { get; private set; } = string.Empty;
    [ObservableProperty] public partial string Summary { get; private set; } = string.Empty;

    private bool CanCompare => owner.Selection.Count == 2 && owner.Selection.All(item => item.CanTransformText);
    private bool CanCopy => !string.IsNullOrEmpty(Diff);
    internal void RefreshCommands() => CompareCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanCompare))]
    private Task CompareAsync() => owner.RunAsync("Compare", async cancellationToken =>
    {
        var items = owner.RequireSelection(2);
        string before = await items[0].ReadComparisonTextAsync();
        string after = await items[1].ReadComparisonTextAsync();
        var result = await Task.Run(() => TextComparison.Compare(before, after, cancellationToken), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (owner.Service.State.PresentationMode) return;
        Diff = result.Diff;
        Summary = WorkspaceStrings.Format("WorkspaceDiffSummary", result.Added, result.Removed)
            + (result.IsTruncated ? " " + WorkspaceStrings.Get("WorkspaceDiffTruncated") : string.Empty);
    });

    [RelayCommand(CanExecute = nameof(CanCopy))]
    private Task CopyAsync() => owner.RunAsync("CopyComparison", async _ =>
    {
        if (owner.Service.State.PresentationMode) throw new InvalidOperationException("Presentation mode is enabled.");
        await ClipboardPasteService.CopyTextAsync(Diff);
        owner.Status = WorkspaceStrings.Get("WorkspaceCopied");
    });

    internal void Clear() { Diff = string.Empty; Summary = string.Empty; }
}
