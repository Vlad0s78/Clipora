[CmdletBinding()]
param(
    [string]$OutputDirectory,
    [switch]$DryRun,
    [switch]$QaCandidate
)

. (Join-Path $PSScriptRoot 'Common.ps1')

$repositoryRoot = Get-CliporaRepositoryRoot
$versionInfo = Get-CliporaVersionInfo -RepositoryRoot $repositoryRoot
$Version = [string]$versionInfo.Version
$BuildNumber = [int]$versionInfo.BuildNumber
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot 'artifacts\release'
}
elseif (-not [System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot $OutputDirectory
}
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
Assert-SafeBuildDirectory -RepositoryRoot $repositoryRoot -LiteralPath $OutputDirectory

$reportsDirectory = Join-Path $OutputDirectory 'reports'
New-Item -ItemType Directory -Path $reportsDirectory -Force | Out-Null
$gateReportPath = Join-Path $reportsDirectory 'release-gates.json'

$portableName = 'clipora-win64-portable.zip'
$setupName = 'clipora-win64-setup.exe'
$publishDirectory = Join-Path $OutputDirectory 'staging\publish'
$portableDirectory = Join-Path $OutputDirectory 'staging\portable'
$sourceDateEpoch = if ($env:SOURCE_DATE_EPOCH) { [long]$env:SOURCE_DATE_EPOCH } else { 946684800L }
$informationalVersion = "$Version+build.$BuildNumber"
$fileVersion = "$Version.$BuildNumber"

$publishArguments = @(
    'publish',
    'src\Clipora.App\Clipora.App.csproj',
    '--configuration', 'Release',
    '--runtime', 'win-x64',
    '--self-contained', 'true',
    '--no-restore',
    '--output', $publishDirectory,
    '-p:Platform=x64',
    '-p:IncludeBundledMediaTools=true',
    '-p:WindowsAppSDKSelfContained=true',
    '-p:Deterministic=true',
    '-p:ContinuousIntegrationBuild=true',
    "-p:PathMap=$repositoryRoot=/_/Clipora",
    '-p:DebugSymbols=false',
    '-p:DebugType=None',
    '-p:PublishReadyToRun=false',
    '-p:PublishSingleFile=false',
    "-p:Version=$Version",
    "-p:VersionPrefix=$Version",
    "-p:CliporaBuild=$BuildNumber",
    "-p:FileVersion=$fileVersion",
    "-p:InformationalVersion=$informationalVersion"
)

$plan = [ordered]@{
    schemaVersion = 1
    dryRun = [bool]$DryRun
    qaCandidate = [bool]$QaCandidate
    version = $Version
    build = $BuildNumber
    runtimeIdentifier = 'win-x64'
    selfContained = $true
    sourceDateEpoch = $sourceDateEpoch
    publish = [ordered]@{
        executable = 'dotnet'
        arguments = $publishArguments
        outputDirectory = $publishDirectory
    }
    portableStagingDirectory = $portableDirectory
    portable = Join-Path $OutputDirectory $portableName
    setup = Join-Path $OutputDirectory $setupName
    gateReport = $gateReportPath
}

if ($DryRun) {
    & (Join-Path $PSScriptRoot 'Test-ReleaseInputs.ps1') -Mode Diagnostic -ReportPath $gateReportPath | Out-Null
    Write-JsonFile -Value $plan -LiteralPath (Join-Path $reportsDirectory 'release-plan.json')
    $plan | ConvertTo-Json -Depth 100
    return
}

# Сначала policy gate: при незакрытой лицензии/подписи/приёмке сеть и компиляция не запускаются.
& (Join-Path $PSScriptRoot 'Test-ReleaseInputs.ps1') -Mode Policy -ReportPath $gateReportPath | Out-Null
# QA candidate разрывает только цикл clean-VM acceptance; legal/signing/integrity gates остаются обязательными.
$packageGateMode = if ($QaCandidate) { 'Candidate' } else { 'Distribution' }
& (Join-Path $PSScriptRoot 'Test-ReleaseInputs.ps1') -Mode $packageGateMode -ReportPath $gateReportPath | Out-Null

$dotnet = (Get-Command 'dotnet.exe' -ErrorAction Stop).Source
$iscc = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
if ($null -eq $iscc) {
    $defaultIscc = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
    if (Test-Path -LiteralPath $defaultIscc -PathType Leaf) {
        $iscc = Get-Command -Name $defaultIscc -ErrorAction Stop
    }
    else {
        throw 'Inno Setup 6 (ISCC.exe) не найден.'
    }
}

$stagingRoot = Join-Path $OutputDirectory 'staging'
Assert-SafeBuildDirectory -RepositoryRoot $repositoryRoot -LiteralPath $stagingRoot
if (Test-Path -LiteralPath $stagingRoot) {
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

Push-Location $repositoryRoot
try {
    Invoke-NativeChecked -FilePath $dotnet -Arguments $publishArguments
}
finally {
    Pop-Location
}

$requiredPublishFiles = @(
    'Clipora.exe',
    'Tools\ffmpeg\ffmpeg.exe',
    'Tools\ffmpeg\ffprobe.exe'
)
foreach ($relativePath in $requiredPublishFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory $relativePath) -PathType Leaf)) {
        throw "Publish не содержит обязательный файл: $relativePath"
    }
}

