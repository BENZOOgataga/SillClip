using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Windows.ApplicationModel.DataTransfer;
using WindowSill.API;
using WindowSill.ClipboardHistory.Services;

namespace WindowSill.ClipboardHistory.ViewModels;

/// <summary>
/// ViewModel for the compact mode clipboard history popup.
/// Manages the collection of clipboard items displayed within the popup.
/// </summary>
internal sealed partial class ClipboardHistoryPopupViewModel : ObservableObject
{
    private readonly PersistentClipboardService _persistentService;

    public ClipboardHistoryPopupViewModel(PersistentClipboardService persistentService)
    {
        _persistentService = persistentService;
        AllItems.CollectionChanged += AllItems_CollectionChanged;
        Items.CollectionChanged += Items_CollectionChanged;
    }

    internal ObservableCollection<ClipboardHistoryItemViewModelBase> AllItems { get; } = [];

    /// <summary>
    /// Gets the collection of clipboard history item ViewModels displayed in the popup.
    /// </summary>
    public ObservableCollection<ClipboardHistoryItemViewModelBase> Items { get; } = [];

    [ObservableProperty]
    public partial string SearchQuery { get; set; } = string.Empty;

    public Visibility EmptyClipboardIndicatorVisibility
        => AllItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility NoSearchResultsVisibility
        => AllItems.Count > 0 && Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    partial void OnSearchQueryChanged(string value) => RefreshFilter();

    /// <summary>
    /// Gets the command to clear all clipboard history.
    /// </summary>
    [RelayCommand]
    private async Task ClearAllAsync()
    {
        await ThreadHelper.RunOnUIThreadAsync(() =>
        {
            Clipboard.ClearHistory();
        });

        await _persistentService.ClearAsync();
    }

    internal void RefreshFilter()
    {
        string[] terms = SearchQuery
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        IEnumerable<ClipboardHistoryItemViewModelBase> matchingItems = terms.Length == 0
            ? AllItems
            : AllItems.Where(item => terms.All(term =>
                item.SearchText.Contains(term, StringComparison.CurrentCultureIgnoreCase)));

        Items.SynchronizeWith(
            matchingItems.ToArray(),
            (existing, updated) => existing.Equals(updated),
            updated => updated);

        OnPropertyChanged(nameof(EmptyClipboardIndicatorVisibility));
        OnPropertyChanged(nameof(NoSearchResultsVisibility));
    }

    private void AllItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (ClipboardHistoryItemViewModelBase item in e.OldItems)
            {
                item.PropertyChanged -= Item_PropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (ClipboardHistoryItemViewModelBase item in e.NewItems)
            {
                item.PropertyChanged += Item_PropertyChanged;
            }
        }

        RefreshFilter();
    }

    private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ClipboardHistoryItemViewModelBase.SearchText)
            && !string.IsNullOrWhiteSpace(SearchQuery))
        {
            RefreshFilter();
        }
    }

    private void Items_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(EmptyClipboardIndicatorVisibility));
        OnPropertyChanged(nameof(NoSearchResultsVisibility));
    }
}
