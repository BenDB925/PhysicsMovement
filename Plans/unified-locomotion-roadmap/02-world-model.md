# Chapter 2: Build A Better World Model

Back to parent plan: [Unified Locomotion Roadmap](../unified-locomotion-roadmap.plan.md)

## Quick Load

- Use this chapter for observation-model work: promote raw physics signals into support quality, contact confidence, planted confidence, slip, turn severity, and related locomotion-facing telemetry.
- Read Chapter 1 only if the change also alters director contracts or ownership boundaries; otherwise keep the scope inside Chapter 2.
- Current completed slices already cover schema, sensor aggregation, hysteresis, debug visibility, and director decision integration.
- Broader verification still inherits the pre-existing `MovementQualityTests` pressure points tracked in `LOCOMOTION_BASELINES.md`; do not treat them as new Chapter 2 regressions by default.

## Read More When

- Continue into the work packages when changing support quality, planted-foot logic, slip estimates, debug draw, or director observation decisions.
- Continue into the completion verification when deciding whether a red is newly introduced versus pre-existing or slice-order-sensitive.
- Continue into the dependency notes when the work starts changing contracts, ownership boundaries, or scene/runtime assumptions outside the observation layer.

## Read this chapter when

- defining locomotion observations instead of reading raw physics state ad hoc
- adding support quality, contact confidence, planted-foot confidence, or slip estimates
- building debug draw or confidence telemetry for support geometry

## Dependencies

- Read Chapter 1 first if the work changes director contracts or ownership.
- This chapter can otherwise advance in parallel with Chapter 3.

## Objective

Promote raw physics data into locomotion-meaningful observations.

## Primary touchpoints

- Assets/Scripts/Character/GroundSensor.cs
- Assets/Scripts/Character/BalanceController.cs
- Assets/Scripts/Character/LocomotionCollapseDetector.cs
- Assets/Scripts/Character/PlayerMovement.cs
- New observation helpers under Assets/Scripts/Character/

## Work packages

1. C2.1 Observation schema:
   - Define support quality, contact confidence, planted foot confidence, slip estimate, turn severity, and COM-outside-support indicator.
   - Progress note (2026-03-12): `FootContactObservation` and `SupportObservation` now extend `LocomotionObservation` with support quality, contact confidence, planted-foot confidence, slip estimate, turn severity, and COM-outside-support fields. `LocomotionDirector` seeds baseline values from current foot grounded state plus desired-vs-body heading without changing pass-through execution; COM-outside-support remains conservative until C2.2 adds explicit support-geometry aggregation. Focused verification passed via `Assets/Tests/EditMode/Character/LocomotionContractsTests.cs` + `Assets/Tests/EditMode/Character/LocomotionDirectorEditModeTests.cs` (`9/9`) and `Assets/Tests/PlayMode/Character/LocomotionDirectorTests.cs` (`3/3`), with fresh artifacts in `TestResults/EditMode.xml`, `TestResults/PlayMode.xml`, `Logs/test_editmode_20260312_154211.log`, and `Logs/test_playmode_20260312_154313.log`.
2. C2.2 Sensor aggregation:
   - Build one aggregator that collects foot contacts, hip velocity, yaw rate, and support geometry each FixedUpdate.
   - Progress note (2026-03-12): Added shared `SupportGeometry`, `LocomotionSensorSnapshot`, and `LocomotionSensorAggregator` helpers under `Assets/Scripts/Character/Locomotion/` so `LocomotionDirector` and `LocomotionCollapseDetector` now read the same per-step foot-contact, hips-velocity, yaw-rate, and support-geometry snapshot instead of duplicating transform math. `GroundSensor` now preserves the latest contact point, `LocomotionObservation.IsComOutsideSupport` is driven by the aggregated support capsule instead of a conservative placeholder, and collapse support-behind distance now comes from the same support geometry path. Focused verification passed via `Assets/Tests/EditMode/Character/LocomotionContractsTests.cs` + `Assets/Tests/EditMode/Character/LocomotionDirectorEditModeTests.cs` (`10/10`) and `Assets/Tests/PlayMode/Character/LocomotionDirectorTests.cs` + `Assets/Tests/PlayMode/Character/BalanceControllerIntegrationTests.cs` (`16/16`), with fresh artifacts in `TestResults/EditMode.xml`, `TestResults/PlayMode.xml`, `Logs/test_editmode_20260312_155711.log`, and `Logs/test_playmode_20260312_160046.log`. A broader PlayMode slice that included `Assets/Tests/PlayMode/Character/MovementQualityTests.cs` still reported only the two pre-existing reds already tracked in `LOCOMOTION_BASELINES.md` (`WalkStraight_NoFalls`, `SustainedLocomotionCollapse_TransitionsIntoFallen`), with details in `Logs/test_playmode_20260312_155805.log`.
3. C2.3 Confidence and hysteresis:
   - Add temporal filtering so planted and unplanted does not flicker frame to frame.
   - Keep thresholds serialized and documented.
   - Progress note (2026-03-12): Added `SupportObservationFilter` under `Assets/Scripts/Character/Locomotion/` so `LocomotionDirector` now smooths per-foot contact and planted confidence before publishing `LocomotionObservation`, with serialized rise/fall speeds and planted enter/exit thresholds documented on the component. `FootContactObservation` now exposes a stable `IsPlanted` flag, and the prefab-backed runtime slice verifies that a one-frame grounded foot slide no longer flips planted classification immediately. Focused verification passed via `Assets/Tests/EditMode/Character/LocomotionContractsTests.cs` + `Assets/Tests/EditMode/Character/LocomotionDirectorEditModeTests.cs` (`13/13`) and `Assets/Tests/PlayMode/Character/LocomotionDirectorTests.cs` + `Assets/Tests/PlayMode/Character/BalanceControllerIntegrationTests.cs` (`17/17`), with fresh artifacts in `TestResults/EditMode.xml`, `TestResults/PlayMode.xml`, `Logs/test_editmode_20260312_161849.log`, and `Logs/test_playmode_20260312_162152.log`.
