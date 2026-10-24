# Clipboard History Unlimited pour WindowSill

Fork fidèle de l’extension officielle **Clipboard History** de WindowSill. Toutes les fonctions d’origine sont conservées ; seule la limite du réglage **Maximum number of items** est modifiée.

- `-1` ou `0` : aucune limite appliquée par WindowSill.
- Valeur positive (`25`, `500`, `1000`, etc.) : nombre maximal demandé.

## Compiler

Prérequis : Windows 11, le SDK .NET 10, WindowSill et Visual Studio/Build Tools avec les composants **Universal** et **Windows App SDK C#**.

```powershell
dotnet restore
dotnet build -c Release
```

La DLL et le paquet WindowSill (`.nupkg`) sont générés sous `bin/Release/`.

Une extension WindowSill ne s’installe pas comme une DLL nue : le `.nupkg` contient également les vues XAML et les ressources. Pour tester localement, ouvrez le projet dans Visual Studio, sélectionnez la plateforme **x64** et le profil **WindowSill Extension**, puis appuyez sur F5. Le profil transmet le paquet à WindowSill via `EXTRAPLUGIN`.

Le Sill conserve volontairement le même identifiant interne et le même `PackageId` que l’extension officielle afin de la remplacer. Désactivez ou désinstallez donc la version officielle avant de charger ce paquet.

## Limitation de Windows

Cette modification enlève la limite ajoutée par l’interface WindowSill. Elle ne peut pas obliger `Windows.ApplicationModel.DataTransfer.Clipboard.GetHistoryItemsAsync()` à retourner davantage d’éléments que Windows lui-même n’en conserve. Obtenir un historique réellement indépendant de plusieurs centaines d’éléments nécessiterait un second moteur de capture et de stockage, ce qui ne serait plus « exactement la même extension avec un seul réglage modifié ».
