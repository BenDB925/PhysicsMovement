# Sprint-Jump Stability Tests — Implementation Plan

Back to parent plan: [Unified Locomotion Roadmap](unified-locomotion-roadmap.plan.md)  
Chapter 9 anchor: [09-validation-debugging-and-tuning.md](unified-locomotion-roadmap/09-validation-debugging-and-tuning.md)

## Problem

Sprinting and then jumping causes the character to fall flat on its face on landing with high likelihood. No existing test covers the **sprint → jump → land → continue sprinting** sequence. All current jump tests are stationary or walking-speed, and all sprint tests never trigger a jump.

## Goal

Create a new PlayMode test fixture `SprintJumpStabilityTests` that reproduces the instability and fails clearly when the torso faceplants post-landing. The tests must be specific enough to act as a regression gate once the bug is fixed.

## Current status

- State: Complete
- Current next step: Resume Chapter 9 C9.3a and leave this child plan as the known-red sprint-jump baseline. If `Tools/test-slices.json` lands before then, backfill the skipped Task 7 `sprint-jump` slice entry.
- Verified artifacts: Fresh Task 9 baseline artifacts are `TestResults/latest-summary.md`, `TestResults/PlayMode.xml`, and `Logs/test_playmode_20260318_075506.log`, which capture the focused PlayMode result `Result=Failed, Total=5, Passed=3, Failed=2` plus the emitted sprint-jump metrics.
- Open observation: The Task 9 rerun kept the same failure shape. The smoke path still ended `PeakTilt=96.2`, `PeakSpeed=4.06`, `FinalState=Fallen`, `FinalTilt=86.5`, `Airborne1=True`, and `Airborne2=False`, while the telemetry path again captured two recovery windows that terminated with `angle_above_ceiling` plus `recovery_surrendered` at `UprightAngle=86.56069` after `RecoveryDurationSoFar=3.649998`. That explains why the shared helper still ends fallen and never reaches a second valid airborne state.
- Repo note: the live codebase exposes `LocomotionDirector` and `RecoveryTelemetryEvent` under `PhysicsDrivenMovement.Character`, not a separate `PhysicsDrivenMovement.Character.Locomotion` namespace.

## Location

`Assets/Tests/PlayMode/Character/SprintJumpStabilityTests.cs`

## Dependencies

| File | Role |
|------|------|
| `Assets/Tests/PlayMode/Utilities/PlayerPrefabTestRig.cs` | Scene setup, prefab spawn, component access |
| `Assets/Tests/PlayMode/Utilities/ScenarioDefinitions.cs` | `StartStop` scenario for forward waypoints/direction |
| `Assets/Tests/PlayMode/Utilities/ScenarioPathUtility.cs` | `ToMoveInput()` for deriving a `Vector2` from direction |
| `Assets/Scripts/Character/PlayerMovement.cs` | `SetMoveInputForTest()`, `SetSprintInputForTest()`, `SetJumpInputForTest()` |
| `Assets/Scripts/Character/BalanceController.cs` | `UprightAngle`, `IsFallen`, `IsGrounded` |
| `Assets/Scripts/Character/CharacterState.cs` | `CurrentState`, `OnStateChanged` |
| `Assets/Scripts/Character/Locomotion/LocomotionDirector.cs` | `RecoveryTelemetryLog` (opt-in ring buffer), `_enableRecoveryTelemetry` |
| `Assets/Scripts/Character/FallPoseRecorder.cs` | `InjectTriggerForTest()` if available, else manual trigger |

---

## Frame-Time Constants

All tests use `Time.fixedDeltaTime = 0.01f` (100 Hz physics). Frame counts below are at that rate.

| Label | Duration | Frames | Purpose |
|-------|----------|--------|---------|
| `SettleFrames` | 0.8 s | 80 | Let ragdoll reach resting balance after spawn |
| `SprintRampFrames` | 500 | 5.0 s | Sprint forward to reach a higher sustained sprint speed before jump 1 |
| `JumpFrame` | 1 | — | Single frame: `SetJumpInputForTest(true)` (consumed automatically) |
| `PostJumpSettleFrames` | 200 | 2.0 s | Wait for airborne → land → stabilise |
| `SecondSprintFrames` | 500 | 5.0 s | Sprint forward again to reload speed before jump 2 |
| `SecondJumpFrame` | 1 | — | Second jump |
| `FinalSettleFrames` | 200 | 2.0 s | Wait for second landing + stabilise |
| `TotalBudgetFrames` | ~1481 | ~14.8 s | Total test budget (fail-safe timeout) |

---

## Work Packages

### Task 1 — Create `SprintJumpStabilityTests.cs` scaffold

**File:** `Assets/Tests/PlayMode/Character/SprintJumpStabilityTests.cs`

