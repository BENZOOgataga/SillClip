# WindowSill Clipboard History — No Limit

This is a fork of WindowSill's official **Clipboard History** extension. The only functional change is the range accepted by **Maximum number of items**.

- `-1` or `0`: WindowSill applies no item limit.
- Any positive value (`25`, `500`, `1000`, etc.): maximum number of items requested from the Windows clipboard history.

## Build

Requirements:

- Windows 11
- .NET 10 SDK
- WindowSill
- Visual Studio or Visual Studio Build Tools with the **Universal** and **Windows App SDK C#** components

```powershell
dotnet restore
dotnet build -c Release
```

The DLL and WindowSill package (`.nupkg`) are generated under `bin/Release/`.

Do not install the DLL by itself. The `.nupkg` also contains the compiled XAML views and resources.

To test the extension:

1. Open the project in Visual Studio.
2. Select the **x64** platform.
3. Select the **WindowSill Extension** launch profile.
4. Press F5.

The launch profile passes the package to WindowSill through `EXTRAPLUGIN`.

The fork uses the same internal Sill name and `PackageId` as the official extension. Disable or uninstall the official version before loading this package.

## Windows limitation

This change removes WindowSill's limit. It does not change how many entries Windows keeps. The extension can only display the entries returned by `Windows.ApplicationModel.DataTransfer.Clipboard.GetHistoryItemsAsync()`.

## Credits and license

Based on the official [WindowSill Extensions](https://github.com/WindowSill-app/Extensions) repository. The original copyright and license are preserved in [LICENSE.md](LICENSE.md).
