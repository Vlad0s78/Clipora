[CmdletBinding()]
param(
    [ValidateSet('Diagnostic', 'Policy', 'Candidate', 'Distribution')]
    [string]$Mode = 'Diagnostic',

    [string]$ReportPath
)

. (Join-Path $PSScriptRoot 'Common.ps1')

$repositoryRoot = Get-CliporaRepositoryRoot
$provenancePath = Join-Path $repositoryRoot 'ffmpeg\PROVENANCE.json'
$policyPath = Join-Path $repositoryRoot 'src\Clipora.Packaging\release-policy.json'
$findings = [System.Collections.Generic.List[object]]::new()

function Add-Finding {
    param(
        [Parameter(Mandatory)][string]$Id,
        [Parameter(Mandatory)][bool]$Passed,
        [Parameter(Mandatory)][string]$Message,
        [ValidateSet('policy', 'distribution', 'acceptance')]
        [string]$Stage = 'policy'
    )

    $findings.Add([ordered]@{
        id = $Id
        stage = $Stage
        passed = $Passed
        severity = if ($Passed) { 'info' } else { 'error' }
        message = $Message
    })
}

function Test-Evidence {
    param(
        [Parameter(Mandatory)][string]$Id,
        [Parameter(Mandatory)][bool]$Complete,
        $EvidencePath,
        [Parameter(Mandatory)][string]$Description,
        [ValidateSet('policy', 'distribution', 'acceptance')]
        [string]$Stage = 'policy'
    )

    $hasEvidence = -not [string]::IsNullOrWhiteSpace([string]$EvidencePath)
    $evidenceExists = $hasEvidence -and (Test-Path -LiteralPath (Join-Path $repositoryRoot ([string]$EvidencePath)) -PathType Leaf)
    Add-Finding -Id $Id -Passed ($Complete -and $evidenceExists) -Message "$Description Complete=$Complete; evidence=$EvidencePath" -Stage $Stage
}

$requiredFiles = @(
    'LICENSE',
    'THIRD_PARTY_NOTICES.md',
    'ffmpeg\LICENSE',
    'ffmpeg\PROVENANCE.json',
    'ffmpeg\SOURCE.md',
    'src\Clipora.Packaging\release-policy.json'
)

foreach ($relativePath in $requiredFiles) {
    $exists = Test-Path -LiteralPath (Join-Path $repositoryRoot $relativePath) -PathType Leaf
    Add-Finding -Id "required-file:$relativePath" -Passed $exists -Message "Обязательный файл: $relativePath"
}

$provenance = $null
$policy = $null
try {
    $provenance = Read-JsonFile -LiteralPath $provenancePath
    Add-Finding -Id 'provenance-json' -Passed $true -Message 'PROVENANCE.json корректен.'
}
catch {
    Add-Finding -Id 'provenance-json' -Passed $false -Message "Ошибка PROVENANCE.json: $($_.Exception.Message)"
}

try {
    $policy = Read-JsonFile -LiteralPath $policyPath
    Add-Finding -Id 'release-policy-json' -Passed $true -Message 'release-policy.json корректен.'
}
catch {
    Add-Finding -Id 'release-policy-json' -Passed $false -Message "Ошибка release-policy.json: $($_.Exception.Message)"
}