$licensesDirectory = Join-Path $publishDirectory 'licenses'
New-Item -ItemType Directory -Path $licensesDirectory -Force | Out-Null
$legalFiles = [ordered]@{
    'Clipora-LICENSE.txt' = 'LICENSE'
    'THIRD_PARTY_NOTICES.md' = 'THIRD_PARTY_NOTICES.md'
    'FFmpeg-LGPL.txt' = 'ffmpeg\LICENSE'
    'FFmpeg-PACKAGE-README.txt' = 'artifacts\release-inputs\ffmpeg-package-README.txt'
    'FFmpeg-PROVENANCE.json' = 'ffmpeg\PROVENANCE.json'
    'FFmpeg-SOURCE.md' = 'ffmpeg\SOURCE.md'
    'LICENSE-INVENTORY.md' = 'src\Clipora.Packaging\evidence\license-inventory.md'
    'UNSIGNED-ALPHA-POLICY.md' = 'src\Clipora.Packaging\evidence\unsigned-alpha-policy.md'
    'PACKAGE-ACCEPTANCE.md' = 'src\Clipora.Packaging\evidence\local-package-acceptance.md'
    'RELEASE-POLICY.json' = 'src\Clipora.Packaging\release-policy.json'
}
foreach ($entry in $legalFiles.GetEnumerator()) {
    Copy-Item -LiteralPath (Join-Path $repositoryRoot $entry.Value) -Destination (Join-Path $licensesDirectory $entry.Key)
}

if ($QaCandidate) {
    [System.IO.File]::WriteAllText(
        (Join-Path $publishDirectory 'NOT-FOR-DISTRIBUTION.txt'),
        "QA candidate. Not for distribution.`r`n",
        [System.Text.UTF8Encoding]::new($false))
}

& (Join-Path $PSScriptRoot 'Test-PublishPayload.ps1') `
    -PublishDirectory $publishDirectory `
    -ReportPath (Join-Path $reportsDirectory 'publish-payload.json') | Out-Null

