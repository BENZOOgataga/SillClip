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
    private readonly ClipboardPasteService _pasteService;
    private readonly IClipboardItemSource _source;

    private PinnedClipboardService? _pinnedService;
    private PersistentClipboardService? _persistentService;
    private ClipboardHistoryDataService? _clipboardDataService;
    private ClipboardItemData? _itemData;
    private ClipboardWorkspaceService? _workspace;
    private bool _hidePasswords;

    internal ClipboardHistoryItemViewModelBase(
        IProcessInteractionService processInteractionService,
        IClipboardItemSource source)
        : base()
    {
        Guard.IsNotNull(processInteractionService);
        Guard.IsNotNull(source);
        _logger = this.Log();
        _pasteService = new ClipboardPasteService(processInteractionService);
        _source = source;
    }

    public DataPackageView Data => _source.Data;

    [ObservableProperty]
    public partial string SearchText { get; private set; } = string.Empty;

    internal void ConfigurePrivacy(bool hidePasswords)
    {
        _hidePasswords = hidePasswords;
        InitializeSearchTextAsync(hidePasswords).Forget();
    }

    internal void ConfigureWorkspace(ClipboardWorkspaceService workspace) => _workspace = workspace;
    internal ClipboardItemData ItemData => _itemData ?? throw new InvalidOperationException("The clipboard item has not been initialized.");
    public string ContentSignature => _itemData?.ContentSignature ?? string.Empty;
    public DateTimeOffset CapturedAt => _itemData?.CapturedAt ?? DateTimeOffset.MinValue;
    public string CapturedAtText => CapturedAt.LocalDateTime.ToString("g");
    public string TypeName => (_itemData?.DataType ?? DetectedDataType).ToString();
    public bool CanTransformText => (_itemData?.DataType ?? DetectedDataType) is DetectedClipboardDataType.Text or DetectedClipboardDataType.Html
        or DetectedClipboardDataType.Rtf or DetectedClipboardDataType.Uri or DetectedClipboardDataType.Color;
    public string Summary
    {
        get
        {
            string text = SearchText.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return text.Length > 180 ? text[..180] + "..." : string.IsNullOrEmpty(text) ? TypeName : text;
        }
    }

    partial void OnSearchTextChanged(string value) => OnPropertyChanged(nameof(Summary));

    internal void UpdateItemData(ClipboardItemData item)
    {
        _itemData = item;
        OnPropertyChanged(nameof(CapturedAt));
        OnPropertyChanged(nameof(CapturedAtText));
        OnPropertyChanged(nameof(TypeName));
        OnPropertyChanged(nameof(CanTransformText));
    }

    private IClipboardItemSource EffectiveSource => _workspace?.Resolve(ContentSignature).Source ?? _source;

    internal async Task<string> ReadComparisonTextAsync()
    {
        string text = await ClipboardPasteService.ReadTextAsync(EffectiveSource);
        if (_hidePasswords && ClipboardPrivacy.LooksSensitive(text))
            throw new InvalidOperationException("This item is hidden by the Hide passwords setting.");
        return text;
    }

    internal async Task CopyTransformedAsync(ClipboardTextTransform transform, CancellationToken cancellationToken = default)
    {
        var source = EffectiveSource;
        string? text = transform == ClipboardTextTransform.Original ? null
            : ClipboardTextTransforms.Apply(await ClipboardPasteService.ReadTextAsync(source), transform);
        cancellationToken.ThrowIfCancellationRequested();
        if (_workspace?.State.PresentationMode == true) throw new InvalidOperationException("Presentation mode is enabled.");
        await _pasteService.CopyAsync(source, text);
    }

    internal async Task<bool> PasteTransformedAsync(ClipboardTextTransform transform, nint window, uint processId)
    {
        var source = EffectiveSource;
        string? text = transform == ClipboardTextTransform.Original ? null
            : ClipboardTextTransforms.Apply(await ClipboardPasteService.ReadTextAsync(source), transform);
        return await _pasteService.PasteAsync(source, ItemData.DataType, (window, processId), text);
    }

    internal Task<bool> PasteCombinedTextAsync(string text, nint window, uint processId)
        => _pasteService.PasteAsync(EffectiveSource, ItemData.DataType, (window, processId), text);

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
        try { await CopyTransformedAsync(ClipboardTextTransform.Original); }
        catch (Exception ex) { _logger.LogWarning(ex, "The clipboard item could not be copied."); }
    }

    [RelayCommand]
    private async Task PasteAsync()
    {
        try { await _pasteService.PasteAsync(EffectiveSource, DetectedDataType, null); }
        catch (Exception ex) { _logger.LogWarning(ex, "The clipboard item could not be pasted."); }
    }

    internal Task<bool> PasteFromPopupAsync(nint window, uint processId)
        => PasteTransformedAsync(ClipboardTextTransform.Original, window, processId);

    [RelayCommand]
    private Task DeleteAsync() => RunMutationAsync(DeleteCoreAsync);

    private async Task DeleteCoreAsync()
    {
        if (_workspace is not null) { await _workspace.DeleteAsync([ItemData]); return; }
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
    private Task ClearAsync() => RunMutationAsync(ClearCoreAsync);

    private async Task ClearCoreAsync()
    {
        if (_workspace is not null) { await _workspace.ClearUnpinnedAsync(); return; }
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
    private Task PinAsync() => RunMutationAsync(PinCoreAsync);

    private async Task PinCoreAsync()
    {
        if (_workspace is not null) { if (!IsPinned) await _workspace.TogglePinAsync(ItemData); return; }
        if (_pinnedService is null || _itemData is null)
        {
            return;
        }

        await Task.Run(() => _pinnedService.PinAsync(_itemData));
    }

    [RelayCommand]
    private Task UnpinAsync() => RunMutationAsync(UnpinCoreAsync);

    private async Task UnpinCoreAsync()
    {
        if (_workspace is not null) { if (IsPinned) await _workspace.TogglePinAsync(ItemData); return; }
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

    private async Task RunMutationAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { _logger.LogWarning("A clipboard library action failed ({ErrorType}).", ex.GetType().Name); }
    }
}
