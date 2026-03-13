# Chapter 3: Replace Cycle-Only Gait With Leg States

Back to parent plan: [Unified Locomotion Roadmap](../unified-locomotion-roadmap.plan.md)

## Quick Load

- Chapter 3 is complete. All five work packages (C3.1-C3.5) have passed their verification gates.
- Use this chapter for reference when future gait-state changes build on the explicit per-leg state labels, transition reasons, and state-driven execution model.
- The main verification surface is `LocomotionContractsTests`, `LocomotionDirectorTests`, `LegAnimatorTests`, `GaitOutcomeTests`, and `StumbleStutterRegressionTests`.
- The Chapter 3 completion gate was `82 total, 79 passed, 3 ignored, 0 failed` in PlayMode and `32/32 passed` in EditMode.

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
   - 2026-03-12: Complete. `LegAnimator.ResolveLegExecutionTargets(...)` now bridges every Chapter 3 runtime state, including a dedicated `RecoveryStep` timing window and a stronger `CatchStep` execution profile, while `_useStateDrivenExecution` keeps the raw sinusoidal command payload as the fallback path.
   - 2026-03-12: Verification passed via focused `LegAnimatorTests` (`58 passed, 3 ignored, 0 failed`) and the broader Chapter 3 mixed PlayMode slice `LocomotionDirectorTests` + `LegAnimatorTests` + `GaitOutcomeTests` + `StumbleStutterRegressionTests` (`77 passed, 3 ignored, 0 failed`). An isolated `MovementQualityTests` rerun remained at the same two known pre-existing reds (`WalkStraight_NoFalls`, `SustainedLocomotionCollapse_TransitionsIntoFallen`) with no new failures.
4. C3.4 State-aware asymmetry:
   - Allow outside and inside legs to diverge in sharp turns.
   - Allow recovery leg to override standard cadence.
   - 2026-03-12: In progress. `LegAnimator.BuildPassThroughCommands(...)` now derives a baseline cadence reason and then splits it per leg so the outside turn leg stays on `TurnSupport`, the inside turn leg uses an override cadence, and catch-step ownership is assigned to a selected recovery leg instead of mirroring `StumbleRecovery` across both legs. Recovery-leg selection now prefers weaker support/trailing-foot evidence over a fixed mirror assumption.
   - 2026-03-12: Idle pass-through commands now publish neutral `HoldPose` targets while the internal phase still decays, and no-input gait activation now uses a braking-scale speed threshold so the stop/idle path does not stay active on settle noise.
   - 2026-03-12: Focused verification passed via `LocomotionDirectorTests` (`11/11`), the EditMode seam slice `LocomotionContractsTests` + `LocomotionDirectorEditModeTests` (`19/19`), isolated `LegAnimatorTests` (`58 passed, 3 ignored, 0 failed`), and the tested pairwise PlayMode combinations `LocomotionDirectorTests` + `LegAnimatorTests` (`69 passed, 3 ignored, 0 failed`), `GaitOutcomeTests` + `LegAnimatorTests` (`62 passed, 3 ignored, 0 failed`), `LegAnimatorTests` + `StumbleStutterRegressionTests` (`63 passed, 3 ignored, 0 failed`), and `GaitOutcomeTests` + `StumbleStutterRegressionTests` (`9/9`).
   - 2026-03-13: Complete. Prefab-backed PlayMode fixtures that build their own rigs now switch onto a fresh runtime-created active scene via `Assets/Tests/PlayMode/Utilities/PlayModeSceneIsolation.cs`, which removed the scene bleed that had been leaving later locomotion fixtures inside Arena_01 or another authored scene.
   - 2026-03-13: `LegAnimatorTests.LowerLeg_WhenWalking_FeetAlternate()` now measures hips-relative lower-leg forward lead plus separated peak timing rather than strict world-position crossing, which better matches the explicit per-leg controller while still catching dead-leg and synchronized-gait regressions.
   - 2026-03-13: The full mixed PlayMode slice `LocomotionDirectorTests` + `LegAnimatorTests` + `GaitOutcomeTests` + `StumbleStutterRegressionTests` passed `78 passed, 3 ignored, 0 failed`, closing the remaining C3.4 blocker.
5. C3.5 Failure handling:
   - If state machine confidence is low, degrade gracefully to stable fallback gait rather than hard snapping.
   - 2026-03-13: Complete. `ComputeStateMachineConfidence` restructured into four explicit steps with the planted-confidence floor applied AFTER all penalties (STEP 4). During recognized sharp turns (`hasTurnAsymmetry`), the floor is guaranteed at least the exit threshold so walking-phase fallback latches release immediately when the turn is recognized. Developing-turn gate softens COM-outside-support penalty, and turn-attenuation reduces the unexplained-asymmetry penalty proportionally to turn severity.
   - 2026-03-13: Root cause from diagnostic logging: during normal walking, `IsComOutsideSupport=true` craters confidence to 0 (latch triggers). During the turn, `PlantedConfidence=0.000` on most frames so the planted-floor cannot unlatch. The fix guarantees a hard floor at exit threshold during recognized turns so the state machine stays confident enough for turn-specific leg roles.
   - 2026-03-13: Verification passed via both target tests (`SharpTurn_TurnSupport` + `ConvergesTowardMirroredFallback`), the full Chapter 3 PlayMode gate (`79 passed, 3 ignored, 0 failed`), and full EditMode suite (`32/32`).

## Verification gate

- Assets/Tests/PlayMode/Character/LegAnimatorTests.cs
- Assets/Tests/PlayMode/Character/GaitOutcomeTests.cs
- Assets/Tests/PlayMode/Character/MovementQualityTests.cs
- Assets/Tests/PlayMode/Character/StumbleStutterRegressionTests.cs

## Exit criteria

- At runtime, each leg has an explicit state and transition reason that can be logged.