**Namespace:** `PhysicsDrivenMovement.Tests.PlayMode`

**Using directives (exact list):**
```csharp
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using PhysicsDrivenMovement.Character;
using UnityEngine;
using UnityEngine.TestTools;
```

**Class declaration:**
```csharp
public class SprintJumpStabilityTests
```

**Constants to define (at top of class):**
```csharp
private const int SettleFrames = 80;
private const int SprintRampFrames = 500;       // 5 s at 100 Hz
private const int PostJumpSettleFrames = 200;    // 2 s landing window
private const int SecondSprintFrames = 500;      // 5 s second sprint leg
private const int FinalSettleFrames = 200;       // 2 s second landing window
private const float FaceplantAngleThreshold = 45f;  // degrees — above this = faceplant
private const float StableUprightCeiling = 25f;     // degrees — must recover below this
private const int PostLandStabilityDeadline = 150;   // 1.5 s after grounding to regain upright
```

**Fields:**
```csharp
private PlayerPrefabTestRig _rig;
```

**`[SetUp]` method — exact implementation:**
```csharp
[SetUp]
public void SetUp()
{
    _rig = PlayerPrefabTestRig.Create(new PlayerPrefabTestRig.Options
    {
        TestOrigin   = new Vector3(200f, 0f, 200f), // isolated from Arena geometry
        SpawnOffset  = new Vector3(0f, 0.5f, 0f),
        GroundScale  = new Vector3(400f, 1f, 400f),
    });
}
```

**`[TearDown]` method — exact implementation:**
```csharp
[TearDown]
public void TearDown()
{
    _rig?.Dispose();
    _rig = null;
}
```

**Commit gate:** File compiles, `[SetUp]`/`[TearDown]` run without error (add a no-op `[UnityTest]` that just yields `WarmUp` and returns, to verify the rig works).

- 2026-03-18: Completed. Added the scaffold fixture at `Assets/Tests/PlayMode/Character/SprintJumpStabilityTests.cs` and verified the warm-up test with `Tools/Run-UnityTests.ps1 -Platform PlayMode -TestFilter "PhysicsDrivenMovement.Tests.PlayMode.SprintJumpStabilityTests"` (`Result=Passed, Total=1, Passed=1, Failed=0`).

---

### Task 2 — Shared helper: `RunSprintJumpSequence()`

Add a private `IEnumerator` helper inside `SprintJumpStabilityTests` that encapsulates the full "sprint → jump → land → sprint → jump → land" timeline and collects diagnostic data into a results struct for assertions.

**Results struct (nested inside the test class):**
```csharp
private sealed class SprintJumpDiagnostics
{
    // ── Per-jump records ──────────────────────────────────────
    public float MaxUprightAngleAfterJump1;
    public float MaxUprightAngleAfterJump2;
    public bool  EnteredFallenAfterJump1;
    public bool  EnteredFallenAfterJump2;
    public int   FramesToRecoverAfterJump1; // -1 if never recovered
    public int   FramesToRecoverAfterJump2; // -1 if never recovered
    public bool  WasAirborneAfterJump1;
    public bool  WasAirborneAfterJump2;

    // ── Continuous tracking ───────────────────────────────────
    public float PeakUprightAngleOverall;
    public float PeakSprintSpeed;
    public bool  EverEnteredFallen;
    public CharacterStateType FinalState;
    public float FinalUprightAngle;

    // ── Telemetry sample lists (for [METRIC] emission) ───────
    public List<float> UprightAngleSamplesAfterJump1 = new List<float>();
    public List<float> UprightAngleSamplesAfterJump2 = new List<float>();
}
```

**`RunSprintJumpSequence` — exact implementation outline:**

```csharp
private IEnumerator RunSprintJumpSequence(SprintJumpDiagnostics diag)
```

The method body must follow these exact phases in order:

**Phase 0 — Warm-up (SettleFrames = 80 frames, 0.8 s):**
```csharp
yield return _rig.WarmUp(physicsFrames: SettleFrames, renderFrames: 0);
```

**Phase 1 — Sprint ramp (SprintRampFrames = 300 frames, 3.0 s):**
- Derive the forward move input from `ScenarioDefinitions.StartStop`:
  ```csharp
  Vector2 forwardInput = ScenarioPathUtility.ToMoveInput(
      ScenarioPathUtility.GetTravelDirections(ScenarioDefinitions.StartStop)[0]);
  ```
  **Fallback** if `StartStop` only has one waypoint: use `new Vector2(0f, 1f)` (pure Z-forward).
- Set inputs:
  ```csharp
  _rig.PlayerMovement.SetMoveInputForTest(forwardInput);
  _rig.PlayerMovement.SetSprintInputForTest(true);
  ```
