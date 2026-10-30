using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WindowSill.ClipboardHistory.Services;
using WindowSill.ClipboardHistory.UI;

namespace WindowSill.ClipboardHistory.ViewModels;

internal sealed record QueueRow(Guid Id, string Signature, string Label);

internal sealed partial class QueueManagerViewModel(WorkspaceViewModel owner) : ObservableObject
{
    public ObservableCollection<QueueRow> Entries { get; } = [];
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand), nameof(MoveUpCommand), nameof(MoveDownCommand))]
    public partial QueueRow? SelectedEntry { get; set; }
    [ObservableProperty] public partial int SeparatorIndex { get; set; }
    public string CountText => WorkspaceStrings.Format("WorkspaceQueueCount", Entries.Count);
    private bool CanAdd => owner.Selection.Count > 0;
    private bool HasEntries => Entries.Count > 0;
    private bool HasSelection => SelectedEntry is not null;
    private bool CanMoveUp => SelectedEntry is not null && Entries.IndexOf(SelectedEntry) > 0;
    private bool CanMoveDown => SelectedEntry is not null && Entries.IndexOf(SelectedEntry) < Entries.Count - 1;
    internal void RefreshCommands()
    {
        AddCommand.NotifyCanExecuteChanged();
        PasteNextCommand.NotifyCanExecuteChanged();
        PasteTogetherCommand.NotifyCanExecuteChanged();
        ClearCommand.NotifyCanExecuteChanged();
        RemoveCommand.NotifyCanExecuteChanged();
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
    }

    internal void Refresh()
    {
        var bySignature = owner.History.AllItems.GroupBy(item => item.ContentSignature).ToDictionary(group => group.Key, group => group.First());
        var selectedId = SelectedEntry?.Id;
        Entries.Clear();
        foreach (var item in owner.Service.Queue.Entries)
            Entries.Add(new(item.Id, item.Signature, bySignature.TryGetValue(item.Signature, out var viewModel) ? viewModel.Summary : WorkspaceStrings.Get("WorkspaceUnavailableItem")));
        SelectedEntry = Entries.FirstOrDefault(entry => entry.Id == selectedId);
        OnPropertyChanged(nameof(CountText));
        RefreshCommands();
    }

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add()
    {
        try { owner.Service.Queue.Add(owner.RequireSelection().Select(item => item.ContentSignature)); }
        catch (ArgumentException) { owner.Status = WorkspaceStrings.Get("WorkspaceCheckInput"); }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))] private void Remove() { if (SelectedEntry is not null) owner.Service.Queue.Remove(SelectedEntry.Id); }
    [RelayCommand(CanExecute = nameof(CanMoveUp))] private void MoveUp() { if (SelectedEntry is not null) owner.Service.Queue.Move(SelectedEntry.Id, -1); }
    [RelayCommand(CanExecute = nameof(CanMoveDown))] private void MoveDown() { if (SelectedEntry is not null) owner.Service.Queue.Move(SelectedEntry.Id, 1); }
    [RelayCommand(CanExecute = nameof(HasEntries))] private void Clear() => owner.Service.Queue.Clear();

    [RelayCommand(CanExecute = nameof(HasEntries))]
    private Task PasteNextAsync() => owner.RunAsync("PasteNext", async _ =>
    {
        var entry = owner.Service.Queue.Entries.FirstOrDefault() ?? throw new ArgumentException("The queue is empty.");
        var item = owner.History.AllItems.FirstOrDefault(item => item.ContentSignature == entry.Signature)
            ?? throw new InvalidOperationException("The queued item is no longer available.");
        if (await owner.Interaction.PasteAsync(item, Core.ClipboardTextTransform.Original)) owner.Service.Queue.Remove(entry.Id);
        else owner.Status = WorkspaceStrings.Get("WorkspaceCopiedFocusChanged");
    });

    [RelayCommand(CanExecute = nameof(HasEntries))]
    private Task PasteTogetherAsync() => owner.RunAsync("PasteTogether", async cancellationToken =>
    {
        var queued = owner.Service.Queue.Entries;
        if (queued.Count == 0) throw new ArgumentException("The queue is empty.");
        var selected = new List<ClipboardHistoryItemViewModelBase>();
        var text = new List<string>();
        foreach (var entry in queued)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = owner.History.AllItems.FirstOrDefault(item => item.ContentSignature == entry.Signature)
                ?? throw new InvalidOperationException("A queued item is unavailable.");
            selected.Add(item); text.Add(await item.ReadComparisonTextAsync());
        }
        string separator = SeparatorIndex switch { 1 => "\t", 2 => Environment.NewLine + Environment.NewLine, _ => Environment.NewLine };
        cancellationToken.ThrowIfCancellationRequested();
        if (await owner.Interaction.PasteAsync(selected[0], Core.ClipboardTextTransform.PlainText, string.Join(separator, text)))
            foreach (var entry in queued) owner.Service.Queue.Remove(entry.Id);
        else owner.Status = WorkspaceStrings.Get("WorkspaceCopiedFocusChanged");
    });
}
