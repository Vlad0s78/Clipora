[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PublishDirectory,
    [string]$ReportPath
)

. (Join-Path $PSScriptRoot 'Common.ps1')

$publishRoot = (Resolve-Path -LiteralPath $PublishDirectory).Path.TrimEnd('\')
$forbiddenExtensions = @('.mp4', '.mkv', '.mov', '.avi', '.webm', '.wmv', '.flv', '.log', '.pdb', '.tmp', '.bak')
$forbiddenNames = @('settings.json', 'appsettings.Development.json')
$forbiddenSegments = @('logs', 'cache', 'TestResults', '.vs', 'obj')
$violations = [System.Collections.Generic.List[object]]::new()

foreach ($item in Get-ChildItem -LiteralPath $publishRoot -File -Recurse) {
    $relativePath = $item.FullName.Substring($publishRoot.Length + 1).Replace('\', '/')
    $segments = $relativePath.Split('/')
    $reason = $null
    if ($forbiddenExtensions -contains $item.Extension.ToLowerInvariant()) {
        $reason = "forbidden-extension:$($item.Extension.ToLowerInvariant())"
    }
    elseif ($forbiddenNames -contains $item.Name) {
        $reason = "forbidden-name:$($item.Name)"
    }
    elseif (@($segments | Where-Object { $forbiddenSegments -contains $_ }).Count -gt 0) {
        $reason = 'forbidden-directory'
    }

    if ($null -ne $reason) {
        $violations.Add([ordered]@{ path = $relativePath; reason = $reason })
    }
}

$report = [ordered]@{
    schemaVersion = 1
    publishDirectory = $publishRoot
    passed = ($violations.Count -eq 0)
    violationCount = $violations.Count
    violations = @($violations)
}

if (-not [string]::IsNullOrWhiteSpace($ReportPath)) {
    Write-JsonFile -Value $report -LiteralPath $ReportPath
}
$report | ConvertTo-Json -Depth 20

if ($violations.Count -gt 0) {
    throw "Publish payload содержит $($violations.Count) запрещённых файлов."
}
