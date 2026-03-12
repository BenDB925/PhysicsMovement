param(
    [string]$ProjectPath = $PSScriptRoot,
    [string]$XmlPath,
    [string]$LogPath,
    [string]$OutputPath,
    [string]$TestFilter,
    [switch]$NoWriteSummary
)

$scriptPath = Join-Path $PSScriptRoot "Tools\ParseResults.ps1"
& $scriptPath `
    -ProjectPath $ProjectPath `
    -XmlPath $XmlPath `
    -LogPath $LogPath `
    -OutputPath $OutputPath `
    -TestFilter $TestFilter `
    -NoWriteSummary:$NoWriteSummary
