$projectPath = "H:\Work\PhysicsDrivenMovementDemo"

Write-Host ""
Write-Host "=== Agent Status Check ===" -ForegroundColor Cyan
Write-Host ""

# Last 3 commits
Write-Host "Recent commits:" -ForegroundColor Yellow
git -C $projectPath log --oneline -3
Write-Host ""

# Is Codex running? (filter specifically for codex.js process)
$codexProc = Get-Process -Name "node" -ErrorAction SilentlyContinue | ForEach-Object {
    $id = $_.Id
    $cmd = (Get-CimInstance Win32_Process -Filter "ProcessId=$id" -ErrorAction SilentlyContinue).CommandLine
    if ($cmd -match "codex\.js") { $_ }
}

if ($codexProc) {
    Write-Host "Codex: RUNNING (pid $($codexProc.Id))" -ForegroundColor Green
} else {
    Write-Host "Codex: not running" -ForegroundColor Red
}

# Is Unity running? (test suite)
$unity = Get-Process -Name "Unity" -ErrorAction SilentlyContinue
if ($unity) {
    Write-Host "Unity: RUNNING (tests active)" -ForegroundColor Green
} else {
    Write-Host "Unity: not running" -ForegroundColor Gray
}

# Last test result
$resultsPath = "$projectPath\TestResults\PlayMode.xml"
if (Test-Path $resultsPath) {
    $xml = [xml](Get-Content $resultsPath)
    $run = $xml."test-run"
    $passed = $run.passed
    $failed = $run.failed
    $time = $run."start-time"
    Write-Host ""
    Write-Host "Last test run ($time):" -ForegroundColor Yellow
    if ($failed -eq "0") {
        Write-Host "  PASSED: $passed passed, $failed failed" -ForegroundColor Green
    } else {
        Write-Host "  FAILED: $passed passed, $failed failed" -ForegroundColor Red
    }
} else {
    Write-Host ""
    Write-Host "No test results found yet." -ForegroundColor Gray
}

Write-Host ""