- Loop `SprintRampFrames` times:
  ```csharp
  for (int f = 0; f < SprintRampFrames; f++)
  {
      yield return new WaitForFixedUpdate();
      RecordContinuousState(diag);
  }
  ```

**Phase 2 — First jump (1 frame):**
```csharp
_rig.PlayerMovement.SetJumpInputForTest(true);
yield return new WaitForFixedUpdate();
_rig.PlayerMovement.SetJumpInputForTest(false); // consume — input is one-frame
RecordContinuousState(diag);
diag.WasAirborneAfterJump1 = true; // will be validated by state check below
```

**Phase 3 — Post-jump-1 settle (PostJumpSettleFrames = 200 frames, 2.0 s):**
- Keep sprint + forward input held (character should maintain sprint through the air and on landing).
- Each frame:
  - Record `UprightAngle` into `diag.UprightAngleSamplesAfterJump1`.
  - Track `diag.MaxUprightAngleAfterJump1 = Mathf.Max(...)`.
  - If `CharacterState.CurrentState == CharacterStateType.Fallen`, set `diag.EnteredFallenAfterJump1 = true`.
  - If `diag.FramesToRecoverAfterJump1 == -1` and the character was post-airborne and `IsGrounded && UprightAngle < StableUprightCeiling`, record the frame index.
  - Check `WasAirborneAfterJump1`: set true if state was ever `Airborne` during this window.
- Implementation:
  ```csharp
  bool wasAirborne1 = false;
  bool landedAfter1 = false;
  for (int f = 0; f < PostJumpSettleFrames; f++)
  {
      yield return new WaitForFixedUpdate();
      RecordContinuousState(diag);

      float angle = _rig.BalanceController.UprightAngle;
      diag.UprightAngleSamplesAfterJump1.Add(angle);
      diag.MaxUprightAngleAfterJump1 = Mathf.Max(diag.MaxUprightAngleAfterJump1, angle);

      var state = _rig.CharacterState.CurrentState;
      if (state == CharacterStateType.Airborne)
          wasAirborne1 = true;
      if (wasAirborne1 && _rig.BalanceController.IsGrounded)
          landedAfter1 = true;
      if (state == CharacterStateType.Fallen)
          diag.EnteredFallenAfterJump1 = true;
      if (diag.FramesToRecoverAfterJump1 == -1 && landedAfter1
          && angle < StableUprightCeiling
          && state != CharacterStateType.Fallen)
          diag.FramesToRecoverAfterJump1 = f;
  }
  diag.WasAirborneAfterJump1 = wasAirborne1;
  ```

**Phase 4 — Second sprint leg (SecondSprintFrames = 300 frames, 3.0 s):**
- Inputs stay the same (sprint + forward already held).
- Loop and `RecordContinuousState(diag)` each frame.

**Phase 5 — Second jump (1 frame):**
- Identical to Phase 2 but for jump 2.

**Phase 6 — Post-jump-2 settle (FinalSettleFrames = 200 frames, 2.0 s):**
- Identical logic to Phase 3 but writing into `diag.*AfterJump2` fields and `UprightAngleSamplesAfterJump2`.

**Phase 7 — Record final state:**
```csharp
diag.FinalState = _rig.CharacterState.CurrentState;
diag.FinalUprightAngle = _rig.BalanceController.UprightAngle;
```

**Phase 8 — Release inputs:**
```csharp
_rig.PlayerMovement.SetMoveInputForTest(Vector2.zero);
_rig.PlayerMovement.SetSprintInputForTest(false);
_rig.PlayerMovement.SetJumpInputForTest(false);
```

**Private helper `RecordContinuousState`:**
```csharp
private void RecordContinuousState(SprintJumpDiagnostics diag)
{
    float angle = _rig.BalanceController.UprightAngle;
    diag.PeakUprightAngleOverall = Mathf.Max(diag.PeakUprightAngleOverall, angle);

    if (_rig.CharacterState.CurrentState == CharacterStateType.Fallen)
        diag.EverEnteredFallen = true;

    Vector3 vel = _rig.HipsBody.linearVelocity;
    float planarSpeed = new Vector2(vel.x, vel.z).magnitude;
    diag.PeakSprintSpeed = Mathf.Max(diag.PeakSprintSpeed, planarSpeed);
}
```

**Commit gate:** Helper compiles. The no-op test from Task 1 can be upgraded to call `RunSprintJumpSequence` and just log the diagnostics without assertions, confirming the sequence runs to completion.

