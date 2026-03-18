description: "Use when writing, updating, or running Unity EditMode or PlayMode tests in PhysicsDrivenMovementDemo. Covers the lock-safe test path, result verification, and outcome-test expectations."
name: "Unity Test Routing"
applyTo: "Assets/Tests/**/*.cs"
---
# Unity Test Routing

- Ran via `Tools/Run-UnityTests.ps1`
- Run EditMode and PlayMode sequentially. Never run two Unity batch commands against this project in parallel.
- Verify fresh XML under `TestResults/` before trusting a batch-script run result. MCP results are returned directly and do not need XML verification.
- When testing movement or physics behaviour, assert world-space outcomes such as displacement, recovery, tilt, grounded state over time, or scene behaviour.
- Use `Assets/Tests/PlayMode/Utilities/GhostDriver.cs` and `Assets/Tests/PlayMode/Utilities/WaypointCourseRunner.cs` before building new movement harnesses from scratch.