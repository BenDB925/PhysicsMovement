# Chapter 9: Validation, Debugging, And Tuning Infrastructure

Back to routing: [Unified Locomotion Roadmap](../unified-locomotion-roadmap.instructions.md)

## Read this chapter when

- adding telemetry, dashboards, or focused regression slices for locomotion work
- capturing and comparing baseline snapshots
- defining failure-triage workflow for locomotion regressions

## Dependencies

- This chapter runs continuously across the roadmap.
- Use it alongside whichever runtime chapter you are actively changing.

## Objective

Prevent the unified system from becoming a black box.

## Primary touchpoints

- Assets/Scripts/Character/FallPoseRecorder.cs
- Assets/Scripts/Character/LapDemoRunner.cs
- Assets/Tests/PlayMode/Character/MovementQualityTests.cs
- Assets/Tests/PlayMode/Character/GaitOutcomeTests.cs
- Assets/Tests/PlayMode/Character/HardSnapRecoveryTests.cs
- parse_results.ps1
- summary.ps1
- AGENT_TEST_RUNNING.md

## Related artifacts

- [LOCOMOTION_BASELINES.md](../../../LOCOMOTION_BASELINES.md)
- [DEBUGGING.md](../../../DEBUGGING.md)

## Work packages

1. C9.1 Scenario matrix:
   - Define named scenarios: start, stop, reversal, hard turn, stumble, terrain, and long-run fatigue.
2. C9.2 Decision telemetry:
   - Log why the director chose each step and recovery mode, not only final motion metrics.
3. C9.3 Outcome dashboards:
   - Expand script output so regressions show displacement, recovery time, fall rate, and step confidence trends.
4. C9.4 Continuous regression slices:
   - Maintain stable focused test filters per subsystem to speed iteration.
5. C9.5 Failure triage workflow:
   - When tests fail, identify decision-layer cause first, then actuator effect.

## Verification gate

- Keep the focused slice for the active chapter green.
- Refresh baseline artifacts when thresholds or behavior expectations change.
- Verify new telemetry is interpretable from NUnit XML or stored logs, not only the Unity Console.

## Exit criteria

- Every major locomotion failure can be traced to a specific decision and observation snapshot.