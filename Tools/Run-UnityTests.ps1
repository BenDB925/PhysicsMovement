param(
    [string]$ProjectPath = (Get-Location).Path,
    [ValidateSet("All", "EditMode", "PlayMode")]
    [string]$Platform = "All",
    [switch]$NoGraphicsForEditMode,
    [int]$MaxAttemptsPerPlatform = 2,
    [switch]$Unattended
)

$ErrorActionPreference = "Stop"
$ConfirmPreference = "None"

function Get-UnityExePath {
    param([string]$ResolvedProjectPath)

    $versionFile = Join-Path $ResolvedProjectPath "ProjectSettings\ProjectVersion.txt"
    if (-not (Test-Path $versionFile)) {
        throw "ProjectVersion.txt not found at '$versionFile'."
    }

    $versionLine = Get-Content $versionFile | Where-Object { $_ -match "^m_EditorVersion:" } | Select-Object -First 1
    if (-not $versionLine) {
        throw "Could not find m_EditorVersion in '$versionFile'."
    }

    $unityVersion = ($versionLine -split ":", 2)[1].Trim()
    $unityExe = "C:\Program Files\Unity\Hub\Editor\$unityVersion\Editor\Unity.exe"
    if (-not (Test-Path $unityExe)) {
        throw "Unity executable not found at '$unityExe'."
    }

    return $unityExe
}

function Stop-UnityProcesses {
    $processNames = @("Unity", "UnityCrashHandler64", "UnityCrashHandler32")

    foreach ($processName in $processNames) {
        Get-Process -Name $processName -ErrorAction SilentlyContinue |
        Stop-Process -Force -Confirm:$false -ErrorAction SilentlyContinue
    }

    $deadline = (Get-Date).AddSeconds(15)
    do {
        $remaining = Get-Process -Name Unity -ErrorAction SilentlyContinue
        if (-not $remaining) {
            return
        }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)

    $stillRunning = Get-Process -Name Unity -ErrorAction SilentlyContinue
    if ($stillRunning) {
        $pids = ($stillRunning | Select-Object -ExpandProperty Id) -join ", "
        throw "Unity process(es) still running after cleanup timeout. PID(s): $pids"
    }
}

function Remove-UnityLockArtifacts {
    param([string]$ResolvedProjectPath)

    $tempDir = Join-Path $ResolvedProjectPath "Temp"
    if (-not (Test-Path $tempDir)) {
        return
    }

    $lockCandidates = @(
        Join-Path $tempDir "UnityLockfile"
        Join-Path $tempDir "UnityTempFile-*"
    )

    foreach ($candidate in $lockCandidates) {
        Get-ChildItem -Path $candidate -ErrorAction SilentlyContinue |
        Remove-Item -Force -Confirm:$false -ErrorAction SilentlyContinue
    }
}

