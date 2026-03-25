# Plan 09 — Slope Traversal

**Status:** In Design — Opus review pending
**Current next step:** Opus review
**Branch prefix:** `slice/09-N-name`
**Slice prompts dir:** `H:\Work\PhysicsDrivenMovementDemo\Plans\slope-traversal\prompts\`

---

## Goal

The character currently struggles on inclines — feet don't plant correctly on slopes, height maintenance fights the terrain, and uphill movement is slow and jank. This plan makes incline traversal feel natural: feet plant flat against the slope, hips maintain the correct height above the surface (not world Y), and the character leans naturally into the slope.

---

## Root Cause

Three stacked problems:

1. **Foot rotation ignores surface normal** — feet stay world-aligned (flat) even when standing on a slope. They clip through or hover, breaking the visual and affecting IK solve quality.

2. **Height maintenance targets world Y** — `BalanceController` maintains hips at `_standingHipsHeight` above world ground, not above the slope surface. Walking uphill means hips are being actively pulled "down" relative to target as the slope rises, draining speed and causing stumbles.

3. **Move force is horizontal** — `PlayerMovement` applies force in the world XZ plane. On a 35° slope, the effective uphill component is `cos(35°) ≈ 82%` of intended. Minor, but stacks with the above.

There's already a `surfaceNormalQuality` concept in `StepPlanner` / `FootContactObservation` — slope detection infrastructure is partially there.

---

## Approach

- **Slice 1 — Slope detection seam**: Add surface normal sampling to `BalanceController` (or a new `SlopeObserver` helper). Raycast down from hips each FixedUpdate, expose `SurfaceNormal` and `SlopeAngleDeg` as public properties. Add `_debugLog` flag to visualise slope readings in the console.

- **Slice 2 — Height maintenance fix**: Adjust height maintenance target to be `_standingHipsHeight` above the raycast hit point, not above world zero. Gate: only when slope is below a max angle (e.g. 50°) and surface normal quality is acceptable.

- **Slice 3 — Foot rotation on slope**: In `LegAnimator`, rotate each foot's target orientation to match the surface normal at the step landing position. Sample normal at `StepTarget.LandingPosition` via raycast. Interpolate smoothly — don't snap.

- **Slice 4 — Lean into slope**: In `BalanceController`, add a small forward lean proportional to slope angle when moving uphill (and backward lean downhill). Pure visual — adjust `uprightTarget` rotation, no forces. Gate on `SlopeAngleDeg > threshold` and move input.

- **Slice 5 — Tests**: Time to traverse a 35° incline, foot alignment on slope test, height maintenance stability test.

---

## Slices

### Slice 1 — Slope Detection Seam
**Goal:** Add surface normal sampling to BalanceController. No behaviour changes — just expose the data and debug visibility.

**Changes:**
- `BalanceController.cs`: add `[SerializeField] bool _debugSlope`, raycast from hips down in FixedUpdate, expose `public Vector3 SurfaceNormal`, `public float SlopeAngleDeg`, `public float SurfaceNormalQuality`. Use `Physics.Raycast` with a generous distance (2m). Store hit normal; if no hit use `Vector3.up`.

**Exit criteria:**
- `SlopeAngleDeg` reads ~35 when standing on a 35° slope (verify in Inspector or debug log)
- `SurfaceNormal` reads `Vector3.up` on flat ground
- No behaviour change on flat ground — regression filter fully green

**Tests:** None — regression run is the gate.

---

### Slice 2 — Surface-Relative Height Maintenance
**Goal:** Hips maintain correct height above slope surface, not world Y.

**Changes:**
- `BalanceController.cs`: replace flat `targetHipsY = _standingHipsHeight` with `targetHipsY = hitPoint.y + _standingHipsHeight`. Gate on `SlopeAngleDeg < _slopeHeightMaxAngle` (new serialized field, default 50°) and `SurfaceNormalQuality > 0.5`. Fall back to current behaviour if no hit or quality too low.

**Exit criteria:**
- On a 35° slope, hips maintain consistent visual height above surface when stationary
- Uphill walking no longer shows the character "sinking" into slope or fighting height loss
- Flat ground regression: hips height unchanged — all existing tests green

**Tests:** None — regression run is the gate.

---

### Slice 3 — Foot Rotation on Slope
**Goal:** Feet plant flat against slope surface rather than staying world-horizontal.

**Changes:**
- `LegAnimator.cs`: when computing foot IK target rotation, raycast down at `StepTarget.LandingPosition` to get surface normal. Blend foot rotation: `Quaternion.Slerp(worldFlat, slopeAligned, blendFactor)`. Blend factor driven by `SlopeAngleDeg / 45f` (clamped 0-1). New serialized field: `_footSlopeAlignmentBlend` (default 1.0, range 0-1) for disable in tests.

**Exit criteria:**
- Feet visually plant flat on a 35° slope (no hovering or clipping)
- Flat ground: foot rotation unchanged
- Full regression filter green

**Tests:** None — regression run is the gate.

---

### Slice 4 — Forward Lean on Slope
**Goal:** Character leans naturally into uphill slopes (and back slightly on downhill).

**Changes:**
- `BalanceController.cs`: compute lean angle as `SlopeAngleDeg * _slopeLeanScale` (new field, default 0.4, range 0-1). Apply as additional forward/back tilt to `uprightTarget`. Gate on moving uphill (dot of move direction vs slope direction > 0) and `SlopeAngleDeg > 5°`. VISUAL ONLY — no force changes.

**Exit criteria:**
- Character visibly leans forward when walking up a 35° slope
- No lean on flat ground
- Lean is smooth, not snappy (use existing smoothing in uprightTarget or add dedicated lerp)
- Full regression filter green

**Tests:** None — regression run is the gate.

---

### Slice 5 — Tests & Regression Gate
**Goal:** Lock in improvements with results-based tests.

**New test file: `Assets/Tests/PlayMode/Character/SlopeTraversalTests.cs`**

- `SlopeTraversal_35Degree_ReachesTopWithinTimeLimit` — spawn character at base of 35° incline 8m long, apply forward move input, assert hips reach top within N seconds (N to be determined after HITL tuning — start with 12s)
- `SlopeTraversal_FeetAlignToSlope_WithinAngleTolerance` — on a 35° slope, after 1s of standing still, assert each foot's up-vector is within 10° of slope surface normal
- `SlopeTraversal_HeightMaintenance_StableOnSlope` — walk up 35° slope, sample hips Y relative to surface every 10 frames for 3s, assert variance is below threshold (no bouncing or sinking)
- `SlopeTraversal_FlatGroundRegression_HeightUnchanged` — assert hips height on flat ground matches pre-plan baseline within 2cm

**Exit criteria:** All new tests pass. Full regression filter green (minus known pre-existing failures).

---

## Parameter Reference

*To be filled after slice 4 tuning.*

---

## Agent Log

*To be filled as slices complete.*

---

## Known Pre-Existing Test Failures (exclude from gate)

- `SustainedLocomotionCollapse_TransitionsIntoFallen`
- `CompleteLap_WithinTimeLimit_NoFalls`
- `TurnAndWalk_CornerRecovery`
- `LandingRecovery_SpringRampsGraduallyAfterLanding`
- `LandingRecovery_DampingDisabledWhenFactorIsOne`
- `SprintJump_TwoConsecutiveJumps_DoesNotFaceplant` (order-sensitive)
- `WalkStraight_NoFalls` (order-sensitive)