- 2026-03-18: Completed. Added `SprintJumpDiagnostics`, `RunSprintJumpSequence()`, and `RecordContinuousState()` to `Assets/Tests/PlayMode/Character/SprintJumpStabilityTests.cs`, upgraded the scaffold smoke test to execute the full sprint → jump → land → sprint → jump → land sequence, and verified the focused PlayMode fixture with `Tools/Run-UnityTests.ps1 -Platform PlayMode -TestFilter "PhysicsDrivenMovement.Tests.PlayMode.SprintJumpStabilityTests"` (`Result=Passed, Total=1, Passed=1, Failed=0`).

---

### Task 3 — Test method: `SprintJump_SingleJump_DoesNotFaceplant`

Exercises a single sprint → jump → land cycle and asserts the character does not faceplant.

```csharp
[UnityTest]
public IEnumerator SprintJump_SingleJump_DoesNotFaceplant()
{
    var diag = new SprintJumpDiagnostics();
    yield return RunSprintJumpSequence(diag);

    // ── Structural: jump actually happened ───────────────────
    Assert.That(diag.WasAirborneAfterJump1, Is.True,
        "Jump 1 should have entered Airborne state.");

    // ── Primary: no faceplant ────────────────────────────────
    Assert.That(diag.MaxUprightAngleAfterJump1, Is.LessThan(FaceplantAngleThreshold),
        $"After sprint-jump landing #1, peak torso tilt was {diag.MaxUprightAngleAfterJump1:F1}° " +
        $"(threshold {FaceplantAngleThreshold}°). Character is faceplanting.");

    Assert.That(diag.EnteredFallenAfterJump1, Is.False,
        "Character should not enter Fallen state after sprint-jump landing #1.");

    // ── Metric emission ──────────────────────────────────────
    TestContext.Out.WriteLine(
        $"[METRIC] SprintJump_SingleJump PeakTiltAfterJump1={diag.MaxUprightAngleAfterJump1:F1}");
    TestContext.Out.WriteLine(
        $"[METRIC] SprintJump_SingleJump RecoveryFrames1={diag.FramesToRecoverAfterJump1}");
    TestContext.Out.WriteLine(
        $"[METRIC] SprintJump_SingleJump PeakSprintSpeed={diag.PeakSprintSpeed:F2}");
}
```

**Commit gate:** Test runs. If the bug exists (expected), the test **fails** with a clear message naming the peak tilt angle. That failure is the proof-of-bug.

- 2026-03-18: Completed. Added `SprintJump_SingleJump_DoesNotFaceplant()` to `Assets/Tests/PlayMode/Character/SprintJumpStabilityTests.cs` and verified the focused PlayMode fixture with `Tools/Run-UnityTests.ps1 -Platform PlayMode -TestFilter "PhysicsDrivenMovement.Tests.PlayMode.SprintJumpStabilityTests"` (`Result=Passed, Total=2, Passed=2, Failed=0`). The new Task 3 test emitted `[METRIC]` lines `PeakTiltAfterJump1=31.5`, `RecoveryFrames1=45`, and `PeakSprintSpeed=3.96`, so the expected proof-of-bug red did not reproduce on this run. The companion smoke test in the same fixture still logged `PeakTilt=94.3`, `FinalState=Fallen`, `Airborne1=False`, and `Airborne2=False`, which is worth watching as Task 4 expands the assertions.
- 2026-03-18: Updated the shared helper to use 5.0 s sprint windows before both jumps (`SprintRampFrames = 500`, `SecondSprintFrames = 500`) so the landing checks run deeper into the actual speed envelope. Two consecutive focused PlayMode runs then reproduced the expected Task 3 failure: `SprintJump_SingleJump_DoesNotFaceplant` failed with `PeakTiltAfterJump1=91.2` against the 45° faceplant threshold while still recording `RecoveryFrames1=45` and `PeakSprintSpeed=4.30`. The companion smoke run also aligned with the faceplant (`PeakTilt=96.2`, `FinalState=Fallen`, `Airborne1=True`), but `Airborne2=False` remains a structural issue for the second jump in the shared helper.

---

### Task 4 — Test method: `SprintJump_TwoConsecutiveJumps_DoesNotFaceplant`

Exercises the full double-jump sprint sequence.

