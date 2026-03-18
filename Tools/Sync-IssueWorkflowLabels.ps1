[CmdletBinding()]
param(
    [Parameter()]
    [string]$Repository
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-GitHubToken {
    foreach ($name in @('GH_TOKEN', 'GITHUB_TOKEN', 'GITHUB_PAT')) {
        $value = [Environment]::GetEnvironmentVariable($name)
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            return $value
        }
    }

    return $null
}

function Get-StatusCodeValue {
    param(
        [Parameter(Mandatory = $true)]
        [System.Exception]$Exception
    )

    if ($null -eq $Exception.Response) {
        return $null
    }

    return [int]$Exception.Response.StatusCode.value__
}

function Sync-LabelViaApi {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ResolvedRepository,

        [Parameter(Mandatory = $true)]
        [hashtable]$Label,

        [Parameter(Mandatory = $true)]
        [string]$Token
    )

    $headers = @{
        Accept               = 'application/vnd.github+json'
        Authorization        = "Bearer $Token"
        'X-GitHub-Api-Version' = '2022-11-28'
    }

    $encodedName = [System.Uri]::EscapeDataString($Label.Name)
    $labelUri = "https://api.github.com/repos/$ResolvedRepository/labels/$encodedName"
    $createUri = "https://api.github.com/repos/$ResolvedRepository/labels"
    $body = @{
        name        = $Label.Name
        color       = $Label.Color
        description = $Label.Description
    } | ConvertTo-Json -Compress

    $exists = $false

    try {
        Invoke-RestMethod -Method Get -Uri $labelUri -Headers $headers | Out-Null
        $exists = $true
    }
    catch {
        $statusCode = Get-StatusCodeValue -Exception $_.Exception
        if ($statusCode -ne 404) {
            throw
        }
    }

    if ($exists) {
        Invoke-RestMethod -Method Patch -Uri $labelUri -Headers $headers -Body $body -ContentType 'application/json' | Out-Null
        return
    }

    Invoke-RestMethod -Method Post -Uri $createUri -Headers $headers -Body $body -ContentType 'application/json' | Out-Null
}

$labels = @(
    @{ Name = 'type:prd'; Color = '0052CC'; Description = 'Parent issue for a feature or initiative outcome.' },
    @{ Name = 'type:slice'; Color = '1D76DB'; Description = 'One narrow AFK or HITL execution slice.' },
    @{ Name = 'type:bug'; Color = 'D73A4A'; Description = 'One narrow reproduced regression or escalated bug thread.' },
    @{ Name = 'type:refactor'; Color = 'FBCA04'; Description = 'Architecture-first cleanup or ownership RFC.' },
    @{ Name = 'mode:afk'; Color = '0E8A16'; Description = 'The agent can advance this issue without waiting on a user decision.' },
    @{ Name = 'mode:hitl'; Color = 'D93F0B'; Description = 'This issue is blocked on explicit human input, approval, or missing external access.' },
    @{ Name = 'status:ready'; Color = '0E8A16'; Description = 'Ready for focused execution.' },
    @{ Name = 'status:blocked'; Color = 'B60205'; Description = 'Blocked by another issue, bug, approval, or dependency.' },
    @{ Name = 'area:workflow'; Color = '006B75'; Description = 'Agent workflow, planning, issue flow, or repo process work.' },
    @{ Name = 'area:docs'; Color = '0366D6'; Description = 'Documentation structure, routing, or durable reference updates.' }
)

$gh = Get-Command gh -ErrorAction SilentlyContinue
$token = Get-GitHubToken

if ([string]::IsNullOrWhiteSpace($Repository)) {
    if ($null -ne $gh) {
        $Repository = (& gh repo view --json nameWithOwner --jq ".nameWithOwner").Trim()
    }
    else {
        throw "Pass -Repository owner/name when GitHub CLI is unavailable."
    }
}

if ([string]::IsNullOrWhiteSpace($Repository)) {
    throw "Could not resolve the repository name. Pass -Repository owner/name."
}

if (($null -eq $gh) -and [string]::IsNullOrWhiteSpace($token)) {
    throw "Neither GitHub CLI nor a GitHub token environment variable (GH_TOKEN, GITHUB_TOKEN, GITHUB_PAT) is available."
}

foreach ($label in $labels) {
    if ($null -ne $gh) {
        & gh label create $label.Name --repo $Repository --color $label.Color --description $label.Description --force | Out-Null
        continue
    }

    Sync-LabelViaApi -ResolvedRepository $Repository -Label $label -Token $token
}

Write-Host "Synced $($labels.Count) issue-workflow labels to $Repository."