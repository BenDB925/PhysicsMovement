# Locomotion Baselines

Purpose: store concrete locomotion regression snapshots and focused test-run results without mixing them into general debugging policy.

---

## Chapter 1 Baseline Snapshot (2026-03-12)

Focused PlayMode slice run for C1.1:

`powershell -NoProfile -ExecutionPolicy Bypass -File "H:/Work/PhysicsDrivenMovementDemo/Tools/Run-UnityTests.ps1" -ProjectPath "H:/Work/PhysicsDrivenMovementDemo" -Platform PlayMode -TestFilter "PhysicsDrivenMovement.Tests.PlayMode.GaitOutcomeTests;PhysicsDrivenMovement.Tests.PlayMode.HardSnapRecoveryTests;PhysicsDrivenMovement.Tests.PlayMode.SpinRecoveryTests;PhysicsDrivenMovement.Tests.PlayMode.MovementQualityTests" -MaxAttemptsPerPlatform 2 -Unattended`

Fresh artifact set:
- `TestResults/PlayMode.xml`
- `Logs/test_playmode_20260312_120734.log`

Run result:
- 12 total
- 8 passed
- 4 failed

Captured metrics from the fresh run:
- GaitOutcome: leg springs `1200 / 1200 / 1200 / 1200`; 5 s displacement `11.32 m`; upper-leg peak rotation `60.7 deg`; simultaneous-forward fraction `0 / 489` active frames (`0.0%`).
- HardSnapRecovery slalom: segment progress `[6.08, 4.82, 1.93, 4.65, 5.50] m`; recovery frames `[218, 138, 261, 186, 240]`; max fallen frames `0`; max stalled frames `157`.
- HardSnapRecovery 90-degree snap: windup `6.30 m`; post-turn progress `5.95 m`; recovery frame `220`; max fallen frames `0`; max stalled frames `109`.
- SpinRecovery: forward displacement after spin `3.594 m`; yaw angular velocity at frame 150 `0.180 rad/s`; crossover frames `0 / 200`.
- MovementQuality straight course: `completed=False`, `framesElapsed=600`, `maxConsecutiveFallen=151`.
- MovementQuality corner course: `completed=True`, `framesElapsed=1058`, `maxConsecutiveFallen=151`.
- MovementQuality sustained collapse: `enteredFallen=False`, `frame=-1`, `finalState=Moving`.
- MovementQuality low-progress false-positive guard: `finalState=Moving` after `90` frames.

Current red gates from that same run:
- `HardSnap90_AtFullSpeed_CharacterRecoversAndMakesProgress` failed because recovery resumed at frame `220`, above the `<= 200` gate.
- `WalkStraight_NoFalls` failed because the straight course did not complete within `600` frames and never advanced beyond waypoint index `0`.
- `TurnAndWalk_CornerRecovery` failed because max consecutive fallen frames reached `151`, above the `<= 120` gate.
- `SustainedLocomotionCollapse_TransitionsIntoFallen` failed because the state never entered `Fallen` within `90` frames.

Interpretation for Chapter 1:
- Gait and spin recovery are currently strong enough to baseline as the pre-director locomotion feel.
- Hard-snap recovery is near the threshold and may need a confirmatory rerun before treating `220` vs `200` as a hard regression.
- MovementQuality is not a green baseline today; Chapter 1 ownership work should treat those failures as known pre-existing pressure points, not as noise.

Implementation note:
- The four suites emit tagged lines in NUnit output using the prefix `[C1.1 Baseline]`, so future baseline refreshes can be harvested directly from `TestResults/PlayMode.xml` without re-reading the whole log.