```csharp
[UnityTest]
public IEnumerator SprintJump_TwoConsecutiveJumps_DoesNotFaceplant()
{
    var diag = new SprintJumpDiagnostics();
    yield return RunSprintJumpSequence(diag);

    // ── Structural: both jumps happened ──────────────────────
    Assert.That(diag.WasAirborneAfterJump1, Is.True,
        "Jump 1 should have entered Airborne.");
    Assert.That(diag.WasAirborneAfterJump2, Is.True,
        "Jump 2 should have entered Airborne.");

    // ── Primary: no faceplant on either landing ──────────────
    Assert.That(diag.MaxUprightAngleAfterJump1, Is.LessThan(FaceplantAngleThreshold),
        $"Landing #1 peak tilt {diag.MaxUprightAngleAfterJump1:F1}° exceeds {FaceplantAngleThreshold}°.");
    Assert.That(diag.MaxUprightAngleAfterJump2, Is.LessThan(FaceplantAngleThreshold),
        $"Landing #2 peak tilt {diag.MaxUprightAngleAfterJump2:F1}° exceeds {FaceplantAngleThreshold}°.");

    Assert.That(diag.EnteredFallenAfterJump1, Is.False,
        "No Fallen state after landing #1.");
    Assert.That(diag.EnteredFallenAfterJump2, Is.False,
        "No Fallen state after landing #2.");

    // ── Secondary: final state is upright ────────────────────
    Assert.That(diag.FinalUprightAngle, Is.LessThan(StableUprightCeiling),
        $"After the full sequence, final tilt is {diag.FinalUprightAngle:F1}° " +
        $"(ceiling {StableUprightCeiling}°).");
    Assert.That(diag.FinalState, Is.Not.EqualTo(CharacterStateType.Fallen),
        "Character should not be in Fallen state at the end of the sequence.");

    // ── Metric emission ──────────────────────────────────────
    TestContext.Out.WriteLine(
        $"[METRIC] SprintJump_TwoJumps PeakTiltAfterJump1={diag.MaxUprightAngleAfterJump1:F1}");
    TestContext.Out.WriteLine(
        $"[METRIC] SprintJump_TwoJumps PeakTiltAfterJump2={diag.MaxUprightAngleAfterJump2:F1}");
    TestContext.Out.WriteLine(
        $"[METRIC] SprintJump_TwoJumps RecoveryFrames1={diag.FramesToRecoverAfterJump1}");
    TestContext.Out.WriteLine(
        $"[METRIC] SprintJump_TwoJumps RecoveryFrames2={diag.FramesToRecoverAfterJump2}");
    TestContext.Out.WriteLine(
        $"[METRIC] SprintJump_TwoJumps FinalTilt={diag.FinalUprightAngle:F1}");
    TestContext.Out.WriteLine(
        $"[METRIC] SprintJump_TwoJumps PeakSprintSpeed={diag.PeakSprintSpeed:F2}");
    TestContext.Out.WriteLine(
        $"[METRIC] SprintJump_TwoJumps EverFallen={diag.EverEnteredFallen}");
}
```

**Commit gate:** Test runs and fails with clear diagnostics if the bug exists.

- 2026-03-18: Completed. Added `SprintJump_TwoConsecutiveJumps_DoesNotFaceplant()` to `Assets/Tests/PlayMode/Character/SprintJumpStabilityTests.cs` and tightened the shared helper with a short jump-ready stabilization window before jump 2 (3 stable grounded `Standing`/`Moving` frames within a 30-frame budget) so the second pulse does not land on a transient non-jumpable frame. Focused verification via `Tools/Run-UnityTests.ps1 -Platform PlayMode -TestFilter "PhysicsDrivenMovement.Tests.PlayMode.SprintJumpStabilityTests"` now reports `Result=Failed, Total=3, Passed=1, Failed=2`: Task 3 still fails on jump-1 faceplant (`PeakTiltAfterJump1=91.2`, `RecoveryFrames1=45`, `PeakSprintSpeed=4.30`), and Task 4 emits `[METRIC]` lines `PeakTiltAfterJump1=95.7`, `PeakTiltAfterJump2=86.5`, `RecoveryFrames1=50`, `RecoveryFrames2=-1`, `FinalTilt=86.5`, `PeakSprintSpeed=4.29`, and `EverFallen=True` before failing on `Jump 2 should have entered Airborne`. The companion smoke run in the same fixture still ended `FinalState=Fallen` with `Airborne2=False`, indicating the first landing collapse persists through the second-jump window.

---

### Task 5 — Test method: `SprintJump_LandingRecovery_RegainsUprightWithinDeadline`

Softer assertion: even if the character wobbles on landing, it must recover to `< StableUprightCeiling` (25°) within `PostLandStabilityDeadline` frames (1.5 s) of grounding. This separates "momentary wobble" from "falls flat on face and can't get up".

