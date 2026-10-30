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
    internal ClipboardWorkspaceService Workspace { get; }
    private bool _filterQueued;
    private bool _disposed;
    private bool _applyingFilter;

    public ClipboardHistoryPopupViewModel(PersistentClipboardService persistentService, ClipboardWorkspaceService workspace)
    {
        Workspace = workspace;
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

    [ObservableProperty]
    public partial string QueryError { get; private set; } = string.Empty;

    public Visibility QueryErrorVisibility => string.IsNullOrEmpty(QueryError) ? Visibility.Collapsed : Visibility.Visible;

    partial void OnSearchQueryChanged(string value) => RefreshFilter();

    /// <summary>
    /// Gets the command to clear all clipboard history.
    /// </summary>
    [RelayCommand]
    private async Task ClearAllAsync()
    {
        try { await Workspace.ClearUnpinnedAsync(); }
        catch (Exception ex)
        {
            Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(typeof(ClipboardHistoryPopupViewModel).Log(),
                "Clearing clipboard history failed ({ErrorType}).", ex.GetType().Name);
            QueryError = UI.WorkspaceStrings.Get("WorkspaceActionFailed");
            OnPropertyChanged(nameof(QueryErrorVisibility));
        }
    }

    internal void RefreshFilter()
    {
        if (_disposed) return;
        var query = HistoryQuery.Parse(SearchQuery);
        QueryError = query.Error is null ? string.Empty : UI.WorkspaceStrings.Get(query.Error);
        OnPropertyChanged(nameof(QueryErrorVisibility));
        IEnumerable<ClipboardHistoryItemViewModelBase> matchingItems = AllItems.Where(item => query.Matches(
            new(item.SearchText, item.TypeName, item.CapturedAt, item.IsPinned, Workspace.State.CollectionsFor(item.ContentSignature))));

        _applyingFilter = true;
        try { CollectionReconciler.Update(Items, matchingItems.ToArray()); }
        finally { _applyingFilter = false; }

        OnPropertyChanged(nameof(EmptyClipboardIndicatorVisibility));
        OnPropertyChanged(nameof(NoSearchResultsVisibility));
    }

    internal void UpdateMetadata(IReadOnlyList<ClipboardItemData> items)
    {
        var byId = items.GroupBy(item => item.Source.Id, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        foreach (var viewModel in AllItems)
            if (byId.TryGetValue(viewModel.SourceId, out var item)) viewModel.UpdateItemData(item);
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
