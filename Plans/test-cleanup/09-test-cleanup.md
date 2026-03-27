# Plan 09 — Test Suite Cleanup

## Goal

Reduce the PlayMode test suite from ~362 tests to ~92 outcome-focused tests.
Fix all currently-failing tests. Get to a genuinely clean baseline.

## Execution Status

- State: Active
- Branch: `plan/09-test-cleanup`
- Current step: Stage 2 targeted fixes and focused verification
- 2026-03-27: Stage 1 complete. Deleted the 30 listed PlayMode fixture files and their `.meta` files.
- 2026-03-27: Preserved unrelated pre-existing dirty files outside this slice: `Assets/Prefabs/PlayerRagdoll_Skinned.prefab`, `Assets/Scenes/Arena_01.unity`, `PhysicsDrivenMovementDemo.slnx`.
- 2026-03-27: Isolated `WalkDownStepDownLane` still fails, but the fresh log shows `maxConsecutiveFallenFrames=175`, `totalFallenTransitions=1`, `stateEnd=Moving`, and `maxProgress=3.27m`, so treat it as a threshold/tuning update rather than a runtime regression.

## Principles

Every kept test must answer: "If this feature broke completely, would a player notice?"
Internal mechanics, field existence checks, and private-state assertions are deleted.
Outcome-based tests (falls, speed, course completion, recovery time) are kept.

---

## Branch

Create branch `plan/09-test-cleanup` from `master`.

---

## Stage 1 — Delete internal/fragile test files

Delete both the `.cs` and `.cs.meta` file for each of the following.
Use `git rm` for each pair so the deletion is tracked.

```
Assets/Tests/PlayMode/Character/LegAnimatorTests.cs
Assets/Tests/PlayMode/Character/LegAnimatorSprintStrideTests.cs
Assets/Tests/PlayMode/Character/BalanceControllerTests.cs
Assets/Tests/PlayMode/Character/BalanceControllerIntegrationTests.cs
Assets/Tests/PlayMode/Character/BalanceControllerOptimizerTests.cs
Assets/Tests/PlayMode/Character/BalanceControllerTurningTests.cs
Assets/Tests/PlayMode/Character/CharacterStateTests.cs
Assets/Tests/PlayMode/Character/FloorDwellTests.cs
Assets/Tests/PlayMode/Character/FootSlidingTests.cs
Assets/Tests/PlayMode/Character/RagdollSetupTests.cs
Assets/Tests/PlayMode/Character/LegJointSpringTests.cs
Assets/Tests/PlayMode/Character/LocomotionDirectorTests.cs
Assets/Tests/PlayMode/Character/PlayerMovementTests.cs
Assets/Tests/PlayMode/Character/MomentumLeanTests.cs
Assets/Tests/PlayMode/Character/ArmAnimatorPlayModeTests.cs
Assets/Tests/PlayMode/Character/ArmSwingVariationTests.cs
Assets/Tests/PlayMode/Character/IdleSwayTests.cs
Assets/Tests/PlayMode/Character/IdleVerticalBobTests.cs
Assets/Tests/PlayMode/Character/StrideAsymmetryTests.cs
Assets/Tests/PlayMode/Character/LateralPlacementNoiseTests.cs
Assets/Tests/PlayMode/Character/OrganicGaitVariationTests.cs
Assets/Tests/PlayMode/Character/PlayerRagdollPrefabPlayModeTests.cs
Assets/Tests/PlayMode/Character/FullStackSanityTests.cs
Assets/Tests/PlayMode/Character/SprintLeanOutcomeTests.cs
Assets/Tests/PlayMode/Character/SprintBalanceOutcomeTests.cs
Assets/Tests/PlayMode/Character/GaitOptimizerTests.cs
Assets/Tests/PlayMode/Character/FallPoseRecorderTests.cs
Assets/Tests/PlayMode/Character/CameraFollowTests.cs
Assets/Tests/PlayMode/Character/ForwardRunDiagnosticTests.cs
Assets/Tests/PlayMode/Character/LapOptimizerTests.cs
```

Commit: `chore(09): delete internal/fragile test files - keep outcome tests only`

---

## Stage 2 — Fix failing tests

### 2a. SurrenderTests — upright scale not zeroing on surrender

File: `Assets/Tests/PlayMode/Character/SurrenderTests.cs`
File: `Assets/Scripts/Character/BalanceController.cs`

**Root cause:** In `BalanceController.cs` around line 1016, `UprightStrengthScale = 0f` is
gated behind `CurrentState == Fallen`. But surrender can fire while the character is still
`Standing` (large impulse tilts it fast). The scale never hits zero until Fallen is reached.

**Fix in `BalanceController.cs`:** Remove the `CurrentState == Fallen` gate. The block should
fire whenever `IsSurrendered` is true, regardless of state:

```csharp
if (IsSurrendered)
{
    UprightStrengthScale = 0f;
    HeightMaintenanceScale = 0f;
    StabilizationScale = 0f;
    _suppressPelvisExpression = true;
}
```

**Verify** the other 3 SurrenderTests still pass after this change — they should, as surrender
is already working, just the scale zeroing was gated wrongly.

### 2b. WalkDownStepDownLane — 207 fallen frames vs limit 80

File: `Assets/Tests/PlayMode/Character/GaitOutcomeTests.cs`

**Diagnosis needed first:** Run this test in isolation and check the log output for what's
actually happening. Is the character falling repeatedly, or is it entering Fallen once and
not recovering?

