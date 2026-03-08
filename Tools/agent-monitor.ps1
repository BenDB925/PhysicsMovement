# Live Codex agent monitor — refreshes every 3 seconds
# Run in a terminal and leave it open

$projectPath = "H:\Work\PhysicsDrivenMovementDemo"

function Get-CodexStatus {
    $procs = Get-Process -Name "node" -ErrorAction SilentlyContinue
    foreach ($p in $procs) {
        $cmd = (Get-CimInstance Win32_Process -Filter "ProcessId=$($p.Id)" -ErrorAction SilentlyContinue).CommandLine
        if ($cmd -match "codex\.js") { return $p }
    }
    return $null
}

$wasRunning = $false

while ($true) {
    Clear-Host
    $now = Get-Date -Format "HH:mm:ss"
    Write-Host "=== Codex Agent Monitor === $now" -ForegroundColor Cyan
    Write-Host ""

    $codex = Get-CodexStatus

    if ($codex) {
        $runtime = (Get-Date) - $codex.StartTime
        $mins = [int]$runtime.TotalMinutes
        $secs = $runtime.Seconds
        Write-Host "  Codex: " -NoNewline
        Write-Host "RUNNING" -ForegroundColor Green -NoNewline
        Write-Host "  (pid $($codex.Id), ${mins}m ${secs}s)"
        $wasRunning = $true
    } else {
        Write-Host "  Codex: " -NoNewline
        if ($wasRunning) {
            Write-Host "FINISHED" -ForegroundColor Yellow
        } else {
            Write-Host "not running" -ForegroundColor Red
        }
    }

    $unity = Get-Process -Name "Unity" -ErrorAction SilentlyContinue
    if ($unity) {
        Write-Host "  Unity: " -NoNewline
        Write-Host "RUNNING" -ForegroundColor Green -NoNewline
        Write-Host " (tests active)"
    }

    Write-Host ""
    Write-Host "  Last commits:" -ForegroundColor Yellow
    $commits = git -C $projectPath log --oneline -3 2>$null
    foreach ($c in $commits) { Write-Host "    $c" }

    Write-Host ""
    Write-Host "  Refreshing every 3s — Ctrl+C to quit" -ForegroundColor DarkGray

    Start-Sleep -Seconds 3
}