if ($null -ne $provenance) {
    Add-Finding -Id 'provenance-schema' -Passed ($provenance.schemaVersion -eq 2) -Message "schemaVersion=$($provenance.schemaVersion)"
    Add-Finding -Id 'ffmpeg-architecture' -Passed ($provenance.package.architecture -eq 'x86-64') -Message "architecture=$($provenance.package.architecture)"
    Add-Finding -Id 'ffmpeg-license-expression' -Passed ($provenance.package.licenseExpression -eq 'LGPL-3.0-or-later') -Message "license=$($provenance.package.licenseExpression)"

    $licensePath = Join-Path $repositoryRoot 'ffmpeg\LICENSE'
    if (Test-Path -LiteralPath $licensePath -PathType Leaf) {
        $actualLicenseHash = Get-Sha256 -LiteralPath $licensePath
        Add-Finding -Id 'ffmpeg-license-sha256' -Passed ($actualLicenseHash -eq [string]$provenance.package.licenseSha256) -Message "LGPL SHA-256=$actualLicenseHash"
    }

    $sourceComplete = [bool]$provenance.distributionReview.correspondingSourceComplete
    Add-Finding -Id 'corresponding-source-complete' -Passed $sourceComplete -Message "Corresponding Source complete=$sourceComplete"

    if ($Mode -in @('Candidate', 'Distribution')) {
        $expectedNames = @('ffmpeg.exe', 'ffprobe.exe')
        $declaredNames = @($provenance.binaries | ForEach-Object { [string]$_.name } | Sort-Object)
        Add-Finding -Id 'binary-set' -Passed (@(Compare-Object ($expectedNames | Sort-Object) $declaredNames).Count -eq 0) -Message "Binaries=$($declaredNames -join ', ')" -Stage distribution

        foreach ($binary in @($provenance.binaries)) {
            $binaryPath = Join-Path $repositoryRoot ([string]$binary.repositoryInputPath)
            $exists = Test-Path -LiteralPath $binaryPath -PathType Leaf
            Add-Finding -Id "binary-exists:$($binary.name)" -Passed $exists -Message "Файл: $($binary.repositoryInputPath)" -Stage distribution
            if ($exists) {
                $item = Get-Item -LiteralPath $binaryPath
                $actualHash = Get-Sha256 -LiteralPath $binaryPath
                Add-Finding -Id "binary-size:$($binary.name)" -Passed ($item.Length -eq [long]$binary.size) -Message "size=$($item.Length); expected=$($binary.size)" -Stage distribution
                Add-Finding -Id "binary-sha256:$($binary.name)" -Passed ($actualHash -eq [string]$binary.sha256) -Message "SHA-256=$actualHash" -Stage distribution
            }
        }

        $packageReadmePath = Join-Path $repositoryRoot 'artifacts\release-inputs\ffmpeg-package-README.txt'
        $packageReadmeExists = Test-Path -LiteralPath $packageReadmePath -PathType Leaf
        Add-Finding -Id 'ffmpeg-package-readme-exists' -Passed $packageReadmeExists -Message 'Точный README.txt из upstream FFmpeg-архива.' -Stage distribution
        if ($packageReadmeExists) {
            $actualPackageReadmeHash = Get-Sha256 -LiteralPath $packageReadmePath
            Add-Finding -Id 'ffmpeg-package-readme-sha256' -Passed ($actualPackageReadmeHash -eq [string]$provenance.package.providerReadmeSha256) -Message "Upstream README SHA-256=$actualPackageReadmeHash" -Stage distribution
        }

        foreach ($sourceArchive in @(
            @{ Name = [string]$provenance.sourceIdentification.ffmpegSourceArchive; Sha256 = [string]$provenance.sourceIdentification.ffmpegSourceArchiveSha256 },
            @{ Name = [string]$provenance.sourceIdentification.buildScriptsArchive; Sha256 = [string]$provenance.sourceIdentification.buildScriptsArchiveSha256 }
        )) {
            $sourcePath = Join-Path $repositoryRoot "artifacts\release-inputs\$($sourceArchive.Name)"
            $sourceExists = Test-Path -LiteralPath $sourcePath -PathType Leaf
            Add-Finding -Id "source-archive-exists:$($sourceArchive.Name)" -Passed $sourceExists -Message "Source archive: $($sourceArchive.Name)" -Stage distribution
            if ($sourceExists) {
                $sourceHash = Get-Sha256 -LiteralPath $sourcePath
                Add-Finding -Id "source-archive-sha256:$($sourceArchive.Name)" -Passed ($sourceHash -eq $sourceArchive.Sha256) -Message "SHA-256=$sourceHash" -Stage distribution
            }
        }
    }
}

if ($null -ne $policy) {
    Add-Finding -Id 'policy-schema' -Passed ($policy.schemaVersion -eq 1) -Message "policy schemaVersion=$($policy.schemaVersion)"
    Add-Finding -Id 'policy-rid' -Passed ($policy.target.runtimeIdentifier -eq 'win-x64') -Message "RID=$($policy.target.runtimeIdentifier)"
    Test-Evidence -Id 'license-inventory-complete' -Complete ([bool]$policy.licenseInventory.complete) -EvidencePath $policy.licenseInventory.evidencePath -Description 'Инвентаризация лицензий.'
    Test-Evidence -Id 'signing-review-complete' -Complete ([bool]$policy.signing.reviewComplete) -EvidencePath $policy.signing.evidencePath -Description 'Политика подписи.'
    Test-Evidence -Id 'clean-windows-acceptance-complete' -Complete ([bool]$policy.cleanWindowsAcceptance.complete) -EvidencePath $policy.cleanWindowsAcceptance.evidencePath -Description 'Приёмка на чистой Windows.' -Stage acceptance
    Test-Evidence -Id 'portable-data-isolation-complete' -Complete ([bool]$policy.portableDataIsolation.complete) -EvidencePath $policy.portableDataIsolation.evidencePath -Description 'Изоляция Portable-данных.'

    $isccCommand = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    $programFilesX86 = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)
    $defaultIscc = if ($programFilesX86) { Join-Path $programFilesX86 'Inno Setup 6\ISCC.exe' } else { '' }
    $isccAvailable = $null -ne $isccCommand -or ($defaultIscc -and (Test-Path -LiteralPath $defaultIscc -PathType Leaf))
    Add-Finding -Id 'inno-setup-available' -Passed $isccAvailable -Message "ISCC.exe available=$isccAvailable"
}

$failed = @($findings | Where-Object { -not $_.passed })
$enforcedStages = switch ($Mode) {
    'Policy' { @('policy') }
    'Candidate' { @('policy', 'distribution') }
    'Distribution' { @('policy', 'distribution', 'acceptance') }
    default { @('policy', 'distribution', 'acceptance') }
}
$enforcedFailed = @($failed | Where-Object { $enforcedStages -contains $_.stage })
$report = [ordered]@{
    schemaVersion = 1
    mode = $Mode
    releaseReady = ($failed.Count -eq 0)
    failedCount = $failed.Count
    enforcedFailedCount = $enforcedFailed.Count
    findings = @($findings)
}

if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $ReportPath = Join-Path $repositoryRoot 'artifacts\release\reports\release-gates.json'
}
elseif (-not [System.IO.Path]::IsPathRooted($ReportPath)) {
    $ReportPath = Join-Path $repositoryRoot $ReportPath
}

Write-JsonFile -Value $report -LiteralPath $ReportPath
$report | ConvertTo-Json -Depth 100

if ($Mode -ne 'Diagnostic' -and $enforcedFailed.Count -gt 0) {
    throw "Release gate '$Mode' заблокирован: $($enforcedFailed.Count) обязательных проверок не пройдено. Отчёт: $ReportPath"
}
