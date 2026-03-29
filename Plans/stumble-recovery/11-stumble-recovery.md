# Plan 11 — Stumble Recovery: Catch Step + Brace Arms
## (Revised after Opus review — 2026-03-29)

## Vision

When the character is pushed or destabilised, instead of leaning and slowly correcting,
it should throw a foot forward urgently to get under its centre of mass, and extend arms
as a brace reflex. This is how humans recover from a stumble. It looks alive.

## Key Finding (Opus review)

The catch-step and arm-brace pipeline is **already built**:
- `LocomotionDirector.ClassifyRecoverySituation` → `RecoverySituation.Stumble/NearFall`
- `LegAnimator` → `LegStateType.CatchStep` / `LegStateType.RecoveryStep`
- `ArmAnimator._currentBraceBlend` driven by `LocomotionDirector.IsRecoveryActive`

The gap: `ClassifyRecoverySituation` only fires on locomotion failure (walking + not
progressing + intent). A physics impulse while standing idle — or a push from behind
during normal movement — never satisfies `strongIntent && lowProjectedProgress`, so the
catch-step never fires.

**Fix: extend `ClassifyRecoverySituation` to also fire on tilt angle alone**, so passive
destabilisation (push, landing impact, collision) triggers the same proven catch-step
pipeline. No new component needed.

---

## Architecture

### Single change: `LocomotionDirector.cs`

Add one new classification path at the top of `ClassifyRecoverySituation`, before the
existing `IsLocomotionCollapsed` check:

```
PassiveTilt → RecoverySituation.Stumble
```

Fires when:
- `BalanceController.UprightAngle >= _passiveTiltStumbleTriggerAngle` (new field, default 18°)
- `BalanceController.IsGrounded` is true
- `CharacterState` is Standing or Moving (not Fallen, Airborne, GettingUp)
- `BalanceController.IsSurrendered` is false
- NOT an intentional jump sequence

Because this inserts before `IsLocomotionCollapsed`, it fires on passive pushes too.
The existing debounce (`_recoveryEntryDebounceFrames`) already prevents single-frame
triggers.

### Lean direction into catch step

The existing `LegStateType.CatchStep` uses support geometry to pick which leg.
For passive tilt there's no locomotion intent vector, so we need to feed lean direction.

Add a new field to `LocomotionObservation`:
```csharp
public Vector3 PassiveLeanDirection; // zero when not passive-tilt triggered
```

In `ClassifyRecoverySituation`, when passive tilt fires: compute lean direction as:
```csharp
Vector3 hipsUp = _hipsBody.rotation * Vector3.up;
Vector3 leanDir = -new Vector3(hipsUp.x, 0f, hipsUp.z).normalized;
// negative because hipsUp tilts away from lean direction
```
(Opus-recommended approach — works at all angles, degenerates gracefully when upright.)

Store in `_currentObservation.PassiveLeanDirection`. The existing step planner can use
this as the "requested direction" substitute when normal locomotion intent is absent.

### Arm brace — already works

`ArmAnimator` reads `LocomotionDirector.IsRecoveryActive` and blends to brace pose
automatically. Once `ClassifyRecoverySituation` fires `RecoverySituation.Stumble`,
`IsRecoveryActive` becomes true and the brace arms automatically engage. No arm code
changes needed.

---

## Slices

### Slice 0.5 — Design spike (no agent, just verify in editor)

Before writing slice 1 prompt, Benny pushes the character in the editor and confirms:
- Recovery currently does NOT fire on passive push (expected)
- `LocomotionDirector` debug log shows `RecoverySituation.None` after a push

This confirms the gap is real before we spend agent time on it.

### Slice 1 — Passive tilt → Stumble classification

Status (2026-03-29): Complete on `plan/11-1-passive-stumble`

**Files changed:**
- `Assets/Scripts/Character/Locomotion/LocomotionDirector.cs`

**Implemented:**
- Added `_passiveTiltAngularVelocityTrigger = 4f` to fire `RecoverySituation.Stumble`
   immediately on fast grounded hips-rotation spikes, bypassing the normal entry debounce.
