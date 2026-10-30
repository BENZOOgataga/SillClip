using CommunityToolkit.Mvvm.ComponentModel;
using WindowSill.ClipboardHistory.Core;
using WindowSill.ClipboardHistory.UI;

namespace WindowSill.ClipboardHistory.ViewModels;

internal sealed partial class PasteOptionsViewModel : ObservableObject
{
    [ObservableProperty] public partial int TransformIndex { get; set; }
    public IReadOnlyList<string> Modes { get; } =
    [
        WorkspaceStrings.Get("WorkspaceOriginal"), WorkspaceStrings.Get("WorkspacePlainText"),
        WorkspaceStrings.Get("WorkspaceSingleLine"), WorkspaceStrings.Get("WorkspaceFormatJson"), WorkspaceStrings.Get("WorkspaceCleanUrl")
    ];
    internal ClipboardTextTransform SelectedTransform => TransformIndex is >= 0 and <= 4 ? (ClipboardTextTransform)TransformIndex : ClipboardTextTransform.Original;
}
