# Research Brief: Pre-Landing Preparation & Post-Landing Recovery

**Project:** PhysicsDrivenMovementDemo (Unity 6000.3.9f1, 100Hz physics)
**Requested by:** Benny
**Output:** Write your findings and proposed plan to `H:\Work\PhysicsDrivenMovementDemo\Plans\landing-prep\06-landing-preparation-and-recovery.md` (create the `landing-prep/` directory). Structure it as a slice-based plan in the same format as `H:\Work\PhysicsDrivenMovementDemo\Plans\gait-variation\05-organic-movement-and-gait-variation.md`.

---

## The Problem

When the character jumps off a platform (even a small one — 0.5–1m height), they land leaning sharply forward (~45°) and have to pause for ~0.5s while the balance controller rights itself before they can move forward again. This kills flow. They never fall backward — the lean is always forward, suggesting horizontal momentum is carrying the torso over the feet on touchdown.

The player feels like: *jump → hang → slam → stuck → recover → finally move*. It should feel like: *jump → land → run*.

---

## System Architecture (read these files before designing)

- `H:\Work\PhysicsDrivenMovementDemo\Assets\Scripts\Character\PlayerMovement.cs` — jump state machine, `_jumpPostLandingGraceDuration` (0.65s), `IsRecentJumpAirborne`, airborne velocity preservation
- `H:\Work\PhysicsDrivenMovementDemo\Assets\Scripts\Character\BalanceController.cs` — upright PD controller, `_jumpAirborneMultiplier` (0.85), `_landingAbsorbHeightOffset`, `_landingAbsorbDuration`, `_landingAbsorbLeanDeg`
- `H:\Work\PhysicsDrivenMovementDemo\Assets\Scripts\Character\LegAnimator.cs` — leg IK, `_airborneSpringMultiplier` (0.15 — legs go loose mid-air), `_isJumpWindUp`, `_isJumpLaunch`, `_isAirborne`; also has `SetJumpWindUpPose()`, `SetJumpLaunchPose()`

**Key existing mechanisms:**
- `_jumpAirborneMultiplier = 0.85` — balance controller torque is 85% strength during intentional jump airborne (intentionally reduced to allow lean during flight)
- `_landingAbsorbHeightOffset = 0.05m`, `_landingAbsorbDuration = 0.15s` — a small knee-bend on landing already exists
- `_airborneSpringMultiplier = 0.15` — legs go very loose/dangly mid-air (intentional for feel)
- `_jumpPostLandingGraceDuration = 0.65s` — grace period after landing before full locomotion resumes
- `_jumpWindUpPullBackDeg = 15` — torso pulls back during wind-up
- `_jumpLaunchThrustDeg = 30` — torso pitches forward during launch

**The forward lean at landing is likely caused by:**
1. `_jumpLaunchThrustDeg = 30°` forward pitch applied at launch is not fully cancelled before touchdown
2. `_jumpAirborneMultiplier = 0.85` means the balance controller is fighting it at 85% strength mid-air — not enough to correct a 30° pitch before touchdown
3. When legs go springy again on touchdown (`_airborneSpringMultiplier` restores to 1.0), horizontal momentum is still high, and the rigid legs create a forward-pitching impulse

---

## Research Questions for Opus

1. **Pre-landing leg extension:** Could extending the legs forward (increasing effective step angle ahead of centre of mass) in the last ~0.2s of airborne phase help absorb forward momentum on touchdown? What would the LegAnimator change look like?

2. **Airborne torso counter-lean:** Could we detect "about to land" (e.g. downward velocity > threshold + short raycast ahead) and briefly increase `_jumpAirborneMultiplier` toward 1.0 to let the balance controller start correcting the forward lean before impact? Risk: might look jerky mid-air.

3. **Post-landing momentum damping:** The existing `_landingAbsorbLeanDeg = 1.5°` is tiny. Could a stronger lean-absorb (capped at e.g. 8°) on touchdown translate horizontal momentum into a controlled knee-bend rather than a torso pitch? Where in BalanceController would this live?

4. **Horizontal velocity bleed on touchdown:** The real problem may be that `_jumpAirborneVelocityPreservationFactor = 0.9` means 90% of sprint momentum is preserved — and all that momentum transfers to angular momentum on impact. Could we reduce horizontal velocity by 20-30% in the first 3-5 frames after touchdown specifically? Or is there a cleaner physics approach?

5. **"Land into a run" target state:** Benny wants Super Mario-style landing — the character hits the ground already running forward, not lurching. What's the minimum viable change set to get there? Consider: does this require animation changes, or can it be achieved purely through force/torque adjustments?

---

## Constraints Opus Must Respect

- Do NOT modify `Run-UnityTests.ps1`
- Do NOT touch `_jumpLaunchHorizontalImpulse = 150` or `_jumpForce = 175` (tuned values Benny likes)
- Do NOT re-enable `_useWorldSpaceSwing` (disabled, turning is broken with it on)
- All test seams must be preserved: `SetMoveInputForTest`, `SetJumpInputForTest`, `SetCameraForTest`
- Existing tests that must stay green: `JumpTests`, `SprintJumpStabilityTests`, `JumpGapOutcomeTests`, `MovementQualityTests`
- `SprintJump_SingleJump_DoesNotFaceplant` currently passes with 50° threshold — any change must keep peak tilt below 50°
- Physics: 100Hz, 12 solver iterations — no changes to these
- Changes to `_kP`, `_kD` must be prefab overrides only, never C# field default changes

---

## Deliverable

Write a plan file to `H:\Work\PhysicsDrivenMovementDemo\Plans\landing-prep\06-landing-preparation-and-recovery.md` with:

1. **Root cause analysis** — what's actually causing the forward lurch (read the code, don't guess)
2. **Proposed approach** — which of the above avenues (or a better one you identify) is most likely to work with least risk of regressions
3. **Slice breakdown** — 2-4 slices, each with: what changes, what tests to write, exit criteria
4. **Risk flags** — what could go wrong, what to watch for

Be concrete. Read the actual code before writing. If you see something in the code that contradicts the problem description or suggests a simpler fix, say so.