- Added `_passiveTiltAngleTrigger = 20f` to classify sustained grounded passive tilt as
   `RecoverySituation.Stumble` through the existing debounce path.
- Kept the no-move idle path from blocking passive stumble recovery while preserving
   move-intent gating for the lower-priority locomotion-only recovery cases.

**Verification:**
- 2026-03-29 PlayMode filter `StumbleStutterRegressionTests|JumpTests|GaitOutcomeTests|AirborneSpringTests|ImpactKnockdownTests` passed (37/37).
- Artifacts: `TestResults/PlayMode.xml`, `TestResults/latest-summary.md`, `Logs/test_playmode_20260329_134757.log`.

**Follow-up kept out of this slice:**
- No step-planner or lean-direction changes were made here; passive recovery still routes
   through the existing catch-step pipeline exactly as-is.

### Slice 2 — Escalating cooldown + 3-step cap

Opus flagged: continuous firing without escalation looks spastic.

Add to `LocomotionDirector`:
```csharp
private int _passiveTiltStumbleCount;
private float _passiveTiltCooldownTimer;
private static readonly float[] PassiveTiltCooldowns = { 0.25f, 0.40f, 0.60f };
private const int PassiveTiltStepCap = 3;
```

In `ClassifyRecoverySituation` passive tilt path: check cooldown + cap, increment count
on fire, reset count when `UprightAngle < _passiveTiltStumbleTriggerAngle * 0.5f`
(character clearly recovered).

### Slice 3 — Tests

New file: `Assets/Tests/PlayMode/Character/StumbleRecoveryTests.cs`

**Test 1: `PassivePush_Forward_TriggersStumbleRecovery`**
- Spawn prefab, settle 80 frames
- Apply forward impulse to hips: enough to reach ~25° tilt
- Run 150 frames
- Assert: `LocomotionDirector.IsRecoveryActive` was true at some point
- Assert: character did NOT enter Fallen state

**Test 2: `PassivePush_WhenAirborne_DoesNotTriggerRecovery`**
- Spawn, jump (use SetJumpInputForTest), apply impulse mid-air
- Assert: `LocomotionDirector.IsRecoveryActive` remains false while Airborne

**Test 3: `PassivePush_WhenAlreadyFallen_DoesNotTriggerRecovery`**
- Spawn, force Fallen state (large impulse), apply second impulse
- Assert: recovery not triggered (already handled by surrender/get-up system)

**Test 4: `PassivePush_EscalatesToCap_ThenStops`**
- Apply 4 consecutive pushes with short gaps
- Assert: `StumbleCount` caps at 3, fourth push does not trigger recovery

Run full suite, confirm no regressions.

---

## Risks to watch

1. **`StumbleStutterRegressionTests`** — 18° default must not fire on normal walking
   gait sway. If it does, raise threshold to 20-22°. Monitor closely in slice 1.

2. **Surrender interaction** — `IsSurrendered` gate prevents double-trigger when
   character is in crumple. Already gated in the classification path above.

3. **Limbo dwell timer** — `_limboForcedDwellTimer` gates Standing→Airborne. The new
   Stumble classification runs before that gate so it should be unaffected.

4. **Jump landing** — `isIntentionalJumpSequence` gate covers active jump phases. But
   hard landings may still briefly trigger if tilt spikes >18°. Monitor in
   `SprintJumpStabilityTests`.

5. **`_recoveryEntryDebounceFrames`** — already provides single-frame noise suppression.
   Verify its value is ≥2 frames to avoid sensor noise on the passive path.

---

## What NOT to change

- Do NOT add a new component — route through LocomotionDirector only
- Do NOT change ArmAnimator — arm brace already works via IsRecoveryActive
- Do NOT change GroundSensor exit delay
- Do NOT change fallen/surrender thresholds
- Do NOT modify Run-UnityTests.ps1

---

## Prefab

`_passiveTiltStumbleTriggerAngle` is a serialized field — set in prefab as 18f initially.
Tune up to 22f if walking triggers false positives.
