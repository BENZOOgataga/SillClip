using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Windowing;
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
    private readonly ISettingsProvider _settingsProvider;
    private AppWindow? _dragWindow;
    private IDisposable? _dragAutoClose;
    private int _dragOffsetX;
    private int _dragOffsetY;

    internal ClipboardHistoryPopupContent(ClipboardHistoryPopupViewModel viewModel, ISettingsProvider settingsProvider)
    {
        _settingsProvider = settingsProvider;
        ViewModel = viewModel;
        InitializeComponent();
        AddHandler(KeyDownEvent, new KeyEventHandler(OnPopupKeyDown), handledEventsToo: true);
    }

    /// <summary>
    /// Gets the ViewModel for the popup content.
    /// </summary>
    internal ClipboardHistoryPopupViewModel ViewModel { get; }

    internal string PopupTitle { get; } = "/WindowSill.ClipboardHistory/Misc/DisplayName".GetLocalizedString();

    internal string ClearHistoryTooltip { get; } = "/WindowSill.ClipboardHistory/Misc/ClearHistory".GetLocalizedString();

    internal string SearchPlaceholder { get; } = "/WindowSill.ClipboardHistory/Misc/SearchPlaceholder".GetLocalizedString();

    internal string NoSearchResultsText { get; } = "/WindowSill.ClipboardHistory/Misc/NoSearchResults".GetLocalizedString();

    private void ClipboardHistoryPopupContent_Loaded(object sender, RoutedEventArgs e)
    {
        SearchBox.Focus(FocusState.Programmatic);
    }

    public override void OnOpening()
    {
        base.OnOpening();
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            int x = _settingsProvider.GetSetting(Settings.Settings.PopupX);
            int y = _settingsProvider.GetSetting(Settings.Settings.PopupY);
            if (x != int.MinValue && y != int.MinValue && XamlRoot is not null)
                MoveWithinWorkArea(AppWindow.GetFromWindowId(XamlRoot.ContentIslandEnvironment.AppWindowId), x, y);
            SearchBox.Focus(FocusState.Programmatic);
        });
    }

    public override void OnClosing()
    {
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
            _settingsProvider.SetSetting(Settings.Settings.PopupX, _dragWindow.Position.X);
            _settingsProvider.SetSetting(Settings.Settings.PopupY, _dragWindow.Position.Y);
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
        }
    }

    private void ClipboardItemsShortcutListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ClipboardHistoryItemViewModelBase viewModel)
        {
            Close();
            viewModel.PasteCommand.Execute(null);
        }
    }

    private void ClipboardItemsShortcutListView_ItemInvoked(object sender, ClipboardHistoryItemViewModelBase e)
    {
        Close();
        e.PasteCommand.Execute(null);
    }
}
