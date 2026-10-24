using CommunityToolkit.Mvvm.ComponentModel;
using WindowSill.API;
using Windows.ApplicationModel.DataTransfer;
using WindowSill.ClipboardHistory.Services;

namespace WindowSill.ClipboardHistory.Settings;

internal sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsProvider _settingsProvider;
    private readonly GitHubUpdateService _updateService;

    public SettingsViewModel(ISettingsProvider settingsProvider, GitHubUpdateService updateService)
    {
        _settingsProvider = settingsProvider;
        _updateService = updateService;
        Clipboard.HistoryEnabledChanged += Clipboard_HistoryEnabledChanged;
        _updateService.StatusChanged += UpdateService_StatusChanged;
    }

    public int MaximumHistoryCount
    {
        get => _settingsProvider.GetSetting(Settings.MaximumHistoryCount);
        set => _settingsProvider.SetSetting(Settings.MaximumHistoryCount, value);
    }

    public bool HidePasswords
    {
        get => _settingsProvider.GetSetting(Settings.HidePasswords);
        set => _settingsProvider.SetSetting(Settings.HidePasswords, value);
    }

    public bool CompactMode
    {
        get => _settingsProvider.GetSetting(Settings.CompactMode);
        set => _settingsProvider.SetSetting(Settings.CompactMode, value);
    }

    public bool PersistentHistory
    {
        get => _settingsProvider.GetSetting(Settings.PersistentHistory);
        set => _settingsProvider.SetSetting(Settings.PersistentHistory, value);
    }

    public bool OverrideWindowsV
    {
        get => _settingsProvider.GetSetting(Settings.OverrideWindowsV);
        set => _settingsProvider.SetSetting(Settings.OverrideWindowsV, value);
    }

    public bool IsClipboardHistoryEnabled => Clipboard.IsHistoryEnabled();

    public bool IsClipboardHistoryDisabled => !IsClipboardHistoryEnabled;

    public string UpdateStatus
    {
        get
        {
            if (_updateService.IsChecking)
            {
                return "/WindowSill.ClipboardHistory/Misc/UpdateChecking".GetLocalizedString();
            }

            if (_updateService.CheckFailed)
            {
                return "/WindowSill.ClipboardHistory/Misc/UpdateFailed".GetLocalizedString();
            }

            if (_updateService.IsUpdateAvailable)
            {
                return string.Format(
                    "/WindowSill.ClipboardHistory/Misc/UpdateAvailable".GetLocalizedString(),
                    _updateService.LatestVersion);
            }

            if (_updateService.HasChecked && _updateService.LatestVersion is null)
            {
                return "/WindowSill.ClipboardHistory/Misc/UpdateNoRelease".GetLocalizedString();
            }

            return string.Format(
                "/WindowSill.ClipboardHistory/Misc/UpdateCurrent".GetLocalizedString(),
                _updateService.CurrentVersion);
        }
    }

    public string UpdateActionText => _updateService.IsUpdateAvailable
        ? "/WindowSill.ClipboardHistory/Misc/OpenRelease".GetLocalizedString()
        : "/WindowSill.ClipboardHistory/Misc/CheckNow".GetLocalizedString();

    public bool IsUpdateActionEnabled => !_updateService.IsChecking;

    internal Uri? LatestReleaseUri => _updateService.LatestReleaseUri;

    internal Task CheckForUpdatesAsync() => _updateService.CheckAsync(force: true);

    private void Clipboard_HistoryEnabledChanged(object? sender, object e)
    {
        OnPropertyChanged(nameof(IsClipboardHistoryEnabled));
        OnPropertyChanged(nameof(IsClipboardHistoryDisabled));
    }

    private void UpdateService_StatusChanged(object? sender, EventArgs e)
    {
        ThreadHelper.RunOnUIThreadAsync(() =>
        {
            OnPropertyChanged(nameof(UpdateStatus));
            OnPropertyChanged(nameof(UpdateActionText));
            OnPropertyChanged(nameof(IsUpdateActionEnabled));
        }).Forget();
    }
}
