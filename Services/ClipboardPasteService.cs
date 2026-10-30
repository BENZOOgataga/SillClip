using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using WindowSill.API;
using WindowSill.ClipboardHistory.Core;

namespace WindowSill.ClipboardHistory.Services;

/// <summary>Owns clipboard writes and the focus check shared by normal, transformed, and queued paste.</summary>
internal sealed class ClipboardPasteService(IProcessInteractionService interaction)
{
    internal static Task CopyTextAsync(string text) => ThreadHelper.RunOnUIThreadAsync(() => SetText(text));
    internal Task CopyAsync(IClipboardItemSource source, string? textOverride = null)
        => ThreadHelper.RunOnUIThreadAsync(() => SetContent(source, textOverride));

    internal async Task<bool> PasteAsync(IClipboardItemSource source, DetectedClipboardDataType type,
        (nint Window, uint ProcessId)? target, string? textOverride = null)
    {
        bool pasted = false;
        await ThreadHelper.RunOnUIThreadAsync(async () =>
        {
            SetContent(source, textOverride);
            if (target is { } expected)
            {
                for (int attempt = 0; attempt < 20 && !PopupInvocationContext.IsTargetActive(expected.Window, expected.ProcessId); attempt++)
                {
                    if (!PopupInvocationContext.IsOwnWindowActive()) return;
                    await Task.Delay(16);
                }
                if (!PopupInvocationContext.IsTargetActive(expected.Window, expected.ProcessId)) return;
            }
            else if (textOverride is null)
            {
                nint window = ExplorerDetector.GetLastActiveWindow();
                if (window != 0 && ExplorerDetector.IsExplorerLikeWindow(window))
                {
                    string? folder = ExplorerFolderResolver.GetCurrentFolderPath(window);
                    if (!string.IsNullOrEmpty(folder) && await ClipboardFileWriter.TryWriteAsFileAsync(source.Data, type, folder))
                    { pasted = true; return; }
                }
            }
            if (target is null) await Task.Delay(200);
            else if (!PopupInvocationContext.IsTargetActive(target.Value.Window, target.Value.ProcessId)) return;
            await interaction.SimulateKeysOnLastActiveWindow(VirtualKey.LeftControl, VirtualKey.V);
            pasted = true;
        });
        return pasted;
    }

    private static void SetContent(IClipboardItemSource source, string? textOverride)
    {
        if (textOverride is null) { source.SetAsClipboardContent(); return; }
        SetText(textOverride);
    }

    private static void SetText(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        if (!Clipboard.SetContentWithOptions(package, new ClipboardContentOptions { IsAllowedInHistory = false, IsRoamable = false }))
            throw new InvalidOperationException("The clipboard is temporarily unavailable.");
    }

    internal static async Task<string> ReadTextAsync(IClipboardItemSource source)
    {
        return await Task.Run(async () =>
        {
            var data = source.Data;
            if (data.AvailableFormats.Contains(StandardDataFormats.Text)) return await data.GetTextAsync();
            if (data.AvailableFormats.Contains(StandardDataFormats.WebLink)) return (await data.GetWebLinkAsync()).ToString();
            throw new NotSupportedException("This item does not contain readable text.");
        });
    }
}
