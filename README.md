# WindowSill Clipboard History: No Limits

This project is a focused fork of the official WindowSill Clipboard History extension. It keeps the original interface and clipboard actions, then adds the features needed for histories larger than the Windows limit.

Download the `.wsext` package from the [current prerelease](https://github.com/BENZOOgataga/WindowSill-NoClipBoardHistoryLimit/releases/tag/v0.10.1-unlimited.2). Installation steps are below. You need Windows 11 and WindowSill 1.0.3 or newer.

## What this fork adds

- `0` or `-1` removes the extension-side item limit.
- Any positive value, such as `500` or `1000`, becomes the retention limit.
- Persistent history keeps items after Windows evicts them and across restarts.
- A search button in the WindowSill bar opens a searchable popup.
- Compact mode includes the same live search field.
- Win+V opens the searchable WindowSill history instead of the Windows panel.
- An automatic GitHub Releases check reports when a newer version is available.
- The popup can be dragged by its heading and remembers its position after a restart.

Pinned items remain separate from the retention limit and stay at the top.

## How persistent history works

Windows exposes only a small clipboard history through `Clipboard.GetHistoryItemsAsync()`. Raising the old setting above that number cannot change the Windows limit by itself.

This fork listens for clipboard changes while WindowSill is running and writes its own archive. Text, HTML, RTF, links, colors, images, and file references can be restored after a restart. Repeated content is deduplicated by a SHA-256 content signature.

Archive metadata and image data are encrypted with Windows DPAPI for the current user. The files cannot be decrypted by another Windows account or on another computer. Clipboard data is still sensitive, so persistent history can be disabled in the extension settings. Clearing the history also clears the encrypted archive, but pinned items are preserved.

Images are read from disk on demand and decoded as thumbnails for the list. Link icons are local; copied URLs are not sent to a favicon service. The Hide passwords option masks detected passwords in the list and preview and excludes them from text search. Detection is heuristic, so this option does not prevent every secret from being displayed or stored.

There is no arbitrary count limit when the setting is `0` or `-1`. Available disk space and memory remain practical limits, especially for image-heavy histories.

## Search

In expanded mode, the bar keeps the 25 most recent entries visible and the search popup provides access to the full archive. This prevents a 500-item history from creating 500 controls in the bar. In compact mode, open the Clipboard History popup. The search field filters as you type and supports multiple terms. Text, links, file names, file paths, and item types are indexed.

## Win+V shortcut

The `Open this history with Win+V` setting is enabled by default. While the extension is active, it intercepts Win+V and opens the searchable WindowSill popup. Disable the setting to restore the standard Windows clipboard panel without uninstalling the extension.

Windows reserves Win+V for the Shell, and the WindowSill SDK does not expose a global shortcut API. This fork therefore uses a narrowly scoped low-level keyboard hook that intercepts only Win+V. The hook runs on a dedicated thread with its own message loop. It is removed when the Sill is deactivated or the setting is disabled.

## Screen recording protection

The Win+V popup is hosted by WindowSill, so it uses WindowSill's screen recording protection. Enable that setting in WindowSill if clipboard content must be excluded from supported capture tools such as compatible Discord capture modes. The extension does not try to detect whether Discord is currently streaming. Process detection would produce false positives and Discord does not expose a reliable public stream-state API for this purpose.

Screen recording protection depends on the capture path used by the recording application. It is useful for accidental disclosure prevention, but it is not a DRM or security boundary. Verify it with the exact Discord, OBS, or screen-sharing mode you use before relying on it.

## Install

WindowSill extensions are installed from a `.wsext` package. The DLL in `bin/Release/` is not installable by itself because the extension also needs its compiled XAML and resources.

This is the direct installation flow documented in the [official WindowSill publishing guide](https://getwindowsill.app/doc/articles/extension-development/getting-started/publish.html).

To install:

1. Open WindowSill Settings.
2. Open Manage Extensions.
3. Disable the official Clipboard History extension to avoid two active clipboard panels.
4. Download `WindowSill.ClipboardHistory.NoLimits.0.10.1-unlimited.2.wsext` from the [release page](https://github.com/BENZOOgataga/WindowSill-NoClipBoardHistoryLimit/releases/tag/v0.10.1-unlimited.2).
5. Double-click the `.wsext` file in File Explorer.
6. Accept the WindowSill installation prompt, then enable Clipboard History No Limits if needed.

This fork uses its own package ID, `WindowSill.ClipboardHistory.NoLimits`, so marketplace updates for the official extension do not replace it. On the first run, encrypted history and pins from the previous fork package are copied into the new data folder. Original files are retained.

Windows clipboard history must be enabled. Open Windows Settings, then System, then Clipboard, and enable Clipboard history if WindowSill asks you to. The extension records history while WindowSill is running.

To update a package that was installed outside the WindowSill marketplace, uninstall the current version, restart WindowSill, then double-click the newer `.wsext` file.

## Build and install a local package

Requirements:

- Windows 11
- .NET 10 SDK
- WindowSill 1.0.3 or newer
- Windows SDK build tools (restored with the project dependencies)

```powershell
dotnet restore
.\scripts\Build-Extension.ps1
```

The script builds the extension, packs it, copies the generated `.nupkg` to `artifacts/`, and changes its extension to `.wsext`. Double-click the resulting `.wsext` file to install it.

The project restores Microsoft's MSIX build tools through NuGet to generate the required `.pri` resources. The build does not require a full Visual Studio installation.

Run the regression tests with:

```powershell
dotnet test tests/UnitTests/UnitTests.csproj -c Release
```

To debug the extension:

1. Open the project in Visual Studio.
2. Select the `x64` platform.
3. Select the `WindowSill Extension` launch profile.
4. Press F5.

## Updates and releases

The extension checks this repository at startup and every six hours while active. Check now bypasses that interval. Only releases with a `WindowSill.ClipboardHistory.NoLimits.*.wsext` asset count as installable updates. Stable installations ignore prereleases; prerelease installations can see both channels.

Updates are not downloaded or installed automatically. When an update is available, Open release opens its GitHub page. Install its `.wsext` package using the steps above. A source commit alone is not an installable update.

## Credits and license

Based on the official [WindowSill Extensions](https://github.com/WindowSill-app/Extensions) repository. The original copyright and license are preserved in [LICENSE.md](LICENSE.md).
