# Running Unity Tests from the Agent Terminal

> **Audience:** AI coding agents (GPT-5.4 via GitHub Copilot).
> **Purpose:** Run the repo's Unity EditMode and PlayMode tests from the terminal, keep `TestResults/` and `Logs/` as the authoritative artifacts, and fall back to raw Unity CLI only when the repo script is insufficient.
> **Authority:** Referenced by `CODING_STANDARDS.md` §2 and the development workflow (Phases C-E).

## Quick Load

- **Use `Tools/Run-UnityTests.ps1` as the primary unattended runner.** It resolves the Unity executable, clears stale lock artifacts, retries infrastructure races, writes fresh XML under `TestResults/`, and refreshes `TestResults/latest-summary.md`.
- Start with the **smallest meaningful `-TestFilter` slice**, then escalate to wider coverage only when the change is cross-cutting or the smaller slice is not defensible.
- Run **EditMode and PlayMode sequentially, never in parallel**, and treat missing fresh XML as an infrastructure failure rather than a trustworthy test result.
- Read `TestResults/latest-summary.md` first, then the platform XML, and only then the matching Unity log under `Logs/` if the summary or XML is insufficient.
- Use raw Unity CLI only when `Tools/Run-UnityTests.ps1` cannot express the run you need.

## Read More When

- Continue into the focused and wide-run sections when you need a copy-paste command, a red-phase rerun, or a broader regression gate.
- Continue into the artifact section when you need to interpret exit codes, verify that XML is fresh, or inspect failures without opening the editor.
- Continue into the manual fallback section only when the repo script is insufficient or you need to reproduce a Unity CLI quirk directly.
- Continue into troubleshooting when Unity reports another instance, writes no XML, or claims success with `Total=0`.

## 1 - Overview

The normal path is one script, one platform at a time, with `TestResults/` as the durable result surface.

`Tools/Run-UnityTests.ps1` currently:

- resolves the Unity executable from `ProjectSettings/ProjectVersion.txt`
- kills lingering Unity processes and clears common lock artifacts before each attempt
- runs `EditMode` and `PlayMode` sequentially
- writes `TestResults/<Platform>.xml`
- writes `Logs/test_<platform>_<timestamp>.log`
- refreshes `TestResults/latest-summary.md`
- returns `0` for all green, `2` for test failures, and `10` for infrastructure failures

Use this path for both red-phase confirmation and green regression verification.

## 2 - Run Focused Tests First

Use the smallest slice that actually covers the changed behavior. Replace the filter with one or more fully qualified fixtures separated by semicolons.

```powershell
$projectPath = "H:\Work\PhysicsDrivenMovementDemo"

powershell -NoProfile -ExecutionPolicy Bypass -File "$projectPath\Tools\Run-UnityTests.ps1" `
    -ProjectPath $projectPath `
    -Platform EditMode `
    -TestFilter "PhysicsDrivenMovement.Tests.EditMode.Core.GameSettingsTests" `
    -MaxAttemptsPerPlatform 2 `
    -NoGraphicsForEditMode `
    -Unattended

powershell -NoProfile -ExecutionPolicy Bypass -File "$projectPath\Tools\Run-UnityTests.ps1" `
    -ProjectPath $projectPath `
    -Platform PlayMode `
    -TestFilter "PhysicsDrivenMovement.Tests.PlayMode.LegAnimatorTests;PhysicsDrivenMovement.Tests.PlayMode.GaitOutcomeTests" `
    -MaxAttemptsPerPlatform 2 `
    -Unattended
```

Use the same commands in Phase C to confirm a new test fails red, then rerun the closest green slice after implementation.

## 3 - Run Wider Coverage When Warranted

Escalate to a whole-platform or all-platform run only when the change is shared infrastructure, scene or bootstrap code, assembly-definition wiring, or otherwise too broad for a focused slice.

```powershell
$projectPath = "H:\Work\PhysicsDrivenMovementDemo"

powershell -NoProfile -ExecutionPolicy Bypass -File "$projectPath\Tools\Run-UnityTests.ps1" `
    -ProjectPath $projectPath `
    -Platform All `
    -NoGraphicsForEditMode `
    -MaxAttemptsPerPlatform 2 `
    -Unattended
```

