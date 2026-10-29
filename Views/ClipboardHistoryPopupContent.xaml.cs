using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Windowing;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.System;
using WindowSill.API;
using WindowSill.ClipboardHistory.Core;
using WindowSill.ClipboardHistory.ViewModels;

namespace WindowSill.ClipboardHistory.Views;

/// <summary>
/// Popup content view for the compact mode clipboard history.
/// Displays all clipboard history items in a scrollable list within a single popup.
/// </summary>
internal sealed partial class ClipboardHistoryPopupContent : SillPopupContent
{
    private PopupInvocationContext? _invocation;
    private PopupOpeningPlacement? _openingPlacement;
    private bool _pasteInProgress;
    private int _openingGeneration;
    private nint _targetWindow;
    private uint _targetProcessId;
    private AppWindow? _dragWindow;
    private IDisposable? _dragAutoClose;
    private int _dragOffsetX;
    private int _dragOffsetY;

    internal ClipboardHistoryPopupContent(ClipboardHistoryPopupViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        AddHandler(PreviewKeyDownEvent, new KeyEventHandler(OnPopupKeyDown), handledEventsToo: true);
    }

    internal bool IsOpen { get; private set; }

    internal void PrepareKeyboardOpen(PopupInvocationContext context)
    {
        _invocation = context;
        Width = Math.Max(1, Math.Min(360, context.WorkArea.Width / context.Scale - 16));
        Height = Math.Max(1, Math.Min(400, context.WorkArea.Height / context.Scale - 16));
    }

    /// <summary>
    /// Gets the ViewModel for the popup content.
    /// </summary>
    internal ClipboardHistoryPopupViewModel ViewModel { get; }

    internal string PopupTitle { get; } = "/WindowSill.ClipboardHistory/Misc/ClipboardTitle".GetLocalizedString();
    internal string CloseTooltip { get; } = "/WindowSill.ClipboardHistory/Misc/Close".GetLocalizedString();
    internal string ClearAllText { get; } = "/WindowSill.ClipboardHistory/Misc/ClearAll".GetLocalizedString();

    internal string ClearHistoryTooltip { get; } = "/WindowSill.ClipboardHistory/Misc/ClearHistory".GetLocalizedString();

    internal string SearchPlaceholder { get; } = "/WindowSill.ClipboardHistory/Misc/SearchPlaceholder".GetLocalizedString();

    internal string NoSearchResultsText { get; } = "/WindowSill.ClipboardHistory/Misc/NoSearchResults".GetLocalizedString();

