# Chapter 5: Recast Balance As Body Support

Back to parent plan: [Unified Locomotion Roadmap](../unified-locomotion-roadmap.plan.md)

## Read this chapter when

- turning BalanceController into an executor of support commands rather than a locomotion strategist
- cleaning up support override precedence and COM support behavior
- grouping or renaming balance tuning fields by support role

## Dependencies

- Read Chapter 1 first because this chapter changes ownership boundaries.
- Read Chapter 4 too if support targets depend on planned steps.

## Objective

BalanceController supports the locomotion plan instead of competing with it.

## Primary touchpoints

- Assets/Scripts/Character/BalanceController.cs
- Assets/Scripts/Character/PlayerMovement.cs
- Assets/Scripts/Character/LocomotionCollapseDetector.cs

## Work packages

1. C5.1 Support command interface:
   - BalanceController accepts support targets from director (upright target, yaw intent, lean envelope, stabilization strength).
2. C5.2 Remove locomotion ownership from balance:
   - Eliminate independent gait and turn heuristics that conflict with director intent.
3. C5.3 COM support behavior:
   - Stabilize torso and hips relative to active support plan and planned step.
4. C5.4 Simplify override layering:
   - Keep only one clear precedence order for support commands and emergency overrides.
5. C5.5 Tuning cleanup:
   - Group and rename serialized fields by role (posture, yaw, damping, recovery assist) to reduce tuning confusion.

## Verification gate

- Assets/Tests/PlayMode/Character/BalanceControllerTests.cs
- Assets/Tests/PlayMode/Character/BalanceControllerTurningTests.cs
- Assets/Tests/PlayMode/Character/BalanceControllerIntegrationTests.cs
- Assets/Tests/PlayMode/Character/HardSnapRecoveryTests.cs

## Exit criteria

- BalanceController no longer introduces independent locomotion strategy.
- Turning and recovery remain stable under regression tests.