```csharp
[UnityTest]
public IEnumerator SprintJump_LandingRecovery_RegainsUprightWithinDeadline()
{
    var diag = new SprintJumpDiagnostics();
    yield return RunSprintJumpSequence(diag);

    Assert.That(diag.WasAirborneAfterJump1, Is.True,
        "Jump 1 must produce Airborne state.");

    Assert.That(diag.FramesToRecoverAfterJump1, Is.GreaterThanOrEqualTo(0),
        $"After landing #1, character never recovered below {StableUprightCeiling}° " +
        $"within {PostJumpSettleFrames} frames. Peak tilt was {diag.MaxUprightAngleAfterJump1:F1}°.");

    Assert.That(diag.FramesToRecoverAfterJump1, Is.LessThanOrEqualTo(PostLandStabilityDeadline),
        $"Recovery after landing #1 took {diag.FramesToRecoverAfterJump1} frames " +
        $"(deadline: {PostLandStabilityDeadline} frames = {PostLandStabilityDeadline * 0.01f:F1} s).");

    // ── Same for jump 2 if it fired ──────────────────────────
    if (diag.WasAirborneAfterJump2)
    {
        Assert.That(diag.FramesToRecoverAfterJump2, Is.GreaterThanOrEqualTo(0),
            $"After landing #2, character never recovered below {StableUprightCeiling}°.");

        Assert.That(diag.FramesToRecoverAfterJump2, Is.LessThanOrEqualTo(PostLandStabilityDeadline),
            $"Recovery after landing #2 took {diag.FramesToRecoverAfterJump2} frames " +
            $"(deadline: {PostLandStabilityDeadline}).");
    }

    // ── Metric emission ──────────────────────────────────────
    TestContext.Out.WriteLine(
        $"[METRIC] SprintJump_Recovery RecoveryFrames1={diag.FramesToRecoverAfterJump1}");
    TestContext.Out.WriteLine(
        $"[METRIC] SprintJump_Recovery RecoveryFrames2={diag.FramesToRecoverAfterJump2}");
}
```

**Commit gate:** Test runs.

- 2026-03-18: Completed. Added `SprintJump_LandingRecovery_RegainsUprightWithinDeadline()` to `Assets/Tests/PlayMode/Character/SprintJumpStabilityTests.cs` and verified the focused PlayMode fixture with `Tools/Run-UnityTests.ps1 -Platform PlayMode -TestFilter "PhysicsDrivenMovement.Tests.PlayMode.SprintJumpStabilityTests"`. The fixture now reports `Result=Failed, Total=4, Passed=2, Failed=2`: the new Task 5 test passes with `[METRIC]` lines `RecoveryFrames1=45` and `RecoveryFrames2=-1`, confirming jump 1 regains upright inside the 150-frame deadline while jump 2 never reaches the conditional recovery assertions because `WasAirborneAfterJump2` remains false. The companion smoke run still ends `FinalState=Fallen` with `Airborne2=False`, so the next slice remains the telemetry capture needed to explain the post-landing collapse.

---

### Task 6 — Test method: `SprintJump_TelemetryCapture_LogsRecoveryEventsAroundLanding`

Enables `LocomotionDirector` recovery telemetry and asserts that structured events are emitted around the landing. This is not a pass/fail stability test — it ensures the diagnostic pipeline is wired up for debugging.

```csharp
[UnityTest]
public IEnumerator SprintJump_TelemetryCapture_LogsRecoveryEventsAroundLanding()
{
    // Enable telemetry on the LocomotionDirector
    LocomotionDirector director = _rig.Instance.GetComponentInChildren<LocomotionDirector>();
    Assert.That(director, Is.Not.Null, "LocomotionDirector must be present on the player prefab.");

    // Use reflection to enable the private serialized field
    var field = typeof(LocomotionDirector).GetField("_enableRecoveryTelemetry",
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    Assert.That(field, Is.Not.Null, "_enableRecoveryTelemetry field must exist.");
    field.SetValue(director, true);

    var diag = new SprintJumpDiagnostics();
    yield return RunSprintJumpSequence(diag);

    IReadOnlyList<RecoveryTelemetryEvent> log = director.RecoveryTelemetryLog;
    Assert.That(log, Is.Not.Null, "RecoveryTelemetryLog should be accessible.");

    // Emit every event as a METRIC line for post-run inspection
    TestContext.Out.WriteLine($"[METRIC] SprintJump_Telemetry EventCount={log.Count}");
    for (int i = 0; i < log.Count; i++)
    {
        TestContext.Out.WriteLine($"[METRIC] SprintJump_Telemetry Event[{i}]={log[i].ToNdjsonLine()}");
    }

    // Structural: if the character wobbled or fell, at least one event should have fired
    if (diag.EverEnteredFallen || diag.PeakUprightAngleOverall > 30f)
    {
        Assert.That(log.Count, Is.GreaterThan(0),
            "Recovery telemetry should log at least one event when the character " +
            $"wobbles (peak tilt {diag.PeakUprightAngleOverall:F1}°) or enters Fallen.");
    }
}
```

**Commit gate:** Test runs. If the character faceplants, the telemetry log should be non-empty and the NDJSON lines visible in NUnit XML output.

