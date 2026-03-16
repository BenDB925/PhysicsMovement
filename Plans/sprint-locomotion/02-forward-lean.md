# WP-2: Forward Lean During Sprint

## Goal
Tilt the character's upright target forward during sprint so the body visually leans into the run — a key visual cue that distinguishes sprinting from walking.

## Current status
- State: Not started
- Current next step: Add sprint-lean parameters to `BalanceController`
- Blockers: WP-1 must land first (needs `SprintNormalized`)

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

## Artifacts

## Progress notes
