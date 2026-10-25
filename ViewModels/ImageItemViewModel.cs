using CommunityToolkit.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using WindowSill.API;
using WindowSill.ClipboardHistory.Core;
using WindowSill.ClipboardHistory.Utils;

namespace WindowSill.ClipboardHistory.ViewModels;

/// <summary>
/// ViewModel for clipboard history items containing image data.
/// </summary>
internal sealed partial class ImageItemViewModel : ClipboardHistoryItemViewModelBase
{
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageItemViewModel"/> class.
    /// </summary>
    /// <param name="processInteractionService">Service for interacting with external processes.</param>
    /// <param name="source">The clipboard item source containing image data.</param>
    internal ImageItemViewModel(IProcessInteractionService processInteractionService, IClipboardItemSource source)
        : base(processInteractionService, source)
    {
        _logger = this.Log();
    }

    [ObservableProperty]
    public partial string Size { get; set; } = string.Empty;

    /// <inheritdoc />
    protected override DetectedClipboardDataType DetectedDataType => DetectedClipboardDataType.Image;

    [ObservableProperty]
    public partial double MaxHeight { get; set; }

    [ObservableProperty]
    public partial double MaxWidth { get; set; }

    private BitmapImage? _image;
    private bool _loading;
    private int _previewVersion;
    public BitmapImage? Image
    {
        get
        {
            if (_image is null && !_loading)
            {
                _loading = true;
                InitializeAsync(_previewVersion).Forget();
            }
            return _image;
        }
        private set => SetProperty(ref _image, value);
    }

    internal void ReleasePreview()
    {
        _previewVersion++;
        _image = null;
        _loading = false;
    }

    private async Task InitializeAsync(int version)
    {
        try
        {
            Guard.IsNotNull(Data);
            var (bitmap, width, height) = await DataHelper.GetBitmapAsync(Data);
            if (version != _previewVersion) return;
            Image = bitmap;

            if (bitmap is not null)
            {
                MaxHeight = height;
                MaxWidth = width;
                Size
                    = string.Format(
                        "/WindowSill.ClipboardHistory/Misc/ImageSize".GetLocalizedString(),
                        width,
                        height);
            }
            else
            {
                // TODO: Show that we can't preview the image.
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to initialize {nameof(ImageItemViewModel)} control.");
        }
    }
}
