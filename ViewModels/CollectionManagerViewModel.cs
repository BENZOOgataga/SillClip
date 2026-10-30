using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WindowSill.ClipboardHistory.UI;

namespace WindowSill.ClipboardHistory.ViewModels;

internal sealed partial class CollectionManagerViewModel(WorkspaceViewModel owner) : ObservableObject
{
    public ObservableCollection<string> Names { get; } = [];
    [ObservableProperty] public partial string NewName { get; set; } = string.Empty;
    [ObservableProperty] public partial string? SelectedName { get; set; }

    private bool CanCreate => !string.IsNullOrWhiteSpace(NewName);
    private bool HasCollection => SelectedName is not null;
    private bool CanAssign => HasCollection && owner.Selection.Count > 0;
    partial void OnNewNameChanged(string value) => CreateCommand.NotifyCanExecuteChanged();
    partial void OnSelectedNameChanged(string? value) => RefreshCommands();
    internal void RefreshCommands()
    {
        AssignCommand.NotifyCanExecuteChanged();
        RemoveMembershipCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        ShowCommand.NotifyCanExecuteChanged();
    }

    internal void Refresh()
    {
        string? selected = SelectedName;
        Core.CollectionReconciler.Update(Names, owner.Service.State.Collections.Order(StringComparer.CurrentCultureIgnoreCase).ToArray());
        SelectedName = Names.FirstOrDefault(name => string.Equals(name, selected, StringComparison.OrdinalIgnoreCase));
    }

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private Task CreateAsync() => owner.RunAsync("CreateCollection", async _ =>
    {
        string name = Core.LibraryState.ValidateCollectionName(NewName);
        await owner.Service.State.CreateCollectionAsync(name);
        Refresh(); SelectedName = name; NewName = string.Empty;
        owner.Status = WorkspaceStrings.Get("WorkspaceCollectionCreated");
    });

    [RelayCommand(CanExecute = nameof(CanAssign))]
    private Task AssignAsync() => owner.RunAsync("AssignCollection", async _ =>
    {
        string name = SelectedName ?? throw new ArgumentException("Choose a collection.");
        var selection = owner.RequireSelection();
        await owner.Service.State.AssignAsync(selection.Select(item => item.ContentSignature), name, remove: false);
        owner.History.RefreshFilter(); owner.Status = WorkspaceStrings.Get("WorkspaceCollectionAssigned");
    });

    [RelayCommand(CanExecute = nameof(CanAssign))]
    private Task RemoveMembershipAsync() => owner.RunAsync("RemoveMembership", async _ =>
    {
        string name = SelectedName ?? throw new ArgumentException("Choose a collection.");
        var selection = owner.RequireSelection();
        await owner.Service.State.AssignAsync(selection.Select(item => item.ContentSignature), name, remove: true);
        owner.History.RefreshFilter(); owner.Status = WorkspaceStrings.Get("WorkspaceCollectionAssigned");
    });

    [RelayCommand(CanExecute = nameof(HasCollection))]
    private Task DeleteAsync() => owner.RunAsync("DeleteCollection", async cancellationToken =>
    {
        string name = SelectedName ?? throw new ArgumentException("Choose a collection.");
        if (!await owner.Interaction.ConfirmAsync(WorkspaceStrings.Get("WorkspaceDeleteCollection"), WorkspaceStrings.Get("WorkspaceDeleteCollectionBody"), cancellationToken)) return;
        await owner.Service.State.RemoveCollectionAsync(name);
        Refresh(); owner.History.RefreshFilter();
    });

    [RelayCommand(CanExecute = nameof(HasCollection))]
    private void Show()
    {
        if (SelectedName is null) return;
        owner.History.SearchQuery = "collection:\"" + SelectedName.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        owner.SelectedTabIndex = 0;
    }
}
