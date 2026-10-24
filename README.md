# WindowSill Clipboard History: No Limits

This project is a focused fork of the official WindowSill Clipboard History extension. It keeps the original interface and clipboard actions, then adds the features needed for histories larger than the Windows limit.

## What this fork adds

- `0` or `-1` removes the extension-side item limit.
- Any positive value, such as `500` or `1000`, becomes the retention limit.
- Persistent history keeps items after Windows evicts them and across restarts.
- A search button in the WindowSill bar opens a searchable popup.
- Compact mode includes the same live search field.
- Win+V opens the searchable WindowSill history instead of the Windows panel.
- An automatic GitHub Releases check reports when a newer version is available.

Pinned items remain separate from the retention limit and stay at the top.

## How persistent history works

Windows exposes only a small clipboard history through `Clipboard.GetHistoryItemsAsync()`. Raising the old setting above that number cannot change the Windows limit by itself.

This fork listens for clipboard changes while WindowSill is running and writes its own archive. Text, HTML, RTF, links, colors, images, and file references can be restored after a restart. Repeated content is deduplicated by a SHA-256 content signature.

Archive metadata and image data are encrypted with Windows DPAPI for the current user. The files cannot be decrypted by another Windows account or on another computer. Clipboard data is still sensitive, so persistent history can be disabled in the extension settings. Clearing the history also clears the encrypted archive, but pinned items are preserved.

There is no arbitrary count limit when the setting is `0` or `-1`. Available disk space and memory remain practical limits, especially for image-heavy histories.

## Search

In expanded mode, the bar keeps the 25 most recent entries visible and the search popup provides access to the full archive. This prevents a 500-item history from creating 500 controls in the bar. In compact mode, open the Clipboard History popup. The search field filters as you type and supports multiple terms. Text, links, file names, file paths, and item types are indexed.

## Win+V shortcut

The `Open this history with Win+V` setting is enabled by default. While the extension is active, it intercepts Win+V and opens the searchable WindowSill popup. Disable the setting to restore the standard Windows clipboard panel without uninstalling the extension.

Windows reserves Win+V for the Shell, and the WindowSill SDK does not expose a global shortcut API. This fork therefore uses a narrowly scoped low-level keyboard hook that intercepts only Win+V. The hook is removed when the Sill is deactivated or the setting is disabled.

## Screen recording protection

The Win+V popup is hosted by WindowSill, so it uses WindowSill's screen recording protection. Enable that setting in WindowSill if clipboard content must be excluded from supported capture tools such as compatible Discord capture modes. The extension does not try to detect whether Discord is currently streaming. Process detection would produce false positives and Discord does not expose a reliable public stream-state API for this purpose.

Screen recording protection depends on the capture path used by the recording application. It is useful for accidental disclosure prevention, but it is not a DRM or security boundary. Verify it with the exact Discord, OBS, or screen-sharing mode you use before relying on it.

## Install

WindowSill extensions are installed from a `.wsext` package. The DLL in `bin/Release/` is not installable by itself because the extension also needs its compiled XAML and resources.

This is the direct installation flow documented in the [official WindowSill publishing guide](https://getwindowsill.app/doc/articles/extension-development/getting-started/publish.html).

When a release package is available:

1. Open WindowSill Settings.
2. Open Manage Extensions.
3. Disable or uninstall the official Clipboard History extension. This fork uses the same package ID, so both versions cannot be active together.
4. Download the `.wsext` file from the GitHub release.
5. Double-click the `.wsext` file in File Explorer.
6. Accept the WindowSill installation prompt, then enable Clipboard History if needed.

To update a package that was installed outside the WindowSill marketplace, uninstall the current version first, then double-click the newer `.wsext` file.

## Build and install a local package

Requirements:

- Windows 11
- .NET 10 SDK
- WindowSill
- Visual Studio or Visual Studio Build Tools with the `WinUI application development` workload

```powershell
dotnet restore
.\scripts\Build-Extension.ps1
```

The script runs `dotnet pack -c Release`, copies the generated `.nupkg` to `artifacts/`, and changes its extension to `.wsext`. Double-click the resulting `.wsext` file to install it.

The required Visual Studio packaging components are important. A plain .NET SDK installation can compile most C# code but cannot generate the `.pri` resource file required by a WindowSill package. For Build Tools, install workload ID `Microsoft.VisualStudio.Workload.UniversalBuildTools` and include its recommended Windows 11 SDK. Microsoft lists the workload and its components in the [Visual Studio Build Tools documentation](https://learn.microsoft.com/visualstudio/install/workload-component-id-vs-build-tools?view=vs-2026).

To debug the extension:

1. Open the project in Visual Studio.
2. Select the `x64` platform.
3. Select the `WindowSill Extension` launch profile.
4. Press F5.

## Updates and releases

The settings page checks the latest release published at this repository. A source commit is not considered an installable update. A release should contain the built `.wsext` file.

## Credits and license

Based on the official [WindowSill Extensions](https://github.com/WindowSill-app/Extensions) repository. The original copyright and license are preserved in [LICENSE.md](LICENSE.md).
