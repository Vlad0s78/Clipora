[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$InputDirectory,
    [Parameter(Mandatory)][string]$OutputPath,
    [Parameter(Mandatory)][string]$RootDirectoryName,
    [long]$SourceDateEpoch = 946684800
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$inputRoot = (Resolve-Path -LiteralPath $InputDirectory).Path.TrimEnd('\')
$outputFullPath = [System.IO.Path]::GetFullPath($OutputPath)
if ($outputFullPath.StartsWith("$inputRoot\", [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'ZIP нельзя создавать внутри упаковываемого каталога.'
}

if ($RootDirectoryName -notmatch '^[0-9A-Za-z._-]+$') {
    throw "Недопустимое имя корневого каталога ZIP: $RootDirectoryName"
}

$timestamp = [DateTimeOffset]::FromUnixTimeSeconds($SourceDateEpoch)
if ($timestamp.Year -lt 1980) {
    throw 'SOURCE_DATE_EPOCH для ZIP должен быть не раньше 1980-01-01.'
}

$parent = Split-Path -Parent $outputFullPath
New-Item -ItemType Directory -Path $parent -Force | Out-Null
if (Test-Path -LiteralPath $outputFullPath) {
    Remove-Item -LiteralPath $outputFullPath -Force
}

Add-Type -AssemblyName System.IO.Compression
$files = @(Get-ChildItem -LiteralPath $inputRoot -File -Recurse | Sort-Object { $_.FullName.Substring($inputRoot.Length + 1) })
$stream = [System.IO.File]::Open($outputFullPath, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
try {
    $archive = [System.IO.Compression.ZipArchive]::new($stream, [System.IO.Compression.ZipArchiveMode]::Create, $false, [System.Text.Encoding]::UTF8)
    try {
        foreach ($file in $files) {
            if (($file.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Reparse point запрещён в Portable ZIP: $($file.FullName)"
            }

            $relativePath = $file.FullName.Substring($inputRoot.Length + 1).Replace('\', '/')
            $entry = $archive.CreateEntry("$RootDirectoryName/$relativePath", [System.IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = $timestamp
            $source = [System.IO.File]::OpenRead($file.FullName)
            try {
                $destination = $entry.Open()
                try {
                    $source.CopyTo($destination)
                }
                finally {
                    $destination.Dispose()
                }
            }
            finally {
                $source.Dispose()
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}
finally {
    $stream.Dispose()
}

Get-Item -LiteralPath $outputFullPath
