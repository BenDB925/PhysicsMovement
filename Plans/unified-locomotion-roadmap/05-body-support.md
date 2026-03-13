# Chapter 5: Recast Balance As Body Support

Back to parent plan: [Unified Locomotion Roadmap](../unified-locomotion-roadmap.plan.md)

## Quick Load

- Chapter 5 is complete. All five work packages (C5.1-C5.5) have passed their verification gates.
- BalanceController now acts as a pure executor of BodySupportCommand from LocomotionDirector rather than introducing independent locomotion heuristics.
- The verification surface is `BalanceControllerTests`, `BalanceControllerTurningTests`, `BalanceControllerIntegrationTests`, and `HardSnapRecoveryTests`.
- The Chapter 5 completion gate was EditMode 58/58, PlayMode 42/42.

## Read More When

- Continue into the work packages when touching support command fields, height maintenance, COM stabilization, or override precedence.
- Continue into the verification gate when changing BalanceController runtime behavior.

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
- Assets/Scripts/Character/Locomotion/LocomotionDirector.cs
- Assets/Scripts/Character/Locomotion/BodySupportCommand.cs

## Work packages

1. C5.1 Support command interface:
   - BalanceController accepts support targets from director (upright target, yaw intent, lean envelope, stabilization strength).
   - 2026-03-13: Complete. Added `HeightMaintenanceScale` to `BodySupportCommand`. Director now publishes height maintenance scale based on hips-height deficit. Verification: EditMode 56/56, PlayMode 42/42. Commit `6e21e34`.
2. C5.2 Remove locomotion ownership from balance:
   - Eliminate independent gait and turn heuristics that conflict with director intent.
   - 2026-03-13: Complete. Director now owns height maintenance scale computation via `ComputeHeightMaintenanceScale()`. Startup stand assist is gated by `commandHeightScale > 0f` and modulated by it so the director controls height recovery authority. `StandingHipsHeight` exposed as public property. Verification: EditMode 57/57, PlayMode 42/42. Commit `0d17aa4`.
3. C5.3 COM support behavior:
   - Stabilize torso and hips relative to active support plan and planned step.
   - 2026-03-13: Complete. Director now populates `DesiredLeanDegrees` from turn severity × `_maxTurnLeanDegrees`. BalanceController shifts COM target toward facing direction by lean fraction × `_maxComLeanOffset` (0.12m default). Feet-midpoint design assumption documented. Verification: EditMode 58/58, PlayMode 42/42. Commit `c5b85c4`.
4. C5.4 Simplify override layering:
   - Keep only one clear precedence order for support commands and emergency overrides.
   - 2026-03-13: Complete. Added DESIGN comment at FixedUpdate top documenting the Observation → Command → Execution precedence chain and the role of each override source. Marked `SetFacingDirection()` as `[Obsolete]` (kept for test compatibility). Documented the yaw dual-gate rationale (IsFallen angle-based + CharacterState FSM). Suppressed CS0618 in 3 test files that call the legacy method. Verification: EditMode 58/58, PlayMode 42/42. Commit `fc531e0`.
5. C5.5 Tuning cleanup:
   - Group and rename serialized fields by role (posture, yaw, damping, recovery assist) to reduce tuning confusion.
   - 2026-03-13: Complete. Added `[Header]` attributes grouping fields into: Upright PD Gains, Yaw Control, Fallen Thresholds, Snap Recovery (existing), Startup Stand Assist, LegAnimator Cooperation, Debug, COM-over-Feet Stabilization (existing), Height Maintenance (existing). Fixed inconsistent indentation on startup assist fields. Verification: EditMode 58/58, PlayMode 42/42. Commit `92dd123`.

## Verification gate

- Assets/Tests/PlayMode/Character/BalanceControllerTests.cs
- Assets/Tests/PlayMode/Character/BalanceControllerTurningTests.cs
- Assets/Tests/PlayMode/Character/BalanceControllerIntegrationTests.cs
- Assets/Tests/PlayMode/Character/HardSnapRecoveryTests.cs

## Chapter 5 Completion Verification (2026-03-13)

- EditMode: 58/58 passed.
- PlayMode gate: 42/42 passed (BalanceControllerTests + BalanceControllerTurningTests + BalanceControllerIntegrationTests + HardSnapRecoveryTests).
- No new regressions introduced.

## Exit criteria

- BalanceController no longer introduces independent locomotion strategy.
- Turning and recovery remain stable under regression tests.