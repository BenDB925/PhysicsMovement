# Task Routing

> Fast-start map for GPT-5.4 agents. Use this before broad codebase search.

## First Minute

1. Read `.copilot-instructions.md` for repo-wide guardrails.
2. Immediately load `CODING_STANDARDS.md` and `ARCHITECTURE.md` before touching code.
3. Use the table below to jump straight to the right scripts, scenes, and tests.
4. If the task depends on live Unity editor state, read `UNITY_MCP.md` before choosing between MCP, terminal, or direct asset-edit workflows.
5. Do not assume folders listed in roadmap docs already contain code.

## Context Budget Rules

1. When a file has `Quick Load` or `Read More When`, read only those sections first and continue deeper only when the task matches a listed condition.
2. Prefer the active parent plan and the freshest artifact summary before raw logs, raw XML, or older child docs.
3. Open `LOCOMOTION_BASELINES.md` only for regression comparison, known reds, or baseline verification.
4. Open only the roadmap chapter docs whose scope matches the task; do not load every chapter by default.
5. For test execution or triage, prefer `TestResults/latest-summary.md` when present, then the relevant XML, and only then the full Unity log if the summary or XML cannot explain the result.

## Task Map

| Task / Symptom | Start Here | Also Read | Verify With |
|---|---|---|---|
| Roadmap-driven locomotion refactor or chapter-scoped locomotion work | `.github/instructions/unified-locomotion-roadmap.instructions.md` | Relevant chapter doc(s) only; `LOCOMOTION_BASELINES.md` when comparing regressions or known reds; `DEBUGGING.md` for active investigation work | Chapter verification gate plus focused PlayMode slices through `Tools/Run-UnityTests.ps1` |
| Locomotion contracts, observation aggregation/filtering, LocomotionDirector command flow, or C1.4-C2.3 ownership/world-model rewiring | `Assets/Scripts/Character/Locomotion/*.cs`, `Assets/Scripts/Character/GroundSensor.cs`, `Assets/Scripts/Character/LocomotionCollapseDetector.cs`, `Assets/Scripts/Character/PlayerMovement.cs`, `Assets/Scripts/Character/BalanceController.cs`, `Assets/Scripts/Character/LegAnimator.cs` | Chapter 1 for ownership work; Chapter 2 for observation/filtering work; read both only when contracts cross those boundaries, plus `ARCHITECTURE.md` and `.copilot-instructions.md` | `Assets/Tests/EditMode/Character/LocomotionContractsTests.cs`, `Assets/Tests/EditMode/Character/LocomotionDirectorEditModeTests.cs`, `Assets/Tests/PlayMode/Character/LocomotionDirectorTests.cs`, `Assets/Tests/PlayMode/Character/BalanceControllerIntegrationTests.cs`, `Assets/Tests/PlayMode/Character/MovementQualityTests.cs`, plus the Chapter 1 verification gate when runtime wiring changes |
| Knockdown, surrender, external impacts, floor dwell, or stand-up sequencing | `Assets/Scripts/Character/BalanceController.cs`, `Assets/Scripts/Character/CharacterState.cs`, `Assets/Scripts/Character/ImpactKnockdownDetector.cs`, `Assets/Scripts/Character/KnockdownSeverity.cs`, `Assets/Scripts/Character/RagdollSetup.cs` | `Plans/comedic-knockdown-overhaul.plan.md`, the matching chapter doc under `Plans/comedic-knockdown-overhaul/`, plus `ARCHITECTURE.md` and `.copilot-instructions.md` | Focused EditMode compile/import coverage via `Assets/Tests/EditMode/Character/LocomotionDirectorEditModeTests.cs`, then the closest runtime slices such as `Assets/Tests/PlayMode/Character/BalanceControllerTests.cs`, `Assets/Tests/PlayMode/Character/HardSnapRecoveryTests.cs`, and `Assets/Tests/PlayMode/Character/RagdollSetupTests.cs` |
| Leg-state contracts, transition reasons, per-leg state machines, catch-step labeling, or Chapter 3 gait-role migration | `Assets/Scripts/Character/Locomotion/LegStateMachine.cs`, `Assets/Scripts/Character/Locomotion/*.cs`, `Assets/Scripts/Character/LegAnimator.cs`, `Assets/Scripts/Character/CharacterState.cs` | `Plans/unified-locomotion-roadmap/03-leg-states.md`; add Chapter 1 or 2 only when ownership boundaries or observation inputs also change; plus `ARCHITECTURE.md` and `.copilot-instructions.md` | `Assets/Tests/EditMode/Character/LocomotionContractsTests.cs`, `Assets/Tests/PlayMode/Character/LocomotionDirectorTests.cs`, `Assets/Tests/PlayMode/Character/LegAnimatorTests.cs`, `Assets/Tests/PlayMode/Character/GaitOutcomeTests.cs`, `Assets/Tests/PlayMode/Character/StumbleStutterRegressionTests.cs` |
| Movement, balance, gait, recovery, turning, getting stuck | `Assets/Scripts/Character/BalanceController.cs`, `Assets/Scripts/Character/PlayerMovement.cs`, `Assets/Scripts/Character/CharacterState.cs`, `Assets/Scripts/Character/ImpactKnockdownDetector.cs`, `Assets/Scripts/Character/LocomotionCollapseDetector.cs`, `Assets/Scripts/Character/LegAnimator.cs` | `DEBUGGING.md`, `Assets/Scripts/Character/ArmAnimator.cs`, `Assets/Scripts/Character/TorsoExpression.cs`, `Assets/Scripts/Character/LapDemoRunner.cs`, `Assets/Scripts/Character/FallPoseRecorder.cs`, `Assets/Scripts/Character/DebugPushForce.cs` | `Assets/Tests/PlayMode/Character/BalanceController*.cs`, `Assets/Tests/PlayMode/Character/PlayerMovementTests.cs`, `Assets/Tests/PlayMode/Character/LegAnimatorTests.cs`, `Assets/Tests/PlayMode/Character/GaitOutcomeTests.cs`, `Assets/Tests/PlayMode/Character/HardSnapRecoveryTests.cs`, `Assets/Tests/PlayMode/Character/TurnRecoveryTests.cs`, `Assets/Tests/PlayMode/Character/SpinRecoveryTests.cs`, `Assets/Tests/PlayMode/Character/MovementQualityTests.cs`, `Assets/Tests/PlayMode/Character/FallPoseRecorderTests.cs` |
| Ragdoll rig, joints, prefab composition, collision filtering | `Assets/Scripts/Character/RagdollSetup.cs`, `Assets/Scripts/Editor/RagdollBuilder.cs` | `ARCHITECTURE.md` joint hierarchy section, `Assets/Scripts/Editor/SkinnedRagdollBuilder.cs`, `Assets/Scripts/Character/RagdollMeshFollower.cs` | `Assets/Tests/EditMode/Character/PlayerRagdollPrefabTests.cs`, `Assets/Tests/PlayMode/Character/RagdollSetupTests.cs`, `Assets/Tests/PlayMode/Character/PlayerRagdollPrefabPlayModeTests.cs` |
| Camera issues | `Assets/Scripts/Character/CameraFollow.cs` | `Assets/Scripts/Character/PlayerMovement.cs`, `Assets/Scenes/Arena_01.unity` | `Assets/Tests/PlayMode/Character/CameraFollowTests.cs` |
| Global physics settings, layers, project bootstrapping | `Assets/Scripts/Core/GameSettings.cs` | `ProjectSettings/ProjectVersion.txt`, `ARCHITECTURE.md` physics settings section | `Assets/Tests/EditMode/Core/GameSettingsTests.cs` |
| Input wiring | `Assets/Scripts/Input/PlayerInputActions.cs`, `Assets/Scripts/Character/PlayerMovement.cs` | `ARCHITECTURE.md` PlayerMovement section, `.copilot-instructions.md` phase tracker | `Assets/Tests/PlayMode/Character/PlayerMovementTests.cs` |
| Environment, room queries, terrain scenario metadata, museum layout, generated arenas | `Assets/Scripts/Environment/ArenaRoom.cs`, `Assets/Scripts/Environment/TerrainScenarioMarker.cs`, `Assets/Scripts/Editor/ArenaBuilder.cs`, `Assets/Scripts/Editor/SceneBuilder.cs`, `Assets/Scripts/Editor/TerrainScenarioBuilder.cs` | `Assets/Scripts/Editor/PropBuilder.cs`, `Assets/Scenes/Museum_01.unity`, `Assets/Scenes/Arena_01.unity`, `CONCEPT.md` | `Assets/Tests/EditMode/Environment/TerrainScenarioSceneTests.cs` plus the smallest relevant scene-level PlayMode slice through `Tools/Run-UnityTests.ps1` |
| Live Unity editor work: scene hierarchy, prefab or component wiring, material edits, menu-item builders, console inspection, or quick in-editor smoke checks | `UNITY_MCP.md` | Relevant runtime or editor scripts, current scene or prefab asset, and `ARCHITECTURE.md` when ownership is unclear | Unity MCP tools for editor-state work; finish with `Tools/Run-UnityTests.ps1` when you need authoritative regression artifacts or fresh XML |
| Test execution, lock handling, result parsing | `AGENT_TEST_RUNNING.md`, `TestResults/latest-summary.md` when present, `Tools/Run-UnityTests.ps1` | `Tools/Write-TestSummary.ps1`, `Tools/ParseResults.ps1`, `parse_results.ps1`, `summary.ps1` | Fresh summary or XML under `TestResults/`; open the raw Unity log only when the summary or XML is insufficient |
| Documentation updates | `.copilot-instructions.md`, `ARCHITECTURE.md`, `TASK_ROUTING.md`, `UNITY_MCP.md`, `PLAN.md`, `Plans/unified-locomotion-roadmap.plan.md` | `Plans/README.md`, the active roadmap instruction or chapter doc when applicable, and `CODING_STANDARDS.md` F9 rule | Confirm references point to real files and current systems, and that the active parent plan or chapter doc reflects the current status, blockers, next step, and first resume artifacts |

