using CommunityToolkit.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;
using WindowSill.API;
using WindowSill.ClipboardHistory.Core;
using WindowSill.ClipboardHistory.Services;

namespace WindowSill.ClipboardHistory.ViewModels;

public abstract partial class ClipboardHistoryItemViewModelBase
    : ObservableObject,
    IEquatable<ClipboardHistoryItemViewModelBase>
{
    private readonly ILogger _logger;
    private readonly IProcessInteractionService _processInteractionService;
    private readonly IClipboardItemSource _source;

    private PinnedClipboardService? _pinnedService;
    private PersistentClipboardService? _persistentService;
    private ClipboardHistoryDataService? _clipboardDataService;
    private ClipboardItemData? _itemData;

    internal ClipboardHistoryItemViewModelBase(
        IProcessInteractionService processInteractionService,
        IClipboardItemSource source)
        : base()
    {
        Guard.IsNotNull(processInteractionService);
        Guard.IsNotNull(source);
        _logger = this.Log();
        _processInteractionService = processInteractionService;
        _source = source;
    }

    public DataPackageView Data => _source.Data;

    [ObservableProperty]
    public partial string SearchText { get; private set; } = string.Empty;

    internal void ConfigurePrivacy(bool hidePasswords)
        => InitializeSearchTextAsync(hidePasswords).Forget();

    /// <summary>
    /// Gets whether the item this view model represents is pinned.
    /// </summary>
    public bool IsPinned => _source.IsPinned;

    /// <summary>
    /// Gets whether another item can still be pinned (only meaningful for unpinned items).
    /// </summary>
    public bool CanPin => !IsPinned && _pinnedService is not null;

    /// <summary>
    /// Gets the stable identifier of the underlying clipboard item source.
    /// </summary>
    internal string SourceId => _source.Id;

    /// <summary>
    /// Wires up the pinning commands for this view model.
    /// </summary>
    internal void ConfigurePinning(ClipboardItemData itemData, PinnedClipboardService pinnedService)
    {
        _itemData = itemData;
        _pinnedService = pinnedService;
        OnPropertyChanged(nameof(CanPin));
        OnPropertyChanged(nameof(CanTogglePin));
    }

    internal void ConfigurePersistence(ClipboardItemData itemData, PersistentClipboardService persistentService,
        ClipboardHistoryDataService clipboardDataService)
    {
        _itemData = itemData;
        _persistentService = persistentService;
        _clipboardDataService = clipboardDataService;
    }

    /// <summary>
    /// Gets whether the pin/unpin toggle should be enabled (always allowed for pinned
    /// items, otherwise only when the pin limit has not been reached).
    /// </summary>
    public bool CanTogglePin => _pinnedService is not null;

    /// <summary>
    /// Gets the glyph for the pin/unpin toggle button (Segoe Fluent Icons).
    /// </summary>
    public string PinGlyph => IsPinned ? "\uE77A" : "\uE718";

    /// <summary>
    /// Gets the tooltip for the pin/unpin toggle button.
    /// </summary>
    public string PinTooltip => IsPinned
        ? "/WindowSill.ClipboardHistory/Misc/Unpin".GetLocalizedString()
        : "/WindowSill.ClipboardHistory/Misc/Pin".GetLocalizedString();

    public string DeleteTooltip => "/WindowSill.ClipboardHistory/Misc/Delete".GetLocalizedString();
    public string OpenTooltip => (DetectedDataType switch
    {
        DetectedClipboardDataType.File => "/WindowSill.ClipboardHistory/Misc/OpenInFileExplorer",
        DetectedClipboardDataType.ApplicationLink => "/WindowSill.ClipboardHistory/Misc/OpenApplication",
        _ => "/WindowSill.ClipboardHistory/Misc/OpenInWebBrowser"
    }).GetLocalizedString();

    /// <summary>
    /// Gets the detected data type for this clipboard item.
    /// Subclasses should override this to return the correct type for paste-as-file support.
    /// </summary>
    protected virtual DetectedClipboardDataType DetectedDataType => DetectedClipboardDataType.Unknown;

    public bool Equals(ClipboardHistoryItemViewModelBase? other)
    {
        return other is not null && string.Equals(_source.Id, other._source.Id, StringComparison.Ordinal);
    }

    /// <summary>
    /// Determines whether this view model represents the given clipboard item source.
    /// </summary>
    internal bool Equals(IClipboardItemSource? other)
    {
        return other is not null && string.Equals(_source.Id, other.Id, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as ClipboardHistoryItemViewModelBase);
    }

    public override int GetHashCode()
    {
        return _source.Id.GetHashCode();
    }

    private async Task InitializeSearchTextAsync(bool hidePasswords)
    {
        try
        {
            var parts = new List<string> { DetectedDataType.ToString() };

            if (Data.AvailableFormats.Contains(StandardDataFormats.Text))
            {
                string text = await Data.GetTextAsync();
                if (hidePasswords && ClipboardPrivacy.LooksSensitive(text))
                {
                    await ThreadHelper.RunOnUIThreadAsync(() => SearchText = DetectedDataType.ToString());
                    return;
                }
                parts.Add(text);
            }

            if (Data.AvailableFormats.Contains(StandardDataFormats.WebLink))
            {
                parts.Add((await Data.GetWebLinkAsync()).ToString());
            }

            if (Data.AvailableFormats.Contains(StandardDataFormats.StorageItems))
            {
                IReadOnlyList<IStorageItem> storageItems = await Data.GetStorageItemsAsync();
                parts.AddRange(storageItems.Select(item => item.Name));
                parts.AddRange(storageItems.Select(item => item.Path));
            }

            string searchText = string.Join('\n', parts.Where(part => !string.IsNullOrWhiteSpace(part)));
            await ThreadHelper.RunOnUIThreadAsync(() => SearchText = searchText);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to build the search index for clipboard item {ItemId}.", _source.Id);
            await ThreadHelper.RunOnUIThreadAsync(() => SearchText = DetectedDataType.ToString());
        }
    }

    [RelayCommand]
    private async Task CopyAsync()
    {
        try { await ThreadHelper.RunOnUIThreadAsync(_source.SetAsClipboardContent); }
        catch (Exception ex) { _logger.LogWarning(ex, "The clipboard item could not be copied."); }
    }

    [RelayCommand]
    private Task PasteAsync() => PasteCoreAsync(null);

    internal Task PasteFromPopupAsync(nint window, uint processId) => PasteCoreAsync((window, processId));

    private async Task PasteCoreAsync((nint Window, uint ProcessId)? expectedTarget)
    {
        try
        {
            await ThreadHelper.RunOnUIThreadAsync(async () =>
            {
                _source.SetAsClipboardContent();
                if (expectedTarget is { } expected)
                {
                    for (int attempt = 0; attempt < 8 && !PopupInvocationContext.IsTargetActive(expected.Window, expected.ProcessId); attempt++)
                    {
                        if (!PopupInvocationContext.IsOwnWindowActive()) return;
                        await Task.Delay(16);
                    }
                    // If focus went elsewhere, leave the item copied instead of pasting into a different app.
                    if (!PopupInvocationContext.IsTargetActive(expected.Window, expected.ProcessId)) return;
                }
                // Check if the last active window is Explorer/Desktop/FileDialog
                nint targetHwnd = expectedTarget?.Window ?? ExplorerDetector.GetLastActiveWindow();
                if (expectedTarget is null && targetHwnd != 0 && ExplorerDetector.IsExplorerLikeWindow(targetHwnd))
                {
                    string? folderPath = ExplorerFolderResolver.GetCurrentFolderPath(targetHwnd);
                    if (!string.IsNullOrEmpty(folderPath))
                    {
                        bool written = await ClipboardFileWriter.TryWriteAsFileAsync(Data, DetectedDataType, folderPath);
                        if (written)
                        {
                            _logger.LogInformation("Pasted clipboard content as file in {Folder}.", folderPath);
                            return;
                        }
                    }
                }

                // Fall back to normal paste via Ctrl+V
                if (expectedTarget is null) await Task.Delay(200);
                else if (!PopupInvocationContext.IsTargetActive(expectedTarget.Value.Window, expectedTarget.Value.ProcessId)) return;

                await _processInteractionService.SimulateKeysOnLastActiveWindow(
                    VirtualKey.LeftControl,
                    VirtualKey.V);
            });
        }
        catch (Exception ex) { _logger.LogWarning(ex, "The clipboard item could not be pasted."); }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        await ThreadHelper.RunOnUIThreadAsync(() =>
        {
            _source.DeleteFromHistory();
        });

        if (_persistentService is not null && _itemData is not null)
        {
            await _persistentService.DeleteBySignatureAsync(_itemData.ContentSignature);
            if (_clipboardDataService is not null)
                await _clipboardDataService.DeleteBySignatureAsync(_itemData.ContentSignature);
        }
        if (IsPinned && _pinnedService is not null) await _pinnedService.UnpinAsync(SourceId);
    }

    [RelayCommand]
    private async Task ClearAsync()
    {
        await ThreadHelper.RunOnUIThreadAsync(() =>
        {
            Clipboard.ClearHistory();
        });

        if (_persistentService is not null)
        {
            await _persistentService.ClearAsync();
        }
    }

    [RelayCommand]
    private async Task PinAsync()
    {
        if (_pinnedService is null || _itemData is null)
        {
            return;
        }

        await Task.Run(() => _pinnedService.PinAsync(_itemData));
    }

    [RelayCommand]
    private async Task UnpinAsync()
    {
        if (_pinnedService is null)
        {
            return;
        }

        await Task.Run(() => _pinnedService.UnpinAsync(SourceId));
    }

    [RelayCommand]
    private async Task TogglePinAsync()
    {
        if (IsPinned)
        {
            await UnpinAsync();
        }
        else
        {
            await PinAsync();
        }
    }
}
