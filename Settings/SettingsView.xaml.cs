using Windows.System;
using WindowSill.API;
using WindowSill.ClipboardHistory.Services;

namespace WindowSill.ClipboardHistory.Settings;

/// <summary>
/// Settings view for the Clipboard History extension.
/// </summary>
internal sealed partial class SettingsView : UserControl
{
    internal SettingsView(ISettingsProvider settingsProvider, GitHubUpdateService updateService)
    {
        ViewModel = new SettingsViewModel(settingsProvider, updateService);
        InitializeComponent();
        Loaded += (_, _) => ViewModel.Attach();
        Unloaded += (_, _) => ViewModel.Detach();
    }

    /// <summary>
    /// Gets the view model for the settings view.
    /// </summary>
    internal SettingsViewModel ViewModel { get; }

    private async void OpenWindowsClipboardHistorySettingsCard_Click(object sender, RoutedEventArgs e)
    {
        await Launcher.LaunchUriAsync(new Uri("ms-settings:clipboard"));
    }

    private async void UpdateActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.HasNewerRelease && ViewModel.LatestReleaseUri is Uri releaseUri)
        {
            await Launcher.LaunchUriAsync(releaseUri);
            return;
        }

        await ViewModel.CheckForUpdatesAsync();
    }
}
