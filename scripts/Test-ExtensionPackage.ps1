[CmdletBinding()]
param([Parameter(Mandatory)][string]$Path)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$privatePaths = @($repositoryRoot, $env:USERPROFILE) | Where-Object { $_ }
$archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $Path).Path)
try {
    $names = $archive.Entries.FullName
    foreach ($required in @('WindowSill.ClipboardHistory.dll', 'WindowSill.ClipboardHistory.pri', 'WindowSill.ClipboardHistory.NoLimits.nuspec')) {
        if (-not ($names | Where-Object { [IO.Path]::GetFileName($_) -eq $required })) {
            throw "Required package file is missing: $required"
        }
    }
    foreach ($entry in $archive.Entries) {
        if ($entry.FullName -match '\.(pdb|dmp|dat|img)$|(^|/)(artifacts|tests|obj|\.git)/|(^|/)\.\.(/|$)|^[\\/]|^[A-Za-z]:') {
            throw "Unexpected package entry: $($entry.FullName)"
        }
        $stream = $entry.Open()
        $buffer = [IO.MemoryStream]::new()
        try {
            $stream.CopyTo($buffer)
            $bytes = $buffer.ToArray()
            foreach ($encoding in @([Text.Encoding]::UTF8, [Text.Encoding]::Unicode)) {
                $text = $encoding.GetString($bytes)
                foreach ($privatePath in $privatePaths) {
                    foreach ($variant in @($privatePath, $privatePath.Replace('\', '/'))) {
                        if ($text.IndexOf($variant, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                            throw "Local build path found in package entry: $($entry.FullName)"
                        }
                    }
                }
                if ($text -match 'ghp_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|-----BEGIN [A-Z ]*PRIVATE KEY') {
                    throw "Possible secret found in package entry: $($entry.FullName)"
                }
            }
        }
        finally { $buffer.Dispose(); $stream.Dispose() }
    }
    Write-Host "Package checks passed: $($archive.Entries.Count) entries, required resources present, no local build paths or diagnostic files."
}
finally { $archive.Dispose() }
