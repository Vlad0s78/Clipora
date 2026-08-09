Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-CliporaRepositoryRoot {
    return (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
}

function Get-Sha256 {
    param([Parameter(Mandatory)][string]$LiteralPath)

    return (Get-FileHash -LiteralPath $LiteralPath -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Read-JsonFile {
    param([Parameter(Mandatory)][string]$LiteralPath)

    return Get-Content -LiteralPath $LiteralPath -Raw -Encoding UTF8 | ConvertFrom-Json -Depth 100
}

function Write-JsonFile {
    param(
        [Parameter(Mandatory)]$Value,
        [Parameter(Mandatory)][string]$LiteralPath
    )

    $parent = Split-Path -Parent $LiteralPath
    if ($parent) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }

    $json = $Value | ConvertTo-Json -Depth 100
    [System.IO.File]::WriteAllText($LiteralPath, "$json`n", [System.Text.UTF8Encoding]::new($false))
}

function Test-SemanticVersion {
    param([Parameter(Mandatory)][string]$Version)

    return $Version -match '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z.-]+)?$'
}

function Get-CliporaVersionInfo {
    param([Parameter(Mandatory)][string]$RepositoryRoot)

    $propsPath = Join-Path $RepositoryRoot 'Directory.Build.props'
    [xml]$props = Get-Content -LiteralPath $propsPath -Raw -Encoding UTF8
    $version = [string]$props.Project.PropertyGroup.VersionPrefix
    $buildText = [string]$props.Project.PropertyGroup.CliporaBuild
    if ($version -notmatch '^\d+\.\d+\.\d+$') {
        throw "Directory.Build.props: VersionPrefix для Setup должен содержать ровно три числовых компонента без prerelease: $version"
    }

    $versionParts = @($version.Split('.') | ForEach-Object { [int]$_ })
    if (@($versionParts | Where-Object { $_ -gt 65535 }).Count -gt 0) {
        throw "Directory.Build.props: компоненты VersionPrefix должны быть <= 65535: $version"
    }

    $build = 0
    if (-not [int]::TryParse($buildText, [ref]$build) -or $build -lt 1 -or $build -gt 65535) {
        throw "Directory.Build.props содержит недопустимый CliporaBuild: $buildText"
    }

    return [ordered]@{ Version = $version; BuildNumber = $build }
}

function Assert-SafeBuildDirectory {
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$LiteralPath
    )

    $root = [System.IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\')
    $candidate = [System.IO.Path]::GetFullPath($LiteralPath).TrimEnd('\')
    $artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $root 'artifacts')).TrimEnd('\')

    if (-not $candidate.StartsWith("$artifactsRoot\", [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Каталог сборки должен находиться внутри '$artifactsRoot': $candidate"
    }

    if ($candidate -eq $artifactsRoot -or $candidate -eq $root) {
        throw "Небезопасный каталог сборки: $candidate"
    }
}

function Invoke-NativeChecked {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Команда завершилась с кодом ${LASTEXITCODE}: $FilePath $($Arguments -join ' ')"
    }
}
