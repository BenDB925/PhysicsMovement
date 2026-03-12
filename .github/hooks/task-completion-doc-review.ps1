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

function Test-CompletionPrompt {
    param(
        [string]$Prompt
    )

    if ([string]::IsNullOrWhiteSpace($Prompt)) {
        return $false
    }

    $patterns = @(
        '\b(task|work|change|fix|feature|bug|investigation|plan)\s+(is\s+)?(complete|completed|done|finished)\b',
        '\b(this|that|it)\s+(is\s+)?(complete|completed|done|finished)\b',
        '\b(we are|we''re|you can|can you)\s+(wrap up|finalize|finish)\b',
        '\bwrap\s+(this|it)\s+up\b',
        '\bready\s+to\s+finish\b'
    )

    foreach ($pattern in $patterns) {
        if ($Prompt -match $pattern) {
            return $true
        }
    }

    return $false
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

        if (-not (Test-CompletionPrompt -Prompt $prompt)) {
            return
        }

        @{
            pendingReview = $true
            updatedAt = (Get-Date).ToString('o')
            prompt = $prompt
        } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $statePath -Encoding utf8

        Write-HookOutput -Payload @{
            systemMessage = 'The user signaled that the task may be complete. Before stopping, review the active task record and update any affected documentation such as PLAN.md, DEBUGGING.md, ARCHITECTURE.md, TASK_ROUTING.md, .copilot-instructions.md, and linked plan or bug docs.'
        }

        return
    }
    'Stop' {
        if (-not (Test-Path -LiteralPath $statePath)) {
            return
        }

        Remove-Item -LiteralPath $statePath -Force -ErrorAction SilentlyContinue

        if ($hookInput.stop_hook_active) {
            return
        }

        Write-HookOutput -Payload @{
            hookSpecificOutput = @{
                hookEventName = 'Stop'
                decision = 'block'
                reason = 'The user signaled task completion. Before ending the session, review the active task record and update any documentation that changed, then provide the completion summary.'
            }
        }

        return
    }
    default {
        return
    }
}