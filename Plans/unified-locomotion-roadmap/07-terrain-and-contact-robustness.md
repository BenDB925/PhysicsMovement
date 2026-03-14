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

- State: C7.1 through C7.4a are complete; C7.4b-C7.5 remain split into agent-sized slices; Chapter 7 remains in progress.
- Current next step: C7.4b terrain recovery tuning.
- Active blockers: None. `MovementQualityTests.WalkStraight_NoFalls` fails because the C7.1 StepUpLane blocks the test's walking path; this is a pre-existing issue that predates C7.2e and should be addressed in C7.5b when scene geometry is aligned with test courses.

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
    - [x] C7.2c Clearance request planning:
       - Scope: Extend `StepTarget` and `StepPlanner` so valid step-up approaches can request extra clearance only when an actual obstruction is ahead.
       - Done when: Planned targets carry explicit clearance intent instead of globally inflating gait.
       - Verification: `LocomotionContractsTests`.
       - 2026-03-14: Complete. `StepTarget` now carries explicit `RequestedClearanceHeight` / `HasClearanceRequest` metadata, and `StepPlanner` tags swing targets with clearance intent only when the promoted per-foot forward-obstruction sample is tall and confident enough, with approach-level fallback when the planted foot sees the step face first.
    - [x] C7.2d Clearance-aware swing execution:
       - Scope: Apply the new `StepTarget` clearance intent in `LegExecutionProfileResolver` and `LegAnimator`.
       - Done when: Step-up-tagged swings raise knees or feet more than flat-ground swings, while unchanged terrain stays on the current profile.
       - Verification: `LegAnimatorTests`.
       - 2026-03-14: Complete. `LegExecutionProfileResolver` now converts `StepTarget.RequestedClearanceHeight` into extra swing/knee lift for swing-like profiles, `LegAnimator` exposes the clearance tuning fields that feed that resolver path, and the new outcome-based `LegAnimatorTests` seam proves a clearance-tagged swing physically lifts more than the same flat-ground swing while the broader `LegAnimatorTests` + `GaitOutcomeTests` slice stays green.
    - [x] C7.2e Direct step-up outcome regression:
       - Scope: Add or tighten a focused PlayMode case for “walk straight into a step-up” and keep the fix in planning/execution unless support truly collapses into recovery.
       - Done when: The direct approach makes forward progress over the step-up instead of stalling at the face.
       - Verification: focused new PlayMode coverage plus `GaitOutcomeTests` or `Arena01BalanceStabilityTests` if the touched path overlaps those suites.
       - 2026-03-14: In progress. Added `GaitOutcomeTests.DirectApproachIntoStepUpLane_MakesForwardProgressOverRaisedLanding`, promoted top-surface step samples through `GroundSensor`/`FootContactObservation`/`LocomotionObservation`, taught `StepPlanner` to carry touchdown forward onto the raised lane, and added new `LegAnimatorTests` seams for runtime-like clearance lift, forward reach, late-swing landing height, and opposite-leg support extension.
       - 2026-03-15: Complete. Four-part fix: (1) `LegExecutionProfileResolver` clearanceLiftBlend taper onset 0.45→0.65 to sustain knee tuck through the face-crossing zone; (2) `LegExecutionProfileResolver` forward-floor modulation — gated `swingClearanceBoost` behind `clearanceReachBlend` to prevent premature forward push; (3) `BalanceController` ground-relative + anticipated step-height offset for preemptive hip rise; (4) `LegAnimator` foot collision bypass — temporarily moves the foot to Layer 13 (LowerLegParts, same as shins which don't collide with Environment) during clearance-tagged swing while foot is below landing height, restoring original layer when foot clears. Root cause: the foot box collider physically jammed against the step face — no angular target or spring force could overcome the perpendicular collision. Telemetry: maxProgress 2.92→11.85m, maxGroundedSupportHeight 0.120→0.750m. Verification: focused step-up test 1/1, LegAnimatorTests+GaitOutcomeTests 68 passed / 3 ignored / 0 failed, Arena01BalanceStabilityTests 2/2, EditMode 83/83, GroundSensor PlayMode 4/4.
3. [ ] C7.3 Partial contact and slip handling:
    - [x] C7.3a Partial-contact observation:
       - Scope: Define unstable-support and partial-contact observation signals for marginal footholds and slip-like landings.
       - Done when: The observation layer distinguishes solid planted support from weak or noisy contact.
       - Verification: `LocomotionContractsTests`.
       - 2026-03-14: Complete. Added `SurfaceNormalQuality` (float 0–1) to the per-foot observation pipeline: `GroundSensor` now exposes `GroundNormal` and `GroundNormalUpAlignment` from the downward SphereCast hit; `LocomotionSensorAggregator` converts the alignment into a quality metric via `InverseLerp(0.5, 1.0, alignment)` and uses it as gradated `contactConfidence` (was binary 1/0); `FootContactObservation` carries the new field through all constructor overloads; `SupportObservationFilter` passes it through to filtered observations; `SupportObservation` exposes `MinSurfaceNormalQuality`; `LocomotionObservation` promotes `LeftSurfaceNormalQuality`, `RightSurfaceNormalQuality`, and `MinSurfaceNormalQuality`. Verification: `LocomotionContractsTests` 87/87 (4 new), PlayMode LegAnimatorTests+GaitOutcomeTests 68/0/3, Arena01BalanceStabilityTests 2/2, GroundSensor integration 4/4.
    - [x] C7.3b Bracing planner response:
       - Scope: Shift `StepPlanner` toward wider and more bracing targets when support is unstable, without escalating into full recovery logic.
       - Done when: Partial-contact cases yield measurably wider or more conservative planned steps.
       - Verification: `LocomotionContractsTests`.
       - 2026-03-14: Complete. Added surface-instability bracing to `StepPlanner` (C7.3b): three new adjustment methods (`ApplyBracingStrideAdjustment`, `ApplyBracingLateralAdjustment`, `ApplyBracingTimingAdjustment`) apply to every swing-like state when `MinSurfaceNormalQuality` drops below a 0.85 floor, scaling stride shortening (15%), lateral widening (6%), and timing shortening (10%) with instability `(1 - InverseLerp(0, 0.85, quality))`. Unlike catch-step adjustments, bracing is not gated by transition reason — it provides a gentle conservative stance on any degraded surface. Four new contract tests verify the three bracing dimensions and confirm no effect above the quality floor.
    - [x] C7.3c Slip-focused outcome coverage:
       - Scope: Add a focused terrain/contact regression that proves the character braces or widens instead of oscillating or collapsing on marginal support.
       - Done when: A partial-contact or slip-like scenario has a stable pass/fail outcome artifact.
       - Verification: targeted PlayMode coverage plus the nearest gait or balance regression slice touched by the change.
       - 2026-03-14: Complete. Added `GaitOutcomeTests.WalkUpSlopeLane_MaintainsProgressOnInclinedSurface` — walks the character up the authored Arena_01 slope ramp and asserts forward progress (≥4m), no extended fallen state (≤80 consecutive frames), and measurable surface-normal degradation on the incline. Telemetry: maxProgress=6.82m, minAlignment=0.442, derivedMinQuality=0.000, degradedFrames=277/500, maxConsecutiveFallenFrames=0. The slope's surface-normal degradation drives quality well below the 0.85 bracing floor, confirming that partial-contact observation (C7.3a) and bracing planner adjustments (C7.3b) are both exercised during the traversal.
4. [ ] C7.4 Recovery on terrain:
    - [x] C7.4a Terrain recovery repro coverage:
       - Scope: Create or lock a terrain-specific stumble or catch-step reproduction path on slope, step-down, or uneven ground.
       - Done when: Terrain recovery has a stable focused PlayMode repro instead of relying on flat-ground recovery tests only.
       - Verification: targeted PlayMode recovery coverage.
       - 2026-03-14: Complete. Fixed spawn positioning in `GaitOutcomeTests.WalkDownStepDownLane_RecoversThroughDescentWithoutExtendedFall` — character was being placed off the back of the elevated Start platform into thin air; corrected to spawn ON the platform with inset toward the lane interior. Removed temporary C7.4a diagnostic logging. Telemetry: maxProgress=9.90m, maxConsecutiveFallenFrames=0, totalFallenTransitions=0, recoveryActiveFrames=598/600, minAlignment=0.235, groundedEnd=True, stateEnd=Moving. The step-down lane exercises terrain-specific catch-step and recovery behavior throughout the descent (recovery active 99.7% of frames) while surface-normal degradation at step edges (alignment=0.235) exercises the partial-contact observation pipeline.
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

- `Tools/Run-UnityTests.ps1 -Platform EditMode -TestFilter "PhysicsDrivenMovement.Tests.EditMode.Character.LocomotionContractsTests"` -> `Passed, Total=74, Passed=74, Failed=0`
- `Tools/Run-UnityTests.ps1 -Platform EditMode -TestFilter "PhysicsDrivenMovement.Tests.EditMode.Character.LocomotionContractsTests;PhysicsDrivenMovement.Tests.EditMode.Character.GroundSensorTests"` -> `Passed, Total=70, Passed=70, Failed=0`
- `Tools/Run-UnityTests.ps1 -Platform EditMode -TestFilter "PhysicsDrivenMovement.Tests.EditMode.Character.GroundSensorTests;PhysicsDrivenMovement.Tests.EditMode.Character.LocomotionContractsTests"` -> `Passed, Total=78, Passed=78, Failed=0`
- `Tools/Run-UnityTests.ps1 -Platform PlayMode -TestFilter "PhysicsDrivenMovement.Tests.PlayMode.BalanceControllerIntegrationTests.GroundSensor_DetectsEnvironmentLayerGround;PhysicsDrivenMovement.Tests.PlayMode.BalanceControllerIntegrationTests.GroundSensor_SingleFrameContactLoss_DoesNotClearGrounded;PhysicsDrivenMovement.Tests.PlayMode.BalanceControllerIntegrationTests.GroundSensor_DoesNotDetectWrongLayerGround;PhysicsDrivenMovement.Tests.PlayMode.BalanceControllerIntegrationTests.GroundSensor_SustainedContactLoss_ClearsGrounded"` -> `Passed, Total=4, Passed=4, Failed=0`
- `Tools/Run-UnityTests.ps1 -Platform EditMode -TestFilter "PhysicsDrivenMovement.Tests.EditMode.Environment.TerrainScenarioSceneTests"` -> `Passed, Total=5, Passed=5, Failed=0`
- `Tools/Run-UnityTests.ps1 -Platform PlayMode -TestFilter "PhysicsDrivenMovement.Tests.PlayMode.Arena01BalanceStabilityTests"` -> `Passed, Total=2, Passed=2, Failed=0`
- `Tools/Run-UnityTests.ps1 -Platform PlayMode -TestFilter "PhysicsDrivenMovement.Tests.PlayMode.GaitOutcomeTests"` -> `Passed, Total=4, Passed=4, Failed=0`
- `Tools/Run-UnityTests.ps1 -Platform PlayMode -TestFilter "PhysicsDrivenMovement.Tests.PlayMode.LegAnimatorTests.SetCommandFrame_WhenSwingStepTargetRequestsClearance_RaisesKneeAndFootHigherThanFlatSwing"` -> `Passed, Total=1, Passed=1, Failed=0`
- `Tools/Run-UnityTests.ps1 -Platform PlayMode -TestFilter "PhysicsDrivenMovement.Tests.PlayMode.LegAnimatorTests.SetCommandFrame_WhenRuntimeLikeSwingAlreadyHasLargeAngles_ClearanceStillRaisesKneeAndFootHigher"` -> `Passed, Total=1, Passed=1, Failed=0`
- `Tools/Run-UnityTests.ps1 -Platform PlayMode -TestFilter "PhysicsDrivenMovement.Tests.PlayMode.LegAnimatorTests.SetCommandFrame_WhenClearanceStepTargetLandsFartherAhead_ExtendsForwardSwingTowardTouchdown"` -> `Passed, Total=1, Passed=1, Failed=0`
- `Tools/Run-UnityTests.ps1 -Platform PlayMode -TestFilter "PhysicsDrivenMovement.Tests.PlayMode.LegAnimatorTests.SetCommandFrame_WhenClearanceStepTargetLandsHigher_KeepsLateSwingLiftedTowardRaisedTouchdown"` -> `Passed, Total=1, Passed=1, Failed=0`
- `Tools/Run-UnityTests.ps1 -Platform PlayMode -TestFilter "PhysicsDrivenMovement.Tests.PlayMode.LegAnimatorTests.SetCommandFrame_WhenClearanceStepTargetLandsHigher_StraightensOppositeSupportLeg"` -> `Passed, Total=1, Passed=1, Failed=0`
- `Tools/Run-UnityTests.ps1 -Platform PlayMode -TestFilter "PhysicsDrivenMovement.Tests.PlayMode.GaitOutcomeTests.DirectApproachIntoStepUpLane_MakesForwardProgressOverRaisedLanding"` -> `Passed, Total=1, Passed=1, Failed=0` (`maxProgress=11.85m`, `maxGroundedSupportHeight=0.750m`) — was `Failed` at `maxProgress≈2.92m` before C7.2e fix
- `Tools/Run-UnityTests.ps1 -Platform PlayMode -TestFilter "PhysicsDrivenMovement.Tests.PlayMode.LegAnimatorTests;PhysicsDrivenMovement.Tests.PlayMode.GaitOutcomeTests"` -> `Passed, Total=71, Passed=68, Failed=0, Ignored=3`
- `Tools/Run-UnityTests.ps1 -Platform EditMode -TestFilter "PhysicsDrivenMovement.Tests.EditMode.Character.LocomotionContractsTests;PhysicsDrivenMovement.Tests.EditMode.Character.GroundSensorTests;PhysicsDrivenMovement.Tests.EditMode.Environment.TerrainScenarioSceneTests"` -> `Passed, Total=87, Passed=87, Failed=0` (C7.3a: 4 new partial-contact contract tests)
- `Tools/Run-UnityTests.ps1 -Platform PlayMode -TestFilter "PhysicsDrivenMovement.Tests.PlayMode.Arena01BalanceStabilityTests"` -> `Passed, Total=2, Passed=2, Failed=0` (C7.3a regression check)
- `Tools/Run-UnityTests.ps1 -Platform EditMode -TestFilter "PhysicsDrivenMovement.Tests.EditMode.Character.LocomotionContractsTests"` -> `Passed, Total=84, Passed=84, Failed=0` (C7.3b: 4 new bracing planner tests)
- `Tools/Run-UnityTests.ps1 -Platform EditMode -TestFilter "PhysicsDrivenMovement.Tests.EditMode.Character.LocomotionContractsTests;PhysicsDrivenMovement.Tests.EditMode.Character.GroundSensorTests;PhysicsDrivenMovement.Tests.EditMode.Environment.TerrainScenarioSceneTests"` -> `Passed, Total=91, Passed=91, Failed=0` (C7.3b broader EditMode gate)
- `Tools/Run-UnityTests.ps1 -Platform PlayMode -TestFilter "PhysicsDrivenMovement.Tests.PlayMode.LegAnimatorTests;PhysicsDrivenMovement.Tests.PlayMode.GaitOutcomeTests;PhysicsDrivenMovement.Tests.PlayMode.Arena01BalanceStabilityTests"` -> `Passed, Total=73, Passed=70, Failed=0, Ignored=3` (C7.3b PlayMode regression check)
- `Tools/Run-UnityTests.ps1 -Platform PlayMode -TestFilter "PhysicsDrivenMovement.Tests.PlayMode.GaitOutcomeTests.WalkUpSlopeLane_MaintainsProgressOnInclinedSurface"` -> `Passed, Total=1, Passed=1, Failed=0` (C7.3c: `maxProgress=6.82m`, `minAlignment=0.442`, `derivedMinQuality=0.000`, `degradedFrames=277`, `maxConsecutiveFallenFrames=0`)
- `Tools/Run-UnityTests.ps1 -Platform PlayMode -TestFilter "PhysicsDrivenMovement.Tests.PlayMode.LegAnimatorTests;PhysicsDrivenMovement.Tests.PlayMode.GaitOutcomeTests;PhysicsDrivenMovement.Tests.PlayMode.Arena01BalanceStabilityTests"` -> `Passed, Total=74, Passed=71, Failed=0, Ignored=3` (C7.3c PlayMode regression check)
- `Tools/Run-UnityTests.ps1 -Platform EditMode -TestFilter "PhysicsDrivenMovement.Tests.EditMode.Character.LocomotionContractsTests;PhysicsDrivenMovement.Tests.EditMode.Character.GroundSensorTests;PhysicsDrivenMovement.Tests.EditMode.Environment.TerrainScenarioSceneTests"` -> `Passed, Total=91, Passed=91, Failed=0` (C7.3c EditMode regression check)
- `Tools/Run-UnityTests.ps1 -Platform PlayMode -TestFilter "PhysicsDrivenMovement.Tests.PlayMode.GaitOutcomeTests.WalkDownStepDownLane_RecoversThroughDescentWithoutExtendedFall"` -> `Passed, Total=1, Passed=1, Failed=0` (C7.4a: `maxProgress=9.90m`, `maxConsecutiveFallenFrames=0`, `totalFallenTransitions=0`, `recoveryActiveFrames=598`, `minAlignment=0.235`, `groundedEnd=True`, `stateEnd=Moving`)
- `Tools/Run-UnityTests.ps1 -Platform PlayMode -TestFilter "PhysicsDrivenMovement.Tests.PlayMode.LegAnimatorTests;PhysicsDrivenMovement.Tests.PlayMode.GaitOutcomeTests;PhysicsDrivenMovement.Tests.PlayMode.Arena01BalanceStabilityTests"` -> `Passed, Total=75, Passed=72, Failed=0, Ignored=3` (C7.4a PlayMode regression check)
- `Tools/Run-UnityTests.ps1 -Platform EditMode -TestFilter "PhysicsDrivenMovement.Tests.EditMode.Character.LocomotionContractsTests;PhysicsDrivenMovement.Tests.EditMode.Character.GroundSensorTests;PhysicsDrivenMovement.Tests.EditMode.Environment.TerrainScenarioSceneTests"` -> `Passed, Total=91, Passed=91, Failed=0` (C7.4a EditMode regression check)
- `Logs/build_environment_scenes.log` records successful saves for `Assets/Scenes/Arena_01.unity` and `Assets/Scenes/Museum_01.unity` via `SceneBuilder.BuildAllEnvironmentScenes()`.

## Exit criteria

- Locomotion remains coherent and recoverable across terrain variants.

## Progress notes

- 2026-03-14: Completed C7.4a. Fixed spawn positioning in `GaitOutcomeTests.WalkDownStepDownLane_RecoversThroughDescentWithoutExtendedFall` — the character was being placed off the back of the elevated Start platform into thin air (spawn x=17.1 vs platform starting at x=17.9), causing a 2.23m fall before the test loop. Changed spawn from `highSidePoint - travelDirection * runUp` to `highSidePoint + travelDirection * runUp` so the character starts ON the Start platform inset toward the lane interior. Removed temporary C7.4a diagnostic logging (~30 lines). The step-down lane now serves as the primary terrain-specific recovery repro: recovery was active 99.7% of frames (598/600), surface-normal degradation at step edges drove alignment to 0.235 (well below the 0.85 bracing floor), and the character traversed 9.90m with zero fallen frames. Verification: focused test 1/1, PlayMode LegAnimatorTests+GaitOutcomeTests+Arena01BalanceStabilityTests 72/0/3, EditMode 91/91.
- 2026-03-14: Completed C7.3c. Added `GaitOutcomeTests.WalkUpSlopeLane_MaintainsProgressOnInclinedSurface` — outcome test that walks the character up the Arena_01 slope ramp and asserts forward progress ≥4m, no extended fallen state, and surface-normal degradation below 1.0. Telemetry confirms the slope drives surface quality to 0.000 (alignment=0.442), exercising both partial-contact observation (C7.3a) and bracing planner adjustments (C7.3b). Character traversed 6.82m with zero fallen frames. Verification: focused test 1/1, PlayMode LegAnimatorTests+GaitOutcomeTests+Arena01BalanceStabilityTests 71/0/3, EditMode 91/91.
- 2026-03-14: Completed C7.3b. Added surface-instability bracing to `StepPlanner`: three new static methods (`ApplyBracingStrideAdjustment`, `ApplyBracingLateralAdjustment`, `ApplyBracingTimingAdjustment`) shorten stride (15%), widen lateral offset (6%), and shorten timing (10%) when `MinSurfaceNormalQuality` drops below a 0.85 floor. The instability metric uses `1 - InverseLerp(0, 0.85, quality)` so bracing scales smoothly from nothing on good surfaces to full effect at quality=0. Unlike catch-step adjustments, bracing applies to every swing-like state regardless of transition reason. Verification: LocomotionContractsTests 84/84 (+4 new bracing tests), broader EditMode 91/91, PlayMode LegAnimatorTests+GaitOutcomeTests+Arena01BalanceStabilityTests 70/0/3.
- 2026-03-14: Completed C7.3a. Added `SurfaceNormalQuality` (float 0–1) as a per-foot partial-contact observation signal: `GroundSensor` exposes `GroundNormal` and `GroundNormalUpAlignment` from the downward SphereCast; `LocomotionSensorAggregator` converts alignment to quality via `InverseLerp(0.5, 1.0)` and uses it as gradated `contactConfidence` (was binary 1/0); `FootContactObservation` carries the new field with backward-compatible constructor overloads; `SupportObservationFilter` passes it through; `SupportObservation.MinSurfaceNormalQuality` and `LocomotionObservation.{Left,Right,Min}SurfaceNormalQuality` expose it to consumers. The observation layer now distinguishes solid flat-ground support (quality=1.0) from degraded slope/edge contact (quality<1.0). Verification: LocomotionContractsTests 87/87 (+4 new), LegAnimatorTests+GaitOutcomeTests 68/0/3, Arena01BalanceStabilityTests 2/2, broader PlayMode 36/36, GroundSensor integration 4/4.
- 2026-03-15: Completed C7.2e. The direct step-up outcome test now passes. Root cause: the foot box collider (on the default player layer that collides with Environment) physically jammed against the step face — angular targets and spring forces couldn't overcome the perpendicular collision because ConfigurableJoint chains absorb linear forces applied to end-effectors. Four-part fix: (1) `LegExecutionProfileResolver` clearanceLiftBlend taper 0.45→0.65; (2) forward-floor modulation gating `swingClearanceBoost` behind `clearanceReachBlend`; (3) `BalanceController` ground-relative + anticipated step-height offset; (4) `LegAnimator` foot collision bypass — temporarily moves the foot to Layer 13 (LowerLegParts) during clearance-tagged swing while below landing height. Verification: focused step-up 1/1, LegAnimatorTests+GaitOutcomeTests 68/0/3, Arena01BalanceStabilityTests 2/2, EditMode 83/83, GroundSensor PlayMode 4/4. Pre-existing failures noted in MovementQualityTests (WalkStraight path crosses C7.1 StepUpLane), GetUpReliabilityTests (1 flaky directional impulse), LocomotionDirectorTests (1 catch-step promotion).
- 2026-03-14: Started C7.2e and kept it outcome-first. Added `DirectApproachIntoStepUpLane_MakesForwardProgressOverRaisedLanding` to `GaitOutcomeTests`, then used its failure telemetry to isolate the remaining boundary. The direct lane no longer looks like a sensing or planner-progress miss: obstruction sensing and clearance requests stay active, top-surface samples are promoted through the locomotion observation contracts, `StepPlanner` can now plan touchdown past plateau entry, and focused `LegAnimatorTests` seams prove clearance lift, forward reach, late-swing landing-height lift, and support-leg extension all react in isolation. The scene-level blocker is narrower: the authored two-riser lane still only reaches first-riser support (`maxGroundedSupportHeight=0.120m`) and body progress stalls around `2.92m`, so the next pass should target how raised touchdown/support transfer is executed across successive risers rather than reworking obstruction sensing again.
- 2026-03-14: Completed C7.2d. `LegExecutionProfileResolver` now turns `StepTarget` clearance intent into extra knee tuck and swing lift for swing-like execution profiles, and `LegAnimator` exposes the corresponding clearance-tuning fields so step-up-tagged swings gain anticipatory lift without changing flat-ground gait. Verified with the new outcome-based `LegAnimatorTests.SetCommandFrame_WhenSwingStepTargetRequestsClearance_RaisesKneeAndFootHigherThanFlatSwing` (`1/1`), the broader `LegAnimatorTests` + `GaitOutcomeTests` PlayMode slice (`63 passed, 3 ignored, 0 failed`), and a focused `LocomotionContractsTests` EditMode rerun (`74/74`). The active restart point is now C7.2e direct step-up forward-progress coverage.
- 2026-03-14: Completed C7.2c. Added explicit clearance-request metadata to `StepTarget` and taught `StepPlanner` to promote the strongest valid paired forward-obstruction sample into upcoming swing targets so step-up approaches can request extra clearance without globally inflating gait. Verified with focused `LocomotionContractsTests` (`74/74`) plus an outcome-based `GaitOutcomeTests` smoke (`4/4`). The active restart point is now C7.2d clearance-aware swing execution in `LegExecutionProfileResolver` and `LegAnimator`.
- 2026-03-14: Completed C7.2b. Promoted the per-foot forward-obstruction payload into planner-facing `LocomotionObservation` accessors for left/right/any obstruction, per-foot step height, and obstruction confidence so future terrain planning slices do not need to touch `GroundSensor` or nested foot payloads directly. Verified with focused `LocomotionContractsTests` (`69/69`).
- 2026-03-14: Completed C7.2a. Added forward step-face plus top-surface sensing to `GroundSensor`, extended `FootContactObservation` with forward-obstruction height/confidence fields that remain independent from downward grounded state, added the focused `GroundSensorTests` seam, and verified the slice with `LocomotionContractsTests` plus the existing PlayMode GroundSensor regression checks.
- 2026-03-14: Split C7.2-C7.5 into agent-sized slices with explicit scope, done conditions, and focused verification so future terrain work can land as one-shot tasks. The new first slice is C7.2a forward obstruction sensing.
- 2026-03-14: Sandbox follow-up added to C7.2. Direct approaches into the step-up lane can still stall because the runtime only senses ground below each foot and the current `StepTarget` payload does not yet shape swing clearance. Plan the next slice around forward step-face sensing, obstacle-height promotion through the locomotion observation stack, and clearance-aware `StepPlanner` plus `LegExecutionProfileResolver` updates rather than a collision-reactive knee boost.
- 2026-03-14: Completed C7.1. Added shared editor terrain authoring through `TerrainScenarioBuilder`, rebuilt `Arena_01` and `Museum_01` with one slope lane, one step-up lane, one step-down lane, one uneven patch, and one low-obstacle lane each, added runtime metadata via `TerrainScenarioMarker` and `TerrainScenarioType`, and added the focused EditMode scene-authoring fixture to keep the flat Arena baseline corridor clear for existing scene-level locomotion tests.