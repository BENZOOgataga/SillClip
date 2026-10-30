using Windows.System;
using WindowSill.API;
using WindowSill.ClipboardHistory.Services;
using WindowSill.ClipboardHistory.ViewModels;
using WindowSill.ClipboardHistory.UI;
using Microsoft.Extensions.Logging;

namespace WindowSill.ClipboardHistory.Settings;

/// <summary>
/// Settings view for the Clipboard History extension.
/// </summary>
internal sealed partial class SettingsView : UserControl
{
    internal SettingsView(ISettingsProvider settingsProvider, GitHubUpdateService updateService, LibraryStateService library)
    {
        ViewModel = new SettingsViewModel(settingsProvider, updateService);
        Controls = new(library);
        InitializeComponent();
        Loaded += (_, _) => { ViewModel.Attach(); Controls.Attach(); };
        Unloaded += (_, _) => { ViewModel.Detach(); Controls.Detach(); };
    }

    /// <summary>
    /// Gets the view model for the settings view.
    /// </summary>
    internal SettingsViewModel ViewModel { get; }
    internal CaptureControlsViewModel Controls { get; }
    internal string Localize(string key) => WorkspaceStrings.Get(key);
    internal string SettingLabel(string key) => ("/WindowSill.ClipboardHistory/SettingsUserControl/" + key + ".HeaderProperty").GetLocalizedString();

    private async void OpenWindowsClipboardHistorySettingsCard_Click(object sender, RoutedEventArgs e)
    {
        await OpenUriAsync(new Uri("ms-settings:clipboard"));
    }

    private async void UpdateActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.HasNewerRelease && ViewModel.LatestReleaseUri is Uri releaseUri)
        {
            await OpenUriAsync(releaseUri);
            return;
        }

        await ViewModel.CheckForUpdatesAsync();
    }

    private async Task OpenUriAsync(Uri uri)
    {
        ViewModel.LaunchError = string.Empty;
        try
        {
            if (!await Launcher.LaunchUriAsync(uri)) ViewModel.LaunchError = Localize("SettingsLaunchFailed");
        }
        catch (Exception ex)
        {
            this.Log().LogWarning("A settings link could not be opened ({ErrorType}).", ex.GetType().Name);
            ViewModel.LaunchError = Localize("SettingsLaunchFailed");
        }
    }
}
