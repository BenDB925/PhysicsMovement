# Chapter 7: Terrain And Contact Robustness

Back to parent plan: [Unified Locomotion Roadmap](../unified-locomotion-roadmap.plan.md)

## Read this chapter when

- adding slopes, step-ups, uneven patches, or obstacle lanes to the test scenes
- feeding terrain normals, step-up obstruction, or contact confidence into step planning
- adding clearance-aware swing execution so the knees rise before a step-up instead of only after collision
- keeping environment builders and runtime room metadata aligned with locomotion scenarios

## Dependencies

- Read Chapters 4 through 6 first if terrain behavior depends on planned footholds, support commands, or recovery strategies.
- Pair this chapter with the environment builder/runtime routing docs whenever scenes or ArenaRoom change.

## Objective

Keep the same locomotion logic stable on non-ideal ground and contact, including step-ups that require anticipatory foot clearance.

## Status

- State: C7.1 through C7.2b are complete; C7.2c-C7.5 remain split into agent-sized slices; Chapter 7 remains in progress.
- Current next step: Start C7.2c by extending `StepTarget` and `StepPlanner` with explicit clearance intent driven by the promoted forward-obstruction observation fields.
- Active blockers: None.

## Primary touchpoints

- Assets/Scripts/Character/GroundSensor.cs
- Assets/Scripts/Character/Locomotion/LocomotionSensorAggregator.cs
- Assets/Scripts/Character/Locomotion/FootContactObservation.cs
- Assets/Scripts/Character/Locomotion/LocomotionObservation.cs
- Assets/Scripts/Character/Locomotion/StepPlanner.cs
- Assets/Scripts/Character/Locomotion/StepTarget.cs
- Assets/Scripts/Character/Locomotion/LegExecutionProfileResolver.cs
- Assets/Scripts/Character/LegAnimator.cs
- Assets/Scripts/Editor/SceneBuilder.cs
- Assets/Scripts/Editor/ArenaBuilder.cs
- Assets/Scripts/Editor/TerrainScenarioBuilder.cs
- Assets/Scripts/Environment/ArenaRoom.cs
- Assets/Scripts/Environment/TerrainScenarioMarker.cs
- Assets/Scripts/Environment/TerrainScenarioType.cs
- Assets/Scenes/Arena_01.unity
- Assets/Scenes/Museum_01.unity
- Assets/Tests/EditMode/Character/LocomotionContractsTests.cs
- Assets/Tests/EditMode/Environment/TerrainScenarioSceneTests.cs
- Assets/Tests/PlayMode/Character/LegAnimatorTests.cs

## Work packages

Each unchecked sub-slice is intentionally small enough for one agent pass: make the change, run the focused verification, and update this chapter.

1. [x] C7.1 Terrain scenarios:
    - Add controlled slope, step-up, step-down, uneven patches, and low-obstacle lanes to test scenes.
    - 2026-03-14: Complete. Shared terrain authoring now exists in `TerrainScenarioBuilder`, both generated scenes contain the same terrain gallery, and focused scene tests lock the authoring contract.
2. [ ] C7.2 Contact-aware planning:
    - [x] C7.2a Forward obstruction sensing:
       - Scope: Teach `GroundSensor` and `FootContactObservation` to report per-foot forward obstruction, estimated step height, and a confidence value without changing planner behavior yet.
       - Done when: The per-foot contact payload can represent “step-up ahead” independently from downward grounded state.
       - Verification: `LocomotionContractsTests` plus any focused sensor seam tests added in the same slice.
       - 2026-03-14: Complete. `GroundSensor` now probes for a forward step face plus reachable top surface, `FootContactObservation` carries obstruction, height, and confidence independently from `IsGrounded`, and the existing sensor aggregation/filter path preserves that per-foot payload without changing planning behavior.
    - [x] C7.2b Observation promotion:
       - Scope: Promote the new per-foot forward-obstruction fields from the sensor/foot payload path into `LocomotionObservation` so planning can read terrain-facing state without touching sensors directly.
       - Done when: The aggregated locomotion observation exposes forward obstruction and estimated step height beside slope/contact confidence.
       - Verification: `LocomotionContractsTests`.
       - 2026-03-14: Complete. `LocomotionObservation` now exposes planner-facing left/right and aggregate forward-obstruction, estimated-step-height, and obstruction-confidence accessors derived from the filtered foot payload, so later planning slices can stay inside the observation layer.
    - [ ] C7.2c Clearance request planning:
       - Scope: Extend `StepTarget` and `StepPlanner` so valid step-up approaches can request extra clearance only when an actual obstruction is ahead.
       - Done when: Planned targets carry explicit clearance intent instead of globally inflating gait.
       - Verification: `LocomotionContractsTests`.
    - [ ] C7.2d Clearance-aware swing execution:
       - Scope: Apply the new `StepTarget` clearance intent in `LegExecutionProfileResolver` and `LegAnimator`.
       - Done when: Step-up-tagged swings raise knees or feet more than flat-ground swings, while unchanged terrain stays on the current profile.
       - Verification: `LegAnimatorTests`.
    - [ ] C7.2e Direct step-up outcome regression:
       - Scope: Add or tighten a focused PlayMode case for “walk straight into a step-up” and keep the fix in planning/execution unless support truly collapses into recovery.
       - Done when: The direct approach makes forward progress over the step-up instead of stalling at the face.
       - Verification: focused new PlayMode coverage plus `GaitOutcomeTests` or `Arena01BalanceStabilityTests` if the touched path overlaps those suites.
