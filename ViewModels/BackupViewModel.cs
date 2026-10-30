using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WindowSill.ClipboardHistory.UI;

namespace WindowSill.ClipboardHistory.ViewModels;

internal sealed partial class BackupViewModel(WorkspaceViewModel owner) : ObservableObject
{
    [ObservableProperty] public partial string Password { get; set; } = string.Empty;
    [ObservableProperty] public partial string Confirmation { get; set; } = string.Empty;
    [ObservableProperty] public partial bool LocalFileReferences { get; set; }

    [RelayCommand]
    private Task ExportAsync() => owner.RunAsync("BackupExport", async cancellationToken =>
    {
        try
        {
            if (Password.Length is < 12 or > 1024 || Password != Confirmation) throw new ArgumentException("Check the export passphrase and confirmation.");
            string? file = await owner.Interaction.PickBackupAsync(save: true, cancellationToken);
            if (file is null) return;
            string password = Password;
            var progress = owner.TransferProgress();
            int count = await Task.Run(() => owner.Service.ExportAsync(file, password, LocalFileReferences, progress, cancellationToken), cancellationToken);
            owner.Status = WorkspaceStrings.Format("WorkspaceExportDone", count);
        }
        finally { ClearPasswords(); }
    });

    [RelayCommand]
    private Task ImportAsync() => owner.RunAsync("BackupImport", async cancellationToken =>
    {
        try
        {
            if (Password.Length is < 1 or > 1024) throw new ArgumentException("Enter the archive passphrase.");
            string? file = await owner.Interaction.PickBackupAsync(save: false, cancellationToken);
            if (file is null) return;
            if (!await owner.Interaction.ConfirmAsync(WorkspaceStrings.Get("WorkspaceImport"), WorkspaceStrings.Get("WorkspaceImportConfirmation"), cancellationToken)) return;
            string password = Password;
            var progress = owner.TransferProgress();
            var result = await Task.Run(() => owner.Service.ImportAsync(file, password, LocalFileReferences, progress, cancellationToken), cancellationToken);
            owner.Status = WorkspaceStrings.Format("WorkspaceImportDone", result.Added, result.Duplicates, result.Skipped);
        }
        finally { ClearPasswords(); }
    });

    internal void ClearPasswords() { Password = string.Empty; Confirmation = string.Empty; }
}