function Write-PayloadManifest {
    param([Parameter(Mandatory)][string]$PayloadDirectory)

    $manifestPath = Join-Path $PayloadDirectory 'manifest.sha256'
    if (Test-Path -LiteralPath $manifestPath) {
        Remove-Item -LiteralPath $manifestPath -Force
    }
    $manifestLines = @(Get-ChildItem -LiteralPath $PayloadDirectory -File -Recurse |
        Sort-Object { $_.FullName.Substring($PayloadDirectory.Length + 1) } |
        ForEach-Object {
            $relativePath = $_.FullName.Substring($PayloadDirectory.Length + 1).Replace('\', '/')
            "$(Get-Sha256 -LiteralPath $_.FullName) *$relativePath"
        })
    [System.IO.File]::WriteAllLines($manifestPath, $manifestLines, [System.Text.UTF8Encoding]::new($false))
}

Write-PayloadManifest -PayloadDirectory $publishDirectory

# Setup использует чистый publish без portable marker; marker существует только в ZIP.
Copy-Item -LiteralPath $publishDirectory -Destination $portableDirectory -Recurse
$portableDataDirectory = Join-Path $portableDirectory 'portable-data'
New-Item -ItemType Directory -Path $portableDataDirectory -Force | Out-Null
[System.IO.File]::WriteAllText(
    (Join-Path $portableDataDirectory '.clipora-portable'),
    "Clipora portable data root v1`n",
    [System.Text.UTF8Encoding]::new($false))
& (Join-Path $PSScriptRoot 'Test-PublishPayload.ps1') `
    -PublishDirectory $portableDirectory `
    -ReportPath (Join-Path $reportsDirectory 'portable-payload.json') | Out-Null
Write-PayloadManifest -PayloadDirectory $portableDirectory

$portablePath = Join-Path $OutputDirectory $portableName
& (Join-Path $PSScriptRoot 'New-DeterministicZip.ps1') `
    -InputDirectory $portableDirectory `
    -OutputPath $portablePath `
    -RootDirectoryName "Clipora-$Version-win-x64" `
    -SourceDateEpoch $sourceDateEpoch | Out-Null

$issPath = Join-Path $repositoryRoot 'src\Clipora.Packaging\Clipora.iss'
$isccArguments = @(
    "/DAppVersion=$Version",
    "/DBuildNumber=$BuildNumber",
    "/DSourceDir=$publishDirectory",
    "/DOutputDir=$OutputDirectory",
    "/DOutputBaseName=$([System.IO.Path]::GetFileNameWithoutExtension($setupName))",
    $issPath
)
Invoke-NativeChecked -FilePath $iscc.Source -Arguments $isccArguments

$setupPath = Join-Path $OutputDirectory $setupName
if (-not (Test-Path -LiteralPath $setupPath -PathType Leaf)) {
    throw "Inno Setup не создал ожидаемый файл: $setupPath"
}

$sourceArchives = @(
    'ffmpeg-source-9b6c8969e05b4f0b29f0f85cd501be6b3e582e6b.zip',
    'ffmpeg-build-scripts-2437e7b868da3c11872367b15f3c613b87c24819.zip'
)
$sourceAssets = @($sourceArchives | ForEach-Object {
    $source = Join-Path $repositoryRoot "artifacts\release-inputs\$_"
    $destination = Join-Path $OutputDirectory $_
    Copy-Item -LiteralPath $source -Destination $destination -Force
    $destination
})
$assets = @($portablePath, $setupPath) + $sourceAssets
$checksums = @($assets | Sort-Object | ForEach-Object { "$(Get-Sha256 -LiteralPath $_) *$(Split-Path -Leaf $_)" })
[System.IO.File]::WriteAllLines((Join-Path $OutputDirectory 'SHA256SUMS.txt'), $checksums, [System.Text.UTF8Encoding]::new($false))

$releaseManifest = [ordered]@{
    schemaVersion = 1
    version = $Version
    build = $BuildNumber
    runtimeIdentifier = 'win-x64'
    selfContained = $true
    channel = if ($QaCandidate) { 'qa-candidate' } else { 'release' }
    sourceDateEpoch = $sourceDateEpoch
    assets = @($assets | Sort-Object | ForEach-Object {
        $item = Get-Item -LiteralPath $_
        [ordered]@{ name = $item.Name; size = $item.Length; sha256 = Get-Sha256 -LiteralPath $item.FullName }
    })
}
Write-JsonFile -Value $releaseManifest -LiteralPath (Join-Path $OutputDirectory 'release-manifest.json')
$releaseManifest | ConvertTo-Json -Depth 100