Exit codes from the repo script:

- `0`: all requested platforms passed
- `2`: tests executed but one or more failed
- `10`: infrastructure failure such as lock contention, missing XML, or timeout

## 4 - Choose The Right Regression Scope

Pick the smallest slice whose green result would actually increase confidence.

- Character movement, balance, gait, or recovery: start with the directly affected fixture plus nearby outcome suites such as `LegAnimatorTests`, `GaitOutcomeTests`, `HardSnapRecoveryTests`, `SpinRecoveryTests`, `TurnRecoveryTests`, or `MovementQualityTests`.
- Ragdoll setup or prefab composition: pair the relevant EditMode prefab checks with the closest PlayMode integration suite.
- Camera-only changes: prefer `CameraFollowTests`.
- Environment or builder changes: run the nearest EditMode scene or builder suite plus the smallest scene-level PlayMode slice that exercises the changed path.
- Cross-cutting infrastructure or uncertain blast radius: escalate to `-Platform All`.
- If you cannot explain why a smaller slice is sufficient, the slice is too small.

## 5 - Read The Artifacts

Treat file artifacts, not terminal exit text, as the source of truth.

- `TestResults/latest-summary.md`: fastest summary for the newest run and the first file to open when triaging failures.
- `TestResults/EditMode.xml` or `TestResults/PlayMode.xml`: authoritative NUnit result, counts, and failing test cases.
- `Logs/test_<platform>_<timestamp>.log`: compile, infrastructure, licensing, or startup diagnostics when XML is missing or incomplete.

Helpful wrapper when you want a refreshed summary without opening XML manually:

```powershell
$projectPath = "H:\Work\PhysicsDrivenMovementDemo"

powershell -NoProfile -ExecutionPolicy Bypass -File "$projectPath\summary.ps1" `
    -ProjectPath $projectPath `
    -Platform Auto
```

Always trust the XML or `latest-summary.md` over the Unity process exit code alone.

## 6 - Manual Fallback

Use raw Unity CLI only when `Tools/Run-UnityTests.ps1` cannot express the run you need.

```powershell
$projectPath = "H:\Work\PhysicsDrivenMovementDemo"
$unityVersion = (Get-Content "$projectPath\ProjectSettings\ProjectVersion.txt" | Select-String "^m_EditorVersion:").ToString().Split(": ")[1].Trim()
$unityExe = "C:\Program Files\Unity\Hub\Editor\$unityVersion\Editor\Unity.exe"

& $unityExe `
    -runTests `
    -batchmode `
    -silent-crashes `
    -projectPath $projectPath `
    -testPlatform PlayMode `
    -testFilter "PhysicsDrivenMovement.Tests.PlayMode.LegAnimatorTests" `
    -testResults "$projectPath\TestResults\PlayMode.xml" `
    -logFile "$projectPath\Logs\manual_playmode.log" `
    -forgetProjectPath
```

Fallback rules:

- run exactly one platform per invocation
- add `-nographics` only for EditMode or logic-only PlayMode runs
- verify that the XML timestamp is fresh before trusting the result
- use the same artifact-reading rules from §5 after the run

## 7 - Troubleshooting

| Problem | What to do |
|---------|------------|
| No fresh XML or script exits `10` | Rerun the same command up to `-MaxAttemptsPerPlatform 2` or `3`, then inspect the newest log under `Logs/`. |
| `HandleProjectAlreadyOpenInAnotherInstance` or another Unity instance message | Close the editor, stop stray `Unity*` processes, and rerun sequentially. Never start two Unity batch runs against this project at once. |
| Result says `Passed` with `Total=0` | The filter is wrong. Use the declared namespace form, e.g. `PhysicsDrivenMovement.Tests.PlayMode.*`, not folder-path guesses. |
| PlayMode run fails only when `-nographics` is present | Remove `-nographics` for that run. The repo script only applies it to EditMode when you ask for it. |
| Exit code `0` but XML says failed | Trust the XML and `latest-summary.md`, not the process exit code. |
| XML is missing and the log shows compile errors | Search the newest platform log for `error CS` and fix the compile break before rerunning. |