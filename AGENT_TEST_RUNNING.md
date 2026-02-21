# Running Unity Tests from the Agent Terminal

> **Audience:** AI coding agents (Claude model via GitHub Copilot).
> **Purpose:** Step-by-step instructions for running Unity EditMode and PlayMode tests directly from the terminal, without requiring the user to manually open the Test Runner window.
> **Authority:** Referenced by `CODING_STANDARDS.md` §2 and the development workflow (Phases C–E).

---

## Overview

Unity's Test Framework supports headless execution via the Unity Editor's **command-line / batch mode**. The agent can invoke the Unity executable with specific arguments to run tests, produce an NUnit XML results file, and then parse that file to determine pass/fail status — all without the user interacting with the Unity GUI.

---

## 1 — Locate the Unity Executable

Unity is installed via Unity Hub. The standard installation path on Windows is:

```
C:\Program Files\Unity\Hub\Editor\<VERSION>\Editor\Unity.exe
```

### How to find the correct version

1. **Read the project's version file:**

   ```powershell
   Get-Content "<ProjectRoot>\ProjectSettings\ProjectVersion.txt"
   ```

   The first line contains:  `m_EditorVersion: 6000.0.33f1`  (example).

2. **Construct the executable path:**

   ```powershell
   $unityVersion = (Get-Content "$projectPath\ProjectSettings\ProjectVersion.txt" | Select-String "m_EditorVersion").ToString().Split(": ")[1].Trim()
   $unityExe = "C:\Program Files\Unity\Hub\Editor\$unityVersion\Editor\Unity.exe"
   ```

3. **Verify the executable exists:**

   ```powershell
   Test-Path $unityExe
   ```

   If `False`, list available versions and pick the closest match:

   ```powershell
   Get-ChildItem "C:\Program Files\Unity\Hub\Editor" | Select-Object Name
   ```

---

## 2 — Run EditMode Tests

EditMode tests exercise pure C# logic without requiring a running scene. They are fast and do not need the GPU.

### Command (PowerShell)

```powershell
$projectPath = "H:\Work\PhysicsDrivenMovementDemo"
$resultsFile = "$projectPath\TestResults\EditMode.xml"
$logFile      = "$projectPath\TestResults\EditMode.log"

# Ensure output directory exists
New-Item -ItemType Directory -Force -Path "$projectPath\TestResults" | Out-Null

& $unityExe `
    -runTests `
    -batchmode `
    -nographics `
    -projectPath $projectPath `
    -testPlatform EditMode `
    -testResults $resultsFile `
    -logFile $logFile `
    -forgetProjectPath
```

### Key arguments explained

| Argument | Purpose |
|----------|---------|
| `-runTests` | Tells Unity to execute the test runner and then exit. |
| `-batchmode` | Runs Unity without the GUI — required for automation. |
| `-nographics` | Skips GPU initialisation (safe for EditMode tests, saves time). |
| `-projectPath` | Absolute path to the Unity project root (the folder containing `Assets/`). |
| `-testPlatform EditMode` | Run only EditMode tests. |
| `-testResults` | Path where the NUnit XML results file will be written. |
| `-logFile` | Path for the Unity Editor log (useful for debugging compile errors). |
| `-forgetProjectPath` | Prevents this run from polluting the Unity Hub recent-projects list. |

### Optional useful arguments

| Argument | Purpose |
|----------|---------|
| `-testFilter "ClassName.MethodName"` | Run only tests matching the filter (semicolon-separated list or regex). |
| `-testCategory "CategoryName"` | Run only tests in the given NUnit category. |
| `-assemblyNames "MyTests"` | Run only tests from specific assembly definitions. |
| `-runSynchronously` | Forces all EditMode tests to run in a single editor update (faster, but skips `[UnityTest]` coroutine tests). |

---

## 3 — Run PlayMode Tests

PlayMode tests require the Unity lifecycle (`Awake`, `Start`, `Update`, physics, etc.). They run inside a temporary scene in the editor.

### Command (PowerShell)

```powershell
$resultsFile = "$projectPath\TestResults\PlayMode.xml"
$logFile      = "$projectPath\TestResults\PlayMode.log"

& $unityExe `
    -runTests `
    -batchmode `
    -projectPath $projectPath `
    -testPlatform PlayMode `
    -testResults $resultsFile `
    -logFile $logFile `
    -forgetProjectPath
