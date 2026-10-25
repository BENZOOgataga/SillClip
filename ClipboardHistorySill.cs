using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using WindowSill.API;
using WindowSill.ClipboardHistory.Core;
using WindowSill.ClipboardHistory.Factories;
using WindowSill.ClipboardHistory.FirstTimeSetup;
using WindowSill.ClipboardHistory.Services;
using WindowSill.ClipboardHistory.Settings;
using WindowSill.ClipboardHistory.ViewModels;
using WindowSill.ClipboardHistory.Views;

namespace WindowSill.ClipboardHistory;

[Export(typeof(ISill))]
[Name("Clipboard History No Limits")]
[Priority(Priority.Low)]
[HideIconInSillListView]
[SupportMultipleMonitors(showOnEveryMonitorsByDefault: true)]
public sealed partial class ClipboardHistorySill : ObservableObject, ISillActivatedByDefault, ISillFirstTimeSetup, ISillListView
{
    private const int ExpandedBarItemLimit = 25;

    private readonly ILogger _logger;
    private readonly IPluginInfo _pluginInfo;
    private readonly ISettingsProvider _settingsProvider;
    private readonly ClipboardHistoryDataService _clipboardDataService;
    private readonly PinnedClipboardService _pinnedService;
    private readonly PersistentClipboardService _persistentService;
    private readonly GitHubUpdateService _updateService;
    private readonly WinVShortcutService _winVShortcutService;
    private readonly ClipboardItemViewFactory _viewFactory;

    private ClipboardHistoryPopupViewModel? _popupViewModel;
    private SillListViewMenuFlyoutItem? _iconItem;
    private SillListViewPopupItem? _searchItem;
    private SillListViewPopupItem? _popupAnchor;
    private ClipboardHistoryPopupContent? _popupContent;
    private SillPopup? _shortcutPopup;
    private ClipboardHistoryPopupContent? _shortcutContent;
    private bool _shortcutPopupOpen;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private CancellationTokenSource? _activation;

    [ImportingConstructor]
    internal ClipboardHistorySill(
        IProcessInteractionService processInteractionService,
        ISettingsProvider settingsProvider,
        IPluginInfo pluginInfo,
        ClipboardHistoryDataService clipboardDataService,
        PinnedClipboardService pinnedService,
        PersistentClipboardService persistentService,
        GitHubUpdateService updateService,
        WinVShortcutService winVShortcutService)
    {
        _logger = this.Log();
        _pluginInfo = pluginInfo;
        _settingsProvider = settingsProvider;
        _clipboardDataService = clipboardDataService;
        _pinnedService = pinnedService;
        _persistentService = persistentService;
        _updateService = updateService;
        _winVShortcutService = winVShortcutService;
        _viewFactory = new ClipboardItemViewFactory(
            _pluginInfo,
            settingsProvider,
            processInteractionService,
            pinnedService,
            persistentService,
            clipboardDataService);
        PlaceholderView = _viewFactory.CreatePlaceholderView();
    }

    public string DisplayName => "/WindowSill.ClipboardHistory/Misc/DisplayName".GetLocalizedString();

    private bool IsCompactMode => _settingsProvider.GetSetting(Settings.Settings.CompactMode);

    public IconElement CreateIcon()
        => new ImageIcon
        {
            Source = new SvgImageSource(new Uri(System.IO.Path.Combine(_pluginInfo.GetPluginContentDirectory(), "Assets", "clipboard.svg")))
        };

    public SillSettingsView[]? SettingsViews =>
        [
        new SillSettingsView(
            DisplayName,
            new(() => new SettingsView(_settingsProvider, _updateService)))
        ];

    public ObservableCollection<SillListViewItem> ViewList { get; } = new();

    public SillView? PlaceholderView { get; }

    public IFirstTimeSetupContributor[] GetFirstTimeSetupContributors()
    {
        if (_clipboardDataService.IsHistoryEnabled)
        {
            return [];
        }

        return [new ClipboardHistoryFirstTimeSetupContributor()];
    }

