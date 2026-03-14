# Chapter 7: Terrain And Contact Robustness

Back to parent plan: [Unified Locomotion Roadmap](../unified-locomotion-roadmap.plan.md)

## Read this chapter when

- adding slopes, step-ups, uneven patches, or obstacle lanes to the test scenes
- feeding terrain normals or contact confidence into step planning
- keeping environment builders and runtime room metadata aligned with locomotion scenarios

## Dependencies

- Read Chapters 4 through 6 first if terrain behavior depends on planned footholds, support commands, or recovery strategies.
- Pair this chapter with the environment builder/runtime routing docs whenever scenes or ArenaRoom change.

## Objective

Keep the same locomotion logic stable on non-ideal ground and contact.

## Status

- State: C7.1 complete; Chapter 7 remains in progress.
- Current next step: Start C7.2 by feeding terrain normals and contact confidence into step timing and landing targets.
- Active blockers: None.

## Primary touchpoints

- Assets/Scripts/Character/GroundSensor.cs
- Assets/Scripts/Character/LegAnimator.cs
- Assets/Scripts/Editor/SceneBuilder.cs
- Assets/Scripts/Editor/ArenaBuilder.cs
- Assets/Scripts/Editor/TerrainScenarioBuilder.cs
- Assets/Scripts/Environment/ArenaRoom.cs
- Assets/Scripts/Environment/TerrainScenarioMarker.cs
- Assets/Scripts/Environment/TerrainScenarioType.cs
- Assets/Scenes/Arena_01.unity
- Assets/Scenes/Museum_01.unity
- Assets/Tests/EditMode/Environment/TerrainScenarioSceneTests.cs

## Work packages

1. [x] C7.1 Terrain scenarios:
   - Add controlled slope, step-up, step-down, uneven patches, and low-obstacle lanes to test scenes.
2. C7.2 Contact-aware planning:
   - Feed slope normal and contact confidence into step timing and landing targets.
3. C7.3 Partial contact and slip handling:
   - Detect unstable support and shift to wider and bracing step plans.
4. C7.4 Recovery on terrain:
   - Validate stumble and catch-step behavior still works on non-flat surfaces.
5. C7.5 Builder alignment:
   - If scene generation changes are required, keep editor builders and runtime metadata aligned.

## Verification gate

- Assets/Tests/EditMode/Environment/TerrainScenarioSceneTests.cs
- Assets/Tests/PlayMode/Character/Arena01BalanceStabilityTests.cs
- Assets/Tests/PlayMode/Character/MovementQualityTests.cs
- Assets/Tests/PlayMode/Character/GaitOutcomeTests.cs
- Assets/Tests/PlayMode/Character/JumpTests.cs

## Verification artifacts

- `Tools/Run-UnityTests.ps1 -Platform EditMode -TestFilter "PhysicsDrivenMovement.Tests.EditMode.Environment.TerrainScenarioSceneTests"` -> `Passed, Total=5, Passed=5, Failed=0`
- `Tools/Run-UnityTests.ps1 -Platform PlayMode -TestFilter "PhysicsDrivenMovement.Tests.PlayMode.Arena01BalanceStabilityTests"` -> `Passed, Total=2, Passed=2, Failed=0`
- `Tools/Run-UnityTests.ps1 -Platform PlayMode -TestFilter "PhysicsDrivenMovement.Tests.PlayMode.GaitOutcomeTests"` -> `Passed, Total=4, Passed=4, Failed=0`
- `Logs/build_environment_scenes.log` records successful saves for `Assets/Scenes/Arena_01.unity` and `Assets/Scenes/Museum_01.unity` via `SceneBuilder.BuildAllEnvironmentScenes()`.

## Exit criteria

- Locomotion remains coherent and recoverable across terrain variants.

## Progress notes

- 2026-03-14: Completed C7.1. Added shared editor terrain authoring through `TerrainScenarioBuilder`, rebuilt `Arena_01` and `Museum_01` with one slope lane, one step-up lane, one step-down lane, one uneven patch, and one low-obstacle lane each, added runtime metadata via `TerrainScenarioMarker` and `TerrainScenarioType`, and added the focused EditMode scene-authoring fixture to keep the flat Arena baseline corridor clear for existing scene-level locomotion tests.