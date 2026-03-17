# Chapter 9: Validation, Debugging, And Tuning Infrastructure

Back to parent plan: [Unified Locomotion Roadmap](../unified-locomotion-roadmap.plan.md)

## Read this chapter when

- adding telemetry, dashboards, or focused regression slices for locomotion work
- capturing and comparing baseline snapshots
- defining failure-triage workflow for locomotion regressions

## Dependencies

- This chapter runs continuously across the roadmap.
- Use it alongside whichever runtime chapter you are actively changing.

## Objective

Prevent the unified system from becoming a black box.

## Primary touchpoints

- Assets/Scripts/Character/FallPoseRecorder.cs
- Assets/Scripts/Character/LapDemoRunner.cs
- Assets/Tests/PlayMode/Character/MovementQualityTests.cs
- Assets/Tests/PlayMode/Character/GaitOutcomeTests.cs
- Assets/Tests/PlayMode/Character/HardSnapRecoveryTests.cs
- Tools/Write-TestSummary.ps1
- Tools/ParseResults.ps1
- parse_results.ps1
- summary.ps1
- AGENT_TEST_RUNNING.md

## Related artifacts

- [LOCOMOTION_BASELINES.md](../../LOCOMOTION_BASELINES.md)
- [DEBUGGING.md](../../DEBUGGING.md)

## Status

- State: In Progress.
- Current next step: C9.2b.
- Active blockers: None.
- Known pre-existing failures: `MovementQualityTests.WalkStraight_NoFalls`, `MovementQualityTests.SustainedLocomotionCollapse_TransitionsIntoFallen` (fail on baseline since C1, unrelated to C9 scope).

## Work packages

Each task is scoped so a single agent pass can implement, verify, and commit it.

### C9.1 Scenario matrix

Define the canonical set of named locomotion scenarios used by every test, baseline, and telemetry capture across the roadmap. This is a documentation + data task; no runtime code changes.

- [x] **C9.1a Create `Assets/Tests/PlayMode/Utilities/ScenarioDefinitions.cs`**
  - Scope: Add a static class `ScenarioDefinitions` with one `static readonly` entry per named scenario. Each entry is a struct containing: a string `Name`, a `Vector3[]` waypoint sequence (reuse or extract from existing tests), a `float` expected duration budget (seconds), and a `string[]` list of subsystems it exercises (e.g. `"gait"`, `"recovery"`, `"terrain"`).
  - Scenarios to define (extract waypoints from existing tests where available):
    1. **StartStop** — 2 waypoints 8 m apart; exercises acceleration and deceleration. Source: `MovementQualityTests` straight course first two waypoints.
    2. **Reversal** — 3 collinear waypoints: forward 10 m, back to origin, forward 10 m. Exercises 180° reversal. No existing source; define inline.
    3. **HardTurn90** — `HardSnapRecoveryTests` 90-degree snap waypoints (extract from test).
    4. **Slalom5** — `HardSnapRecoveryTests` slalom waypoints (extract from test).
    5. **StumbleRecovery** — `StumbleStutterRegressionTests` waypoints (extract or mirror; scenario should include an abrupt direction change that historically produces stumble).
    6. **TerrainStepUp** — `GaitOutcomeTests.DirectApproachIntoStepUpLane` spawn + waypoint (extract).
    7. **TerrainSlope** — `GaitOutcomeTests.WalkUpSlopeLane` spawn + waypoint (extract).
    8. **LapCircuit** — `LapDemoRunner.CourseWaypoints` full 24-waypoint Top Gear circuit (reference directly, do not duplicate).
    9. **LongRunFatigue** — LapCircuit repeated 3 times. Budget = 3× single-lap budget.
  - Done when: `ScenarioDefinitions.All` returns all 9 entries; an EditMode test asserts count, unique names, and non-empty waypoints.
  - Verification: EditMode compile + new EditMode test `ScenarioDefinitionsTests.AllScenariosAreValid`.
  - 2026-03-17: Added `ScenarioDefinitions` under `Assets/Tests/PlayMode/Utilities/` with all 9 canonical entries, terrain lane anchors derived from the current `Arena_01` terrain-gallery geometry, and `LapCircuit` sourced directly from `LapDemoRunner.CourseWaypoints` via reflection. Added reflection-backed EditMode coverage in `ScenarioDefinitionsTests`; focused verification passed `1/1`.

