# WP-3: Sprint Arm Behaviour

## Goal
Make arms pump more aggressively during sprint — wider swing, tighter elbow bend — to visually sell the higher effort level.

## Current status
- State: In progress
- Current next step: Add outcome-based PlayMode coverage for sprint arm swing and elbow bend on `Arena_01`
- Blockers: None

## Scope

### 1. Speed-scaled arm parameters
- Read `SprintNormalized` from `PlayerMovement` (or from `LegAnimator`'s cached copy if already propagated).
- Blend `_armSwingAngle` from its walk value (20°) up to a sprint value. New field `_sprintArmSwingAngle` (default 45°, range 20–60°).
- Blend `_elbowBendAngle` from 15° to a sprint value. New field `_sprintElbowBendAngle` (default 35°, range 15–45°).
- Effective angle = `Mathf.Lerp(walkValue, sprintValue, sprintNormalized)`.

### 2. Optional: Reduce abduction during sprint
- Runners typically hold arms tighter to the body at speed. Consider reducing `_restAbductionAngle` slightly during sprint (12° → 8°). Mark this as a polish item — only implement if it looks better.

### 3. Phase relationship
- Arms must remain counter-phase with legs (already the case). No change to phase logic needed.

## Tests — outcome-based

### T3-1: Sprint_ArmSwingAngleIncreases
- **Setup**: Arena_01, sprint forward 3 s.
- **Assert**: Peak upper-arm joint rotation (measured on UpperArm_L or UpperArm_R `ConfigurableJoint.targetRotation` angle magnitude) during sprint ≥ 30°. Walk baseline is ≈20°.

### T3-2: Sprint_ElbowBendIncreases
- **Setup**: Arena_01, sprint forward 3 s.
- **Assert**: LowerArm elbow-bend target during sprint ≥ 25°. Walk baseline is ≈15°.

### T3-3: Walk_ArmSwingUnchanged (regression)
- **Setup**: Walk forward 3 s (no sprint).
- **Assert**: Peak arm swing stays within 15–25° — proves sprint parameters are not leaking into walk.

## Decisions
- `ArmAnimator` reads `SprintNormalized` directly from sibling `PlayerMovement` instead of routing the value through `LegAnimator`.
- Rest pose keeps the sprint-blended elbow bend while `SprintNormalized` ramps back down so sprint release does not snap forearms to the walk bend on the first idle frame.

## Artifacts
- `Assets/Scripts/Character/ArmAnimator.cs`
- `Assets/Tests/EditMode/Character/ArmAnimatorTests.cs`

## Progress notes
- 2026-03-16: Scope step 1 implemented. `ArmAnimator` now lerps upper-arm swing from 20°→45° and elbow bend from 15°→35° using `PlayerMovement.SprintNormalized`. Focused verification passed: `ArmAnimatorTests` EditMode 7/7 and `ArmAnimatorPlayModeTests` PlayMode 2/2.
