# WP-2: Forward Lean During Sprint

## Goal
Tilt the character's upright target forward during sprint so the body visually leans into the run — a key visual cue that distinguishes sprinting from walking.

## Current status
- State: Complete
- Current next step: None. WP-2 acceptance scope is complete; resume from the parent sprint plan for the next slice.
- Blockers: None for step 1. `SprintNormalized` is already available and the lean path now runs through `LocomotionDirector` -> `BodySupportCommand` -> `BalanceController`.

## Scope

### 1. Lean target in BalanceController
- Read `PlayerMovement.SprintNormalized` in `BalanceController.FixedUpdate`.
- Blend a forward tilt offset into the upright target quaternion: `leanAngle = Mathf.Lerp(0, _sprintLeanAngle, sprintNormalized)`.
- New field: `_sprintLeanAngle` (default 8°, range 0–15°). This is independent of the existing pelvis-expression tilt (0–3°) and stacks additively in the travel direction.
- The lean direction follows `PlayerMovement.CurrentFacingDirection` projected onto the ground plane.

### 2. Smooth ramp
- Lean onset/offset should track the `SprintNormalized` ramp (0.25 s) so there is no pop when sprint starts or stops.

### 3. Safety: lean vs. fall detection
- The additional ~8° of intentional lean must not trigger `CharacterState.Fallen` (threshold 65°). Current walk tilts are small (~3°), so 8° additive is well within margin, but verify.

## Tests — outcome-based

### T2-1: Sprint_ForwardLeanIncrease
- **Setup**: Arena_01, settle 1 s, sprint forward 3 s.
- **Assert**: Average hips-forward tilt during sprint ≥ 5° (measured as angle between hips-up and world-up projected on the travel plane).
- **Regression guard**: Walk-only tilt stays < 5° average over the same 3 s window.

### T2-2: SprintEnd_LeanRecovers
- **Setup**: Sprint 3 s, release sprint, walk 2 s.
- **Assert**: Hips-forward tilt during the final 1 s of walking < 4° average, proving the lean has released.

### T2-3: Sprint_DoesNotTriggerFallen
- **Setup**: Sprint forward 5 s on flat ground.
- **Assert**: `CharacterState` never enters `Fallen` during the run.

## Decisions
- 2026-03-16: Recovery-profile lean attenuation now applies only to turn lean. Sprint lean remains independent so stable straight sprint keeps its full posture cue even when observation recovery is active.

## Artifacts
- `Assets/Scripts/Character/Locomotion/LocomotionDirector.cs`: Sprint lean now bypasses recovery-only turn-lean attenuation while still ramping from `SprintNormalized`.
- `Assets/Tests/PlayMode/Character/LocomotionDirectorTests.cs`: Direct PlayMode coverage that the director lean command rises and falls through an in-between posture during sprint onset and release.
- `Assets/Tests/PlayMode/Character/SprintLeanOutcomeTests.cs`: Arena_01 outcome coverage for sprint lean increase, lean recovery after sprint release, and Fallen-state safety.

## Progress notes
- 2026-03-16: Step 1 implemented through the existing body-support command path instead of a direct `BalanceController` read. `LocomotionDirector` now adds sprint-normalized lean degrees, and `BalanceController` applies commanded lean to the upright target in addition to the existing COM lean shift.
- 2026-03-16: Added focused coverage for the new sprint-lean tuning field, sprint-to-support-command propagation, and runtime commanded-lean posture on the real prefab. Focused verification passed: `LocomotionDirectorEditModeTests` (8/8) and `LocomotionDirectorTests` + `BalanceControllerIntegrationTests` (26/26).
- 2026-03-16: Added the remaining WP-2 PlayMode coverage: a direct director ramp test plus Arena_01 outcome tests for forward-lean increase, release back to walk posture, and Fallen-state safety. Focused sprint-lean verification passed 4/4.
- 2026-03-16: Fixed a runtime gap where recovery response scaling damped the entire lean budget and erased the sprint posture cue on straight runs. The broader nearby regression slice `LocomotionDirectorTests` + `BalanceControllerIntegrationTests` + `SprintLeanOutcomeTests` passed 29/30; the remaining `FixedUpdate_WhenStateMachineConfidenceDrops_ConvergesTowardMirroredFallbackWithoutOneFrameSnap` failure is pre-existing/unrelated and still fails in isolation.
