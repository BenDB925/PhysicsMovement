# Chapter 7: Terrain And Contact Robustness

Back to routing: [Unified Locomotion Roadmap](../unified-locomotion-roadmap.instructions.md)

## Read this chapter when

- adding slopes, step-ups, uneven patches, or obstacle lanes to the test scenes
- feeding terrain normals or contact confidence into step planning
- keeping environment builders and runtime room metadata aligned with locomotion scenarios

## Dependencies

- Read Chapters 4 through 6 first if terrain behavior depends on planned footholds, support commands, or recovery strategies.
- Pair this chapter with the environment builder/runtime routing docs whenever scenes or ArenaRoom change.

## Objective

Keep the same locomotion logic stable on non-ideal ground and contact.

## Primary touchpoints

- Assets/Scripts/Character/GroundSensor.cs
- Assets/Scripts/Character/LegAnimator.cs
- Assets/Scripts/Editor/SceneBuilder.cs
- Assets/Scripts/Editor/ArenaBuilder.cs
- Assets/Scripts/Environment/ArenaRoom.cs
- Assets/Scenes/Arena_01.unity
- Assets/Scenes/Museum_01.unity

## Work packages

1. C7.1 Terrain scenarios:
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

- Assets/Tests/PlayMode/Character/Arena01BalanceStabilityTests.cs
- Assets/Tests/PlayMode/Character/MovementQualityTests.cs
- Assets/Tests/PlayMode/Character/GaitOutcomeTests.cs
- Assets/Tests/PlayMode/Character/JumpTests.cs

## Exit criteria

- Locomotion remains coherent and recoverable across terrain variants.