```

> **Note:** Do NOT use `-nographics` for PlayMode tests that depend on rendering, physics visualisation, or GPU compute. If your PlayMode tests are purely logic-based (networking, coroutines), `-nographics` is acceptable.

---

## 4 — Run All Tests (EditMode + PlayMode)

Unity's CLI only supports one `-testPlatform` per invocation. To run both, execute two sequential commands:

```powershell
# EditMode
& $unityExe -runTests -batchmode -nographics -projectPath $projectPath `
    -testPlatform EditMode `
    -testResults "$projectPath\TestResults\EditMode.xml" `
    -logFile "$projectPath\TestResults\EditMode.log" `
    -forgetProjectPath

# PlayMode
& $unityExe -runTests -batchmode -projectPath $projectPath `
    -testPlatform PlayMode `
    -testResults "$projectPath\TestResults\PlayMode.xml" `
    -logFile "$projectPath\TestResults\PlayMode.log" `
    -forgetProjectPath
```

---

## 5 — Parse Test Results (NUnit XML)

The results file is standard NUnit 3 XML. Here is how to extract a summary and any failures.

### Quick summary (PowerShell)

```powershell
[xml]$results = Get-Content "$projectPath\TestResults\EditMode.xml"
$run = $results.'test-run'

Write-Host "Result : $($run.result)"
Write-Host "Total  : $($run.total)"
Write-Host "Passed : $($run.passed)"
Write-Host "Failed : $($run.failed)"
Write-Host "Skipped: $($run.skipped)"
Write-Host "Duration: $($run.duration)s"
```

### List failed tests with messages

```powershell
function Get-FailedTests {
    param([string]$ResultsPath)

    [xml]$xml = Get-Content $ResultsPath
    $failures = $xml.SelectNodes("//test-case[@result='Failed']")

    foreach ($f in $failures) {
        [PSCustomObject]@{
            Name    = $f.fullname
            Message = $f.failure.message.'#cdata-section'
            Stack   = $f.failure.'stack-trace'.'#cdata-section'
        }
    }
}

Get-FailedTests "$projectPath\TestResults\EditMode.xml" | Format-List
```

### Read results into a one-line pass/fail check

```powershell
[xml]$r = Get-Content "$projectPath\TestResults\EditMode.xml"
if ($r.'test-run'.result -eq 'Passed') { Write-Host "ALL TESTS PASSED" }
else { Write-Host "TESTS FAILED — check results file"; exit 1 }
```

---

## 6 — Complete Copy-Paste Script

Below is a self-contained PowerShell script that agents can use. Adapt paths as needed.

```powershell
# === Configuration ===
$projectPath = "H:\Work\PhysicsDrivenMovementDemo"
$unityVersion = ((Get-Content "$projectPath\ProjectSettings\ProjectVersion.txt") -match "m_EditorVersion")[0].Split(": ")[-1].Trim()
$unityExe     = "C:\Program Files\Unity\Hub\Editor\$unityVersion\Editor\Unity.exe"
$outDir       = "$projectPath\TestResults"

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

# === Run EditMode Tests ===
Write-Host "`n=== Running EditMode Tests ===" -ForegroundColor Cyan
& $unityExe -runTests -batchmode -nographics `
    -projectPath $projectPath `
    -testPlatform EditMode `
    -testResults "$outDir\EditMode.xml" `
    -logFile "$outDir\EditMode.log" `
    -forgetProjectPath

# === Run PlayMode Tests ===
Write-Host "`n=== Running PlayMode Tests ===" -ForegroundColor Cyan
& $unityExe -runTests -batchmode `
    -projectPath $projectPath `
    -testPlatform PlayMode `
    -testResults "$outDir\PlayMode.xml" `
    -logFile "$outDir\PlayMode.log" `
    -forgetProjectPath

# === Parse & Display Results ===
function Show-TestResults {
    param([string]$Label, [string]$Path)

    if (-not (Test-Path $Path)) {
        Write-Host "$Label results file not found at $Path" -ForegroundColor Red
        return
    }

    [xml]$xml = Get-Content $Path
    $run = $xml.'test-run'

    Write-Host "`n--- $Label ---" -ForegroundColor Yellow
    Write-Host "Result : $($run.result)"
    Write-Host "Total  : $($run.total)  |  Passed: $($run.passed)  |  Failed: $($run.failed)  |  Skipped: $($run.skipped)"
    Write-Host "Duration: $($run.duration)s"

    $failures = $xml.SelectNodes("//test-case[@result='Failed']")
    foreach ($f in $failures) {
        Write-Host "`n  FAIL: $($f.fullname)" -ForegroundColor Red
        Write-Host "    Message: $($f.failure.message.'#cdata-section')"
    }
}

