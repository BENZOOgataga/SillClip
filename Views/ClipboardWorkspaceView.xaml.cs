using Windows.Storage.Pickers;
using WindowSill.ClipboardHistory.Core;
using WindowSill.ClipboardHistory.UI;
using WindowSill.ClipboardHistory.ViewModels;

namespace WindowSill.ClipboardHistory.Views;

internal sealed partial class ClipboardWorkspaceView : UserControl, IWorkspaceInteraction, IDisposable
{
    private readonly Func<IDisposable> _preventAutoClose;
    private readonly Func<ClipboardHistoryItemViewModelBase, ClipboardTextTransform, string?, Task<bool>> _paste;
    internal event EventHandler? BackRequested;
    internal WorkspaceViewModel ViewModel { get; }
    internal CaptureControlsViewModel Controls { get; }
    internal bool IsDialogOpen { get; private set; }

    internal ClipboardWorkspaceView(ClipboardHistoryPopupViewModel history, CaptureControlsViewModel controls,
        Func<IDisposable> preventAutoClose, Func<ClipboardHistoryItemViewModelBase, ClipboardTextTransform, string?, Task<bool>> paste)
    {
        _preventAutoClose = preventAutoClose; _paste = paste; Controls = controls;
        ViewModel = new(history, this);
        try { InitializeComponent(); }
        catch { ViewModel.Dispose(); throw; }
    }

    internal string Localize(string key) => WorkspaceStrings.Get(key);
    internal Visibility BusyVisibility(bool busy) => busy ? Visibility.Visible : Visibility.Collapsed;
    internal Visibility StatusVisibility(string status, bool busy) => busy || !string.IsNullOrEmpty(status) ? Visibility.Visible : Visibility.Collapsed;

    private void WorkspaceTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!e.AddedItems.OfType<TabViewItem>().Any() || WorkspaceTabs.SelectedIndex != 4 || ViewModel.IsBusy) return;
        ViewModel.Storage.RefreshCommand.Execute(null);
    }

    private void BrowseList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => ViewModel.SetSelection(BrowseList.SelectedItems.OfType<ClipboardHistoryItemViewModelBase>());
    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    public async Task<bool> ConfirmAsync(string title, string message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var suppression = _preventAutoClose();
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = Localize("WorkspaceConfirm"),
            CloseButtonText = Localize("WorkspaceCancel"),
            DefaultButton = ContentDialogButton.Close
        };
        IsDialogOpen = true;
        using var registration = cancellationToken.Register(() => DispatcherQueue.TryEnqueue(dialog.Hide));
        try
        {
            var result = await dialog.ShowAsync();
            cancellationToken.ThrowIfCancellationRequested();
            return result == ContentDialogResult.Primary;
        }
        finally { IsDialogOpen = false; }
    }

    public async Task<string?> PickBackupAsync(bool save, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var suppression = _preventAutoClose();
        nint window = Microsoft.UI.Win32Interop.GetWindowFromWindowId(XamlRoot.ContentIslandEnvironment.AppWindowId);
        if (save)
        {
            var picker = new FileSavePicker { SuggestedFileName = "SillClip-" + DateTime.Now.ToString("yyyyMMdd") };
            picker.FileTypeChoices.Add("SillClip", new List<string> { ".sillclip" });
            WinRT.Interop.InitializeWithWindow.Initialize(picker, window);
            var file = await picker.PickSaveFileAsync().AsTask(cancellationToken);
            return file?.Path;
        }
        else
        {
            var picker = new FileOpenPicker(); picker.FileTypeFilter.Add(".sillclip");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, window);
            var file = await picker.PickSingleFileAsync().AsTask(cancellationToken);
            return file?.Path;
        }
    }

    public Task<bool> PasteAsync(ClipboardHistoryItemViewModelBase item, ClipboardTextTransform transform, string? combinedText = null)
        => _paste(item, transform, combinedText);

    public void Dispose() => ViewModel.Dispose();
}
