Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-StateRoot {
    $tempRoot = [System.IO.Path]::GetTempPath()
    $stateRoot = Join-Path $tempRoot 'vscode-copilot-hooks\PhysicsDrivenMovementDemo'

    if (-not (Test-Path -LiteralPath $stateRoot)) {
        New-Item -ItemType Directory -Path $stateRoot -Force | Out-Null
    }

    return $stateRoot
}

function Get-StatePath {
    param(
        [string]$SessionId
    )

    if ([string]::IsNullOrWhiteSpace($SessionId)) {
        $SessionId = 'unknown-session'
    }

    $safeSessionId = ($SessionId -replace '[^A-Za-z0-9_.-]', '_')
    return Join-Path (Get-StateRoot) ($safeSessionId + '.json')
}

function Get-ReviewTriggerKind {
    param(
        [string]$Prompt
    )

    if ([string]::IsNullOrWhiteSpace($Prompt)) {
        return $null
    }

    $completionPatterns = @(
        '\b(task|work|change|fix|feature|bug|investigation|plan)\s+(is\s+)?(complete|completed|done|finished)\b',
        '\b(this|that|it)\s+(is\s+)?(complete|completed|done|finished)\b',
        '\b(we are|we''re|you can|can you)\s+(wrap up|finalize|finish)\b',
        '\bwrap\s+(this|it)\s+up\b',
        '\bready\s+to\s+finish\b'
    )

    foreach ($pattern in $completionPatterns) {
        if ($Prompt -match $pattern) {
            return 'complete'
        }
    }

    $pausePatterns = @(
        '\b(task|work|change|fix|feature|bug|investigation|plan)\s+(is\s+)?(paused|on\s+hold)\b',
        '\b(pause|stop)\s+(here|for\s+now|today)\b',
        '\b(pick|continue|resume)\s+(this|it)?\s*(up\s+)?(later|tomorrow)\b',
        '\b(hand\s*off|handoff)\b'
    )

    foreach ($pattern in $pausePatterns) {
        if ($Prompt -match $pattern) {
            return 'pause'
        }
    }

    return $null
}

function Get-ReviewSystemMessage {
    param(
        [string]$ReviewKind
    )

    if ($ReviewKind -eq 'pause') {
        return 'The user signaled a pause or handoff. Before stopping, review the active parent task record, refresh its Quick Resume and Verified Artifacts sections, and update any affected documentation so the next agent can resume without replaying chat history.'
    }

    return 'The user signaled that the task may be complete. Before stopping, review the active parent task record, refresh its Quick Resume and Verified Artifacts sections, and update any affected documentation so a future agent can resume without replaying chat history.'
}

function Get-StopBlockReason {
    param(
        [string]$ReviewKind
    )

    if ($ReviewKind -eq 'pause') {
        return 'The user signaled a pause or handoff. Before ending the session, update the active parent plan with Quick Resume and Verified Artifacts, review any changed docs, and then provide the handoff summary.'
    }

    return 'The user signaled task completion. Before ending the session, update the active parent plan with Quick Resume and Verified Artifacts, review any changed docs, and then provide the completion summary.'
}

function Write-HookOutput {
    param(
        [hashtable]$Payload
    )

    $Payload | ConvertTo-Json -Depth 10 -Compress
}

$rawInput = [Console]::In.ReadToEnd()
if ([string]::IsNullOrWhiteSpace($rawInput)) {
    return
}

$hookInput = $rawInput | ConvertFrom-Json
$hookEventName = [string]$hookInput.hookEventName
$sessionId = [string]$hookInput.sessionId
$statePath = Get-StatePath -SessionId $sessionId

switch ($hookEventName) {
    'UserPromptSubmit' {
        $prompt = [string]$hookInput.prompt
        $reviewKind = Get-ReviewTriggerKind -Prompt $prompt

        if ([string]::IsNullOrWhiteSpace($reviewKind)) {
            return
        }

        @{
            pendingReview = $true
            reviewKind    = $reviewKind
            updatedAt     = (Get-Date).ToString('o')
            prompt        = $prompt
        } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $statePath -Encoding utf8

        Write-HookOutput -Payload @{
            systemMessage = Get-ReviewSystemMessage -ReviewKind $reviewKind
        }

        return
    }
    'Stop' {
        if (-not (Test-Path -LiteralPath $statePath)) {
            return
        }

        $state = $null
        try {
            $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
        }
        catch {
            $state = $null
        }

        $reviewKind = if ($null -ne $state -and -not [string]::IsNullOrWhiteSpace([string]$state.reviewKind)) {
            [string]$state.reviewKind
        }
        else {
            'complete'
        }

        Remove-Item -LiteralPath $statePath -Force -ErrorAction SilentlyContinue

        if ($hookInput.stop_hook_active) {
            return
        }

        Write-HookOutput -Payload @{
            hookSpecificOutput = @{
                hookEventName = 'Stop'
                decision      = 'block'
                reason        = Get-StopBlockReason -ReviewKind $reviewKind
            }
        }

        return
    }
    default {
        return
    }
}