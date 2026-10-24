[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot "WindowSill.ClipboardHistory.csproj"
$packageDirectory = Join-Path $repositoryRoot "bin\$Configuration"
$outputDirectory = Join-Path $repositoryRoot "artifacts"

dotnet build $projectPath -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE."
}

dotnet pack $projectPath -c $Configuration --no-build
if ($LASTEXITCODE -ne 0) {
    throw "dotnet pack failed with exit code $LASTEXITCODE."
}

$package = Get-ChildItem -LiteralPath $packageDirectory -Filter "WindowSill.ClipboardHistory.*.nupkg" |
    Sort-Object LastWriteTimeUtc -Descending |
    Select-Object -First 1

if ($null -eq $package) {
    throw "No WindowSill extension package was found in $packageDirectory."
}

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$extensionPath = Join-Path $outputDirectory ($package.BaseName + ".wsext")
Copy-Item -LiteralPath $package.FullName -Destination $extensionPath -Force

Write-Host "WindowSill extension package: $extensionPath"