- 2026-03-18: Completed. Added `SprintJump_TelemetryCapture_LogsRecoveryEventsAroundLanding()` to `Assets/Tests/PlayMode/Character/SprintJumpStabilityTests.cs`, enabling `_enableRecoveryTelemetry` through reflection so the test can stay inside the existing runtime seam without broadening runtime visibility. Focused verification via `Tools/Run-UnityTests.ps1 -Platform PlayMode -TestFilter "PhysicsDrivenMovement.Tests.PlayMode.SprintJumpStabilityTests"` now reports `Result=Failed, Total=5, Passed=3, Failed=2`: the new Task 6 test passes and emits `[METRIC] SprintJump_Telemetry EventCount=7` plus NDJSON entries for `Slip` entry, `NearFall` escalation, a natural `recovery_window_elapsed` exit, then a second `Slip` entry, `NearFall` escalation, `angle_above_ceiling`, and the terminal `recovery_surrendered` event (`UprightAngle=86.56069`, `RecoveryDurationSoFar=3.649998`, `WasSurrender=true`). Task 3 and Task 4 remain the same known-red proof-of-bug checks. Because `Tools/test-slices.json` is not present in the current repo state, Task 7 is skipped and the plan advances to Task 8.

---

### Task 7 — Add `SprintJumpStabilityTests` to `test-slices.json`

**Condition:** Only if `Tools/test-slices.json` exists (created by C9.4a). If it doesn't exist yet, skip this task.

Add a new entry:
```json
{
  "name": "sprint-jump",
  "platform": "PlayMode",
  "filter": "SprintJumpStabilityTests",
  "description": "Sprint-jump landing stability regression tests.",
  "expectedGreenCount": 0
}
```

Set `expectedGreenCount` to 0 initially (known failing). Update to the correct count after the bug is fixed.

---

### Task 8 — Add `SprintJump` scenario to `ScenarioDefinitions.cs`

**Condition:** Only do this if the existing `ScenarioDefinitions.cs` doesn't already have a sprint-jump scenario.

Add a new entry:
```csharp
public static readonly ScenarioDefinition SprintJump = new ScenarioDefinition(
    "SprintJump",
    new[]
    {
        new Vector3(200f, 0f, 200f),
        new Vector3(200f, 0f, 230f),  // 30 m forward
    },
    expectedDurationSeconds: 12f,
    exercisedSubsystems: new[] { "gait", "balance", "recovery", "jump" }
);
```

Update the `All` array to include it. Update the EditMode count assertion in `ScenarioDefinitionsTests.AllScenariosAreValid` from 9 → 10.

- 2026-03-18: Completed. Added `ScenarioDefinitions.SprintJump` to `Assets/Tests/PlayMode/Utilities/ScenarioDefinitions.cs`, updated `ScenarioDefinitions.All`, and tightened `Assets/Tests/EditMode/Character/ScenarioDefinitionsTests.cs` so the reflection-backed gate now asserts 10 scenarios including `SprintJump`. Focused verification via `Tools/Run-UnityTests.ps1 -Platform EditMode -TestFilter "PhysicsDrivenMovement.Tests.EditMode.Character.ScenarioDefinitionsTests"` followed the intended red → green path: the first run failed `0/1` on the new count/presence expectation before the catalog entry landed, and the second run passed `1/1` after the scenario was added. The plan now advances to Task 9.

---

### Task 9 — Run tests, record baseline, commit

1. **Compile check:** Build the project to confirm no syntax errors.
2. **Run the new tests:**
   ```
   MCP run_tests filter: SprintJumpStabilityTests
   ```
   Or fallback:
   ```powershell
   .\Tools\Run-UnityTests.ps1 -Platform PlayMode -TestFilter "SprintJumpStabilityTests"
   ```
3. **Expected outcome:** Tests **fail** with clear assertion messages showing the peak tilt angle (likely 60–90° if the character is faceplanting). The `[METRIC]` lines should appear in the output with concrete numbers.
4. **Record in this plan:**
   - Paste the actual peak tilt values from the test output.
   - Paste the actual `RecoveryFrames` values (-1 if never recovered).
   - Note which tests failed and which (if any) passed.
5. **Commit** with message: `test: add SprintJumpStabilityTests — reproduces sprint+jump faceplant instability`
6. **Update** Chapter 9 plan status to note the new tests and their known-failing status.

