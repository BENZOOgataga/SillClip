using WindowSill.ClipboardHistory.ViewModels;

namespace WindowSill.ClipboardHistory.Views;

/// <summary>
/// Preview view for image clipboard items, displayed in the detail pane.
/// </summary>
internal sealed partial class ImageItemPreviewView : UserControl
{
    internal ImageItemPreviewView(ImageItemViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Loaded += (_, _) => ViewModel.RequestPreview();
    }

    /// <summary>
    /// Gets the view model for this view.
    /// </summary>
    internal ImageItemViewModel ViewModel { get; }
}
