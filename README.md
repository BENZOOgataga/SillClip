# WindowSill Clipboard History — No Limit

A minimal fork of WindowSill's official **Clipboard History** extension. All original features and UI are preserved; only the **Maximum number of items** setting has been changed.

- `-1` or `0`: WindowSill applies no item limit.
- Any positive value (`25`, `500`, `1000`, and so on): requested maximum number of items.

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

A WindowSill extension should not be installed as a standalone DLL: the `.nupkg` also contains its compiled XAML views and resources. To test locally, open the project in Visual Studio, select the **x64** platform and the **WindowSill Extension** launch profile, then press F5. The profile passes the package to WindowSill through `EXTRAPLUGIN`.

This fork deliberately keeps the same internal Sill name and `PackageId` as the official extension so that it can replace it. Disable or uninstall the official version before loading this package.

## Windows limitation

This change removes the limit applied by WindowSill. It cannot force `Windows.ApplicationModel.DataTransfer.Clipboard.GetHistoryItemsAsync()` to return more entries than Windows itself retains. A truly independent history containing hundreds of entries would require a separate capture and storage engine, rather than this deliberately minimal fork.

## Credits and license

Based on the official [WindowSill Extensions](https://github.com/WindowSill-app/Extensions) repository. The original copyright and license are preserved in [LICENSE.md](LICENSE.md).