- [x] **C9.1b Migrate existing tests to use `ScenarioDefinitions`**
  - Scope: In `HardSnapRecoveryTests`, `MovementQualityTests`, `StumbleStutterRegressionTests`, and `GaitOutcomeTests`, replace inline waypoint arrays with references to the corresponding `ScenarioDefinitions` entry. Keep assertion thresholds unchanged.
  - Done when: All migrated tests still pass with identical waypoints sourced from `ScenarioDefinitions`.
  - Verification: PlayMode focused filter `HardSnapRecoveryTests;MovementQualityTests;StumbleStutterRegressionTests;GaitOutcomeTests` — all existing green tests stay green.
  - 2026-03-17: Added `ScenarioPathUtility` under `Assets/Tests/PlayMode/Utilities/` so scenario-driven suites can derive shared planar directions and `PlayerMovement` test inputs from the canonical Chapter 9 scenario catalog. `HardSnapRecoveryTests` now source hard-turn/slalom directions from `HardTurn90`, `Slalom5`, and `StartStop`; `MovementQualityTests` now build their straight/corner courses from `StartStop` and `HardTurn90` geometry; `StumbleStutterRegressionTests` now source sustained-forward, hard-turn, and reversal inputs from `StartStop`, `HardTurn90`, and `Reversal`; and `GaitOutcomeTests` now source flat-ground motion plus the step-up/slope lane anchors from `StartStop`, `TerrainStepUp`, and `TerrainSlope`. Focused verification passed `16/18`, with only the two known pre-existing `MovementQualityTests` reds remaining (`WalkStraight_NoFalls`, `SustainedLocomotionCollapse_TransitionsIntoFallen`).

### C9.2 Decision telemetry

Add structured per-event logging to `LocomotionDirector` so recovery decisions are traceable after the fact. Currently, only a throttled one-line observation string is emitted; there is no record of *why* a recovery started, escalated, or ended.

- [x] **C9.2a Create `Assets/Scripts/Character/Locomotion/RecoveryTelemetryEvent.cs`**
  - Scope: Define a lightweight `readonly struct RecoveryTelemetryEvent` with fields: `int FrameNumber`, `float Time`, `RecoverySituation Situation`, `string Reason` (short tag like `"slip_exceeded"`, `"angle_above_ceiling"`, `"exit_cooldown_elapsed"`), `float UprightAngle`, `float SlipEstimate`, `float SupportQuality`, `float TurnSeverity`. Add a `ToNdjsonLine()` method returning a single JSON string (manual concatenation, no dependency on Newtonsoft).
  - Done when: EditMode test instantiates struct, calls `ToNdjsonLine()`, and asserts the output contains all field names.
  - Verification: EditMode compile + new EditMode test `RecoveryTelemetryEventTests.ToNdjsonLine_ContainsAllFields`.
  - 2026-03-17: Added `RecoveryTelemetryEvent` under `Assets/Scripts/Character/Locomotion/` as an internal immutable payload with manual NDJSON serialization and JSON-string escaping for the `Situation`/`Reason` tags. Added focused reflection-backed EditMode coverage in `RecoveryTelemetryEventTests`; targeted verification passed `1/1`.

- [ ] **C9.2b Wire `RecoveryTelemetryEvent` emission into `LocomotionDirector`**
  - Scope: In `LocomotionDirector`, add a `[SerializeField] bool _enableRecoveryTelemetry` (default false) and a `List<RecoveryTelemetryEvent>` ring buffer (capacity 256). Emit an event at: (1) recovery entry (with the triggering observation values), (2) recovery situation change (e.g. Stumble→NearFall), (3) recovery exit (with exit reason), (4) surrender trigger. Expose `IReadOnlyList<RecoveryTelemetryEvent> RecoveryTelemetryLog` for test queries.
  - Done when: Enabling `_enableRecoveryTelemetry` in a PlayMode test that drives through a hard turn produces ≥2 logged events (entry + exit).
  - Verification: New PlayMode test `LocomotionDirectorTests.RecoveryTelemetry_HardTurnScenario_LogsEntryAndExit` using `ScenarioDefinitions.HardTurn90` waypoints. Existing `LocomotionDirectorTests` still green.

