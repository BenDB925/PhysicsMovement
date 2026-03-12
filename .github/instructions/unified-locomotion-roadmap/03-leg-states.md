# Chapter 3: Replace Cycle-Only Gait With Leg States

Back to routing: [Unified Locomotion Roadmap](../unified-locomotion-roadmap.instructions.md)

## Read this chapter when

- replacing symmetric phase-only gait behavior with explicit per-leg roles
- adding state transition reasons for braking, turns, or stumble recovery
- giving each leg an explicit fallback or recovery behavior

## Dependencies

- Read Chapter 1 first if the gait work changes ownership boundaries.
- Coordinate with Chapter 2 when state transitions depend on new observation signals.

## Objective

Move from pure phase-offset gait to explicit per-leg state roles.

## Primary touchpoints

- Assets/Scripts/Character/LegAnimator.cs
- Assets/Scripts/Character/CharacterState.cs
- New leg state files under Assets/Scripts/Character/

## Work packages

1. C3.1 Leg state model:
   - Add explicit states: Stance, Swing, Plant, RecoveryStep, CatchStep.
   - Add per-leg transition reasons (speed-up, braking, turn support, stumble recovery).
2. C3.2 Per-leg controller:
   - Implement left and right state machines instead of symmetric phase-only mirror.
3. C3.3 Animator bridge:
   - LegAnimator executes state-driven targets and timing windows.
   - Keep current sinusoidal path as fallback during migration.
4. C3.4 State-aware asymmetry:
   - Allow outside and inside legs to diverge in sharp turns.
   - Allow recovery leg to override standard cadence.
5. C3.5 Failure handling:
   - If state machine confidence is low, degrade gracefully to stable fallback gait rather than hard snapping.

## Verification gate

- Assets/Tests/PlayMode/Character/LegAnimatorTests.cs
- Assets/Tests/PlayMode/Character/GaitOutcomeTests.cs
- Assets/Tests/PlayMode/Character/MovementQualityTests.cs
- Assets/Tests/PlayMode/Character/StumbleStutterRegressionTests.cs

## Exit criteria

- At runtime, each leg has an explicit state and transition reason that can be logged.