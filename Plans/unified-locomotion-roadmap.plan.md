# Unified Locomotion Roadmap

## Status
- State: In Progress
- Acceptance target: Finish the locomotion authority migration through validation, terrain robustness, and expression without regressing the focused chapter gates or baseline artifacts.
- Current next step: Begin Chapter 5 (Recast Balance As Body Support) now that Chapter 4 is complete.
- Active blockers: None.

## Quick Resume
- Canonical roadmap planning now lives in `Plans/`; the instruction file only routes roadmap tasks into this parent plan and the relevant chapter docs.
- Chapters 1, 2, 3, and 4 are complete. Chapter 5 is next.
- Chapter 4 delivered: `StepTarget` contract, `StepPlanner` with turn/braking/catch-step differentiation, and visual debug draw.
- Next: begin Chapter 5 (Recast Balance As Body Support).

## Verified Artifacts
- `Plans/unified-locomotion-roadmap/04-step-planning.md`: active Chapter 4 work package with C4.1 and C4.2 completion notes.
- `Assets/Scripts/Character/Locomotion/StepPlanner.cs`: Chapter 4 step planner computing world-space step targets.
- `Assets/Scripts/Character/Locomotion/StepTarget.cs`: Chapter 4 step target contract.
- `Assets/Tests/PlayMode/Utilities/PlayModeSceneIsolation.cs`: PlayMode-safe scene isolation helper used to stop mixed-slice scene bleed in prefab-backed locomotion fixtures.

## Child docs
- [x] Chapter 1: Define The Single Voice (`Plans/unified-locomotion-roadmap/01-single-voice.md`)
- [x] Chapter 2: Build A Better World Model (`Plans/unified-locomotion-roadmap/02-world-model.md`)
- [x] Chapter 3: Replace Cycle-Only Gait With Leg States (`Plans/unified-locomotion-roadmap/03-leg-states.md`)
- [x] Chapter 4: Add Step Planning And Foot Placement (`Plans/unified-locomotion-roadmap/04-step-planning.md`)
- [ ] Chapter 5: Recast Balance As Body Support (`Plans/unified-locomotion-roadmap/05-body-support.md`)
- [ ] Chapter 6: Turn Recovery, Stumbles, And Catch Steps (`Plans/unified-locomotion-roadmap/06-recovery-and-catch-steps.md`)
- [ ] Chapter 7: Terrain And Contact Robustness (`Plans/unified-locomotion-roadmap/07-terrain-and-contact-robustness.md`)
- [ ] Chapter 8: Expressive Motion And Feel (`Plans/unified-locomotion-roadmap/08-expressive-motion-and-feel.md`)
- [ ] Chapter 9: Validation, Debugging, And Tuning Infrastructure (`Plans/unified-locomotion-roadmap/09-validation-debugging-and-tuning.md`)

