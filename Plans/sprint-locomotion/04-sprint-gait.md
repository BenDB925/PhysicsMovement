# WP-4: Sprint Gait — Stride and Cadence

## Goal
Make the sprint gait visually distinct from the walk gait: longer strides, higher knee lift, slightly faster cadence. The character should look like it is running, not just walking faster.

## Current status
- State: Not started
- Current next step: Add sprint-scaled gait parameters to `LegAnimator`
- Blockers: WP-1 must land first (needs `SprintNormalized`)

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

## Artifacts

## Progress notes