3. [ ] C7.3 Partial contact and slip handling:
    - [ ] C7.3a Partial-contact observation:
       - Scope: Define unstable-support and partial-contact observation signals for marginal footholds and slip-like landings.
       - Done when: The observation layer distinguishes solid planted support from weak or noisy contact.
       - Verification: `LocomotionContractsTests`.
    - [ ] C7.3b Bracing planner response:
       - Scope: Shift `StepPlanner` toward wider and more bracing targets when support is unstable, without escalating into full recovery logic.
       - Done when: Partial-contact cases yield measurably wider or more conservative planned steps.
       - Verification: `LocomotionContractsTests`.
    - [ ] C7.3c Slip-focused outcome coverage:
       - Scope: Add a focused terrain/contact regression that proves the character braces or widens instead of oscillating or collapsing on marginal support.
       - Done when: A partial-contact or slip-like scenario has a stable pass/fail outcome artifact.
       - Verification: targeted PlayMode coverage plus the nearest gait or balance regression slice touched by the change.
4. [ ] C7.4 Recovery on terrain:
    - [ ] C7.4a Terrain recovery repro coverage:
       - Scope: Create or lock a terrain-specific stumble or catch-step reproduction path on slope, step-down, or uneven ground.
       - Done when: Terrain recovery has a stable focused PlayMode repro instead of relying on flat-ground recovery tests only.
       - Verification: targeted PlayMode recovery coverage.
    - [ ] C7.4b Terrain recovery tuning:
       - Scope: Keep Chapter 6 recovery and catch-step behavior coherent on non-flat surfaces without regressing flat-ground behavior.
       - Done when: Terrain recovery passes its focused repro and the nearest Chapter 6 regression slice still stays green.
       - Verification: terrain recovery repro plus the touched recovery suites.
5. [ ] C7.5 Builder alignment:
    - [ ] C7.5a Runtime metadata contract:
       - Scope: Update `ArenaRoom`, `TerrainScenarioMarker`, or related runtime metadata only if later slices need new queryable scene data.
       - Done when: Runtime consumers can find the required terrain metadata without hard-coded scene assumptions.
       - Verification: `TerrainScenarioSceneTests`.
    - [ ] C7.5b Scene regeneration parity:
       - Scope: Reflect any metadata or layout changes through `TerrainScenarioBuilder`, `SceneBuilder`, and `ArenaBuilder` so Arena and Museum stay aligned.
       - Done when: Both generated scenes preserve the same intended terrain authoring contract.
       - Verification: `TerrainScenarioSceneTests` plus refreshed scene-build artifacts if the scenes are regenerated.

## Verification gate

- Assets/Tests/EditMode/Character/LocomotionContractsTests.cs
- Assets/Tests/EditMode/Environment/TerrainScenarioSceneTests.cs
- Assets/Tests/PlayMode/Character/Arena01BalanceStabilityTests.cs
- Assets/Tests/PlayMode/Character/MovementQualityTests.cs
- Assets/Tests/PlayMode/Character/GaitOutcomeTests.cs
- Assets/Tests/PlayMode/Character/JumpTests.cs
- Assets/Tests/PlayMode/Character/LegAnimatorTests.cs

