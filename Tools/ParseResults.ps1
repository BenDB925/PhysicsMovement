param(
    [string]$ProjectPath = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,
    [string]$XmlPath,
    [string]$LogPath,
    [string]$OutputPath,
    [string]$TestFilter,
    [switch]$NoWriteSummary
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($XmlPath)) {
    $XmlPath = Join-Path $ProjectPath "TestResults\PlayMode.xml"
}

$summaryScriptPath = Join-Path $PSScriptRoot "Write-TestSummary.ps1"
$summaryArgs = @{
    ProjectPath = $ProjectPath
    XmlPaths    = @($XmlPath)
    PassThru    = $true
}

if (-not [string]::IsNullOrWhiteSpace($LogPath)) {
    $summaryArgs.LogPaths = @($LogPath)
}

if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $summaryArgs.OutputPath = $OutputPath
}

if (-not [string]::IsNullOrWhiteSpace($TestFilter)) {
    $summaryArgs.TestFilter = $TestFilter
}

if ($NoWriteSummary) {
    $summaryArgs.NoWriteFile = $true
}

$summary = & $summaryScriptPath @summaryArgs
$platformSummary = $summary.PlatformSummaries | Select-Object -First 1

Write-Host ("Result=" + $platformSummary.Result +
    " Total=" + $platformSummary.Total +
    " Passed=" + $platformSummary.Passed +
    " Failed=" + $platformSummary.Failed +
    " Skipped=" + $platformSummary.Skipped)

if (-not $NoWriteSummary -and $summary.RelativeOutputPath) {
    Write-Host ("SUMMARY: " + $summary.RelativeOutputPath)
}

foreach ($failure in $platformSummary.Failures) {
    $displayName = if ([string]::IsNullOrWhiteSpace($failure.FullName)) {
        $failure.Name
    }
    else {
        $failure.FullName
    }

    Write-Host ("FAIL: " + $displayName + " [" + $failure.Classification + "]")
    Write-Host ("  MSG: " + $failure.MessageSnippet)
}