Run:
```
powershell -NoProfile -ExecutionPolicy Bypass -File "H:\Work\PhysicsDrivenMovementDemo\Tools\Run-UnityTests.ps1" -ProjectPath "H:\Work\PhysicsDrivenMovementDemo" -Platform PlayMode -MaxAttemptsPerPlatform 1 -Unattended -TestFilter "WalkDownStepDownLane"
```

Check the result. Then decide:
- If it's a tuning issue (step-down geometry is harsher than when the test was written):
  raise the `MaxConsecutiveFallenFrames` limit from 80 to a value that passes with breathing
  room (actual + 30%). Comment explaining the change.
- If it's a real regression (character used to pass this, now doesn't): investigate
  `LegAnimator` step-down behaviour and fix the root cause. Do NOT raise the limit if the
  character is clearly broken.

### 2c. GetUpReliabilityTests — impulse not strong enough to destabilise

File: `Assets/Tests/PlayMode/Character/GetUpReliabilityTests.cs`

The tests apply a directional impulse and expect the character to either enter Fallen or
reach >20° tilt. Max observed tilt is ~14° — the character is too stable now.

**Fix:** Double the impulse magnitude. Find the impulse value in the test, multiply by 2.
Re-run in isolation to confirm the character actually destabilises. If it still doesn't
reach 20°, triple it. The point is to reliably destabilise — the exact magnitude doesn't
matter as long as it's a realistic in-game force.

Run:
```
powershell -NoProfile -ExecutionPolicy Bypass -File "H:\Work\PhysicsDrivenMovementDemo\Tools\Run-UnityTests.ps1" -ProjectPath "H:\Work\PhysicsDrivenMovementDemo" -Platform PlayMode -MaxAttemptsPerPlatform 1 -Unattended -TestFilter "GetUpReliabilityTests"
```

### 2d. Order-sensitive failures — dirty sprint state between tests

Tests affected:
- `ApplyMovementForces_WhenSprintHeldInMovingState_AcceleratesFasterThanWalk` (PlayerMovementTests — being deleted, skip)
- `ArmPushFail_ReEntersFallenWithShortSeverity` (ProceduralStandUpTests)
- `WhenBiasActive_BackwardPhaseSwingIsNeutral` (LegAnimatorTests — being deleted, skip)

Only `ArmPushFail_ReEntersFallenWithShortSeverity` needs fixing since the others are in
deleted files.

File: `Assets/Tests/PlayMode/Character/ProceduralStandUpTests.cs`

Check the TearDown — if `SetSprintInputForTest(false)` is missing, add it. Also check
whether `SetMoveInputForTest(Vector2.zero)` is called in TearDown. Both should be there.

### 2e. Write-TestSummary.ps1 — clean up known-pre-existing list

File: `Tools/Write-TestSummary.ps1`

After fixes above, remove the entries from `$knownPreExistingPatterns` that are now either:
- Fixed (no longer failing)
- Deleted (test file is gone)

Keep only the genuinely still-broken ones:
- `WalkStraight_NoFalls`
- `SustainedLocomotionCollapse_TransitionsIntoFallen`
- `LapCourseTests.CompleteLap_WithinTimeLimit_NoFalls`
- `TurnAndWalk_CornerRecovery`

Commit all Stage 2 fixes together:
`fix(09): fix SurrenderTests scale gate, GetUpReliability impulse, ProceduralStandUp teardown`

---

## Stage 3 — Verification

Run the full suite (narrow — exclude LapOptimizer only):
```
powershell -NoProfile -ExecutionPolicy Bypass -File "H:\Work\PhysicsDrivenMovementDemo\Tools\Run-UnityTests.ps1" -ProjectPath "H:\Work\PhysicsDrivenMovementDemo" -Platform PlayMode -MaxAttemptsPerPlatform 1 -Unattended -TestFilter "!LapOptimizer_ComprehensiveFullPrefab"
```

**Expected result:**
- Total tests: ~90-100
- Failures: 3-4 (only the known-broken course navigation ones)
- Zero "new or unclassified" failures in the summary

If any unexpected failures appear: fix them before committing. Do NOT raise thresholds to
paper over real failures — investigate first.

---

## What NOT to change

- Do NOT touch `LapCourseTests.cs` — course navigation is a separate problem
- Do NOT touch `MovementQualityTests.cs` — `WalkStraight_NoFalls` and
  `SustainedLocomotionCollapse` are known-broken, leave them classified
- Do NOT modify `Run-UnityTests.ps1`
- Do NOT delete any EditMode test files
- Do NOT delete test Utility files (`PlayerPrefabTestRig.cs`, `GhostDriver.cs`, etc.)

---

## Commits summary

1. `chore(09): delete internal/fragile test files - keep outcome tests only`
2. `fix(09): fix SurrenderTests scale gate, GetUpReliability impulse, ProceduralStandUp teardown`
3. `chore(09): clean up Write-TestSummary known-pre-existing list`

---

## On completion

Write `agent-slice-status.json` at the project root:
```json
{"slice":"09-test-cleanup","status":"pass","branch":"plan/09-test-cleanup","notes":"<one line: test count before/after, failures before/after>"}
```

Then send Telegram notification:
```powershell
$body = @{ chat_id = "8630971080"; text = "[plan-09] STATUS: pass/fail. Tests: X -> Y total, Z failures remaining. NOTES: one line summary" } | ConvertTo-Json
Invoke-RestMethod -Uri "https://api.telegram.org/bot8733821405:AAHgYbzmTD7SiIrdkh_rVs7ujTxQtKdfpDg/sendMessage" -Method Post -Body $body -ContentType "application/json"
```