- [ ] **C9.2c Add recovery duration and outcome fields**
  - Scope: Extend `RecoveryTelemetryEvent` with `float RecoveryDurationSoFar` (time since recovery entry) and `bool WasSurrender` (true only on surrender events). In `LocomotionDirector`, track `_recoveryEntryTime` and populate these fields at exit/surrender. Expose `float LastRecoveryDuration` and `bool LastRecoveryEndedInSurrender` as public read-only properties.
  - Done when: PlayMode test drives a scenario that triggers surrender (angle stuck above 50° for >0.8 s), asserts `LastRecoveryEndedInSurrender == true` and `LastRecoveryDuration > 0.8f`.
  - Verification: New PlayMode test `LocomotionDirectorTests.RecoveryTelemetry_SurrenderScenario_RecordsDurationAndOutcome`. Existing tests still green.

### C9.3 Outcome dashboards

Expand the PowerShell summary pipeline so post-run output includes quantitative locomotion metrics, not just pass/fail counts.

- [ ] **C9.3a Emit tagged metric lines from PlayMode tests**
  - Scope: In `MovementQualityTests`, `HardSnapRecoveryTests`, `SpinRecoveryTests`, and `GaitOutcomeTests`, after each assertion block emit a `TestContext.Out.WriteLine` line with the format `[METRIC] <TestName> <MetricName>=<Value>`. Metrics to emit:
    - `MovementQualityTests.WalkStraight_NoFalls`: `Displacement`, `MaxConsecutiveFallenFrames`, `Completed`
    - `HardSnapRecoveryTests.HardSnap90_*`: `RecoveryFrame`, `PostTurnProgress`, `MaxStalledFrames`
    - `HardSnapRecoveryTests.Slalom5_*`: per-segment `SegmentProgress`, `RecoveryFrames`, `MaxStalledFrames`
    - `SpinRecoveryTests.*`: `ForwardDisplacement`, `YawAngularVelocityAtFrame150`, `CrossoverFrames`
    - `GaitOutcomeTests.HoldingMoveInput_*`: `Displacement5s`, `UpperLegPeakRotation`, `SimultaneousForwardFraction`
  - Done when: A PlayMode run of these tests produces `[METRIC]` lines in NUnit XML output.
  - Verification: Run focused filter, parse XML, grep for `[METRIC]` — at least 10 metric lines present.

- [ ] **C9.3b Extend `Write-TestSummary.ps1` to extract and display metrics**
  - Scope: In `Tools/Write-TestSummary.ps1`, after the pass/fail summary section, add a "Metrics" section that parses `[METRIC]` lines from NUnit XML `<output>` elements. Group by test name, display as a Markdown table: `| Test | Metric | Value |`. Write to `TestResults/latest-summary.md` as a new `## Metrics` heading after the existing `## Failures` section.
  - Done when: Running `./summary.ps1` after a PlayMode run that contains `[METRIC]` lines produces a readable metrics table in `TestResults/latest-summary.md`.
  - Verification: Run summary script, inspect output file for `## Metrics` section with ≥10 rows.

- [ ] **C9.3c Add baseline comparison to summary output**
  - Scope: Create `TestResults/metric-baselines.json` with the current metric values from LOCOMOTION_BASELINES.md Chapter 8 snapshot (e.g., `HardSnap90.RecoveryFrame: 56`, `Slalom5.MaxStalledFrames: 4`). In `Write-TestSummary.ps1`, load this JSON and, for each `[METRIC]` line that has a baseline entry, append a `Delta` column showing `+N` / `-N` / `=` relative to baseline. Flag any metric where the delta exceeds a configurable threshold (default: 20% regression) with a `⚠` marker.
  - Done when: Summary output shows `Delta` column and at least one `=` or numeric delta for a known metric.
  - Verification: Run summary, inspect `latest-summary.md` metrics table for delta column.

