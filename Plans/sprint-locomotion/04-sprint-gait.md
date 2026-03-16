# WP-4: Sprint Gait — Stride and Cadence

## Goal
Make the sprint gait visually distinct from the walk gait: longer strides, higher knee lift, slightly faster cadence. The character should look like it is running, not just walking faster.

## Current status
- State: In progress
- Current next step: Promote WP-4 from focused command/executor coverage to the broader Arena_01 outcome checks (`T4-1` through `T4-4`) once the remaining sprint-locomotion dependencies are ready
- Blockers: Step 4 itself is no longer blocked; the remaining WP-4 outcome-gate work still depends on the outstanding WP-1 sprint-locomotion integration slices

## Scope

### 1. Stride length (step angle)
- Blend `_stepAngle` from walk value (60°) to a sprint value. New field: `_sprintStepAngle` (default 75°, range 60–90°).
- Effective = `Mathf.Lerp(_stepAngle, _sprintStepAngle, sprintNormalized)`.
- Wider step angle = longer visual stride without changing foot-placement logic.

### 2. Knee lift
- Blend `_upperLegLiftBoost` from current value (31.9°) to a sprint value. New field: `_sprintUpperLegLiftBoost` (default 42°, range 30–55°).
- Higher lift during forward swing gives the characteristic high-knee sprint look.

### 3. Cadence (step frequency)
- Current formula: `max(1 Hz, horizontalSpeed × _cadenceSpeedScale)`. At 9 m/s sprint this naturally yields higher frequency (~0.9 × 2π).
- Only add explicit sprint scaling if the natural speed-based cadence looks too slow. If needed, add `_sprintCadenceBoost` (default 1.2×, range 1–2).

### 4. Knee angle
- Optionally increase `_kneeAngle` during sprint for a more aggressive rear kick. Field: `_sprintKneeAngle` (default 70°, range 60–90°). Walk default is 60°.
- Mark as polish — only add if the gait looks under-rotated at sprint speed.

## Tests — outcome-based

### T4-1: Sprint_UpperLegRotationExceedsWalk
- **Setup**: Arena_01, sprint forward 3 s.
- **Assert**: Peak upper-leg rotation during sprint ≥ 25° (walk baseline peak is ~15–20° in existing test). This proves stride length increased.

### T4-2: Sprint_KneeLiftExceedsWalk
- **Setup**: Sprint forward 3 s.
- **Assert**: Maximum upper-leg lift angle during forward swing ≥ 35°.

### T4-3: Walk_GaitUnchanged (regression)
- **Setup**: Walk forward 5 s (no sprint).
- **Assert**: Peak upper-leg rotation stays within the existing 15° threshold from `GaitOutcomeTests`. Cadence fundamentally unchanged.

### T4-4: Sprint_LegsAlternate
- **Setup**: Sprint forward 3 s.
- **Assert**: Left and right upper-leg phase difference stays within 0.3–0.7 of a full cycle (anti-phase). Proves legs don't synchronize at higher speed.

## Decisions
- 2026-03-16: Step 1 landed by blending `LegAnimator` upper-leg swing amplitude from `_stepAngle` to `_sprintStepAngle` via `SprintNormalized`. `StepPlanner` and step-target placement were left unchanged so this slice only changes visual stride amplitude.
- 2026-03-16: Step 2 landed by blending `LegAnimator` upper-leg lift from `_upperLegLiftBoost` to `_sprintUpperLegLiftBoost` via `SprintNormalized`. The same effective lift boost now feeds both the explicit per-leg gait path and the low-confidence mirrored fallback so sprint keeps the higher-knee look even when confidence drops.
- 2026-03-16: Step 3 landed by adding `_sprintCadenceBoost` to `LegAnimator` and blending the base cadence by `SprintNormalized` only during sprint. The live prefab cadence floor (`_stepFrequency = 1.25`, `_stepFrequencyScale = 0.1`) was still clamping sprint to walk speed, so the explicit 1.2x sprint multiplier was needed. `StepPlanner` now receives the same effective cadence so swing timing and planned landings stay aligned.
- 2026-03-16: Step 4 landed by blending `LegAnimator` lower-leg knee bend from `_kneeAngle` to `_sprintKneeAngle` via `SprintNormalized`. The effective knee angle now feeds pass-through command generation, low-confidence fallback, the state-driven execution resolver, and the step-up knee-shaping helpers so sprint keeps the stronger rear kick across all runtime execution paths instead of mixing walk and sprint knee targets in the same frame.

## Artifacts
- `Assets/Scripts/Character/LegAnimator.cs`: runtime sprint stride, knee-lift, knee-angle, and cadence blends (`_sprintStepAngle`, `_sprintUpperLegLiftBoost`, `_sprintKneeAngle`, `_sprintCadenceBoost`, effective blend helpers, fallback usage, planner/executor knee-shaping alignment)
- `Assets/Tests/PlayMode/Character/LegAnimatorSprintStrideTests.cs`: focused PlayMode coverage for walk, mid-blend, and full-sprint stride, knee-lift, knee-angle, cadence command output, plus an explicit recovery-profile knee-resolution assertion through `SetCommandFrame`
- `TestResults/latest-summary.md`: focused PlayMode slice `LegAnimatorSprintStrideTests;LegAnimatorTests;GaitOutcomeTests.HoldingMoveInput_For5Seconds_UpperLegsActuallyRotate` passed after the step 4 update on 2026-03-16

## Progress notes
- 2026-03-16: Implemented step 1. Sprint now widens the effective leg step angle without changing step-target planning, and the focused PlayMode regression slice stayed green after adding dedicated sprint-stride tests.
- 2026-03-16: Implemented step 2. Sprint now raises the effective upper-leg lift boost through the same `SprintNormalized` input used by stride blending, and the focused PlayMode sprint-gait slice covers the walk, mid-blend, and full-sprint lift outputs.
- 2026-03-16: Implemented step 3. Sprint now multiplies the authored cadence by `_sprintCadenceBoost` through the same `SprintNormalized` blend used by the other gait changes, and `StepPlanner` consumes that effective cadence so faster sprint swing timing does not outrun planned landings. The focused PlayMode sprint-gait slice and the full EditMode suite both passed after the update.
- 2026-03-16: Implemented step 4. Sprint now blends the lower-leg knee bend from `_kneeAngle` to `_sprintKneeAngle`, and the effective value is shared by pass-through commands, fallback blending, state-driven execution, and step-up knee shaping so sprint keeps the stronger rear kick even during recovery or terrain-clearance execution. The focused PlayMode sprint-gait slice and the adjacent `LegAnimatorTests` / `GaitOutcomeTests` regression slice both passed after the update.
