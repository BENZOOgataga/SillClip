using CommunityToolkit.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Windows.ApplicationModel.DataTransfer;
using WindowSill.API;
using WindowSill.ClipboardHistory.Core;

namespace WindowSill.ClipboardHistory.ViewModels;

internal sealed partial class RtfItemViewModel : ClipboardHistoryItemViewModelBase
{
    private readonly ILogger _logger;
    private readonly ISettingsProvider _settingsProvider;
    protected override DetectedClipboardDataType DetectedDataType => DetectedClipboardDataType.Rtf;

    /// <summary>
    /// Gets or sets the truncated display text shown in the list item.
    /// </summary>
    [ObservableProperty]
    public partial string DisplayText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the full text shown in the preview flyout.
    /// </summary>
    [ObservableProperty]
    public partial string PreviewText { get; set; } = string.Empty;

    internal RtfItemViewModel(ISettingsProvider settingsProvider, IProcessInteractionService processInteractionService, IClipboardItemSource source)
        : base(processInteractionService, source)
    {
        _logger = this.Log();
        _settingsProvider = settingsProvider;

        InitializeAsync().Forget();
    }

    private async Task InitializeAsync()
    {
        try
        {
            Guard.IsNotNull(Data);

            // Try to use plain text rather than RTF if available
            string text;
            if (Data.Contains(StandardDataFormats.Text))
            {
                text = await Data.GetTextAsync();
            }
            else
            {
                text = await Data.GetRtfAsync();
            }

            text = ClipboardPrivacy.Protect(text, _settingsProvider.GetSetting(Settings.Settings.HidePasswords));
            DisplayText = text
                .Substring(0, Math.Min(text.Length, 256))
                .Trim()
                .Replace("\r\n", "⏎")
                .Replace("\n\r", "⏎")
                .Replace('\r', '⏎')
                .Replace('\n', '⏎');

            PreviewText = text.Substring(0, Math.Min(text.Length, 1000));
            if (text.Length > 1000)
            {
                PreviewText += "…";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to initialize {nameof(RtfItemViewModel)} control.");
        }
    }
}
