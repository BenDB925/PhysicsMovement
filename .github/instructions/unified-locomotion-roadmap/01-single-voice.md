# Chapter 1: Define The Single Voice

Back to routing: [Unified Locomotion Roadmap](../unified-locomotion-roadmap.instructions.md)

## Read this chapter when

- introducing LocomotionDirector or locomotion command contracts
- moving locomotion intent ownership out of PlayerMovement, BalanceController, CharacterState, or LegAnimator
- capturing parity baselines before authority refactors

## Dependencies

- None. This chapter establishes the authority model that later chapters build on.

## Objective

Create one locomotion decision owner without changing final feel yet.

## Primary touchpoints

- Assets/Scripts/Character/PlayerMovement.cs
- Assets/Scripts/Character/BalanceController.cs
- Assets/Scripts/Character/CharacterState.cs
- Assets/Scripts/Character/LegAnimator.cs
- Assets/Scripts/Character/LocomotionCollapseDetector.cs
- Assets/Scripts/Character/Locomotion/

## Related artifacts

- [LOCOMOTION_BASELINES.md](../../../LOCOMOTION_BASELINES.md)
- `Logs/test_playmode_20260312_140637.log`
- `Logs/test_editmode_20260312_140805.log`

## Work packages

1. C1.1 Baseline lock:
   - Capture current outcome metrics from GaitOutcomeTests, HardSnapRecoveryTests, SpinRecoveryTests, MovementQualityTests.
   - Save the baseline summary in LOCOMOTION_BASELINES.md and note the snapshot location here.
   - Progress note (2026-03-12): first focused baseline snapshot saved in LOCOMOTION_BASELINES.md using TestResults/PlayMode.xml and Logs/test_playmode_20260312_120734.log.
2. C1.2 Introduce locomotion contracts:
   - Add lightweight data contracts for DesiredInput, LocomotionObservation, BodySupportCommand, and per-leg command output.
   - Keep contracts internal to Character assembly first.
   - Progress note (2026-03-12): internal contracts now live under Assets/Scripts/Character/Locomotion/ with reflection-backed EditMode coverage in Assets/Tests/EditMode/Character/LocomotionContractsTests.cs. Focused verification passed via TestResults/EditMode.xml and Logs/test_editmode_20260312_125019.log.
3. C1.3 Add LocomotionDirector skeleton:
   - New runtime coordinator on Hips that reads desired input plus observations and emits commands.
   - Initially run in pass-through mode so behavior stays unchanged.
   - Progress note (2026-03-12): `LocomotionDirector` now lives on the PlayerRagdoll Hips in pass-through mode, snapshots `DesiredInput` from `PlayerMovement`, aggregates current locomotion observations, and mirrors legacy body-support plus leg-cycle commands without taking execution ownership yet. Focused verification passed via `TestResults/EditMode.xml` + `Logs/test_editmode_20260312_131145.log` and `TestResults/PlayMode.xml` + `Logs/test_playmode_20260312_131045.log`.
4. C1.4 Rewire ownership boundaries:
   - PlayerMovement stops deciding gait intent; it only reports desired movement and jump request.
   - LegAnimator consumes explicit leg intent instead of deriving all decisions from smoothed input alone.
   - BalanceController consumes support targets from director instead of introducing independent locomotion heuristics.
   - Progress note (2026-03-12): `PlayerMovement` now reports desired locomotion and jump intent without directly retargeting `BalanceController`; `LocomotionDirector` now owns support recovery detection and publishes `BodySupportCommand` plus explicit leg command frames into `BalanceController` and `LegAnimator`; the three Step 4 boundary regressions are green.
5. C1.5 Safety role cleanup:
   - LocomotionCollapseDetector remains watchdog only.
   - CharacterState remains the authority for high-level state labels, but no longer produces gait strategy.
6. C1.6 Regression gate:
   - Keep behavior parity against the recorded baseline snapshot and the focused Chapter 1 verification slices before enabling new logic paths.
   - Progress note (2026-03-12): focused EditMode seam coverage passed `6/6`, and the targeted broader Chapter 1 PlayMode slice passed `107/110` with `3` ignored and `0` failures after the explicit command-frame timing path was stabilized. Fresh artifacts: `TestResults/EditMode.xml`, `TestResults/PlayMode.xml`, `Logs/test_editmode_20260312_140805.log`, and `Logs/test_playmode_20260312_140637.log`.

## Verification gate

- Assets/Tests/EditMode/Character/LocomotionContractsTests.cs
- Assets/Tests/EditMode/Character/LocomotionDirectorEditModeTests.cs
- Assets/Tests/PlayMode/Character/LocomotionDirectorTests.cs
- Assets/Tests/PlayMode/Character/PlayerMovementTests.cs
- Assets/Tests/PlayMode/Character/CharacterStateTests.cs
- Assets/Tests/PlayMode/Character/LegAnimatorTests.cs
- Assets/Tests/PlayMode/Character/BalanceControllerTests.cs
- Assets/Tests/PlayMode/Character/BalanceControllerTurningTests.cs
- Assets/Tests/PlayMode/Character/FullStackSanityTests.cs

## Exit criteria

- One script (LocomotionDirector) can be named as locomotion intent authority.
- Focused Chapter 1 verification slices remain green, and any wider baseline gaps are either unchanged from [LOCOMOTION_BASELINES.md](../../../LOCOMOTION_BASELINES.md) or explicitly called out there.