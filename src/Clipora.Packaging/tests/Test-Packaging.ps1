[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$packagingRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$repositoryRoot = (Resolve-Path (Join-Path $packagingRoot '..\..')).Path
$scriptsRoot = Join-Path $packagingRoot 'scripts'
$failures = [System.Collections.Generic.List[string]]::new()

function Assert-True {
    param([Parameter(Mandatory)][bool]$Condition, [Parameter(Mandatory)][string]$Message)
    if (-not $Condition) {
        $failures.Add($Message)
    }
}

foreach ($script in Get-ChildItem -LiteralPath $scriptsRoot -Filter '*.ps1' -File) {
    $tokens = $null
    $errors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($script.FullName, [ref]$tokens, [ref]$errors) | Out-Null
    Assert-True -Condition ($errors.Count -eq 0) -Message "PowerShell parse: $($script.Name): $($errors -join '; ')"
}

$policy = Get-Content -LiteralPath (Join-Path $packagingRoot 'release-policy.json') -Raw -Encoding UTF8 | ConvertFrom-Json -Depth 100
Assert-True -Condition ($policy.schemaVersion -eq 1) -Message 'release-policy schemaVersion должен быть 1.'
Assert-True -Condition ([bool]$policy.licenseInventory.complete) -Message 'License inventory должен быть завершён для pinned LGPL package.'
Assert-True -Condition ([bool]$policy.signing.reviewComplete) -Message 'Signing policy должна быть явно утверждена.'
Assert-True -Condition ([bool]$policy.cleanWindowsAcceptance.complete) -Message 'Package acceptance должна быть завершена.'
Assert-True -Condition ([bool]$policy.portableDataIsolation.complete) -Message 'portableDataIsolation.complete должен быть подтверждён после реализации.'
$portableEvidencePath = Join-Path $repositoryRoot ([string]$policy.portableDataIsolation.evidencePath)
Assert-True -Condition (Test-Path -LiteralPath $portableEvidencePath -PathType Leaf) -Message 'Не найдено evidence изоляции Portable-данных.'

$iss = Get-Content -LiteralPath (Join-Path $packagingRoot 'Clipora.iss') -Raw -Encoding UTF8
foreach ($requiredPattern in @(
    'ArchitecturesAllowed=x64compatible',
    'PrivilegesRequired=lowest',
    'DefaultDirName={localappdata}\Programs\{#AppName}',
    'LicenseFile=',
    'OutputBaseFilename={#OutputBaseName}',
    'UninstallDisplayIcon=',
    'Root: HKCU',
    'Clipora.Open',
    'Clipora.Compress',
    'Flags: uninsdeletekey',
    'procedure CurUninstallStepChanged',
    'RegDeleteKeyIncludingSubkeys(HKCU',
    'recursesubdirs'
)) {
    Assert-True -Condition $iss.Contains($requiredPattern, [System.StringComparison]::Ordinal) -Message "Clipora.iss: отсутствует $requiredPattern"
}

$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) "clipora-packaging-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $temporaryRoot -Force | Out-Null
try {
    $fixture = Join-Path $temporaryRoot 'fixture'
    New-Item -ItemType Directory -Path (Join-Path $fixture 'nested') -Force | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $fixture 'a.txt'), 'alpha', [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText((Join-Path $fixture 'nested\b.txt'), 'beta', [System.Text.UTF8Encoding]::new($false))
    (Get-Item -LiteralPath (Join-Path $fixture 'a.txt')).LastWriteTimeUtc = [datetime]'2026-01-01T00:00:00Z'
    (Get-Item -LiteralPath (Join-Path $fixture 'nested\b.txt')).LastWriteTimeUtc = [datetime]'2025-01-01T00:00:00Z'

    $zipOne = Join-Path $temporaryRoot 'one.zip'
    $zipTwo = Join-Path $temporaryRoot 'two.zip'
    & (Join-Path $scriptsRoot 'New-DeterministicZip.ps1') -InputDirectory $fixture -OutputPath $zipOne -RootDirectoryName 'Clipora-fixture' | Out-Null
    (Get-Item -LiteralPath (Join-Path $fixture 'a.txt')).LastWriteTimeUtc = [datetime]'2024-01-01T00:00:00Z'
    & (Join-Path $scriptsRoot 'New-DeterministicZip.ps1') -InputDirectory $fixture -OutputPath $zipTwo -RootDirectoryName 'Clipora-fixture' | Out-Null
    $zipOneHash = (Get-FileHash -LiteralPath $zipOne -Algorithm SHA256).Hash
    $zipTwoHash = (Get-FileHash -LiteralPath $zipTwo -Algorithm SHA256).Hash
    Assert-True -Condition ($zipOneHash -eq $zipTwoHash) -Message "Детерминированный ZIP различается: $zipOneHash != $zipTwoHash"

    $dryRunOutput = Join-Path $repositoryRoot 'artifacts\packaging-test-dry-run'
    & (Join-Path $scriptsRoot 'Build-Packages.ps1') -OutputDirectory $dryRunOutput -DryRun | Out-Null
    $gateReport = Get-Content -LiteralPath (Join-Path $dryRunOutput 'reports\release-gates.json') -Raw -Encoding UTF8 | ConvertFrom-Json -Depth 100
    $planReport = Get-Content -LiteralPath (Join-Path $dryRunOutput 'reports\release-plan.json') -Raw -Encoding UTF8 | ConvertFrom-Json -Depth 100
    [xml]$directoryProps = Get-Content -LiteralPath (Join-Path $repositoryRoot 'Directory.Build.props') -Raw -Encoding UTF8
    Assert-True -Condition ($planReport.version -eq [string]$directoryProps.Project.PropertyGroup.VersionPrefix) -Message 'Dry-run version расходится с Directory.Build.props.'
    Assert-True -Condition ($planReport.build -eq [int]$directoryProps.Project.PropertyGroup.CliporaBuild) -Message 'Dry-run build расходится с Directory.Build.props.'
    Assert-True -Condition ([bool]$gateReport.releaseReady) -Message 'Diagnostic gate должен подтверждать готовность release inputs.'
    Assert-True -Condition (@($gateReport.findings | Where-Object { $_.id -eq 'corresponding-source-complete' -and $_.passed }).Count -eq 1) -Message 'Corresponding source должен быть подтверждён.'
    $versionInfo = & {
        . (Join-Path $scriptsRoot 'Common.ps1')
        Get-CliporaVersionInfo -RepositoryRoot $repositoryRoot
    }
Assert-True -Condition (-not (Test-Path -LiteralPath (Join-Path $dryRunOutput 'clipora-win64-portable.zip'))) -Message 'Dry-run создал Portable ZIP.'
Assert-True -Condition (-not (Test-Path -LiteralPath (Join-Path $dryRunOutput 'clipora-win64-setup.exe'))) -Message 'Dry-run создал Setup EXE.'

    $policyPassed = $true
    try {
        & (Join-Path $scriptsRoot 'Test-ReleaseInputs.ps1') -Mode Policy -ReportPath (Join-Path $temporaryRoot 'policy.json') | Out-Null
    }
    catch {
        $policyPassed = $false
    }
    Assert-True -Condition $policyPassed -Message 'Policy gate должен принимать LGPL/signing evidence.'

    $candidatePassed = $true
    try {
        & (Join-Path $scriptsRoot 'Test-ReleaseInputs.ps1') -Mode Candidate -ReportPath (Join-Path $temporaryRoot 'candidate.json') | Out-Null
    }
    catch {
        $candidatePassed = $false
    }
    Assert-True -Condition $candidatePassed -Message 'Candidate gate должен принимать проверенные legal/signing/integrity inputs.'
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}

if ($failures.Count -gt 0) {
    throw "Packaging tests failed ($($failures.Count)):`n- $($failures -join "`n- ")"
}

Write-Host 'Packaging tests: PASS'
