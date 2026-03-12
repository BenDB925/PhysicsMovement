# Chapter 3: Replace Cycle-Only Gait With Leg States

Back to parent plan: [Unified Locomotion Roadmap](../unified-locomotion-roadmap.plan.md)

## Quick Load

- Use this chapter for gait-state migration: replace mirrored phase-only cadence with explicit per-leg state labels, transition reasons, and eventually state-driven execution.
- Completed work so far covers C3.1 explicit state/reason contracts and C3.2 per-leg controller timing; C3.3-C3.5 are still the remaining bridge, asymmetry, and fallback slices.
- The main verification surface is `LocomotionContractsTests`, `LocomotionDirectorTests`, `LegAnimatorTests`, `GaitOutcomeTests`, and `StumbleStutterRegressionTests`.
- Read Chapter 1 first if a gait-state change also moves ownership boundaries, and coordinate with Chapter 2 when new state transitions depend on observation-model signals.

## Read More When

- Continue into the work packages when touching leg-state labels, transition reasons, per-leg controller rules, or the executor bridge.
- Continue into the verification gate when changing `LegAnimator` runtime behaviour or the Chapter 3 regression surface.
- Continue into the dependency notes when the change also alters observation inputs or authority boundaries.

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
   - 2026-03-12: Complete. Added `LegStateType`, `LegStateTransitionReason`, and `LegStateFrame` under `Assets/Scripts/Character/Locomotion/`, and threaded explicit per-leg state plus reason through `LegCommandOutput`.
   - 2026-03-12: `LegAnimator.BuildPassThroughCommands(...)` now classifies the existing mirrored gait into explicit `Stance` / `Swing` / `Plant` roles, promotes `RecoveryStep` and `CatchStep` labels from weak-support recovery conditions, and logs state transitions through `_debugStateTransitions` without changing the current gait execution path.
   - 2026-03-12: Verification passed via `LocomotionContractsTests` + `LocomotionDirectorEditModeTests` (`16/16`), `LocomotionDirectorTests` (`9/9`), and the broader Chapter 3 feature slice `LegAnimatorTests` + `GaitOutcomeTests` + `StumbleStutterRegressionTests` (`63 passed, 3 ignored, 0 failed`).
2. C3.2 Per-leg controller:
   - Implement left and right state machines instead of symmetric phase-only mirror.
   - 2026-03-12: Complete. Added internal `LegStateMachine` under `Assets/Scripts/Character/Locomotion/` so each leg now owns independent phase, state, and transition-reason progression while keeping the existing sinusoidal executor path.
   - 2026-03-12: `LegAnimator.BuildPassThroughCommands(...)` now advances left and right state machines independently, preserves the legacy left-phase seam for collaborators, restricts catch-step promotion to materially behind-body feet or confirmed collapse, and gives explicit `Swing` / `CatchStep` roles a stronger forward arc so gait lift stays viable.
   - 2026-03-12: Verification passed via `LocomotionContractsTests` + `LocomotionDirectorEditModeTests` (`19/19`) and the broader Chapter 3 PlayMode slice `LocomotionDirectorTests` + `LegAnimatorTests` + `GaitOutcomeTests` + `StumbleStutterRegressionTests` (`73 passed, 3 ignored, 0 failed`).
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