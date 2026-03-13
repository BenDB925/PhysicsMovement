# Chapter 4: Add Step Planning And Foot Placement

Back to parent plan: [Unified Locomotion Roadmap](../unified-locomotion-roadmap.plan.md)

## Quick Load

- C4.1, C4.2, and C4.3 are complete: `StepTarget` contract, `StepPlanner` wired into `LegAnimator` moving path, and turn-specific stride/timing differentiation for inside/outside legs.
- Next active slice is C4.4 (Braking and reversal steps): add explicit braking step logic for stop and reversal entries.
- The verification surface is the Chapter 3 PlayMode gate plus StepPlanner EditMode tests.

## Read More When

- Continue into the work packages when adding planner logic, turn-specific widening, braking steps, or debug draw.
- Continue into the verification gate when running regression checks after step-planning changes.

## Read this chapter when

- introducing explicit step target data or foothold planning
- making turns, braking, reversals, or catch steps choose purposeful landing locations
- adding visual debug for planned and accepted footholds

## Dependencies

- Read Chapters 2 and 3 first. Step planning depends on both the observation model and explicit leg roles.

## Objective

Decide where each step should land based on movement goals and support needs.

## Primary touchpoints

- Assets/Scripts/Character/LegAnimator.cs
- Assets/Scripts/Character/BalanceController.cs
- Assets/Scripts/Character/Locomotion/StepTarget.cs
- Assets/Scripts/Character/Locomotion/StepPlanner.cs (C4.2+)
- Assets/Scripts/Character/Locomotion/LegCommandOutput.cs

## Work packages

1. C4.1 Step target contract:
   - Add world-space step target data: landing position, desired timing, width bias, braking bias, and confidence.
   - 2026-03-13: Complete. Added `StepTarget` readonly struct under `Assets/Scripts/Character/Locomotion/` with `LandingPosition`, `DesiredTiming`, `WidthBias`, `BrakingBias`, `Confidence`, and `IsValid`. `LegCommandOutput` constructor now takes `StepTarget` instead of `Vector3`; legacy `FootTarget` property preserved. All 10 `LegAnimator` call sites updated to `StepTarget.Invalid`. 4 new EditMode contract tests, reflection-based PlayMode test helper updated. Verification: EditMode 20/20, PlayMode 82 (79 passed, 3 ignored, 0 failed).
2. C4.2 Basic planner:
   - Compute target from desired speed, heading, current COM drift, and turn severity.
   - 2026-03-13: Complete. Added internal `StepPlanner` class under `Assets/Scripts/Character/Locomotion/`. Stateless sealed class with one public entry point `ComputeSwingTarget()` that computes stride offset (speed-scaled), lateral offset (left/right), drift compensation, desired timing, width bias, braking bias, and confidence. Wired into `LegAnimator.BuildPassThroughCommands()` moving path; recovery/idle/falling paths still use `StepTarget.Invalid`. 7 new reflection-backed EditMode tests. Bisect proof confirmed pre-existing `LocomotionDirectorTests` intra-fixture order sensitivity is unrelated to the planner. Verification: EditMode 43/43, PlayMode 82 (79 passed, 3 ignored, 0 failed). Commit `0866f63`.
3. C4.3 Turn-specific planning:
   - Differentiate inside and outside leg step length, width, and timing.
   - 2026-03-13: Complete. `StepPlanner.ComputeSwingTarget` now accepts `LegStateTransitionReason` to differentiate outside (TurnSupport: +20% stride, +25% timing) and inside (SpeedUp: -15% stride) turn legs, both scaled by `TurnSeverity`. Inside shortening gated at severity > 0.1 to avoid noise. `LegAnimator` call sites pass per-leg transition reasons. 4 new EditMode tests. Verification: EditMode 47/47, PlayMode 82 (79 passed, 3 ignored, 0 failed). Commit `9fb37c4`.
4. C4.4 Braking and reversal steps:
   - Add explicit braking step logic for stop and reversal entries.
5. C4.5 Catch-step planning:
   - When support quality drops, prioritize wider or farther catch-step instead of normal cadence.
6. C4.6 Visual debug:
   - Draw planned footholds and accepted footholds in scene debug mode.

## Verification gate

- Assets/Tests/EditMode/Character/LocomotionContractsTests.cs
- Assets/Tests/PlayMode/Character/GaitOutcomeTests.cs
- Assets/Tests/PlayMode/Character/LegAnimatorTests.cs
- Assets/Tests/PlayMode/Character/LocomotionDirectorTests.cs
- Assets/Tests/PlayMode/Character/MovementQualityTests.cs

## Exit criteria

- Step placement is purposeful and scenario-dependent, not purely decorative swing.