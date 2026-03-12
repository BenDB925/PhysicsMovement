param(
    [string]$ProjectPath = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,
    [ValidateSet("Auto", "All", "EditMode", "PlayMode")]
    [string]$Platform = "Auto",
    [string[]]$XmlPaths,
    [string[]]$LogPaths,
    [string]$OutputPath,
    [string]$TestFilter,
    [switch]$PassThru,
    [switch]$NoWriteFile
)

$ErrorActionPreference = "Stop"

$resolvedProjectPath = (Resolve-Path $ProjectPath).Path

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $resolvedProjectPath "TestResults\latest-summary.md"
}
elseif (-not [System.IO.Path]::IsPathRooted($OutputPath)) {
    $OutputPath = Join-Path $resolvedProjectPath $OutputPath
}

$knownPreExistingPatterns = @(
    "WalkStraight_NoFalls",
    "SustainedLocomotionCollapse_TransitionsIntoFallen",
    "LapCourseTests.CompleteLap_WithinTimeLimit_NoFalls"
)

$suspectedOrderSensitivePatterns = @(
    "TurnAndWalk_CornerRecovery",
    "HardSnap90_AtFullSpeed_CharacterRecoversAndMakesProgress",
    "SpinRecoveryTests.AfterFullSpinThenForwardInput_DisplacementRecoveredWithin2s"
)