### C9.4 Continuous regression slices

Define and document stable focused test filters per subsystem so agents and CI can run narrow slices without guessing filter strings.

- [ ] **C9.4a Create `Tools/test-slices.json`**
  - Scope: Create a JSON file with named filter entries. Each entry has: `name` (string), `platform` (`EditMode` or `PlayMode`), `filter` (semicolon-separated NUnit filter string), `description` (one sentence), `expectedGreenCount` (int, current known passing count). Define these slices:
    1. `"editmode-all"` — `"PhysicsDrivenMovement.Tests.EditMode"`, expected 119.
    2. `"gait-core"` — `GaitOutcomeTests;LegAnimatorTests;LegAnimatorSprintStrideTests`, expected count from current passing.
    3. `"recovery-hard"` — `HardSnapRecoveryTests;SpinRecoveryTests;StumbleStutterRegressionTests`, expected count.
    4. `"balance-stability"` — `BalanceControllerTests;Arena01BalanceStabilityTests;BalanceControllerTurningTests`, expected count.
    5. `"knockdown-recovery"` — `SurrenderTests;ImpactKnockdownTests;FloorDwellTests;ProceduralStandUpTests;GetUpReliabilityTests;HardSnapRecoveryTests`, expected count.
    6. `"jump"` — `JumpTests`, expected count.
    7. `"expression"` — `ArmAnimatorPlayModeTests;SprintLeanOutcomeTests;SprintBalanceOutcomeTests;MovementQualityTests`, expected count.
    8. `"full-stack"` — `FullStackSanityTests;LapCourseTests`, expected count.
    9. `"director"` — `LocomotionDirectorTests`, expected count.
  - Done when: File parses as valid JSON; each entry has all required fields.
  - Verification: PowerShell one-liner: `Get-Content Tools/test-slices.json | ConvertFrom-Json | ForEach-Object { $_.name }` lists 9 names.

- [ ] **C9.4b Add `Run-Slice.ps1` wrapper script**
  - Scope: Create `Tools/Run-Slice.ps1` that accepts `-SliceName <string>` (required), looks up the entry in `Tools/test-slices.json`, calls `Tools/Run-UnityTests.ps1` with the correct `-Platform` and `-TestFilter`, then calls `Tools/Write-TestSummary.ps1` to produce a summary. Print a one-line PASS/FAIL verdict comparing actual passed count against `expectedGreenCount`. Exit code 0 on match, 1 on mismatch.
  - Done when: `.\Tools\Run-Slice.ps1 -SliceName "jump"` runs JumpTests and prints verdict.
  - Verification: Run with `"editmode-all"` slice and confirm exit code 0 + correct count.

- [ ] **C9.4c Document slices in `AGENT_TEST_RUNNING.md`**
  - Scope: Add a new section `## Focused Regression Slices` to `AGENT_TEST_RUNNING.md` with: a table listing each slice name, platform, description, and expected green count; the `Run-Slice.ps1` invocation syntax; guidance on when to use which slice (e.g., "after touching BalanceController, run `balance-stability`; after touching LegAnimator, run `gait-core`").
  - Done when: Section exists and is consistent with `test-slices.json`.
  - Verification: Visual review.

### C9.5 Failure triage workflow

Codify the step-by-step process for diagnosing a locomotion test failure, from high-level summary to root-cause decision layer.

