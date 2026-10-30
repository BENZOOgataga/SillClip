using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text.Json;
using WindowSill.API;
using WindowSill.ClipboardHistory.Services;
using WindowSill.ClipboardHistory.UI;

namespace WindowSill.ClipboardHistory.ViewModels;

internal interface IWorkspaceInteraction
{
    Task<bool> ConfirmAsync(string title, string message, CancellationToken cancellationToken);
    Task<string?> PickBackupAsync(bool save, CancellationToken cancellationToken);
    Task<bool> PasteAsync(ClipboardHistoryItemViewModelBase item, Core.ClipboardTextTransform transform, string? combinedText = null);
}

/// <summary>Owns operation lifetime and selection; feature view models contain their own behavior.</summary>
internal sealed partial class WorkspaceViewModel : ObservableObject, IDisposable
{
    private readonly ILogger _logger = typeof(WorkspaceViewModel).Log();
    private CancellationTokenSource? _operation;
    private bool _disposed;
    internal ClipboardWorkspaceService Service { get; }
    internal IWorkspaceInteraction Interaction { get; }
    public ClipboardHistoryPopupViewModel History { get; }
    public CollectionManagerViewModel Collections { get; }
    public ComparisonViewModel Comparison { get; }
    public QueueManagerViewModel Queue { get; }
    public StorageManagerViewModel Storage { get; }
    public BackupViewModel Backup { get; }
    public PasteOptionsViewModel PasteOptions { get; } = new();
    internal IReadOnlyList<ClipboardHistoryItemViewModelBase> Selection { get; private set; } = [];

    [ObservableProperty]
    public partial bool IsBusy { get; private set; }
    [ObservableProperty]
    public partial string Status { get; internal set; } = string.Empty;
    [ObservableProperty]
    public partial int SelectedTabIndex { get; set; }
    public bool CanInteract => !IsBusy;
    public bool HasSingleSelection => Selection.Count == 1;
    public string SelectionText => WorkspaceStrings.Format("WorkspaceSelectionCount", Selection.Count);

    internal WorkspaceViewModel(ClipboardHistoryPopupViewModel history, IWorkspaceInteraction interaction)
    {
        History = history; Service = history.Workspace; Interaction = interaction;
        Collections = new(this); Comparison = new(this); Queue = new(this); Storage = new(this); Backup = new(this);
        Service.State.Changed += State_Changed;
        Service.Queue.Changed += Queue_Changed;
        Collections.Refresh(); Queue.Refresh();
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanInteract));

    internal void SetSelection(IEnumerable<ClipboardHistoryItemViewModelBase> items)
    {
        Selection = items.ToArray();
        OnPropertyChanged(nameof(SelectionText));
        OnPropertyChanged(nameof(HasSingleSelection));
        CopySelectedCommand.NotifyCanExecuteChanged();
        PasteSelectedCommand.NotifyCanExecuteChanged();
        Collections.RefreshCommands();
        Comparison.RefreshCommands();
        Queue.RefreshCommands();
    }

    internal IReadOnlyList<ClipboardHistoryItemViewModelBase> RequireSelection(int? exactCount = null)
    {
        if (Selection.Count == 0 || exactCount.HasValue && Selection.Count != exactCount.Value)
            throw new ArgumentException("Choose the required clipboard entries first.");
        if (Service.State.PresentationMode) throw new InvalidOperationException("Presentation mode is enabled.");
        return Selection.ToArray();
    }

    internal async Task RunAsync(string operationName, Func<CancellationToken, Task> action)
    {
        if (IsBusy || _disposed) return;
        IsBusy = true; Status = string.Empty;
        using var operation = new CancellationTokenSource();
        _operation = operation;
        try { await action(operation.Token); }
        catch (OperationCanceledException) { if (!_disposed) Status = WorkspaceStrings.Get("WorkspaceCancelled"); }
        catch (Exception ex)
        {
            _logger.LogWarning("Workspace operation {Operation} failed with {ErrorType}.", operationName, ex.GetType().Name);
            if (!_disposed) Status = WorkspaceStrings.Get(ex switch
            {
                CryptographicException => "WorkspaceArchiveAuthenticationFailed",
                JsonException => "WorkspaceInvalidJson",
                IOException or UnauthorizedAccessException => "WorkspaceFileError",
                ArgumentException or FormatException => "WorkspaceCheckInput",
                _ => "WorkspaceActionFailed"
            });
        }
        finally { _operation = null; IsBusy = false; }
    }

    internal IProgress<int> TransferProgress()
    {
        var operation = _operation;
        return new Progress<int>(count =>
        {
            if (!_disposed && operation is not null && ReferenceEquals(_operation, operation) && !operation.IsCancellationRequested)
                Status = WorkspaceStrings.Format("WorkspaceTransferProgress", count);
        });
    }

    [RelayCommand]
    private void Cancel() => _operation?.Cancel();

    [RelayCommand(CanExecute = nameof(HasSingleSelection))]
    private Task CopySelectedAsync() => RunAsync("Copy", async cancellationToken =>
    {
        var item = RequireSelection(1)[0];
        await item.CopyTransformedAsync(PasteOptions.SelectedTransform, cancellationToken);
        Status = WorkspaceStrings.Get("WorkspaceCopied");
    });

    [RelayCommand(CanExecute = nameof(HasSingleSelection))]
    private Task PasteSelectedAsync() => RunAsync("Paste", async _ =>
    {
        var item = RequireSelection(1)[0];
        bool pasted = await Interaction.PasteAsync(item, PasteOptions.SelectedTransform);
        if (!pasted) Status = WorkspaceStrings.Get("WorkspaceCopiedFocusChanged");
    });

    private void State_Changed(object? sender, EventArgs e)
        => ThreadHelper.RunOnUIThreadAsync(() => { if (!_disposed) { Collections.Refresh(); History.RefreshFilter(); } }).Forget();
    private void Queue_Changed(object? sender, EventArgs e)
        => ThreadHelper.RunOnUIThreadAsync(() => { if (!_disposed) Queue.Refresh(); }).Forget();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _operation?.Cancel();
        Service.State.Changed -= State_Changed;
        Service.Queue.Changed -= Queue_Changed;
        Backup.ClearPasswords();
        Comparison.Clear();
        Selection = [];
    }
}