- 2026-03-18: Completed. Ran the focused PlayMode fixture via `Tools/Run-UnityTests.ps1 -Platform PlayMode -TestFilter "PhysicsDrivenMovement.Tests.PlayMode.SprintJumpStabilityTests"`, which compiled successfully before executing the tests and produced fresh artifacts at `TestResults/latest-summary.md`, `TestResults/PlayMode.xml`, and `Logs/test_playmode_20260318_075506.log`. The known-red baseline is now recorded as `Result=Failed, Total=5, Passed=3, Failed=2`.
- 2026-03-18: Baseline metrics from that run: `SprintJump_SingleJump` emitted `PeakTiltAfterJump1=95.7`, `RecoveryFrames1=50`, and `PeakSprintSpeed=4.29` before failing on the faceplant assertion. `SprintJump_TwoJumps` emitted `PeakTiltAfterJump1=96.8`, `PeakTiltAfterJump2=86.6`, `RecoveryFrames1=47`, `RecoveryFrames2=-1`, `FinalTilt=86.5`, `PeakSprintSpeed=4.28`, and `EverFallen=True` before failing on `Jump 2 should have entered Airborne`. The passing tests were `RunSprintJumpSequence_WithFreshRig_CompletesWithoutErrors`, `SprintJump_LandingRecovery_RegainsUprightWithinDeadline` (`RecoveryFrames1=45`, `RecoveryFrames2=-1`), and `SprintJump_TelemetryCapture_LogsRecoveryEventsAroundLanding` (`EventCount=7`, terminal `angle_above_ceiling` / `recovery_surrendered`). This child plan is now complete, with Task 7 still deferred until `Tools/test-slices.json` exists.

---

## Assertion Thresholds Summary

| Threshold | Value | Rationale |
|-----------|-------|-----------|
| `FaceplantAngleThreshold` | 45° | Halfway to horizontal; anything above this on landing is a clear faceplant. Well below the 65° Fallen threshold — we want to catch instability before it becomes a full fall. |
| `StableUprightCeiling` | 25° | Generous: normal upright walking is ~0–10°. 25° allows a wobble but not a stumble. |
| `PostLandStabilityDeadline` | 150 frames (1.5 s) | Matches the existing `UprightRecoveryDeadlineFrames` used in `SprintBalanceOutcomeTests`. |

---

## File Layout Reference

After all tasks, the single new file should be ~200–250 lines:

```
SprintJumpStabilityTests.cs
├── Constants (thresholds, frame counts)
├── Fields (_rig)
├── [SetUp] / [TearDown]
├── SprintJumpDiagnostics (nested class)
├── RunSprintJumpSequence() (private IEnumerator helper)
├── RecordContinuousState() (private helper)
├── SprintJump_SingleJump_DoesNotFaceplant [UnityTest]
├── SprintJump_TwoConsecutiveJumps_DoesNotFaceplant [UnityTest]
├── SprintJump_LandingRecovery_RegainsUprightWithinDeadline [UnityTest]
└── SprintJump_TelemetryCapture_LogsRecoveryEventsAroundLanding [UnityTest]
```

## Key Implementation Notes for the Agent

1. **Jump input is one-frame-consume.** Call `SetJumpInputForTest(true)` on exactly one frame, then `SetJumpInputForTest(false)` on the next. The wind-up phase lasts `_jumpWindUpDuration` (0.2 s = 20 frames at 100 Hz) before the actual impulse fires. Do **not** hold jump input across multiple frames.

2. **Sprint input is held.** Call `SetSprintInputForTest(true)` once and leave it. The blend ramps over `_sprintBlendDuration` (0.25 s). Keep it held through the entire sequence including airborne and landing.

3. **Move input is continuous.** Call `SetMoveInputForTest(forwardInput)` once at the start of Phase 1 and leave it. The `GhostDriver` is **not** used — these tests drive raw input directly.

4. **`PlayerPrefabTestRig` resets all inputs** in `SetUp` (jump=false, sprint=false, move=zero). You must set them after `WarmUp`.

5. **`BalanceController.UprightAngle`** is 0° when perfectly vertical, 90° when horizontal. It's always positive (unsigned angle from world-up).

6. **`CharacterState.CurrentState`** transitions through `Standing → Moving → Airborne → (landing) → Moving/Standing` during a successful jump. If the character faceplants, it goes `Airborne → Fallen → GettingUp → ...`.

7. **Recovery telemetry** is opt-in via a private `[SerializeField]` field. Use reflection to enable it (pattern already established in `LocomotionDirectorTests.RecoveryTelemetry_HardTurnScenario_LogsEntryAndExit`).

8. **`[METRIC]` lines** follow the `[METRIC] <TestName> <Key>=<Value>` format established by the C9.3a work package. Emit them via `TestContext.Out.WriteLine`.

9. **Prefab path** is `"Assets/Prefabs/PlayerRagdoll_Skinned.prefab"` — `PlayerPrefabTestRig.Create()` handles this internally.

10. **Physics settings** are saved/restored by `PlayerPrefabTestRig` — no manual save/restore needed in the test class.