## Verification artifacts

- `Tools/Run-UnityTests.ps1 -Platform EditMode -TestFilter "PhysicsDrivenMovement.Tests.EditMode.Character.LocomotionContractsTests"` -> `Passed, Total=69, Passed=69, Failed=0`
- `Tools/Run-UnityTests.ps1 -Platform EditMode -TestFilter "PhysicsDrivenMovement.Tests.EditMode.Character.LocomotionContractsTests;PhysicsDrivenMovement.Tests.EditMode.Character.GroundSensorTests"` -> `Passed, Total=70, Passed=70, Failed=0`
- `Tools/Run-UnityTests.ps1 -Platform PlayMode -TestFilter "PhysicsDrivenMovement.Tests.PlayMode.BalanceControllerIntegrationTests.GroundSensor_DetectsEnvironmentLayerGround;PhysicsDrivenMovement.Tests.PlayMode.BalanceControllerIntegrationTests.GroundSensor_SingleFrameContactLoss_DoesNotClearGrounded;PhysicsDrivenMovement.Tests.PlayMode.BalanceControllerIntegrationTests.GroundSensor_DoesNotDetectWrongLayerGround;PhysicsDrivenMovement.Tests.PlayMode.BalanceControllerIntegrationTests.GroundSensor_SustainedContactLoss_ClearsGrounded"` -> `Passed, Total=4, Passed=4, Failed=0`
- `Tools/Run-UnityTests.ps1 -Platform EditMode -TestFilter "PhysicsDrivenMovement.Tests.EditMode.Environment.TerrainScenarioSceneTests"` -> `Passed, Total=5, Passed=5, Failed=0`
- `Tools/Run-UnityTests.ps1 -Platform PlayMode -TestFilter "PhysicsDrivenMovement.Tests.PlayMode.Arena01BalanceStabilityTests"` -> `Passed, Total=2, Passed=2, Failed=0`
- `Tools/Run-UnityTests.ps1 -Platform PlayMode -TestFilter "PhysicsDrivenMovement.Tests.PlayMode.GaitOutcomeTests"` -> `Passed, Total=4, Passed=4, Failed=0`
- `Logs/build_environment_scenes.log` records successful saves for `Assets/Scenes/Arena_01.unity` and `Assets/Scenes/Museum_01.unity` via `SceneBuilder.BuildAllEnvironmentScenes()`.

## Exit criteria

- Locomotion remains coherent and recoverable across terrain variants.

## Progress notes

- 2026-03-14: Completed C7.2b. Promoted the per-foot forward-obstruction payload into planner-facing `LocomotionObservation` accessors for left/right/any obstruction, per-foot step height, and obstruction confidence so future terrain planning slices do not need to touch `GroundSensor` or nested foot payloads directly. Verified with focused `LocomotionContractsTests` (`69/69`).
- 2026-03-14: Completed C7.2a. Added forward step-face plus top-surface sensing to `GroundSensor`, extended `FootContactObservation` with forward-obstruction height/confidence fields that remain independent from downward grounded state, added the focused `GroundSensorTests` seam, and verified the slice with `LocomotionContractsTests` plus the existing PlayMode GroundSensor regression checks.
- 2026-03-14: Split C7.2-C7.5 into agent-sized slices with explicit scope, done conditions, and focused verification so future terrain work can land as one-shot tasks. The new first slice is C7.2a forward obstruction sensing.
- 2026-03-14: Sandbox follow-up added to C7.2. Direct approaches into the step-up lane can still stall because the runtime only senses ground below each foot and the current `StepTarget` payload does not yet shape swing clearance. Plan the next slice around forward step-face sensing, obstacle-height promotion through the locomotion observation stack, and clearance-aware `StepPlanner` plus `LegExecutionProfileResolver` updates rather than a collision-reactive knee boost.
- 2026-03-14: Completed C7.1. Added shared editor terrain authoring through `TerrainScenarioBuilder`, rebuilt `Arena_01` and `Museum_01` with one slope lane, one step-up lane, one step-down lane, one uneven patch, and one low-obstacle lane each, added runtime metadata via `TerrainScenarioMarker` and `TerrainScenarioType`, and added the focused EditMode scene-authoring fixture to keep the flat Arena baseline corridor clear for existing scene-level locomotion tests.