## Current Implemented Surface

| Area | Status | Notes |
|---|---|---|
| `Assets/Scripts/Character/` | Implemented | Core active-ragdoll runtime, internal locomotion contract types, and director-owned support plus leg command publication, plus debug/demo helpers |
| `Assets/Scripts/Core/` | Implemented | Physics/layer bootstrap via `GameSettings` |
| `Assets/Scripts/Input/` | Implemented | Generated input wrapper present |
| `Assets/Scripts/Environment/` | Implemented | `ArenaRoom` and `TerrainScenarioMarker` support runtime scene queries for generated rooms and terrain scenarios |
| `Assets/Scripts/Editor/` | Implemented | Builder utilities for ragdoll, arena, props, and scenes |
| `Assets/Tests/EditMode/` | Implemented | Core + character edit-time coverage, plus environment scene-authoring tests for generated terrain scenarios |
| `Assets/Tests/PlayMode/` | Implemented | Physics and movement regression suite |
| `Assets/Scripts/Networking/` | Reserved | Folder planned, no runtime code yet |
| `Assets/Scripts/UI/` | Reserved | Folder planned, no runtime code yet |
| `Assets/Scripts/GameLoop/` | Reserved | Folder planned, no runtime code yet |

## Scene Intent

| Scene | Purpose |
|---|---|
| `Assets/Scenes/Arena_01.unity` | Main movement and camera validation scene for the current physics prototype, including the Chapter 7 terrain gallery kept away from the flat baseline corridor |
| `Assets/Scenes/Museum_01.unity` | Generated museum concept/prototype scene that uses `ArenaRoom`, `ArenaBuilder`, and the Chapter 7 room-based terrain gallery |

