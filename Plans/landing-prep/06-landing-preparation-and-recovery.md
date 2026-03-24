# Plan 06 — Landing Preparation & Post-Landing Recovery

**Status:** Complete (slices 1–3 shipped; slice 4 deferred — landing feels correct without it)
**Branch prefix:** `slice/06-N-name`
**Slice prompts dir:** `H:\Work\PhysicsDrivenMovementDemo\Plans\landing-prep\prompts\`

**Latest progress:** All three primary slices complete and merged to master (2026-03-24). Slice 1 (spring ramp, 29/29), Slice 2 (tilt suppression + counter-lean, 32/32), Slice 3 (horizontal damping, 35/35). Landing confirmed feeling correct in playtesting. Slice 4 (pre-landing leg extension) deferred — not needed.

---

## Goal

Eliminate the ~0.5s forward-lurch pause after jump landings so the character transitions smoothly from airborne to grounded locomotion. The player should feel *jump → land → run*, not *jump → slam → stuck → recover → move*.

---

## Root Cause Analysis

**Correction from the brief:** `_jumpLaunchThrustDeg = 30°` and `_jumpWindUpPullBackDeg = 15°` are **arm swing angles** on `ArmAnimator.cs` (lines 120, 130). They do NOT rotate the torso. The brief's hypothesis that these cause forward lean is incorrect — they only move the arms forward/back during launch. The torso forward lean has different causes.

### Primary Cause: Instant Spring Restoration Shock

When landing, `LegAnimator.OnCharacterStateChanged` (line 2357) calls `_jointDriver.SetSpringMultiplier(1f)` — an **instantaneous** snap from 0.15x (dangling airborne) to 1.0x (full stiffness). This 6.7x stiffness jump creates a rigid-impact event:

1. During airborne, leg springs are at 15% strength → legs trail behind / hang loosely
2. At the instant of ground contact, springs snap to 100% → legs become rigid pillars
3. The torso, still carrying full horizontal momentum (preserved at 90% by `_jumpAirborneVelocityPreservationFactor`), pivots forward over the suddenly-rigid feet like a pole-vaulter
4. The angular impulse from this pivot is the dominant source of the ~45° forward pitch

This is a **physics shock** — the same mechanism as landing on locked knees in real life.

### Contributing Cause 1: Acceleration-Driven Pelvis Tilt Spike

`BalanceController` lines 1396–1411 compute `_smoothedPelvisTiltDeg` from the delta between instantaneous and smoothed forward speed. On landing:

- Instantaneous speed drops sharply as ground friction bites
- `_smoothedForwardSpeed` lags behind (Lerp with `_pelvisTiltSmoothing * 0.5`)
- `speedDelta = forwardSpeed - _smoothedForwardSpeed` goes negative → forward tilt target
- This adds additional forward lean to the upright target **on top of** the physics-induced pitch

The tilt system was designed for smooth ground locomotion, not the sudden speed transients of landing.

### Contributing Cause 2: Landing Absorption Adds Forward Lean

`_landingAbsorbLeanDeg = 1.5°` (clamped to max 1.5° in `Awake` at line 198) is added to `totalPelvisTilt` during the squat phase (line 1443). This tilts the upright **target** forward during landing, meaning the PD controller (now at 5x boost from `_jumpLandingGainBoost`) actively drives the torso forward rather than fighting the pitch.

### Contributing Cause 3: No Horizontal Velocity Damping on Landing

`ApplyRecentJumpAirborneVelocityPreservation()` is gated on `!_balance.IsGrounded` (line 923), so it correctly stops at landing. However, there is no corresponding damping path — the character lands with 90%+ of its sprint horizontal velocity intact, and the only deceleration comes from ground friction and the normal movement forces. With the PD controller fighting a tilted target, and legs suddenly rigid, all that horizontal momentum converts to angular momentum before friction can bleed it off.

### Summary: The Cause Chain

```
airborne (springs 0.15x, legs loose, torso slightly forward)
    ↓ ground contact
springs snap 0.15x → 1.0x instantly (rigid-impact shock)
    ↓
horizontal momentum pivots torso forward around rigid feet
    ↓
pelvis tilt detects "deceleration" → adds MORE forward lean target
    ↓
landing absorb adds +1.5° forward lean to upright target
    ↓
PD at 5x boost aggressively corrects… toward a forward-leaning target
    ↓
