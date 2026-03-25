# Plan 07 — Get-Up & Crumple Fall

**Status:** In Design
**Current next step:** Opus review of slice sizing + test design
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
- On surrender: joints drop to near-zero stiffness immediately (abrupt)
- No stiffness ramp-down — character stiffens into a controlled faceplant instead of crumpling naturally

---

## Design Principles

- **Physics first** — every phase applies forces/torques and lets the simulation play out; no position overrides, no animation curves
- **Observe before tuning** — slice 1 adds debug output to see what's actually failing before touching any values
- **Soft fallback** — the forced-stand impulse becomes a gentle uprighting force, not a rocket; last resort only
- **Crumple is additive** — the fall improvement is a stiffness ramp on surrender, nothing more; surrender logic stays as-is
- **No test regressions** — existing passing tests must stay green; new tests guard the specific improvements

---

## Slices

### Slice 1 — Diagnose & Fix Forced Stand
**Goal:** Stop the rocket launch. Understand what's failing in the phase sequence.

**Changes:**
1. Add per-phase debug logging to `ProceduralStandUp.cs` (gated by `[SerializeField] bool _debugLog = false`) — log which phase is active, current height measurements vs thresholds, success/fail decisions
2. Lower `_forcedStandImpulse` from `350f` → `60f` — soft upright nudge as last resort, not a launch
3. Check `_maxStandUpAttempts` value — if ≤ 2, raise to 3 so the sequence gets proper chances before falling back
4. Fix `_groundY` drift: re-capture lowest body position each FixedUpdate during the sequence, not just at Begin()

**Exit criteria:** Character no longer launches into air after getting up. Debug log is visible in Console during play. _groundY tracks correctly.

**Tests:** None new in this slice — existing tests must still pass.

---

### Slice 2 — Tune Phase Sequence + Add Crumple
**Goal:** Each phase completes successfully using data from slice 1 debug output.

**Changes (get-up):**
- Tune `_armPushTargetHeight`, `_armPushForce`, `_legTuckTargetHeight`, `_legTuckAssistForce` to match actual ragdoll proportions (values from debug log guide this)
- Face-up detection: if `Dot(hips.up, Vector3.up) > 0.5` at sequence start, skip OrientProne (already face-up) and go directly to ArmPush with different arm targets
- Phase timeouts: review whether 0.6/0.8/0.7s are long enough for our 100Hz physics sim

**Changes (crumple fall):**
- On `TriggerSurrender()` in BalanceController: instead of immediate spring drop, ramp joint stiffness to near-zero over `_surrenderCrumpleDuration = 0.2f` seconds
- Add `_surrenderCrumpleDuration` as serialized field (0.05–0.5s range)
- Crumple ramp uses existing `SetSpringProfile` / `RampSpringProfile` if available, otherwise lerps multiplier directly

**Exit criteria:** Character visibly pushes itself up using arms and legs. Falls crumple naturally under momentum rather than stiffening. Both look physically believable in play testing.

**Tests:** None new — Benny play-tests and confirms.

---

### Slice 3 — Tests & Regression Gate
**Goal:** Lock in the improvements with results-based tests.

**New tests:**

`GetUpTests.cs`:
- `GetUp_FromFaceDown_ReachesStanding_WithinTimeout` — trigger surrender, wait for Fallen state, verify character reaches Standing within N seconds without player input
- `GetUp_NeverExceedsMaxLaunchHeight` — during get-up sequence, assert hips never exceed `standingHipsHeight * 2.5` — regression guard for the rocket launch bug
- `GetUp_CompletesWithoutForcedStandImpulse` — verify `_standUpAttempts` never reaches `_maxStandUpAttempts` under normal conditions (sequence always succeeds on its own)

`FallCrumpleTests.cs`:
- `Fall_JointStiffnessDropsGraduallyOnSurrender` — trigger surrender, sample spring multiplier at 0.05s intervals, assert it ramps down rather than snapping
- `Fall_DoesNotSnapToFaceplantAngle` — verify that after surrender, hips forward direction does not change faster than N deg/frame for first 10 frames (crumple check)

**Exit criteria:** All new tests pass. Full regression filter green (minus known pre-existing 4).

---

## Parameter Reference (after slice 2)

To be filled in after tuning. Will include all ProceduralStandUp and crumple fields with tuned prefab values, ranges, and descriptions.

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
