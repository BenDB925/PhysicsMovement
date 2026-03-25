# Plan 07 — Get-Up & Crumple Fall

**Status:** In Progress — Slices 1, 2a, and hotfix complete ✅
**Current next step:** Slice 2b (face-up detection)
**Branch prefix:** `slice/07-N-name`
**Slice prompts dir:** `H:\Work\PhysicsDrivenMovementDemo\Plans\get-up-and-fall\prompts\`

---

## Goal

Two complementary improvements to make falling and recovering feel physically believable:

1. **Crumple fall** — when balance is lost, the character collapses naturally under momentum rather than stiffening and faceplanting in a scripted sequence.
2. **Physics-driven get-up** — the character uses its own limbs to push up from the ground, rather than being launched into the air by a single impulse.

Both are already partially built. The architecture is sound. This plan tunes, fixes, and completes what's there.

---

## Current State

### ProceduralStandUp.cs (435 lines)
A 4-phase physics get-up sequence already exists and is wired up to CharacterState + prefab:
- **Phase 0 — OrientProne**: applies torque to roll hips face-down (timeout 0.6s, advances regardless)
- **Phase 1 — ArmPush**: applies upward force to chest, partial spring restore (timeout 0.8s)
- **Phase 2 — LegTuck**: applies upward assist to hips, keeps chest propped (timeout 0.7s)
- **Phase 3 — Stand**: restores full balance controller, hands off to standing state (timeout configurable)

### Why it doesn't work right now
- `_forcedStandImpulse = 350f` fires as a last resort after `_maxStandUpAttempts` is exhausted
- 350 N impulse = twice the jump force — this is what launches the character into the air
- The phase sequence is probably failing its height gates (thresholds were set as guesses, never tuned against actual ragdoll proportions)
- No debug visibility — no one has ever seen which phase fails and why
- `_groundY` is captured once at sequence start — if the character slides, height measurements drift

### Fall / Surrender
- Surrender fires at 80° tilt (or 65° + angular velocity threshold)
- On surrender: joints drop to near-zero stiffness **immediately** (abrupt snap, not a ramp)
- `TriggerSurrender` calls `CancelAllRamps()` then zeros `UprightStrengthScale`, `HeightMaintenanceScale`, `StabilizationScale`, and calls `SetSpringProfile(0.25, 0.25, 0.25, 0.15)` — all instant
- No stiffness ramp-down — character stiffens into a controlled faceplant instead of crumpling naturally

---

## Design Principles

- **Physics first** — every phase applies forces/torques and lets the simulation play out; no position overrides, no animation curves
- **Observe before tuning** — slice 1 adds debug output to see what's actually failing before touching any values
- **Soft fallback** — the forced-stand becomes a gentle multi-frame force, not an impulse rocket; last resort only
- **Crumple is additive** — the fall improvement replaces the snap in TriggerSurrender with a timed ramp; surrender logic structure stays as-is
- **No test regressions** — existing passing tests must stay green; new tests guard the specific improvements

---

## Opus Review Findings (2026-03-25)

Key traps identified before coding:

1. **TriggerSurrender snap trap**: The crumple ramp must *replace* the immediate zero-set of support scales, not add a ramp after them. `CancelAllRamps()` fires first — if we just add a ramp call after the existing code, the snap has already happened and the ramp has nothing left to do.

2. **`_groundY` drift fix must be clamped**: Re-capture lowest body position each FixedUpdate, but use `Mathf.Min(_groundY, newLowestY)` — only ever lower, never raise. Raising `_groundY` would make phase height gates easier to pass than they should be.

3. **ForceMode.Impulse at 60 N·s is still a pop**: 60 N·s on ~8 kg hips = 7.5 m/s instantaneous. The forced-stand fallback should use `ForceMode.Force` over several frames instead of a single impulse.

4. **`_standUpAttempts` accumulates across re-knockdowns**: `Fail()` does not reset it. With a lower impulse, the soft fallback will still eventually fire after N failures — intentional, but worth noting.

5. **Face-up detection is non-trivial**: Skipping OrientProne and targeting different arm positions interacts with every downstream phase. Keep it as its own slice.

---

## Slices

### Slice 1 — Diagnose & Fix Forced Stand
**Goal:** Stop the rocket launch. Understand what's failing in the phase sequence.

**Changes (ProceduralStandUp.cs only):**
1. Add per-phase debug logging gated by `[SerializeField] bool _debugLog = false` — log phase name, current height vs threshold, success/fail decisions each FixedUpdate
2. Lower `_forcedStandImpulse` from `350f` → `60f` AND change `ForceMode.Impulse` → `ForceMode.Force` (applied once per frame for `_forcedStandFrames = 8` frames, achieving a soft multi-frame push rather than a single pop)
3. Add `_forcedStandFrames` as a serialized field (range 1–30)
4. Check `_maxStandUpAttempts` — if ≤ 2 in prefab, note it in debug log; don't change the code value yet
5. Fix `_groundY` drift: re-capture lowest body Y each FixedUpdate during active sequence using `Mathf.Min(_groundY, newLowestY)` — only ever lower, never raise

**Exit criteria:**
- Character no longer launches into air after getting up (may still look rough — that's slice 2c)
- Debug log visible in Console during play showing which phase is active and height readings
- `_groundY` tracks correctly (verify in debug log)
- Run full PlayMode regression filter after changes; no new failures beyond known 4

**Tests:** None new — regression run is the gate.

---

### Slice 2a — Crumple Fall Ramp
**Goal:** Replace the snap in `TriggerSurrender` with a timed stiffness ramp so the character crumples naturally.

**Changes (BalanceController.cs only):**
1. Add `[SerializeField] float _surrenderCrumpleDuration = 0.2f` (range 0.05–0.5s)
2. In `TriggerSurrender`: remove the immediate zero-set of `UprightStrengthScale`, `HeightMaintenanceScale`, `StabilizationScale` and the immediate `SetSpringProfile` call. Replace with ramp calls: `RampUprightStrength(0, _surrenderCrumpleDuration)`, `RampHeightMaintenance(0, _surrenderCrumpleDuration)`, `RampStabilization(0, _surrenderCrumpleDuration)`
3. The `SetSpringProfile(0.25, 0.25, 0.25, 0.15)` call: replace with a `RampSpringProfile` equivalent if it exists; if not, add a simple `_springProfileRampTimer` that lerps the spring multiplier from current → 0.25 over `_surrenderCrumpleDuration`
4. `CancelAllRamps()` must still fire at the start of TriggerSurrender (before the new ramp calls) — this is correct; it cancels any in-progress stand-up ramps before starting the surrender ramp

**Note on ClearSurrender cooldown:** The 0.5s cooldown after ClearSurrender prevents immediate re-trigger. If a crumple ramp is in-progress and ClearSurrender fires prematurely (e.g. a partial stand attempt), the 0.5s cooldown could mask a genuine re-fall. The plan accepts this risk for now — flag in code comment.

**Exit criteria:**
- After surrender triggers, hips forward direction changes at a rate ≤ 30 deg/frame for first 10 frames (not a snap)
- Spring multiplier ramps monotonically down during crumple window (sample at 0.05s intervals)
- Falls look like crumples in play testing
- Full regression filter green (minus known 4)

**Tests:** Add to slice 3 — `Fall_JointStiffnessRampsMonotonicallyOnSurrender`

---

### Slice 2b — Face-Up Detection
**Goal:** When the character falls on their back, skip OrientProne (rolling face-down first) and go directly to a back-up sequence.

**Changes (ProceduralStandUp.cs):**
1. At start of `Begin()`, after `CacheRigidbodies()`: check `Vector3.Dot(_hipsRb.transform.up, Vector3.up) > 0.5f` → character is face-up
2. If face-up: skip `EnterPhase(StandUpPhase.OrientProne)`, go directly to `EnterPhase(StandUpPhase.ArmPush)` with modified arm push direction (push chest upward from supine position — same force direction, just skip the roll)
3. Add `_isFaceUp` private bool set in `Begin()`, used to gate OrientProne skip
4. Add `public bool IsFaceUp => _isFaceUp;` property for test seam

**Exit criteria:**
- Character starting face-up skips the roll-over phase and goes straight to pushing up
- Character starting face-down still rolls over as before
- Full regression filter green

**Tests:** Add to slice 3 — `GetUp_FromFaceUp_SkipsOrientProne`

---

### Slice 2c — Phase Threshold Tuning (HITL)
**Goal:** Use slice 1 debug output to tune height thresholds so phases complete successfully.

**This is a values-only slice — no code changes.** Benny plays with debug log enabled, reads which phase is failing and what the height readings are, then adjusts serialized fields on the prefab:
- `_armPushTargetHeight` — chest height for ArmPush success
- `_armPushForce` — upward force during ArmPush
- `_legTuckTargetHeight` — hips height for LegTuck success
- `_legTuckAssistForce` — upward assist during LegTuck
- Phase timeouts if consistently too short

**Exit criteria:**
- Character gets up visibly using arms and legs (no forced-stand fallback firing)
- `_standUpAttempts` stays at 1 across multiple get-up events (no phase failures requiring retries)
- Looks physically believable in play testing

**No agent for this slice** — Benny tunes values, commits prefab change only.

---

### Slice 3 — Tests & Regression Gate
**Goal:** Lock in all improvements with results-based tests.

**New test file: `GetUpTests.cs`**
- `GetUp_FromFaceDown_ReachesStanding_WithinTimeout` — trigger surrender, wait for Fallen, verify reaches Standing within N seconds without player input
- `GetUp_NeverExceedsMaxLaunchHeight` — during full get-up sequence, assert hips never exceed `standingHipsHeight * 2.5` — regression guard for rocket launch
- `GetUp_ReKnockdownDuringStandUp_ReEntersFallen` — trigger surrender, enter GettingUp, trigger second surrender mid-sequence, assert character re-enters Fallen (not stuck in GettingUp or Standing)
- `GetUp_FromFaceUp_SkipsOrientProne` — (from 2b) verify IsFaceUp=true skips OrientProne phase

**New test file: `FallCrumpleTests.cs`**
- `Fall_JointStiffnessRampsMonotonicallyOnSurrender` — trigger surrender, sample spring multiplier at 5-frame intervals for first 20 frames, assert monotonically decreasing (not a snap)
- `Fall_DoesNotSnapToFaceplantAngle` — trigger surrender, sample hips upright angle at 5-frame intervals for first 10 frames, assert no single frame exceeds 15 deg/frame change

**Exit criteria:** All new tests pass. Full regression filter green (minus known pre-existing 4).

---

## Parameter Reference

To be filled in after slice 2c tuning. Will include all ProceduralStandUp and crumple fields with tuned prefab values, ranges, and descriptions.

---

## Agent Log

_To be filled in as slices complete._

---

## Known Pre-Existing Test Failures (exclude from gate)

These fail on pre-Plan-07 code and are unrelated to this plan:
- `SustainedLocomotionCollapse_TransitionsIntoFallen`
- `CompleteLap_WithinTimeLimit_NoFalls`
- `TurnAndWalk_CornerRecovery`
- `LandingRecovery_SpringRampsGraduallyAfterLanding`
