using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Windows.ApplicationModel.DataTransfer;
using WindowSill.API;
using WindowSill.ClipboardHistory.Services;
using WindowSill.ClipboardHistory.Core;

namespace WindowSill.ClipboardHistory.ViewModels;

/// <summary>
/// ViewModel for the compact mode clipboard history popup.
/// Manages the collection of clipboard items displayed within the popup.
/// </summary>
internal sealed partial class ClipboardHistoryPopupViewModel : ObservableObject, IDisposable
{
    private readonly PersistentClipboardService _persistentService;
    private bool _filterQueued;
    private bool _disposed;
    private bool _applyingFilter;

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
        if (_disposed) return;
        string[] terms = SearchQuery
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        IEnumerable<ClipboardHistoryItemViewModelBase> matchingItems = terms.Length == 0
            ? AllItems
            : AllItems.Where(item => terms.All(term =>
                item.SearchText.Contains(term, StringComparison.CurrentCultureIgnoreCase)));

        _applyingFilter = true;
        try { CollectionReconciler.Update(Items, matchingItems.ToArray()); }
        finally { _applyingFilter = false; }

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

        QueueFilter();
    }

    private void QueueFilter()
    {
        if (!_filterQueued && !_disposed)
        {
            _filterQueued = true;
            Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                _filterQueued = false;
                if (!_disposed) RefreshFilter();
            });
        }
    }

    private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ClipboardHistoryItemViewModelBase.SearchText)
            && !string.IsNullOrWhiteSpace(SearchQuery))
        {
            QueueFilter();
        }
    }

    private void Items_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_applyingFilter) return;
        OnPropertyChanged(nameof(EmptyClipboardIndicatorVisibility));
        OnPropertyChanged(nameof(NoSearchResultsVisibility));
    }

    public void Dispose()
    {
        _disposed = true;
        foreach (var item in AllItems) item.PropertyChanged -= Item_PropertyChanged;
        AllItems.CollectionChanged -= AllItems_CollectionChanged;
        Items.CollectionChanged -= Items_CollectionChanged;
        Items.Clear();
        AllItems.Clear();
    }
}