function Convert-ToProjectRelativePath {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $null
    }

    $resolvedPath = [System.IO.Path]::GetFullPath($Path)
    $projectRoot = [System.IO.Path]::GetFullPath($resolvedProjectPath)

    if ($resolvedPath.StartsWith($projectRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        $relativePath = $resolvedPath.Substring($projectRoot.Length).TrimStart([char]'\', [char]'/')
        return $relativePath -replace '\\', '/'
    }

    return $resolvedPath -replace '\\', '/'
}

function Get-PlatformNameFromPath {
    param([string]$Path)

    $fileName = [System.IO.Path]::GetFileNameWithoutExtension($Path)
    if ($fileName -match '(?i)editmode') {
        return 'EditMode'
    }

    if ($fileName -match '(?i)playmode') {
        return 'PlayMode'
    }

    return $fileName
}

function Convert-ToIntOrZero {
    param([string]$Value)

    $parsedValue = 0
    [void][int]::TryParse($Value, [ref]$parsedValue)
    return $parsedValue
}

function Convert-ToDoubleOrZero {
    param([string]$Value)

    $parsedValue = 0.0
    [void][double]::TryParse(
        $Value,
        [System.Globalization.NumberStyles]::Float,
        [System.Globalization.CultureInfo]::InvariantCulture,
        [ref]$parsedValue)
    return $parsedValue
}

function Normalize-Whitespace {
    param([string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return "No failure message captured."
    }

    return (($Text -replace '\s+', ' ').Trim())
}

function Shorten-Text {
    param(
        [string]$Text,
        [int]$MaxLength = 160
    )

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return ""
    }

    if ($Text.Length -le $MaxLength) {
        return $Text
    }

    return $Text.Substring(0, $MaxLength - 3).TrimEnd() + "..."
}

function Test-NameMatchesPattern {
    param(
        [string]$Name,
        [string]$FullName,
        [string]$Pattern
    )

    if ($Name -eq $Pattern -or $FullName -eq $Pattern) {
        return $true
    }

    if ($FullName -like "*.$Pattern") {
        return $true
    }

    return $false
}

function Get-FailureClassification {
    param(
        [string]$Name,
        [string]$FullName
    )

    foreach ($pattern in $knownPreExistingPatterns) {
        if (Test-NameMatchesPattern -Name $Name -FullName $FullName -Pattern $pattern) {
            return "known pre-existing"
        }
    }

    foreach ($pattern in $suspectedOrderSensitivePatterns) {
        if (Test-NameMatchesPattern -Name $Name -FullName $FullName -Pattern $pattern) {
            return "suspected order-sensitive"
        }
    }

    return "new or unclassified"
}

function Format-NameList {
    param([string[]]$Names)

    if (-not $Names -or $Names.Count -eq 0) {
        return "none"
    }

    return ($Names | Sort-Object -Unique | ForEach-Object { '`' + $_ + '`' }) -join ', '
}

function Resolve-XmlInputs {
    if ($XmlPaths -and $XmlPaths.Count -gt 0) {
        return @(
            $XmlPaths |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            ForEach-Object { (Resolve-Path $_).Path }
        )
    }

    $testResultsDir = Join-Path $resolvedProjectPath "TestResults"
    $editModeXml = Join-Path $testResultsDir "EditMode.xml"
    $playModeXml = Join-Path $testResultsDir "PlayMode.xml"

    switch ($Platform) {
        "EditMode" {
            if (Test-Path $editModeXml) {
                return @((Resolve-Path $editModeXml).Path)
            }
        }
        "PlayMode" {
            if (Test-Path $playModeXml) {
                return @((Resolve-Path $playModeXml).Path)
            }
        }
        "All" {
            $paths = @()
            if (Test-Path $editModeXml) {
                $paths += (Resolve-Path $editModeXml).Path
            }
            if (Test-Path $playModeXml) {
                $paths += (Resolve-Path $playModeXml).Path
            }

            if ($paths.Count -gt 0) {
                return $paths
            }
        }
        default {
            $candidatePaths = @($editModeXml, $playModeXml) |
                Where-Object { Test-Path $_ } |
                Sort-Object { (Get-Item $_).LastWriteTime } -Descending

            if ($candidatePaths.Count -gt 0) {
                return @((Resolve-Path $candidatePaths[0]).Path)
            }
        }
    }

    throw "No NUnit XML files found for Platform='$Platform'."
}

$providedLogPathsByPlatform = @{}
if ($LogPaths -and $LogPaths.Count -gt 0) {
    foreach ($path in $LogPaths) {
        if ([string]::IsNullOrWhiteSpace($path) -or -not (Test-Path $path)) {
            continue
        }

        $resolvedLogPath = (Resolve-Path $path).Path
        $providedLogPathsByPlatform[(Get-PlatformNameFromPath -Path $resolvedLogPath)] = $resolvedLogPath
    }
}

function Find-BestLogPath {
    param(
        [string]$PlatformName,
        [datetime]$ReferenceTime
    )

    if ($providedLogPathsByPlatform.ContainsKey($PlatformName)) {
        return $providedLogPathsByPlatform[$PlatformName]
    }

    $logsDir = Join-Path $resolvedProjectPath "Logs"
    if (-not (Test-Path $logsDir)) {
        return $null
    }

    $logPattern = "test_{0}_*.log" -f $PlatformName.ToLowerInvariant()
    $candidates = @(Get-ChildItem -Path (Join-Path $logsDir $logPattern) -ErrorAction SilentlyContinue)
    if ($candidates.Count -eq 0) {
        return $null
    }

    $bestCandidate = $candidates |
        Sort-Object { [Math]::Abs(($_.LastWriteTime - $ReferenceTime).TotalSeconds) }, LastWriteTime -Descending |
        Select-Object -First 1

    return $bestCandidate.FullName
}

$resolvedXmlPaths = @(Resolve-XmlInputs)
$platformSummaries = @()

foreach ($xmlPath in $resolvedXmlPaths) {
    $xmlText = [System.IO.File]::ReadAllText($xmlPath)
    $xmlDocument = [System.Xml.XmlDocument]::new()
    $xmlDocument.LoadXml($xmlText)

    $runNode = $xmlDocument.'test-run'
    if ($null -eq $runNode) {
        throw "Results XML '$xmlPath' does not contain a test-run node."
    }

    $platformName = Get-PlatformNameFromPath -Path $xmlPath
    $xmlInfo = Get-Item $xmlPath
    $logPath = Find-BestLogPath -PlatformName $platformName -ReferenceTime $xmlInfo.LastWriteTime

    $failures = @()
    $failedNodes = @($xmlDocument.SelectNodes("//test-case[@result='Failed']"))
    foreach ($failedNode in $failedNodes) {
        $failureName = [string]$failedNode.GetAttribute("name")
        $failureFullName = [string]$failedNode.GetAttribute("fullname")
        $messageNode = $failedNode.SelectSingleNode("failure/message")
        $messageText = if ($messageNode) { $messageNode.InnerText } else { "" }
        $classification = Get-FailureClassification -Name $failureName -FullName $failureFullName

        $failures += [PSCustomObject]@{
            Name = $failureName
            FullName = $failureFullName
            Classification = $classification
            MessageSnippet = Shorten-Text -Text (Normalize-Whitespace -Text $messageText)
        }
    }

    $knownFailures = @($failures | Where-Object { $_.Classification -eq "known pre-existing" } | Select-Object -ExpandProperty Name -Unique)
    $orderSensitiveFailures = @($failures | Where-Object { $_.Classification -eq "suspected order-sensitive" } | Select-Object -ExpandProperty Name -Unique)
    $newFailures = @($failures | Where-Object { $_.Classification -eq "new or unclassified" } | Select-Object -ExpandProperty Name -Unique)

    $platformSummaries += [PSCustomObject]@{
        Platform = $platformName
        Timestamp = $xmlInfo.LastWriteTime.ToString("s")
        Result = [string]$runNode.GetAttribute("result")
        Total = Convert-ToIntOrZero -Value ([string]$runNode.GetAttribute("total"))
        Passed = Convert-ToIntOrZero -Value ([string]$runNode.GetAttribute("passed"))
        Failed = Convert-ToIntOrZero -Value ([string]$runNode.GetAttribute("failed"))
        Skipped = Convert-ToIntOrZero -Value ([string]$runNode.GetAttribute("skipped"))
        DurationSeconds = Convert-ToDoubleOrZero -Value ([string]$runNode.GetAttribute("duration"))
        XmlPath = $xmlPath
        RelativeXmlPath = Convert-ToProjectRelativePath -Path $xmlPath
        LogPath = $logPath
        RelativeLogPath = Convert-ToProjectRelativePath -Path $logPath
        KnownPreExisting = $knownFailures
        SuspectedOrderSensitive = $orderSensitiveFailures
        NewFailures = $newFailures
        Failures = $failures
    }
}

$aggregateTotal = ($platformSummaries | Measure-Object -Property Total -Sum).Sum
$aggregatePassed = ($platformSummaries | Measure-Object -Property Passed -Sum).Sum
$aggregateFailed = ($platformSummaries | Measure-Object -Property Failed -Sum).Sum
$aggregateSkipped = ($platformSummaries | Measure-Object -Property Skipped -Sum).Sum
$platformList = ($platformSummaries | Select-Object -ExpandProperty Platform) -join ', '
$generatedTimestamp = (Get-Date).ToString("s")

$markdownLines = @(
    "# Latest Test Summary",
    "",
    "- Generated: $generatedTimestamp",
    "- Platforms: $platformList",
    "- Result: $aggregatePassed passed, $aggregateFailed failed, $aggregateSkipped skipped, $aggregateTotal total"
)

if (-not [string]::IsNullOrWhiteSpace($TestFilter)) {
    $markdownLines += '- Test filter: `' + $TestFilter + '`'
}

foreach ($platformSummary in $platformSummaries) {
    $markdownLines += @(
        "",
        "## $($platformSummary.Platform)",
        "",
        "- Timestamp: $($platformSummary.Timestamp)",
        "- Result: $($platformSummary.Passed) passed, $($platformSummary.Failed) failed, $($platformSummary.Skipped) skipped, $($platformSummary.Total) total",
        ('- XML: `' + $platformSummary.RelativeXmlPath + '`')
    )

    if ([string]::IsNullOrWhiteSpace($platformSummary.RelativeLogPath)) {
        $markdownLines += "- Fresh log: not found"
    }
    else {
        $markdownLines += ('- Fresh log: `' + $platformSummary.RelativeLogPath + '`')
    }

    $markdownLines += "- Known pre-existing reds: $(Format-NameList -Names $platformSummary.KnownPreExisting)"
    $markdownLines += "- Suspected order-sensitive reds: $(Format-NameList -Names $platformSummary.SuspectedOrderSensitive)"
    $markdownLines += "- New or unclassified reds: $(Format-NameList -Names $platformSummary.NewFailures)"

    if ($platformSummary.Failures.Count -eq 0) {
        $markdownLines += "- Failed tests: none"
        continue
    }

    $markdownLines += @(
        "",
        "### Failure snippets",
        ""
    )

    foreach ($failure in $platformSummary.Failures) {
        $displayName = if ([string]::IsNullOrWhiteSpace($failure.FullName)) { $failure.Name } else { $failure.FullName }
        $markdownLines += ('- `' + $displayName + '` [' + $failure.Classification + ']: ' + $failure.MessageSnippet)
    }
}

$summaryObject = [PSCustomObject]@{
    OutputPath = $OutputPath
    RelativeOutputPath = Convert-ToProjectRelativePath -Path $OutputPath
    Generated = $generatedTimestamp
    Platforms = $platformSummaries | Select-Object -ExpandProperty Platform
    Total = $aggregateTotal
    Passed = $aggregatePassed
    Failed = $aggregateFailed
    Skipped = $aggregateSkipped
    TestFilter = $TestFilter
    PlatformSummaries = $platformSummaries
}

if (-not $NoWriteFile) {
    $outputDirectory = Split-Path -Parent $OutputPath
    if (-not (Test-Path $outputDirectory)) {
        New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
    }

    Set-Content -LiteralPath $OutputPath -Value ($markdownLines -join [Environment]::NewLine) -Encoding ascii
}

if (-not $PassThru) {
    if ($NoWriteFile) {
        Write-Host "Computed test summary without writing a file."
    }
    else {
        Write-Host "Wrote test summary to $(Convert-ToProjectRelativePath -Path $OutputPath)"
    }
}

if ($PassThru) {
    $summaryObject
}