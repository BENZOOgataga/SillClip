using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WindowSill.API;
using WindowSill.ClipboardHistory.Services;
using WindowSill.ClipboardHistory.UI;

namespace WindowSill.ClipboardHistory.ViewModels;

internal sealed partial class CaptureControlsViewModel(LibraryStateService state) : ObservableObject
{
    private bool _attached;
    [ObservableProperty] public partial string Error { get; private set; } = string.Empty;
    [ObservableProperty] public partial bool IsBusy { get; private set; }
    public bool CanInteract => !IsBusy;
    public bool HasError => !string.IsNullOrEmpty(Error);
    public bool IsPaused => state.PausedUntil is not null;
    public string CaptureStatus => state.LoadError is not null ? WorkspaceStrings.Get("WorkspaceMetadataUnavailable")
        : state.PausedUntil is { } until
            ? until == DateTimeOffset.MaxValue ? WorkspaceStrings.Get("WorkspacePausedUntilResume")
                : WorkspaceStrings.Format("WorkspacePausedUntil", until.LocalDateTime)
            : WorkspaceStrings.Get("WorkspaceRecording");
    public string PresentationAction => WorkspaceStrings.Get(state.PresentationMode ? "WorkspaceReveal" : "WorkspacePresentation");
    public string ShortPresentationAction => WorkspaceStrings.Get(state.PresentationMode ? "SettingsShowPreviews" : "SettingsHidePreviews");
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanInteract));
    partial void OnErrorChanged(string value) => OnPropertyChanged(nameof(HasError));
    internal void ReportUiFailure() => Error = WorkspaceStrings.Get("WorkspaceOpenFailed");

    internal void Attach()
    {
        if (_attached) return;
        _attached = true; state.Changed += State_Changed; Refresh();
    }
    internal void Detach() { _attached = false; state.Changed -= State_Changed; }

    [RelayCommand] private Task Pause15Async() => RunAsync(() => state.PauseAsync(TimeSpan.FromMinutes(15)));
    [RelayCommand] private Task PauseHourAsync() => RunAsync(() => state.PauseAsync(TimeSpan.FromHours(1)));
    [RelayCommand] private Task PauseUntilResumeAsync() => RunAsync(() => state.PauseAsync(null));
    [RelayCommand] private Task ResumeAsync() => RunAsync(state.ResumeAsync);
    [RelayCommand] private Task TogglePresentationAsync() => RunAsync(() => state.SetPresentationAsync(!state.PresentationMode));

    private async Task RunAsync(Func<Task> action)
    {
        if (IsBusy) return;
        IsBusy = true; Error = string.Empty;
        try { await action(); }
        catch (Exception ex)
        {
            typeof(CaptureControlsViewModel).Log().LogWarning("Privacy state could not be saved ({ErrorType}).", ex.GetType().Name);
            Error = WorkspaceStrings.Get("WorkspacePrivacySaveFailed");
        }
        finally { IsBusy = false; Refresh(); }
    }

    private void State_Changed(object? sender, EventArgs e) => ThreadHelper.RunOnUIThreadAsync(() => { if (_attached) Refresh(); }).Forget();
    private void Refresh()
    {
        OnPropertyChanged(nameof(IsPaused)); OnPropertyChanged(nameof(CaptureStatus)); OnPropertyChanged(nameof(PresentationAction));
        OnPropertyChanged(nameof(ShortPresentationAction));
    }
}