function Invoke-UnityTestPlatform {
    param(
        [string]$UnityExe,
        [string]$ResolvedProjectPath,
        [ValidateSet("EditMode", "PlayMode")]
        [string]$SinglePlatform,
        [bool]$UseNoGraphics,
        [bool]$RunHidden
    )

    $testResultsDir = Join-Path $ResolvedProjectPath "TestResults"
    $logsDir = Join-Path $ResolvedProjectPath "Logs"
    New-Item -ItemType Directory -Force -Path $testResultsDir | Out-Null
    New-Item -ItemType Directory -Force -Path $logsDir | Out-Null

    $resultPath = Join-Path $testResultsDir "$SinglePlatform.xml"
    $logPath = Join-Path $logsDir ("test_{0}_{1}.log" -f $SinglePlatform.ToLowerInvariant(), (Get-Date -Format "yyyyMMdd_HHmmss"))

    # Delete stale results file before the run so freshness check is unambiguous.
    # If Unity produces ANY file at this path after we start, it must be fresh.
    if (Test-Path $resultPath) {
        Remove-Item $resultPath -Force -ErrorAction SilentlyContinue
    }

    $runStart = Get-Date

    $arguments = @(
        "-runTests"
        "-batchmode"
        "-silent-crashes"
    )

    if ($UseNoGraphics) {
        $arguments += "-nographics"
    }

    $arguments += @(
        "-projectPath", $ResolvedProjectPath,
        "-testPlatform", $SinglePlatform,
        "-testResults", $resultPath,
        "-logFile", $logPath,
        "-forgetProjectPath"
    )

    $procStartParams = @{
        FilePath     = $UnityExe
        ArgumentList = $arguments
        PassThru     = $true
    }

    if ($RunHidden) {
        $procStartParams.WindowStyle = "Hidden"
    }
    else {
        $procStartParams.NoNewWindow = $true
    }

    $proc = Start-Process @procStartParams

    # Wait for Unity to exit — up to 10 minutes (PlayMode physics tests can take 3-4 min).
    # Start-Process -Wait is unreliable in nested PowerShell invocations; use WaitForExit() directly.
    $exited = $proc.WaitForExit(600000)
    if (-not $exited) {
        $proc.Kill()
        return [PSCustomObject]@{
            Platform   = $SinglePlatform
            Status     = "InfraFailure"
            Message    = "Unity process timed out after 10 minutes."
            ResultPath = $resultPath
            LogPath    = $logPath
            ExitCode   = -1
        }
    }

    if (-not (Test-Path $resultPath)) {
        return [PSCustomObject]@{
            Platform   = $SinglePlatform
            Status     = "InfraFailure"
            Message    = "Results file was not generated."
            ResultPath = $resultPath
            LogPath    = $logPath
            ExitCode   = $proc.ExitCode
        }
    }

    $newWriteTime = (Get-Item $resultPath).LastWriteTime
    # Since we deleted the old XML before the run, any file here was written by this run.
    # We also keep a generous 10-second grace on the timestamp to handle filesystem/clock skew.
    $isFresh = $newWriteTime -gt $runStart.AddSeconds(-10)

    if (-not $isFresh) {
        return [PSCustomObject]@{
            Platform   = $SinglePlatform
            Status     = "InfraFailure"
            Message    = "Results file exists but timestamp was not refreshed by this run."
            ResultPath = $resultPath
            LogPath    = $logPath
            ExitCode   = $proc.ExitCode
        }
    }

    [xml]$xml = Get-Content $resultPath
    $run = $xml.'test-run'
    if ($null -eq $run) {
        return [PSCustomObject]@{
            Platform   = $SinglePlatform
            Status     = "InfraFailure"
            Message    = "Results XML does not contain a test-run node."
            ResultPath = $resultPath
            LogPath    = $logPath
            ExitCode   = $proc.ExitCode
        }
    }

    $runResult = [string]$run.result
    $failedCount = [int]$run.failed
    $passedCount = [int]$run.passed
    $totalCount = [int]$run.total

    $status = if ($failedCount -gt 0 -or $runResult -like "Failed*") { "TestsFailed" } else { "Passed" }

    return [PSCustomObject]@{
        Platform   = $SinglePlatform
        Status     = $status
        Message    = "Result=$runResult, Total=$totalCount, Passed=$passedCount, Failed=$failedCount"
        ResultPath = $resultPath
        LogPath    = $logPath
        ExitCode   = $proc.ExitCode
        RunResult  = $runResult
        Total      = $totalCount
        Passed     = $passedCount
        Failed     = $failedCount
    }
}

$resolvedProjectPath = (Resolve-Path $ProjectPath).Path
$unityExe = Get-UnityExePath -ResolvedProjectPath $resolvedProjectPath

$platformsToRun = switch ($Platform) {
    "All" { @("EditMode", "PlayMode") }
    default { @($Platform) }
}

$allResults = @()

foreach ($singlePlatform in $platformsToRun) {
    $attempt = 0
    $result = $null
    $useNoGraphics = $singlePlatform -eq "EditMode" -and $NoGraphicsForEditMode.IsPresent
    $runHidden = $true
    if ($PSBoundParameters.ContainsKey("Unattended")) {
        $runHidden = $Unattended.IsPresent
    }

    while ($attempt -lt $MaxAttemptsPerPlatform) {
        $attempt++
        Write-Host "`n=== $singlePlatform attempt $attempt/$MaxAttemptsPerPlatform ===" -ForegroundColor Cyan

        Stop-UnityProcesses
        Remove-UnityLockArtifacts -ResolvedProjectPath $resolvedProjectPath

        $result = Invoke-UnityTestPlatform `
            -UnityExe $unityExe `
            -ResolvedProjectPath $resolvedProjectPath `
            -SinglePlatform $singlePlatform `
            -UseNoGraphics:$useNoGraphics `
            -RunHidden:$runHidden

        Write-Host "[$($result.Platform)] $($result.Status): $($result.Message)"
        Write-Host "  XML: $($result.ResultPath)"
        Write-Host "  LOG: $($result.LogPath)"

        if ($result.Status -ne "InfraFailure") {
            break
        }

        if ($attempt -lt $MaxAttemptsPerPlatform) {
            Write-Host "Retrying due to infrastructure/lock issue..." -ForegroundColor Yellow
            Start-Sleep -Seconds 2
        }
    }

    $allResults += $result
}

Write-Host "`n=== Summary ===" -ForegroundColor Cyan
foreach ($r in $allResults) {
    Write-Host "[$($r.Platform)] $($r.Status) :: $($r.Message)"
}

if ($allResults | Where-Object { $_.Status -eq "InfraFailure" }) {
    exit 10
}

if ($allResults | Where-Object { $_.Status -eq "TestsFailed" }) {
    exit 2
}

exit 0