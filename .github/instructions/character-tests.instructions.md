---
description: "Use when changing character-specific EditMode or PlayMode tests in PhysicsDrivenMovementDemo. Routes agents from character regressions to the real runtime scripts and existing outcome-based test suites."
name: "Character Test Routing"
applyTo: "Assets/Tests/**/Character/**/*.cs"
---
# Character Test Routing

- Start by identifying which runtime loop the test exercises: `BalanceController`, `PlayerMovement`, `CharacterState`, `LegAnimator`, `ArmAnimator`, or `CameraFollow`.
- Prefer extending existing outcome-based suites such as `GaitOutcomeTests`, `HardSnapRecoveryTests`, `TurnRecoveryTests`, and `SpinRecoveryTests` before creating new harnesses.
- For stuck, stop-start, gate, or cornering regressions, inspect `LapDemoRunner` and existing movement utilities before changing thresholds.
- If a test failure reveals a real system change, keep `TASK_ROUTING.md`, `.copilot-instructions.md`, and `ARCHITECTURE.md` aligned with the new runtime behaviour.