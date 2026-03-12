# Unified Locomotion Roadmap

## Status
- State: In Progress
- Acceptance target: Finish the locomotion authority migration through validation, terrain robustness, and expression without regressing the focused chapter gates or baseline artifacts.
- Current next step: Finish Chapter 3 C3.3 by resolving the mixed-slice `LowerLeg_WhenWalking_FeetAlternate` instability, then proceed to C3.4 state-aware asymmetry.
- Active blockers: Broader mixed PlayMode slices still expose `LowerLeg_WhenWalking_FeetAlternate` instability while the focused `LegAnimatorTests` slice stays green.

## Quick Resume
- Canonical roadmap planning now lives in `Plans/`; the instruction file only routes roadmap tasks into this parent plan and the relevant chapter docs.
- Chapters 1 and 2 are complete, Chapter 3 is active, and Chapters 4 through 9 remain queued in order.
- Open Chapter 3 for live gait-state work and Chapter 9 for validation or telemetry workflow context; use `LOCOMOTION_BASELINES.md` only when comparing regressions or known reds.

## Verified Artifacts
- `Plans/unified-locomotion-roadmap/03-leg-states.md`: active work package and current Chapter 3 blocker.
- `LOCOMOTION_BASELINES.md`: baseline snapshots and known pre-existing locomotion reds.

## Child docs
- [x] Chapter 1: Define The Single Voice (`Plans/unified-locomotion-roadmap/01-single-voice.md`)
- [x] Chapter 2: Build A Better World Model (`Plans/unified-locomotion-roadmap/02-world-model.md`)
- [ ] Chapter 3: Replace Cycle-Only Gait With Leg States (`Plans/unified-locomotion-roadmap/03-leg-states.md`)
- [ ] Chapter 4: Add Step Planning And Foot Placement (`Plans/unified-locomotion-roadmap/04-step-planning.md`)
- [ ] Chapter 5: Recast Balance As Body Support (`Plans/unified-locomotion-roadmap/05-body-support.md`)
- [ ] Chapter 6: Turn Recovery, Stumbles, And Catch Steps (`Plans/unified-locomotion-roadmap/06-recovery-and-catch-steps.md`)
- [ ] Chapter 7: Terrain And Contact Robustness (`Plans/unified-locomotion-roadmap/07-terrain-and-contact-robustness.md`)
- [ ] Chapter 8: Expressive Motion And Feel (`Plans/unified-locomotion-roadmap/08-expressive-motion-and-feel.md`)
- [ ] Chapter 9: Validation, Debugging, And Tuning Infrastructure (`Plans/unified-locomotion-roadmap/09-validation-debugging-and-tuning.md`)

## Work packages
1. [x] Clean Chapter 1 authority boundaries and lock the pre-director baseline.
2. [x] Promote a stable locomotion world model through Chapter 2.
3. [ ] Finish Chapter 3 gait-state migration and clear the remaining mixed-slice regression.
4. [ ] Execute Chapters 4 through 8 in order once the Chapter 3 bridge is stable enough to build on.
5. [ ] Keep Chapter 9 validation, telemetry, and baseline artifacts current throughout the roadmap.

## Target Runtime Authority Model

Input -> LocomotionDirector -> LegStateMachine + StepPlanner -> Actuators -> Safety Layer

1. Input says what the player wants.
2. LocomotionDirector decides what movement solution to run.
3. LegStateMachine and StepPlanner decide what each leg should do.
4. Actuators execute that decision.
   - LegAnimator executes leg targets.
   - BalanceController stabilizes body support.
   - ArmAnimator adds supportive counter motion.
5. Safety layer steps in only when the plan is failing.
   - LocomotionCollapseDetector
   - CharacterState fall/get-up transitions

## Shared work rules

1. Start from current behavior and record a baseline before refactoring.
2. Make authority changes in thin slices, not one large rewrite.
3. Keep feature-scoped verification green after each slice.
4. Prefer outcome-based PlayMode verification for locomotion behavior.
5. Run Unity tests sequentially through [Tools/Run-UnityTests.ps1](../Tools/Run-UnityTests.ps1).

## Recommended execution order

1. Finish Chapter 3.
2. Chapter 4 for real step planning.
3. Chapters 5 and 6 for support and recovery integration.
4. Chapter 7 terrain hardening.
5. Chapter 8 expression pass.
6. Chapter 9 continues throughout and scales with each chapter.

## Progress notes
- 2026-03-12: Migrated the roadmap's working chapters out of `.github/instructions/unified-locomotion-roadmap/` into `Plans/unified-locomotion-roadmap/` so the instruction layer stays a thin router and the execution record lives under the canonical plan tree.