## Work packages
1. [x] Clean Chapter 1 authority boundaries and lock the pre-director baseline.
2. [x] Promote a stable locomotion world model through Chapter 2.
3. [x] Finish Chapter 3 gait-state migration with C3.1-C3.5.
4. [ ] Execute Chapters 4 through 8 in order now that the Chapter 3 bridge is stable.
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
- 2026-03-12: Completed Chapter 3 C3.3 by giving `LegAnimator` dedicated `RecoveryStep` timing and stronger `CatchStep` execution, clearing the focused `LegAnimatorTests` slice (`58 passed, 3 ignored, 0 failed`) and the broader mixed Chapter 3 gate (`77 passed, 3 ignored, 0 failed`). A separate `MovementQualityTests` rerun stayed at the same two known pre-existing reds.
- 2026-03-12: Started Chapter 3 C3.4 by splitting `LegAnimator` pass-through planning into per-leg turn/recovery reasons so sharp turns now give the outside leg `TurnSupport`, the inside leg an override cadence, and catch-step ownership to one selected recovery leg instead of mirroring stumble recovery across both legs. Focused verification passed via `LocomotionDirectorTests` (`11/11`), EditMode seam coverage (`19/19`), isolated `LegAnimatorTests` (`58 passed, 3 ignored, 0 failed`), and pairwise PlayMode combinations (`69/72`, `62/65`, and `63/66` all green aside from the expected ignores). The remaining blocker is the larger four-fixture mixed slice, which still shows order-sensitive `LowerLeg_WhenWalking_FeetAlternate` and `SharpTurn90_NoFallenTransition` reds.
- 2026-03-13: Closed the C3.4 mixed-slice blocker. Prefab-backed PlayMode fixtures now switch to a fresh runtime scene via `PlayModeSceneIsolation.ResetToEmptyScene()` before instantiating their rigs, which stopped scene bleed from earlier scene-loading tests. `LegAnimatorTests.LowerLeg_WhenWalking_FeetAlternate()` now measures hips-relative lower-leg forward lead and separated peak timing instead of strict world-position crossing. The full mixed Chapter 3 PlayMode slice `LocomotionDirectorTests` + `LegAnimatorTests` + `GaitOutcomeTests` + `StumbleStutterRegressionTests` passed `78 passed, 3 ignored, 0 failed`, so the next open Chapter 3 work is C3.5 failure handling.
- 2026-03-13: Completed C3.5 failure handling. Root cause: during walking `IsComOutsideSupport=true` craters confidence to 0, latching fallback blend; during the turn `PlantedConfidence=0.000` meant the planted-floor couldn't unlatch it. Fix: guaranteed a hard confidence floor at exit threshold during recognized sharp turns (`hasTurnAsymmetry`). Chapter 3 complete: PlayMode `82 total, 79 passed, 3 ignored, 0 failed`; EditMode `32/32`.
- 2026-03-13: Completed C4.1 step target contract. Added `StepTarget` readonly struct with `LandingPosition`, `DesiredTiming`, `WidthBias`, `BrakingBias`, `Confidence`, `IsValid`. Threaded through `LegCommandOutput`; all 10 `LegAnimator` call sites pass `StepTarget.Invalid`. 4 new EditMode contract tests. Verification: EditMode 20/20, PlayMode 82 (79 passed, 3 ignored, 0 failed).
- 2026-03-13: Completed C4.2 basic step planner. Added internal `StepPlanner` class under `Assets/Scripts/Character/Locomotion/`. Wired into `LegAnimator.BuildPassThroughCommands()` moving path so swing-like legs now get computed step targets with stride, lateral offset, drift compensation, turn width bias, braking bias, timing, and confidence. Recovery/idle/falling paths still use `StepTarget.Invalid`. 7 new EditMode tests. Verification: EditMode 43/43, PlayMode 82 (79 passed, 3 ignored, 0 failed). Commit `0866f63`.
- 2026-03-13: Completed C4.3 turn-specific step planning. `StepPlanner.ComputeSwingTarget` now accepts `LegStateTransitionReason` to differentiate outside (TurnSupport: +20% stride, +25% timing) and inside (SpeedUp: -15% stride) turn legs, both scaled by `TurnSeverity`. 4 new EditMode tests. Verification: EditMode 47/47, PlayMode 82 (79 passed, 3 ignored, 0 failed). Commit `9fb37c4`.
- 2026-03-13: Completed C4.4 braking stride/timing shortening. Added `BrakingStrideScale=0.35` and `BrakingTimingScale=0.25` constants. `ApplyBrakingStrideAdjustment` and `ApplyBrakingTimingAdjustment` shorten stride and timing proportional to residual planar speed when `Braking` active. 4 new EditMode tests. Verification: EditMode 51/51, PlayMode gate green (pre-existing intra-fixture reds only). Commit `134cd10`.
- 2026-03-13: Completed C4.5 catch-step planning. Added `CatchStepStrideScale=0.30`, `CatchStepWidenScale=0.12`, `CatchStepTimingScale=0.20` constants. `ApplyCatchStepStrideAdjustment`, `ApplyCatchStepLateralAdjustment`, and `ApplyCatchStepTimingAdjustment` extend stride, widen lateral, and shorten timing when `StumbleRecovery` active, all scaled by support urgency (1 - SupportQuality). 4 new EditMode tests. Verification: EditMode 55/55, PlayMode 70 (67 passed, 3 ignored, 0 failed). Commit `62534dc`.
- 2026-03-13: Completed C4.6 visual debug draw. Added `_debugStepTargetDraw` toggle to `LocomotionDirector` that draws per-leg step target markers (cross + confidence circle + timing pillar) in Scene view. Left=blue, right=green-yellow. No new tests (debug-draw-only). Verification: EditMode 55/55, PlayMode 70 (67 passed, 3 ignored, 0 failed). Commit `b81e8cb`. Chapter 4 is now complete.