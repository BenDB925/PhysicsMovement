# Chapter 2: Build A Better World Model

Back to routing: [Unified Locomotion Roadmap](../unified-locomotion-roadmap.instructions.md)

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
2. C2.2 Sensor aggregation:
   - Build one aggregator that collects foot contacts, hip velocity, yaw rate, and support geometry each FixedUpdate.
3. C2.3 Confidence and hysteresis:
   - Add temporal filtering so planted and unplanted does not flicker frame to frame.
   - Keep thresholds serialized and documented.
4. C2.4 Debug visibility:
   - Add optional debug draw and log path for support polygon, predicted drift direction, and active confidence values.
5. C2.5 Integrate with director:
   - Director decisions switch to observation model instead of ad hoc readings from multiple classes.

## Verification gate

- Assets/Tests/PlayMode/Character/BalanceControllerIntegrationTests.cs
- Assets/Tests/PlayMode/Character/MovementQualityTests.cs
- Assets/Tests/PlayMode/Character/HardSnapRecoveryTests.cs
- Assets/Tests/PlayMode/Character/StumbleStutterRegressionTests.cs

## Exit criteria

- Locomotion decisions can be explained in locomotion language, not only raw velocity or tilt.