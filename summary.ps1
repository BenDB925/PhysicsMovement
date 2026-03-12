param(
    [string]$ProjectPath = $PSScriptRoot,
    [ValidateSet("Auto", "All", "EditMode", "PlayMode")]
    [string]$Platform = "Auto",
    [string[]]$XmlPaths,
    [string[]]$LogPaths,
    [string]$OutputPath,
    [string]$TestFilter,
    [switch]$PassThru,
    [switch]$NoWriteFile
)

$scriptPath = Join-Path $PSScriptRoot "Tools\Write-TestSummary.ps1"
& $scriptPath `
    -ProjectPath $ProjectPath `
    -Platform $Platform `
    -XmlPaths $XmlPaths `
    -LogPaths $LogPaths `
    -OutputPath $OutputPath `
    -TestFilter $TestFilter `
    -PassThru:$PassThru `
    -NoWriteFile:$NoWriteFile
