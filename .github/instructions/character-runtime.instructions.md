description: "Use when changing ragdoll movement, gait, balance, jump, recovery, camera follow, or character playmode tests in PhysicsDrivenMovementDemo. Routes agents to the real runtime entry points and regression tests."
name: "Character Runtime Routing"
applyTo: "Assets/Scripts/Character/**/*.cs"
---
# Character Runtime Routing

- Start with the actual control loop: `BalanceController`, `PlayerMovement`, `CharacterState`, `LegAnimator`, then `ArmAnimator` if arm motion is implicated.
- For stuck, stop-start, or cornering issues, inspect `LapDemoRunner` and the PlayMode regression tests before changing tuning.
- Treat `DebugPushForce` as a debug helper and `RagdollMeshFollower` as a visual/mesh sync helper, not core locomotion logic.
- Prefer outcome-based PlayMode assertions. The strongest existing references are `GaitOutcomeTests`, `HardSnapRecoveryTests`, `TurnRecoveryTests`, and `SpinRecoveryTests`.
- If the change adds or removes a character-side system, update `.copilot-instructions.md`, `ARCHITECTURE.md`, and `TASK_ROUTING.md` together.