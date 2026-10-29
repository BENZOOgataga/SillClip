using WindowSill.ClipboardHistory.ViewModels;

namespace WindowSill.ClipboardHistory.UI;

public sealed class ShortcutListView : ListView
{
    public ShortcutListView()
    {
        DefaultStyleKey = typeof(ShortcutListView);
    }

    public event EventHandler<ClipboardHistoryItemViewModelBase>? ItemInvoked;

    internal void ResetScrollToTop()
    {
        ApplyTemplate();
        // The virtualized panel may still be anchored to an old item after copies arrive while hidden.
        UpdateLayout();
        if (Items.Count > 0) ScrollIntoView(Items[0], ScrollIntoViewAlignment.Leading);
        if (GetTemplateChild("ScrollViewer") is ScrollViewer scroller)
            scroller.ChangeView(0, 0, null, disableAnimation: true);
    }

    protected override DependencyObject GetContainerForItemOverride()
    {
        return new ShortcutListViewItem(this);
    }

    protected override bool IsItemItsOwnContainerOverride(object item)
    {
        return item is ShortcutListViewItem;
    }

    internal void OnItemInvoked(ClipboardHistoryItemViewModelBase item)
    {
        ItemInvoked?.Invoke(this, item);
    }
}