Show-TestResults "EditMode" "$outDir\EditMode.xml"
Show-TestResults "PlayMode" "$outDir\PlayMode.xml"
```

---

## 7 — Troubleshooting

| Problem | Solution |
|---------|----------|
| **Unity exits with code 1 but no XML file** | Check the log file (`-logFile`). Usually a compile error. Search for `error CS` in the log. |
| **"Unity is already running"** | Only one Unity instance can open a project at a time. The user must close the Unity Editor before the agent can run tests in batch mode, **or** use a separate copy of the project. |
| **PlayMode tests hang** | Add a timeout: use `-quit` (but note: `-quit` can cause issues if tests haven't finished). Better: set `-testSettingsFile` with a timeout. |
| **No tests found** | Ensure test assemblies have `.asmdef` files referencing `UnityEngine.TestRunner` and `UnityEditor.TestRunner`, and that the test scripts have `[Test]` or `[UnityTest]` attributes. |
| **`-nographics` causes PlayMode failures** | Some PlayMode tests need a GPU. Remove `-nographics` for PlayMode runs. |
| **Exit code 0 but result is "Failed"** | Unity may return 0 even when tests fail. Always parse the XML file for the true result — do not rely solely on the exit code. |
| **Tests pass in Editor but fail in batchmode** | Batch mode doesn't call certain Unity messages the same way. Check for reliance on `EditorApplication` callbacks or GUI-dependent code. |

---

## 8 — Agent Workflow Integration

When following the **Development Workflow** from `CODING_STANDARDS.md`:

### Phase C (Write Tests First)
- After writing tests, run **EditMode** tests to confirm they compile but fail (red).
- Command: use the EditMode command from §2 above.
- Parse the results: confirm `failed > 0` and `result = "Failed"`.

### Phase D (Implement to Pass Tests)
- After implementing, run **all tests** (EditMode + PlayMode) to confirm they pass (green).
- Command: use the full script from §6 above.
- Parse the results: confirm `result = "Passed"` for both.

### Phase E (Verify Feature)
- If any test fails, report the failure details from the XML and return to Phase B.

### Phase F (Pre-Commit Self-Review)
- **F10 (Regression):** Run all tests one final time using §6 and confirm zero failures.

### Important Constraints

1. **The user must close the Unity Editor** before the agent can run batch-mode tests on the same project. Alert the user if Unity is already open.
2. **Large projects** may take several minutes to import on first batch-mode launch. Use the `-logFile` to monitor progress.
3. **Add `TestResults/` to `.gitignore`** to avoid committing generated XML/log files.

---

## 9 — NUnit XML Schema Quick Reference

The results file follows the NUnit 3 XML format. Key elements:

```xml
<test-run result="Passed" total="42" passed="42" failed="0" skipped="0" duration="3.14">
  <test-suite type="Assembly" name="EditModeTests" ...>
    <test-suite type="TestFixture" name="MuscleControllerTests" ...>
      <test-case name="ApplyForce_WhenGrounded_IncreasesVelocity"
                 result="Passed" duration="0.012" />
      <test-case name="ApplyForce_WhenAirborne_AppliesGravity"
                 result="Failed" duration="0.008">
        <failure>
          <message><![CDATA[Expected: 9.8 But was: 0.0]]></message>
          <stack-trace><![CDATA[at MuscleControllerTests.cs:line 42]]></stack-trace>
        </failure>
      </test-case>
    </test-suite>
  </test-suite>
</test-run>
```

Key attributes on `<test-run>`: `result`, `total`, `passed`, `failed`, `skipped`, `duration`.
Key attributes on `<test-case>`: `name`, `fullname`, `result`, `duration`.
Failure details are in `<failure><message>` and `<failure><stack-trace>` (CDATA sections).

---

*End of agent test-running guide. This document is versioned alongside the project.*
