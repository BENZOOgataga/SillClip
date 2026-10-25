using System.Text.RegularExpressions;
using CommunityToolkit.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using WindowSill.API;
using WindowSill.ClipboardHistory.Core;
using WindowSill.ClipboardHistory.Utils;

namespace WindowSill.ClipboardHistory.ViewModels;

/// <summary>
/// ViewModel for clipboard history items containing URI/web link data.
/// </summary>
internal sealed partial class UriItemViewModel : ClipboardHistoryItemViewModelBase
{
    private readonly ILogger _logger;
    protected override DetectedClipboardDataType DetectedDataType => DetectedClipboardDataType.Uri;

    /// <summary>
    /// Initializes a new instance of the <see cref="UriItemViewModel"/> class.
    /// </summary>
    /// <param name="processInteractionService">Service for interacting with external processes.</param>
    /// <param name="source">The clipboard item source containing URI data.</param>
    internal UriItemViewModel(IProcessInteractionService processInteractionService, IClipboardItemSource source)
        : base(processInteractionService, source)
    {
        _logger = this.Log();
        InitializeAsync().Forget();
    }

    [ObservableProperty]
    public partial string Url { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PageTitle { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenInBrowserCommand))]
    public partial bool HasError { get; set; }

    [RelayCommand(CanExecute = nameof(CanOpenInBrowser))]
    private async Task OpenInBrowserAsync()
    {
        await Launcher.LaunchUriAsync(new Uri(Url));
    }

    private bool CanOpenInBrowser() => !HasError;

    private async Task InitializeAsync()
    {
        try
        {
            Guard.IsNotNull(Data);

            if (Data.AvailableFormats.Contains(StandardDataFormats.WebLink))
            {
                Uri webLink = await Data.GetWebLinkAsync();
                Url = webLink?.ToString() ?? string.Empty;
            }
            else if (Data.AvailableFormats.Contains(StandardDataFormats.Uri))
            {
                Uri uri = await Data.GetUriAsync();
                Url = uri?.ToString() ?? string.Empty;
            }
            else
            {
                Url = await Data.GetTextAsync();
            }

            if (Data.Contains(StandardDataFormats.Html))
            {
                string html = await Data.GetHtmlFormatAsync();
                if (!string.IsNullOrEmpty(html))
                {
                    // Extract the HTML part
                    int startHtml = html.IndexOf("<html>");
                    if (startHtml >= 0)
                    {
                        string htmlContent = html.Substring(startHtml);

                        // Use regex to find the text inside the <a> tag
                        Match match = AHrefTagRegex().Match(htmlContent);
                        if (match.Success)
                        {
                            string pageTitle = match.Groups[1].Value.Trim();
                            PageTitle = pageTitle;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"Failed to initialize {nameof(UriItemViewModel)} control.");
            Url = "/WindowSill.ClipboardHistory/Misc/LinkError".GetLocalizedString();
            HasError = true;
        }
    }

    [GeneratedRegex(@"<a[^>]*>(.*?)<\/a>", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex AHrefTagRegex();
}
