[CmdletBinding()]
param(
    [string]$CacheDirectory,
    [switch]$Development
)

. (Join-Path $PSScriptRoot 'Common.ps1')

$repositoryRoot = Get-CliporaRepositoryRoot
if (-not $Development) {
    & (Join-Path $PSScriptRoot 'Test-ReleaseInputs.ps1') -Mode Policy -ReportPath 'artifacts\release\reports\policy-before-media-tools.json' | Out-Null
}

$provenance = Read-JsonFile -LiteralPath (Join-Path $repositoryRoot 'ffmpeg\PROVENANCE.json')
if ([string]::IsNullOrWhiteSpace($CacheDirectory)) {
    $CacheDirectory = Join-Path $repositoryRoot 'artifacts\release-cache'
}
elseif (-not [System.IO.Path]::IsPathRooted($CacheDirectory)) {
    $CacheDirectory = Join-Path $repositoryRoot $CacheDirectory
}

Assert-SafeBuildDirectory -RepositoryRoot $repositoryRoot -LiteralPath $CacheDirectory
New-Item -ItemType Directory -Path $CacheDirectory -Force | Out-Null
$archivePath = Join-Path $CacheDirectory ([string]$provenance.archive.fileName)

if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf) -or (Get-Sha256 -LiteralPath $archivePath) -ne [string]$provenance.archive.sha256) {
    Invoke-WebRequest -Uri ([string]$provenance.archive.url) -OutFile $archivePath
}

$archiveHash = Get-Sha256 -LiteralPath $archivePath
if ($archiveHash -ne [string]$provenance.archive.sha256) {
    throw "SHA-256 архива FFmpeg не совпал: $archiveHash"
}

$tarPath = Join-Path $env:SystemRoot 'System32\tar.exe'
if (-not (Test-Path -LiteralPath $tarPath -PathType Leaf)) {
    throw 'Для извлечения проверенного FFmpeg-архива требуется системный tar.exe.'
}

$extractDirectory = Join-Path $CacheDirectory 'extracted'
Assert-SafeBuildDirectory -RepositoryRoot $repositoryRoot -LiteralPath $extractDirectory
if (Test-Path -LiteralPath $extractDirectory) {
    Remove-Item -LiteralPath $extractDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $extractDirectory -Force | Out-Null
Invoke-NativeChecked -FilePath $tarPath -Arguments @('-xf', $archivePath, '-C', $extractDirectory)

foreach ($binary in @($provenance.binaries)) {
    $sourcePath = Join-Path $extractDirectory ([string]$binary.pathInArchive)
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        throw "В архиве отсутствует $($binary.pathInArchive)"
    }

    $item = Get-Item -LiteralPath $sourcePath
    $hash = Get-Sha256 -LiteralPath $sourcePath
    if ($item.Length -ne [long]$binary.size -or $hash -ne [string]$binary.sha256) {
        throw "Проверка $($binary.name) не пройдена: size=$($item.Length), SHA-256=$hash"
    }
}

$packageLicensePath = Join-Path $extractDirectory ([string]$provenance.package.licensePathInArchive)
if (-not (Test-Path -LiteralPath $packageLicensePath -PathType Leaf)) {
    throw "В архиве отсутствует $($provenance.package.licensePathInArchive)"
}
$packageLicenseHash = Get-Sha256 -LiteralPath $packageLicensePath
if ($packageLicenseHash -ne [string]$provenance.package.licenseSha256) {
    throw "SHA-256 upstream LGPL не совпал: $packageLicenseHash"
}

foreach ($binary in @($provenance.binaries)) {
    $sourcePath = Join-Path $extractDirectory ([string]$binary.pathInArchive)
    $destinationPath = Join-Path $repositoryRoot ([string]$binary.repositoryInputPath)
    Copy-Item -LiteralPath $sourcePath -Destination $destinationPath -Force
}
Copy-Item -LiteralPath $packageLicensePath -Destination (Join-Path $repositoryRoot 'ffmpeg\LICENSE') -Force

if ($Development) {
    Write-Output 'FFmpeg/FFprobe development inputs downloaded and verified.'
    return
}

$releaseInputs = Join-Path $repositoryRoot 'artifacts\release-inputs'
Assert-SafeBuildDirectory -RepositoryRoot $repositoryRoot -LiteralPath $releaseInputs
New-Item -ItemType Directory -Path $releaseInputs -Force | Out-Null
$packageReadmePath = Join-Path $releaseInputs 'ffmpeg-package-README.txt'
Invoke-WebRequest -Uri ([string]$provenance.package.providerReadmeUrl) -OutFile $packageReadmePath
$packageReadmeHash = Get-Sha256 -LiteralPath $packageReadmePath
if ($packageReadmeHash -ne [string]$provenance.package.providerReadmeSha256) {
    throw "SHA-256 upstream README.md не совпал: $packageReadmeHash"
}

foreach ($sourceArchive in @(
    @{ Name = [string]$provenance.sourceIdentification.ffmpegSourceArchive; Url = [string]$provenance.sourceIdentification.ffmpegSourceArchiveUrl; Sha256 = [string]$provenance.sourceIdentification.ffmpegSourceArchiveSha256 },
    @{ Name = [string]$provenance.sourceIdentification.buildScriptsArchive; Url = [string]$provenance.sourceIdentification.buildScriptsArchiveUrl; Sha256 = [string]$provenance.sourceIdentification.buildScriptsArchiveSha256 }
)) {
    $sourcePath = Join-Path $releaseInputs $sourceArchive.Name
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf) -or (Get-Sha256 -LiteralPath $sourcePath) -ne $sourceArchive.Sha256) {
        Invoke-WebRequest -Uri $sourceArchive.Url -OutFile $sourcePath
    }
    $sourceHash = Get-Sha256 -LiteralPath $sourcePath
    if ($sourceHash -ne $sourceArchive.Sha256) {
        throw "SHA-256 source archive не совпал: $($sourceArchive.Name): $sourceHash"
    }
}

& (Join-Path $PSScriptRoot 'Test-ReleaseInputs.ps1') -Mode Candidate -ReportPath 'artifacts\release\reports\candidate-after-media-tools.json'
