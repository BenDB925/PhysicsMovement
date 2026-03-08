# Task Routing

> Fast-start map for GPT-5.4 agents. Use this before broad codebase search.

## First Minute

1. Read `.copilot-instructions.md` for repo-wide guardrails.
2. Immediately load `CODING_STANDARDS.md` and `ARCHITECTURE.md` before touching code.
3. Use the table below to jump straight to the right scripts, scenes, and tests.
4. Do not assume folders listed in `PLAN.md` already contain code.

## Task Map

| Task / Symptom | Start Here | Also Read | Verify With |
|---|---|---|---|
| Movement, balance, gait, recovery, turning, getting stuck | `Assets/Scripts/Character/BalanceController.cs`, `Assets/Scripts/Character/PlayerMovement.cs`, `Assets/Scripts/Character/CharacterState.cs`, `Assets/Scripts/Character/LegAnimator.cs` | `DEBUGGING.md`, `Assets/Scripts/Character/ArmAnimator.cs`, `Assets/Scripts/Character/LapDemoRunner.cs`, `Assets/Scripts/Character/FallPoseRecorder.cs`, `Assets/Scripts/Character/DebugPushForce.cs` | `Assets/Tests/PlayMode/Character/BalanceController*.cs`, `Assets/Tests/PlayMode/Character/PlayerMovementTests.cs`, `Assets/Tests/PlayMode/Character/LegAnimatorTests.cs`, `Assets/Tests/PlayMode/Character/GaitOutcomeTests.cs`, `Assets/Tests/PlayMode/Character/HardSnapRecoveryTests.cs`, `Assets/Tests/PlayMode/Character/TurnRecoveryTests.cs`, `Assets/Tests/PlayMode/Character/SpinRecoveryTests.cs`, `Assets/Tests/PlayMode/Character/MovementQualityTests.cs`, `Assets/Tests/PlayMode/Character/FallPoseRecorderTests.cs` |
| Ragdoll rig, joints, prefab composition, collision filtering | `Assets/Scripts/Character/RagdollSetup.cs`, `Assets/Scripts/Editor/RagdollBuilder.cs` | `ARCHITECTURE.md`, `PLAN.md` joint hierarchy, `Assets/Scripts/Editor/SkinnedRagdollBuilder.cs`, `Assets/Scripts/Character/RagdollMeshFollower.cs` | `Assets/Tests/EditMode/Character/PlayerRagdollPrefabTests.cs`, `Assets/Tests/PlayMode/Character/RagdollSetupTests.cs`, `Assets/Tests/PlayMode/Character/PlayerRagdollPrefabPlayModeTests.cs` |
| Camera issues | `Assets/Scripts/Character/CameraFollow.cs` | `Assets/Scripts/Character/PlayerMovement.cs`, `Assets/Scenes/Arena_01.unity` | `Assets/Tests/PlayMode/Character/CameraFollowTests.cs` |
| Global physics settings, layers, project bootstrapping | `Assets/Scripts/Core/GameSettings.cs` | `ProjectSettings/ProjectVersion.txt`, `ARCHITECTURE.md` physics settings section | `Assets/Tests/EditMode/Core/GameSettingsTests.cs` |
| Input wiring | `Assets/Scripts/Input/PlayerInputActions.cs`, `Assets/Scripts/Character/PlayerMovement.cs` | `PLAN.md` Phase 3A/3B notes | `Assets/Tests/PlayMode/Character/PlayerMovementTests.cs` |
| Environment, room queries, museum layout, generated arenas | `Assets/Scripts/Environment/ArenaRoom.cs`, `Assets/Scripts/Editor/ArenaBuilder.cs` | `Assets/Scripts/Editor/PropBuilder.cs`, `Assets/Scripts/Editor/SceneBuilder.cs`, `Assets/Scenes/Museum_01.unity`, `Assets/Scenes/Arena_01.unity`, `CONCEPT.md` | Scene-level verification in Unity; no dedicated Environment tests yet |
| Test execution, lock handling, result parsing | `AGENT_TEST_RUNNING.md`, `Tools/Run-UnityTests.ps1` | `Tools/ParseResults.ps1`, `parse_results.ps1`, `summary.ps1` | Fresh XML under `TestResults/`; prefer focused `-TestFilter` runs unless the change is cross-cutting |
| Documentation updates | `.copilot-instructions.md`, `ARCHITECTURE.md`, `PLAN.md` | This file, `CODING_STANDARDS.md` F9 rule | Confirm references point to real files and current systems |

## Current Implemented Surface

| Area | Status | Notes |
|---|---|---|
| `Assets/Scripts/Character/` | Implemented | Core active-ragdoll runtime plus debug/demo helpers, including rolling fall pose diagnostics |
| `Assets/Scripts/Core/` | Implemented | Physics/layer bootstrap via `GameSettings` |
| `Assets/Scripts/Input/` | Implemented | Generated input wrapper present |
| `Assets/Scripts/Environment/` | Implemented | `ArenaRoom` supports the museum scene |
| `Assets/Scripts/Editor/` | Implemented | Builder utilities for ragdoll, arena, props, and scenes |
| `Assets/Tests/EditMode/` | Implemented | Core + character edit-time coverage |
| `Assets/Tests/PlayMode/` | Implemented | Physics and movement regression suite |
| `Assets/Scripts/Networking/` | Reserved | Folder planned, no runtime code yet |
| `Assets/Scripts/UI/` | Reserved | Folder planned, no runtime code yet |
| `Assets/Scripts/GameLoop/` | Reserved | Folder planned, no runtime code yet |

## Scene Intent

| Scene | Purpose |
|---|---|
| `Assets/Scenes/Arena_01.unity` | Main movement and camera validation scene for the current physics prototype |
| `Assets/Scenes/Museum_01.unity` | Generated museum concept/prototype scene that uses `ArenaRoom` and `ArenaBuilder` |

## Guardrails

- If the task mentions networking, UI, lobby, rounds, spectating, or game loop, check whether the user wants new implementation work. Those folders are still empty placeholders.
- For movement regressions, prefer outcome-based PlayMode tests over only checking `targetRotation` or internal state.
- For scene-generation work, look in `Assets/Scripts/Editor/` before assuming the scene was hand-authored.
- Run Unity tests sequentially, never in parallel.
- Finish with the smallest relevant regression slice for the touched feature, and only escalate to the full suite when the change crosses system boundaries.