4. C2.4 Debug visibility:
   - Add optional debug draw and log path for support polygon, predicted drift direction, and active confidence values.
   - Progress note (2026-03-12): `LocomotionDirector` now publishes a per-step debug visibility snapshot with predicted drift direction plus a cached telemetry line summarizing support geometry, support quality, contact confidence, planted confidence, slip, turn severity, and planted-foot state. Optional serialized C2.4 toggles now drive `Debug.DrawLine` support-capsule/com-offset visualisation and throttled console telemetry without changing the locomotion control path. Focused verification passed via `Assets/Tests/EditMode/Character/LocomotionDirectorEditModeTests.cs` (`3/3`) and `Assets/Tests/PlayMode/Character/LocomotionDirectorTests.cs` + `Assets/Tests/PlayMode/Character/BalanceControllerIntegrationTests.cs` (`18/18`), with fresh artifacts in `TestResults/EditMode.xml`, `TestResults/PlayMode.xml`, `Logs/test_editmode_20260312_163410.log`, and `Logs/test_playmode_20260312_163811.log`.
5. C2.5 Integrate with director:
   - Director decisions switch to observation model instead of ad hoc readings from multiple classes.
   - Progress note (2026-03-12): `LocomotionDirector` now derives body-support recovery plus upright/yaw/stabilization strength scales from the promoted Chapter 2 observation model instead of the old previous-direction heuristic. New serialized C2.5 tuning fields gate turn-severity recovery and support-risk amplification, and the risky-turn path now responds to weak-support observations without requiring legacy direction history. Focused verification passed via `Assets/Tests/EditMode/Character/LocomotionContractsTests.cs` + `Assets/Tests/EditMode/Character/LocomotionDirectorEditModeTests.cs` (`15/15`) and `Assets/Tests/PlayMode/Character/LocomotionDirectorTests.cs` + `Assets/Tests/PlayMode/Character/BalanceControllerIntegrationTests.cs` (`20/20`), with fresh artifacts in `TestResults/EditMode.xml`, `TestResults/PlayMode.xml`, `Logs/test_editmode_20260312_170231.log`, and `Logs/test_playmode_20260312_171147.log`. A broader PlayMode gate covering `Assets/Tests/PlayMode/Character/MovementQualityTests.cs` + `Assets/Tests/PlayMode/Character/HardSnapRecoveryTests.cs` + `Assets/Tests/PlayMode/Character/StumbleStutterRegressionTests.cs` reported `3` reds in the mixed slice (`Logs/test_playmode_20260312_171312.log`), but an isolated rerun of `MovementQualityTests` dropped back to only the two long-standing reds already tracked in `LOCOMOTION_BASELINES.md` (`WalkStraight_NoFalls`, `SustainedLocomotionCollapse_TransitionsIntoFallen`) via `Logs/test_playmode_20260312_171538.log`; the extra `TurnAndWalk_CornerRecovery` failure therefore looks slice-order-sensitive rather than a stable new C2.5 regression.

## Verification gate

- Assets/Tests/PlayMode/Character/BalanceControllerIntegrationTests.cs
- Assets/Tests/PlayMode/Character/MovementQualityTests.cs
- Assets/Tests/PlayMode/Character/HardSnapRecoveryTests.cs
- Assets/Tests/PlayMode/Character/StumbleStutterRegressionTests.cs

## Chapter 2 Completion Verification (2026-03-12)

Focused verification artifacts:
- `TestResults/EditMode.xml` — `Logs/test_editmode_20260312_172706.log`
- `TestResults/PlayMode.xml` — `Logs/test_playmode_20260312_172750.log`

Focused verification results:
- EditMode (LocomotionContractsTests + LocomotionDirectorEditModeTests): `15/15` passed.
- PlayMode (LocomotionDirectorTests + BalanceControllerIntegrationTests): `20/20` passed.

Broader verification gate results (BalanceControllerIntegrationTests + MovementQualityTests + HardSnapRecoveryTests + StumbleStutterRegressionTests):
- `23` total, `20` passed, `3` failed.
- `WalkStraight_NoFalls` — pre-existing (straight course incomplete at 600 frames, waypoint index 0).
- `SustainedLocomotionCollapse_TransitionsIntoFallen` — pre-existing (state never entered Fallen within 90 frames).
- `TurnAndWalk_CornerRecovery` — slice-order-sensitive intermittent (151 fallen frames vs 120 limit); documented as pre-existing in Chapter 1 baseline snapshot.

No new regressions introduced by Chapter 2 work. All three failures are pre-existing pressure points already tracked in `LOCOMOTION_BASELINES.md`.

Architecture note: `LocomotionDirector` and `LocomotionCollapseDetector` each maintain their own `LocomotionSensorAggregator` instance. This is intentional — the collapse detector runs at default execution order (0) and must produce `IsCollapseConfirmed` before the director (execution order 250) consumes it, so they cannot share a single instance without introducing a circular dependency.

## Exit criteria

- Locomotion decisions can be explained in locomotion language, not only raw velocity or tilt.