~45° forward pitch → 0.5s recovery pause
```

### What Does NOT Need to Change

- `_jumpLaunchHorizontalImpulse = 150` (now 2600 in code; brief says 150 — using code value): **do not touch** per constraint
- `_jumpForce = 175` (code shows 100; brief says 175 — using code value): **do not touch** per constraint
- `_jumpAirborneVelocityPreservationFactor = 0.9`: good for flight; the problem is at touchdown
- Arm swing angles: cosmetic only, not contributing to torso pitch

---

## Design Principles

- **Gradual not instant** — All transitions between airborne and grounded states should ramp, not snap
- **Prevention over cure** — Prepare the body for landing before impact rather than fighting the consequences after
- **Physics-native** — Use spring curves, damping forces, and target blending — not animation overrides
- **No test regressions** — `JumpTests|SprintJumpStabilityTests|JumpGapOutcomeTests|MovementQualityTests` must stay green throughout
- **Prefab-tunable** — New parameters are serialized fields with sane defaults; `_kP`/`_kD` changes via prefab overrides only

---

## Slices

### Slice 1 — Gradual Spring Restoration (Primary Fix)
**Files:** `LegAnimator.cs`, `LegJointDriver.cs` (if needed)

**Status:** Complete (2026-03-24)

**Implemented:** `LegAnimator` now starts a landing spring ramp instead of snapping directly to full stiffness, restarts the ramp from real touchdown contact even while `CharacterState` is still in airborne grace, and cancels any active ramp when jump wind-up / launch re-enters the airborne path. `LegJointDriver` exposes the current spring multiplier for test observation. Prefab overrides currently use `_landingSpringRampDuration = 0.06` and `_landingSpringRampCurve = 0.5`.

**Verification:** Added `LandingRecoveryTests` and updated `AirborneSpringTests` to pin legacy instant-restore assertions to the explicit `duration = 0` compatibility path. Focused PlayMode verification on `LandingRecoveryTests|JumpTests|SprintJumpStabilityTests|JumpGapOutcomeTests` finished 29/29 green.

**What:** Replace the instant 0.15x → 1.0x spring snap with a time-based ramp over a tunable duration.

**Implementation:**
1. Add serialized fields to `LegAnimator`:
   - `_landingSpringRampDuration` (float, default 0.12s) — time to ramp from airborne spring to full spring after landing
   - `_landingSpringRampCurve` (float, default 0.5) — exponent for non-linear ramp (< 1.0 = fast start, > 1.0 = slow start; 0.5 gives a square-root ease that reaches ~70% stiffness in the first half)
2. On `OnCharacterStateChanged` exiting Airborne (line 2352), instead of immediately setting spring to 1.0:
   - Set `_landingSpringRampTimer = _landingSpringRampDuration`
   - Set `_landingSpringStartMultiplier = _airborneSpringMultiplier` (capture current value)
3. In `FixedUpdate` (or wherever joint drives are updated), while `_landingSpringRampTimer > 0`:
   - `float t = 1f - (_landingSpringRampTimer / _landingSpringRampDuration)`
   - `float curvedT = Mathf.Pow(t, _landingSpringRampCurve)`
   - `float currentMul = Mathf.Lerp(_landingSpringStartMultiplier, 1f, curvedT)`
   - `_jointDriver.SetSpringMultiplier(currentMul)`
   - On timer expiry: `_jointDriver.SetSpringMultiplier(1f)` (ensure clean finish)
4. Bypass: If `_landingSpringRampDuration <= 0`, keep existing instant behaviour (backwards compatible)

**Why this works:** The legs gradually stiffen over 0.12s (12 frames at 100Hz) instead of in a single frame. The torso receives a distributed support ramp rather than an impulse, preventing the pole-vault pivot. The square-root ease means the legs are already at ~70% stiffness by frame 6, so the character doesn't feel floaty on landing.

**Test seam:** `SetLandingSpringRampDurationForTest(float duration)` — allows tests to control the ramp. Existing tests can set 0 to get instant behaviour if needed.

**Tests (new `LandingRecoveryTests.cs`):**
- `SprintJump_Landing_SpringRampsGradually`: Sprint → jump → land. Assert: immediately after grounding, spring multiplier < 1.0; after `_landingSpringRampDuration` seconds, spring multiplier == 1.0.
- `SprintJump_Landing_PeakTiltReduced`: Sprint → jump → land. Record peak upright angle after touchdown. Assert: peak tilt < 35° (current system allows up to ~45°). This is the primary quality gate.
- `SprintJump_Landing_NoFall`: Sprint → jump → land. Assert: no Fallen state.

**Existing test regression check:**
- `SprintJump_SingleJump_DoesNotFaceplant`: must still pass (50° threshold) — should IMPROVE since peak tilt decreases
- `JumpGapOutcomeTests`: must still pass — gap crossing depends on flight, not landing spring

**Exit criteria:** Peak landing tilt < 35° on flat-ground sprint jump; gradual spring ramp visible in metrics; all existing tests green.

---

### Slice 2 — Landing Pelvis Tilt Suppression + Absorb Lean Fix
**Files:** `BalanceController.cs`

**What:** Two targeted fixes to the upright target computation during landing:

**Part A — Suppress acceleration-driven pelvis tilt during landing absorption:**
1. In the pelvis tilt computation (line 1396), gate on landing absorb phase:
   ```
   if (_landingAbsorbTimer > 0f)
   {
       pelvisTiltTarget = 0f;
       _smoothedForwardSpeed = forwardSpeed; // Reset baseline to avoid spike when absorb ends
   }
   ```
2. This prevents the speed-delta detector from interpreting the landing deceleration as intentional slowing and adding forward lean.

**Part B — Zero out or invert landing absorb lean:**
1. Change `_landingAbsorbLeanDeg` default from 1.5° to 0° (or make it slightly negative, e.g., -2° for subtle counter-lean)
2. The rationale: adding forward lean on landing was designed for a slow gentle touchdown, but with sprint momentum it amplifies the forward pitch instead of absorbing it
3. Add a new serialized field `_landingCounterLeanDeg` (float, default 2.0°) that replaces the forward lean with a brief backward lean impulse during the squat phase, helping the PD controller fight the forward pitch instead of assisting it
4. The counter-lean contribution in `totalPelvisTilt` (line 1443) becomes:
   ```
   - _landingCounterLeanDeg * landingAbsorbBlend  // note: subtracted, tilts backward
   ```
5. `_landingAbsorbLeanDeg` is kept but defaults to 0° — if Benny wants a small forward component it can be re-added via prefab override

**Tests (added to `LandingRecoveryTests.cs`):**
- `SprintJump_Landing_PelvisTiltSuppressed`: Sprint → jump → land. Record `_smoothedPelvisTiltDeg` during the first 15 frames (0.15s) after touchdown. Assert: absolute value stays < 3° (no deceleration spike).
- `SprintJump_Landing_CounterLeanActive`: Sprint → jump → land. Assert: during landing absorb phase, the upright target has a slight backward lean (total pelvis tilt < 0° for at least 5 consecutive frames).
- `SprintJump_Landing_RecoveryTime`: Sprint → jump → land. Record frames until upright angle < 5°. Assert: recovery < 25 frames (0.25s) — current system is ~50 frames (0.5s).

**Existing test regression check:**
- `SprintJump_SingleJump_DoesNotFaceplant`: threshold 50° — should improve significantly with both Slice 1 + 2

**Exit criteria:** No pelvis tilt spike on landing; counter-lean visible in metrics; recovery time < 0.25s; all existing tests green.

---

### Slice 3 — Post-Landing Horizontal Velocity Damping
**Files:** `PlayerMovement.cs`, `BalanceController.cs`

**What:** Apply a brief, bounded horizontal velocity reduction in the first frames after touchdown to bleed off the momentum that otherwise becomes angular momentum.

**Implementation:**
1. Add serialized fields to `PlayerMovement`:
   - `_jumpLandingHorizontalDampingFactor` (float, default 0.75) — fraction of horizontal speed to preserve on landing (0.75 = 25% bleed)
   - `_jumpLandingHorizontalDampingDuration` (float, default 0.08s) — window after touchdown during which damping applies
   - `_jumpLandingHorizontalDampingIsActive` (bool, runtime) — true during damping window
   - `_jumpLandingHorizontalDampingTimer` (float, runtime)
2. On landing detection (when `_recentJumpAirborne && _balance.IsGrounded` first becomes true):
   - Apply a one-shot velocity reduction: `_rb.linearVelocity = new Vector3(hv.x * factor, _rb.linearVelocity.y, hv.z * factor)` where factor ramps from `_jumpLandingHorizontalDampingFactor` to 1.0 over the duration
   - Alternatively: use `AddForce` with `ForceMode.VelocityChange` to subtract a fraction of horizontal velocity each frame over the window — this is smoother and more physics-friendly
3. The damping applies only during `_jumpLandingHorizontalDampingDuration` — not the entire 0.65s grace period
4. Bypass: factor of 1.0 means no damping (backwards compatible default if desired, though 0.75 is the recommended default)

**Why this works:** Removing 25% of horizontal velocity in the first 8 frames after contact means significantly less angular momentum at the pivot point. Combined with Slice 1's gradual spring ramp, the torso no longer receives a violent forward pitch.

**Why not more aggressive?** Benny wants "land into a run" — too much damping kills forward momentum and makes landing feel stuck. 25% over 0.08s is barely perceptible as a speed change but significantly reduces pitch torque.

**Tests (added to `LandingRecoveryTests.cs`):**
- `SprintJump_Landing_HorizontalSpeedReduced`: Sprint at known speed → jump → land. Measure horizontal speed 10 frames after touchdown. Assert: speed is between 65% and 85% of pre-jump speed (bounded damping, not a hard stop).
- `SprintJump_Landing_SpeedRecovery`: Sprint → jump → land → continue sprinting. Assert: within 30 frames (0.3s) of landing, horizontal speed returns to within 90% of sprint speed (locomotion forces restore speed quickly).
- `SprintJump_Landing_NoFall`: Assert no Fallen state throughout.

**Existing test regression check:**
- `JumpGapOutcomeTests`: damping only applies after grounding, so gap crossing is unaffected
- `MovementQualityTests`: landing damping is brief; sustained sprint metrics should be unaffected

**Exit criteria:** Measurable horizontal speed reduction on landing; speed recovers quickly; no test regressions.

---

### Slice 4 — Pre-Landing Leg Extension (Polish)
**Files:** `LegAnimator.cs`

**What:** In the last ~0.15s before touchdown while descending, extend legs forward and slightly stiffen springs (pre-brace) so the feet reach ahead of the centre of mass and the body is prepared for impact.

**Implementation:**
1. Add serialized fields to `LegAnimator`:
   - `_preLandingDetectionDistance` (float, default 0.8m) — raycast distance below hips to detect upcoming ground
   - `_preLandingMinDescentSpeed` (float, default 1.0 m/s) — minimum downward velocity to trigger pre-landing
   - `_preLandingSpringMultiplier` (float, default 0.5) — intermediate spring stiffness during pre-landing (between 0.15 and 1.0)
   - `_preLandingLegExtensionDeg` (float, default 10°) — forward leg extension angle (shifts feet ahead of COM)
2. Each `FixedUpdate` while `_isAirborne`:
   - Cast a short ray downward from hips: `Physics.Raycast(hipsPos, Vector3.down, out hit, _preLandingDetectionDistance)`
   - Check `_rb.linearVelocity.y < -_preLandingMinDescentSpeed`
   - If both true → enter pre-landing state:
     - Ramp spring multiplier from current (0.15) toward `_preLandingSpringMultiplier` (0.5)
     - Shift step targets slightly forward of COM by `_preLandingLegExtensionDeg` to widen the support base
3. The pre-landing state transitions into the Slice 1 landing ramp naturally — Slice 1's ramp starts from whatever spring multiplier is current (which may now be 0.5 instead of 0.15, making the ramp smoother)
4. Bypass: `_preLandingDetectionDistance = 0` disables the feature

**Why this helps:** The character's legs tighten and extend before contact, like an athlete preparing for a landing. This:
- Reduces the spring shock from 6.7x to 2x (0.5 → 1.0 instead of 0.15 → 1.0)
- Shifts the effective contact point forward, widening the support polygon and reducing the pivot torque
- Looks natural — athletes always extend their legs before landing

**Risk:** Raycast might trigger prematurely on slopes or platforms above the character. Use `QueryTriggerInteraction.Ignore` and layer mask the same as `GroundSensor`.

**Tests (added to `LandingRecoveryTests.cs`):**
- `SprintJump_PreLanding_SpringStiffensBeforeTouchdown`: Sprint → jump. During descent, record spring multiplier. Assert: multiplier rises above 0.15 before `IsGrounded` becomes true.
- `SprintJump_PreLanding_PeakTiltFurtherReduced`: Sprint → jump → land. Assert: peak tilt < 25° (improvement over Slice 1's 35° target)
- `SprintJump_PreLanding_NoFalseActivation`: Sprint on flat ground (no jump). Assert: pre-landing state never activates.
- `SprintJump_PlatformDrop_LandClean`: Walk off a 1m platform → land. Assert: recovery < 20 frames, no Fallen state.

**Exit criteria:** Legs visibly extend before landing; spring multiplier ramps before touchdown; peak tilt < 25°; no false activations during normal ground movement; all existing tests green.

---

## Risk Flags

### Regression Risk: Landing Absorption Timing
The landing absorb knee-bend (LegAnimator) and height offset (BalanceController) fire on the same `OnCharacterStateChanged` event. If Slice 1's gradual spring ramp interacts badly with the knee-bend boost, the character might sink too low on landing. **Mitigation:** Knee-bend boost should be independent of spring multiplier — verify in testing.

### Regression Risk: Gap Crossing
`JumpGapOutcomeTests` depend on the character maintaining airborne physics during flight. Slice 3's horizontal damping is gated on `IsGrounded`, so it won't affect flight. Slice 4's pre-landing leg extension could marginally reduce the flight trajectory if it fires too early. **Mitigation:** Gate pre-landing on downward velocity threshold (no activation during the ascending phase).

### Regression Risk: Double Jump
If the character jumps again quickly after landing, the spring ramp (Slice 1) might still be active. Entering a new wind-up while springs are at 0.7x could weaken the launch. **Mitigation:** On wind-up entry, immediately set spring to 1.0 and cancel any active ramp.

### Tuning Risk: Horizontal Damping vs. "Land Into Run"
Slice 3's damping factor needs careful tuning. Too much (factor < 0.6) makes landing feel like hitting a wall. Too little (factor > 0.9) doesn't fix the pitch problem. **Mitigation:** Start conservative (0.75 = 25% reduction), measure, tune via prefab override.

### Timing Risk: Pelvis Tilt Reset
Slice 2's `_smoothedForwardSpeed = forwardSpeed` reset on landing absorb entry could cause a visual pop if the smoothed speed jumps discontinuously. **Mitigation:** Verify in PlayMode that the transition looks clean; if not, use a fast Lerp (e.g., `10 * Time.fixedDeltaTime`) instead of instant reset.

---

## Slice Dependencies

```
Slice 1 (spring ramp)  ← standalone, primary fix
Slice 2 (tilt suppress) ← standalone, stacks with Slice 1
Slice 3 (h-vel damping) ← standalone, stacks with Slice 1 + 2
Slice 4 (pre-landing)   ← depends on Slice 1 (ramp start value)
```

Slices 1–3 are independent and can be done in any order. Slice 4 depends on Slice 1 because it feeds into the spring ramp's start value.

**Recommended execution order:** 1 → 2 → 3 → 4 (most impactful fix first, polish last).

---

## Expected Combined Outcome

| Metric | Current | After Slice 1 | After 1+2 | After 1+2+3 | After All |
|--------|---------|---------------|-----------|-------------|-----------|
| Peak landing tilt (sprint jump, flat) | ~45° | < 35° | < 25° | < 20° | < 15° |
| Recovery to < 5° upright (frames) | ~50 (0.5s) | ~30 (0.3s) | ~20 (0.2s) | ~15 (0.15s) | ~10 (0.1s) |
| Horizontal speed retained | ~90% | ~90% | ~90% | ~70% → 90% within 0.3s | ~70% → 90% within 0.3s |
| Subjective feel | slam → stuck → recover | softer land → recover | softer land → quick recover | land → brief slow → run | land → run |

---

## Existing Tests That Must Stay Green

- `JumpTests` (all)
- `SprintJumpStabilityTests` — `FaceplantAngleThreshold = 50°` (should improve)
- `JumpGapOutcomeTests` (all)
- `MovementQualityTests` (all)
- `OrganicGaitVariationTests`, `IdleSwayTests`, `IdleVerticalBobTests` (Plan 05 tests)

---

## Parameters Summary (All New Serialized Fields)

| Slice | Class | Field | Type | Default | Prefab Override Expected |
|-------|-------|-------|------|---------|--------------------------|
| 1 | LegAnimator | `_landingSpringRampDuration` | float | 0.12 | Yes, tune 0.08–0.20 |
| 1 | LegAnimator | `_landingSpringRampCurve` | float | 0.5 | Maybe, 0.3–1.0 |
| 2 | BalanceController | `_landingCounterLeanDeg` | float | 2.0 | Yes, tune 0–5 |
| 3 | PlayerMovement | `_jumpLandingHorizontalDampingFactor` | float | 0.75 | Yes, tune 0.6–0.9 |
| 3 | PlayerMovement | `_jumpLandingHorizontalDampingDuration` | float | 0.08 | Maybe, 0.05–0.15 |
| 4 | LegAnimator | `_preLandingDetectionDistance` | float | 0.8 | Maybe |
| 4 | LegAnimator | `_preLandingMinDescentSpeed` | float | 1.0 | Maybe |
| 4 | LegAnimator | `_preLandingSpringMultiplier` | float | 0.5 | Yes, tune 0.3–0.7 |
| 4 | LegAnimator | `_preLandingLegExtensionDeg` | float | 10.0 | Yes, tune 5–20 |