    public override void OnOpening()
    {
        base.OnOpening();
        IsOpen = true;
        int opening = ++_openingGeneration;
        _pasteInProgress = false;
        ViewModel.SearchQuery = string.Empty;
        // An unchanged empty query does not run the generated property-change callback.
        ViewModel.RefreshFilter();
        _targetWindow = _invocation?.TargetWindow ?? ExplorerDetector.GetLastActiveWindow();
        _targetProcessId = _invocation?.TargetProcessId ?? PopupInvocationContext.ProcessIdOf(_targetWindow);
        ViewModel.Items.CollectionChanged += Items_CollectionChanged;
        if (_invocation is not null && XamlRoot is not null)
        {
            _openingPlacement?.Dispose();
            var window = Microsoft.UI.Win32Interop.GetWindowFromWindowId(XamlRoot.ContentIslandEnvironment.AppWindowId);
            try { _openingPlacement = new PopupOpeningPlacement(window, _invocation, Width, Height); }
            catch (Exception ex) { this.Log().LogWarning(ex, "Automatic popup placement is unavailable; using the host placement."); }
        }
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!IsOpen || opening != _openingGeneration) return;
            // Move focus off the previous item before changing its selection and scroll position.
            SearchBox.Focus(FocusState.Programmatic);
            ClipboardItems.SelectedIndex = ViewModel.Items.Count > 0 ? 0 : -1;
            ClipboardItems.ResetScrollToTop();
        });
    }

    public override void OnClosing()
    {
        IsOpen = false;
        ++_openingGeneration;
        ViewModel.Items.CollectionChanged -= Items_CollectionChanged;
        _openingPlacement?.Dispose();
        _openingPlacement = null;
        EndDrag();
        base.OnClosing();
    }

    private void DragHandle_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(DragHandle).Properties.IsLeftButtonPressed || XamlRoot is null || !GetCursorPos(out var cursor)) return;
        _dragWindow = AppWindow.GetFromWindowId(XamlRoot.ContentIslandEnvironment.AppWindowId);
        _dragOffsetX = cursor.X - _dragWindow.Position.X;
        _dragOffsetY = cursor.Y - _dragWindow.Position.Y;
        _dragAutoClose = PreventAutoClose();
        DragHandle.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void DragHandle_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragWindow is null || !GetCursorPos(out var cursor)) return;
        _dragWindow.Move(new PointInt32(cursor.X - _dragOffsetX, cursor.Y - _dragOffsetY));
        e.Handled = true;
    }

    private void DragHandle_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        EndDrag();
        DragHandle.ReleasePointerCapture(e.Pointer);
    }

    private void DragHandle_PointerCaptureLost(object sender, PointerRoutedEventArgs e) => EndDrag();

    private void EndDrag()
    {
        if (_dragWindow is not null)
        {
            MoveWithinWorkArea(_dragWindow, _dragWindow.Position.X, _dragWindow.Position.Y);
            _dragWindow = null;
        }
        _dragAutoClose?.Dispose();
        _dragAutoClose = null;
    }

    private static void MoveWithinWorkArea(AppWindow window, int x, int y)
    {
        var area = DisplayArea.GetFromRect(new RectInt32(x, y, window.Size.Width, window.Size.Height), DisplayAreaFallback.Nearest).WorkArea;
        var position = PopupPlacement.Clamp(x, y, window.Size.Width, window.Size.Height, area.X, area.Y, area.Width, area.Height);
        window.Move(new PointInt32(position.X, position.Y));
    }

    private void ClipboardItems_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.Item is ImageItemViewModel image)
        {
            if (args.InRecycleQueue) image.ReleasePreview();
            else image.RequestPreview();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ScreenPoint { internal int X; internal int Y; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out ScreenPoint point);

    private void OnPopupKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            Close();
            return;
        }
        bool control = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (control && e.Key == VirtualKey.F) { SearchBox.Focus(FocusState.Keyboard); e.Handled = true; return; }
        if (IsActionButton(e.OriginalSource as DependencyObject)) return;
        if (e.Key is VirtualKey.Down or VirtualKey.Up)
        {
            e.Handled = true;
            int index = PopupNavigation.Next(ClipboardItems.SelectedIndex, ViewModel.Items.Count, e.Key == VirtualKey.Down ? 1 : -1);
            ClipboardItems.SelectedIndex = index;
            if (index >= 0)
            {
                ClipboardItems.ScrollIntoView(ViewModel.Items[index]);
                ClipboardItems.Focus(FocusState.Keyboard);
                (ClipboardItems.ContainerFromIndex(index) as Control)?.Focus(FocusState.Keyboard);
            }
        }
        else if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            if (ClipboardItems.SelectedItem is ClipboardHistoryItemViewModelBase item) PasteSelectedAsync(item).Forget();
        }
    }

    private static bool IsActionButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase) return true;
            source = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    private void Items_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (IsOpen && ClipboardItems.SelectedIndex < 0 && ViewModel.Items.Count > 0) ClipboardItems.SelectedIndex = 0;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private async Task PasteSelectedAsync(ClipboardHistoryItemViewModelBase item)
    {
        if (_pasteInProgress) return;
        _pasteInProgress = true;
        nint target = _targetWindow;
        uint processId = _targetProcessId;
        Close();
        try { await item.PasteFromPopupAsync(target, processId); }
        catch (Exception ex) { this.Log().LogWarning(ex, "The selected clipboard item could not be pasted."); }
    }

    private void ClipboardItemsShortcutListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ClipboardHistoryItemViewModelBase viewModel)
        {
            PasteSelectedAsync(viewModel).Forget();
        }
    }

    private void ClipboardItemsShortcutListView_ItemInvoked(object sender, ClipboardHistoryItemViewModelBase e)
    {
        PasteSelectedAsync(e).Forget();
    }
}