- [ ] **C9.5a Extend `DEBUGGING.md` with locomotion failure triage flowchart**
  - Scope: Add a new section `## Locomotion Failure Triage` to `DEBUGGING.md` containing a numbered step-by-step decision list:
    1. Run `./summary.ps1` or `./Tools/Run-Slice.ps1 -SliceName <relevant>` — read `TestResults/latest-summary.md`.
    2. Check failure classification: if "known pre-existing", skip. If "new or unclassified", continue.
    3. Check the `## Metrics` table: did any metric regress >20% from baseline? Note which.
    4. Identify the decision layer: is the failure in a recovery test (→ check `RecoveryTelemetryLog`), a gait test (→ check leg-state transitions), a balance test (→ check upright angle timeline), or an expression test (→ check cap violations)?
    5. Reproduce in isolation: run the single failing test with `-TestFilter` and `_enableRecoveryTelemetry = true` if applicable.
    6. If the decision layer looks correct but the outcome is wrong → the bug is in the actuator (BalanceController, LegAnimator, ArmAnimator). If the decision layer made the wrong call → the bug is in the observation model or LocomotionDirector thresholds.
    7. Write a focused failing test for the root cause before fixing.
    8. After fix, run the relevant slice and refresh `metric-baselines.json` if thresholds changed.
  - Done when: Section exists and references the new telemetry and slice infrastructure from C9.2–C9.4.
  - Verification: Visual review; section references `RecoveryTelemetryLog`, `test-slices.json`, `metric-baselines.json`, and `latest-summary.md`.

- [ ] **C9.5b Add `FallPoseRecorder` auto-trigger documentation and test-seam**
  - Scope: In `FallPoseRecorder.cs`, add a public method `InjectTriggerForTest(string reason)` that calls `TriggerRollingCapture` without requiring keyboard input (for automated triage). In `DEBUGGING.md`, add a sub-section under the triage flowchart explaining: how to enable `FallPoseRecorder` on the prefab, how to trigger capture from a test, how to read the NDJSON output (`Logs/fall-pose-log.ndjson`), and how to correlate timestamps with NUnit test output.
  - Done when: A PlayMode test can call `FallPoseRecorder.InjectTriggerForTest("test-driven")` and assert `CompletedSessionCount > 0` after a few seconds.
  - Verification: New PlayMode test `FallPoseRecorderTests.InjectTrigger_CapturesSession`.

### C9.6 Baseline refresh ceremony

Define the repeatable process and tooling for capturing a new baseline snapshot when behavior expectations change.

- [ ] **C9.6a Create `Tools/Capture-Baseline.ps1`**
  - Scope: Create a script that: (1) runs every slice in `test-slices.json` sequentially, (2) collects all `[METRIC]` values from the NUnit XML, (3) writes them to `TestResults/metric-baselines.json` (overwriting previous), (4) appends a new dated section to `LOCOMOTION_BASELINES.md` with the full metric table and pass/fail counts per slice, (5) prints a summary diff against the previous baselines if the old JSON existed.
  - Done when: Running `.\Tools\Capture-Baseline.ps1` produces updated `metric-baselines.json` and a new section in `LOCOMOTION_BASELINES.md`.
  - Verification: Run the script; inspect both output files for completeness.

- [ ] **C9.6b Document baseline refresh in chapter plan and `LOCOMOTION_BASELINES.md`**
  - Scope: Add a "When to refresh" section at the top of `LOCOMOTION_BASELINES.md` with rules: refresh after completing any roadmap chapter, after changing assertion thresholds, or after fixing a known pre-existing failure. Reference `Tools/Capture-Baseline.ps1` as the canonical command. Add a "Baseline Refresh" sub-section to this chapter's verification gate explaining the same.
  - Done when: Both files reference the script and the trigger conditions.
  - Verification: Visual review.

## Verification gate

- Keep the focused slice for the active chapter green.
- Refresh baseline artifacts via `Tools/Capture-Baseline.ps1` when thresholds or behavior expectations change.
- Prefer `TestResults/latest-summary.md` for first-pass triage, then open raw XML or logs only when the digest is insufficient.
- Verify new telemetry is interpretable from NUnit XML or stored logs, not only the Unity Console.
- After completing C9.4, use `Tools/Run-Slice.ps1` as the primary verification command for all future roadmap chapters.

## Exit criteria

- Every major locomotion failure can be traced to a specific decision and observation snapshot via `RecoveryTelemetryLog` and `FallPoseRecorder`.
- All 9 named scenarios are defined in `ScenarioDefinitions` and referenced by at least one test.
- `TestResults/latest-summary.md` includes a metrics table with baseline deltas.
- `DEBUGGING.md` contains a concrete triage flowchart referencing the telemetry and slice infrastructure.
- `Tools/Capture-Baseline.ps1` can reproduce the full baseline snapshot in one command.