    public async ValueTask OnActivatedAsync()
    {
        _activation = new CancellationTokenSource();
        if (!_settingsProvider.GetSetting(Settings.Settings.UnlimitedDefaultsApplied))
        {
            if (_settingsProvider.GetSetting(Settings.Settings.MaximumHistoryCount) == 25)
            {
                _settingsProvider.SetSetting(Settings.Settings.MaximumHistoryCount, 0);
            }
            _settingsProvider.SetSetting(Settings.Settings.UnlimitedDefaultsApplied, true);
        }

        _logger.LogInformation("Clipboard retention limit: {MaximumItemCount} (0 or -1 means unlimited).",
            _settingsProvider.GetSetting(Settings.Settings.MaximumHistoryCount));
        _settingsProvider.SettingChanged += SettingsProvider_SettingChanged;
        _clipboardDataService.DataUpdated += ClipboardDataService_DataUpdated;
        _pinnedService.PinsChanged += PinnedService_PinsChanged;
        _persistentService.HistoryChanged += PersistentService_HistoryChanged;
        _clipboardDataService.Subscribe();
        await ExplorerDetector.StartTrackingAsync();

        try { await _pinnedService.LoadAsync(_activation.Token); }
        catch (Exception ex) { _logger.LogWarning(ex, "Pinned history could not be loaded. Live clipboard remains available."); }
        try
        {
            if (_settingsProvider.GetSetting(Settings.Settings.PersistentHistory))
                await _persistentService.LoadAsync(_activation.Token);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Persistent history could not be loaded. Live clipboard remains available."); }
        await RefreshClipboardDataAsync();
        _winVShortcutService.ShortcutPressed += WinVShortcutService_ShortcutPressed;
        await ThreadHelper.RunOnUIThreadAsync(ConfigureWinVShortcut);
        _updateService.Start();
    }

    public ValueTask OnDeactivatedAsync()
    {
        _activation?.Cancel();
        _popupContent?.Close();
        _shortcutContent?.Close();
        ExplorerDetector.StopTracking();
        _settingsProvider.SettingChanged -= SettingsProvider_SettingChanged;
        _clipboardDataService.DataUpdated -= ClipboardDataService_DataUpdated;
        _pinnedService.PinsChanged -= PinnedService_PinsChanged;
        _persistentService.HistoryChanged -= PersistentService_HistoryChanged;
        _clipboardDataService.Unsubscribe();
        _winVShortcutService.ShortcutPressed -= WinVShortcutService_ShortcutPressed;
        _winVShortcutService.Stop();
        _updateService.Stop();
        _popupViewModel?.Dispose();
        _popupViewModel = null;
        _searchItem = null;
        _popupAnchor = null;
        _popupContent = null;
        _shortcutPopup = null;
        _shortcutContent = null;
        return ValueTask.CompletedTask;
    }

    private void PersistentService_HistoryChanged(object? sender, EventArgs e)
    {
        RefreshClipboardDataAsync().Forget();
    }

    private void PinnedService_PinsChanged(object? sender, EventArgs e)
    {
        RefreshClipboardDataAsync().Forget();
    }

    private void SettingsProvider_SettingChanged(ISettingsProvider sender, SettingChangedEventArgs args)
    {
        if (args.SettingName == Settings.Settings.MaximumHistoryCount.Name
            || args.SettingName == Settings.Settings.PersistentHistory.Name)
        {
            if (args.SettingName == Settings.Settings.PersistentHistory.Name
                && _settingsProvider.GetSetting(Settings.Settings.PersistentHistory))
            {
                _persistentService.LoadAsync().Forget();
            }
            RefreshClipboardDataAsync().Forget();
        }
        else if (args.SettingName == Settings.Settings.HidePasswords.Name)
        {
            ResetShortcutPopup();
            ViewList.Clear();
            _popupViewModel?.Dispose();
            _popupViewModel = null;
            _searchItem = null;
            _clipboardDataService.ClearCache();
            RefreshClipboardDataAsync().Forget();
        }
        else if (args.SettingName == Settings.Settings.CompactMode.Name)
        {
            ResetShortcutPopup();
            ViewList.Clear();
            _popupViewModel?.Dispose();
            _popupViewModel = null;
            _searchItem = null;
            RefreshClipboardDataAsync().Forget();
        }
        else if (args.SettingName == Settings.Settings.OverrideWindowsV.Name)
        {
            ConfigureWinVShortcut();
        }
    }

    private void ClipboardDataService_DataUpdated(object? sender, EventArgs e)
    {
        RefreshClipboardDataAsync().Forget();
    }

    private async Task RefreshClipboardDataAsync()
    {
        CancellationToken token = _activation?.Token ?? new CancellationToken(true);
        if (token.IsCancellationRequested) return;
        await _refreshGate.WaitAsync();
        try
        {
        if (token.IsCancellationRequested) return;
        int maxItems = _settingsProvider.GetSetting(Settings.Settings.MaximumHistoryCount);
        await _clipboardDataService.RefreshAsync();

        if (_settingsProvider.GetSetting(Settings.Settings.PersistentHistory))
        {
            try
            {
            await _persistentService.LoadAsync(token);
            var pinnedSignatures = new HashSet<string>(
                _pinnedService.GetPinnedItems().Select(item => item.ContentSignature),
                StringComparer.Ordinal);
            await _persistentService.CaptureAsync(
                _clipboardDataService.GetCachedItems(),
                maxItems,
                pinnedSignatures, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception ex) { _logger.LogWarning(ex, "Persistent history is unavailable. Live clipboard history remains accessible."); }
        }

        if (token.IsCancellationRequested) return;
        if (IsCompactMode)
        {
            await UpdatePopupViewModelAsync();
        }
        else
        {
            await UpdateViewListAsync();
        }
        }
        finally { _refreshGate.Release(); }
    }

    /// <summary>
    /// Combines pinned items (shown first) with the live clipboard history, excluding any
    /// live items whose content is already pinned.
    /// </summary>
    private IReadOnlyList<ClipboardItemData> GetCombinedItems()
    {
        IReadOnlyList<ClipboardItemData> pinned = _pinnedService.GetPinnedItems();
        var pinnedSignatures = new HashSet<string>(pinned.Select(p => p.ContentSignature), StringComparer.Ordinal);

        IEnumerable<ClipboardItemData> live = _clipboardDataService.GetCachedItems()
            .Where(i => !pinnedSignatures.Contains(i.ContentSignature));

        var liveSignatures = new HashSet<string>(live.Select(i => i.ContentSignature), StringComparer.Ordinal);
        IEnumerable<ClipboardItemData> persisted = _settingsProvider.GetSetting(Settings.Settings.PersistentHistory)
            ? _persistentService.GetItems().Where(i =>
                !pinnedSignatures.Contains(i.ContentSignature)
                && !liveSignatures.Contains(i.ContentSignature))
            : [];

        IEnumerable<ClipboardItemData> unpinned = live.Concat(persisted)
            .OrderByDescending(item => item.CapturedAt)
            .DistinctBy(item => string.IsNullOrEmpty(item.ContentSignature) ? item.Source.Id : item.ContentSignature);
        int maximumItemCount = _settingsProvider.GetSetting(Settings.Settings.MaximumHistoryCount);
        if (maximumItemCount > 0)
        {
            unpinned = unpinned.Take(maximumItemCount);
        }

        return [.. pinned, .. unpinned];
    }

    /// <summary>
    /// Updates the popup ViewModel's items collection for compact mode.
    /// Creates the popup SillListViewPopupItem on first call.
    /// </summary>
    private async Task UpdatePopupViewModelAsync()
    {
        IReadOnlyList<ClipboardItemData> cachedItems = GetCombinedItems();

        await ThreadHelper.RunOnUIThreadAsync(() =>
        {
            if (_popupViewModel is null)
            {
                _popupViewModel = new ClipboardHistoryPopupViewModel(_persistentService);
                _popupContent = new ClipboardHistoryPopupContent(_popupViewModel, _settingsProvider);
                var popupItem = new SillListViewPopupItem(
                    new ImageIcon
                    {
                        Source = new SvgImageSource(new Uri(System.IO.Path.Combine(_pluginInfo.GetPluginContentDirectory(), "Assets", "clipboard.svg")))
                    },
                    DisplayName,
                    _popupContent);

                ViewList.Clear();
                ViewList.Add(popupItem);
                _popupAnchor = popupItem;
            }

            _popupViewModel.AllItems.SynchronizeWith(
                cachedItems,
                (existingVm, newItemData) => existingVm.Equals(newItemData.Source),
                (itemData) =>
                {
                    try
                    {
                        return _viewFactory.CreateViewModel(itemData);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to create a viewmodel for a clipboard item in compact mode.");
                        return _viewFactory.CreateViewModel(new ClipboardItemData(itemData.Source, DetectedClipboardDataType.Unknown, itemData.ContentSignature));
                    }
                });

            _popupViewModel.RefreshFilter();
        });
    }

    /// <summary>
    /// Updates the ViewList with individual SillListViewButtonItems for normal (expanded) mode.
    /// </summary>
    private async Task UpdateViewListAsync()
    {
        IReadOnlyList<ClipboardItemData> cachedItems = GetCombinedItems();
        IReadOnlyList<ClipboardItemData> barItems = [.. cachedItems.Take(ExpandedBarItemLimit)];

        await ThreadHelper.RunOnUIThreadAsync(() =>
        {
            if (_popupViewModel is null)
            {
                _popupViewModel = new ClipboardHistoryPopupViewModel(_persistentService);
                _popupContent = new ClipboardHistoryPopupContent(_popupViewModel, _settingsProvider);
                _searchItem = new SillListViewPopupItem(
                    new FontIcon { Glyph = "\uE721", FontSize = 16, Width = 16, Height = 16 },
                    "/WindowSill.ClipboardHistory/Misc/SearchHistory".GetLocalizedString(),
                    _popupContent);
                _popupAnchor = _searchItem;
            }

            if (_iconItem is null)
            {
                _iconItem = new SillListViewMenuFlyoutItem(
                    new ImageIcon
                    {
                        Source = new SvgImageSource(new Uri(System.IO.Path.Combine(_pluginInfo.GetPluginContentDirectory(), "Assets", "clipboard.svg")))
                    },
                    null,
                    new MenuFlyout
                    {
                        Items =
                        {
                            new MenuFlyoutItem
                            {
                                Text = "/WindowSill.ClipboardHistory/Misc/ClearHistory".GetLocalizedString(),
                                Icon = new SymbolIcon(Symbol.Clear),
                                Command = ClearCommand
                            }
                        }
                    });
            }

            ViewList.Remove(_iconItem);
            if (_searchItem is not null)
            {
                ViewList.Remove(_searchItem);
            }

            ViewList.SynchronizeWith(
                barItems,
                (oldItem, newItem) =>
                {
                    if (oldItem.DataContext is ClipboardHistoryItemViewModelBase oldItemViewModel)
                    {
                        return oldItemViewModel.Equals(newItem.Source);
                    }
                    throw new Exception($"Unexpected item type in ViewList: {oldItem.DataContext?.GetType().FullName ?? "null"}");
                },
                (itemData) =>
                {
                    ClipboardHistoryItemViewModelBase viewModel;
                    SillListViewItem view;

                    try
                    {
                        (viewModel, view) = _viewFactory.Create(itemData);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to create a view and viewmodel for a clipboard item.");
                        (viewModel, view) = _viewFactory.Create(new ClipboardItemData(itemData.Source, DetectedClipboardDataType.Unknown, itemData.ContentSignature));
                    }

                    CreateContextMenu(viewModel, view);

                    if (itemData.Source.IsPinned)
                    {
                        DecorateAsPinned(view);
                    }

                    return view;
                });

            if (_searchItem is not null)
            {
                ViewList.Insert(0, _searchItem);
            }

            _popupViewModel.AllItems.SynchronizeWith(
                cachedItems,
                (existingVm, newItemData) => existingVm.Equals(newItemData.Source),
                itemData => _viewFactory.CreateViewModel(itemData));
            _popupViewModel.RefreshFilter();

            if (barItems.Count > 0)
            {
                ViewList.Insert(_searchItem is null ? 0 : 1, _iconItem);
            }
        });
    }

    private void ConfigureWinVShortcut()
    {
        if (_settingsProvider.GetSetting(Settings.Settings.OverrideWindowsV))
        {
            if (!_winVShortcutService.Start())
            {
                _logger.LogWarning("Win+V could not be overridden. The Windows clipboard shortcut remains active.");
            }
            else
            {
                _logger.LogInformation("Win+V clipboard shortcut is active.");
            }
        }
        else
        {
            _winVShortcutService.Stop();
        }
    }

    private void WinVShortcutService_ShortcutPressed(object? sender, EventArgs e)
    {
        Task.Run(() => ThreadHelper.RunOnUIThreadAsync(ShowSearchPopupAsync)).Forget();
    }

    private async Task ShowSearchPopupAsync()
    {
        if (_shortcutPopupOpen)
        {
            return;
        }

        _shortcutPopupOpen = true;
        try
        {
            if (_activation?.IsCancellationRequested != false) return;
            if (_popupAnchor is null || _popupViewModel is null)
            {
                return;
            }

            _popupViewModel.SearchQuery = string.Empty;
            if (_shortcutPopup is null)
            {
                _shortcutContent = new ClipboardHistoryPopupContent(_popupViewModel, _settingsProvider);
                _shortcutPopup = new SillPopup { Content = _shortcutContent };
            }

            await _shortcutPopup.ShowAsync(_popupAnchor);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogDebug(ex, "The clipboard popup is already open.");
        }
        finally
        {
            _shortcutPopupOpen = false;
        }
    }

    private void ResetShortcutPopup()
    {
        _shortcutContent?.Close();
        _shortcutPopup = null;
        _shortcutContent = null;
    }

    [RelayCommand]
    private async Task ClearAsync()
    {
        await ThreadHelper.RunOnUIThreadAsync(() =>
        {
            Clipboard.ClearHistory();
        });

        await _persistentService.ClearAsync();
    }

    private static void CreateContextMenu(ClipboardHistoryItemViewModelBase viewModel, SillListViewItem view)
    {
        var menuFlyout = new MenuFlyout();

        if (viewModel.IsPinned)
        {
            menuFlyout.Items.Add(new MenuFlyoutItem
            {
                Text = "/WindowSill.ClipboardHistory/Misc/Unpin".GetLocalizedString(),
                Icon = new SymbolIcon(Symbol.UnPin),
                Command = viewModel.UnpinCommand
            });
        }
        else
        {
            menuFlyout.Items.Add(new MenuFlyoutItem
            {
                Text = "/WindowSill.ClipboardHistory/Misc/Pin".GetLocalizedString(),
                Icon = new SymbolIcon(Symbol.Pin),
                Command = viewModel.PinCommand,
                IsEnabled = viewModel.CanPin
            });

            menuFlyout.Items.Add(new MenuFlyoutItem
            {
                Text = "/WindowSill.ClipboardHistory/Misc/ClearHistory".GetLocalizedString(),
                Icon = new SymbolIcon(Symbol.Clear),
                Command = viewModel.ClearCommand
            });
            menuFlyout.Items.Add(new MenuFlyoutItem
            {
                Text = "/WindowSill.ClipboardHistory/Misc/Delete".GetLocalizedString(),
                Icon = new SymbolIcon(Symbol.Delete),
                Command = viewModel.DeleteCommand
            });
        }

        view.ContextFlyout = menuFlyout;
    }

    /// <summary>
    /// Adds a small pin indicator overlay to a pinned item's content.
    /// </summary>
    private static void DecorateAsPinned(SillListViewItem view)
    {
        if (view is SillListViewButtonItem buttonItem && buttonItem.Content is UIElement existing)
        {
            var grid = new Grid();
            grid.Children.Add(existing);
            grid.Children.Add(new FontIcon
            {
                Glyph = "\uE840",
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(2, 2, 0, 0),
                Opacity = 0.7
            });

            buttonItem.Content = grid;
        }
    }
}