## Guardrails

- If the task mentions networking, UI, lobby, rounds, spectating, or game loop, check whether the user wants new implementation work. Those folders are still empty placeholders.
- For roadmap locomotion work, treat the active chapter doc as the parent record and update it as soon as subtasks close, bugs or hypotheses appear, blockers or next steps change, or verification artifacts move.
- If a touched class is already near the size cap in `CODING_STANDARDS.md`, split or extract collaborators before adding more logic.
- Treat `LocomotionCollapseDetector` as watchdog-only in Chapter 1 slices: `CharacterState` consumes it for fall transitions, while movement, balance, and leg execution should react through authoritative state labels or `LocomotionDirector` commands instead of reading the raw detector directly.
- For movement regressions, prefer outcome-based PlayMode tests over only checking `targetRotation` or internal state.
- For scene-generation work, look in `Assets/Scripts/Editor/` before assuming the scene was hand-authored.
- Prefer Unity MCP over direct `.unity`, `.prefab`, or `.mat` YAML edits when the task depends on live editor serialization, hierarchy state, or menu-driven builders.
- Keep `Tools/Run-UnityTests.ps1` as the authoritative unattended test path; Unity MCP test execution is a quick in-editor supplement, not the final regression artifact source.
- Run Unity tests sequentially, never in parallel.
- Finish with the smallest relevant regression slice for the touched feature, and only escalate to the full suite when the change crosses system boundaries.