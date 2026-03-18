# Locomotion Baselines

Purpose: keep the current locomotion baseline reference, known active reds, and the artifact paths worth opening first. Detailed baseline history lives in [Plans/archive/locomotion-baselines-history.md](Plans/archive/locomotion-baselines-history.md).

## Quick Load

- Treat this file as the current baseline index, not the full regression history.
- Current metric anchor: Chapter 8 regression baseline refresh on 2026-03-17.
- Current full-gate reference: Chapter 8 completion verification on 2026-03-17.
- Foot sliding regression gate locked on 2026-03-18; open [Plans/foot-sliding-speed-envelope.plan.md](Plans/foot-sliding-speed-envelope.plan.md) for the thresholds, honest speed ceiling, and the safe method-level filter.
- Stable known red in isolation: `SustainedLocomotionCollapse_TransitionsIntoFallen`.
- Known order-sensitive full-suite pressure points: `WalkStraight_NoFalls` and `TurnAndWalk_CornerRecovery`; `LapCourseTests.CompleteLap_WithinTimeLimit_NoFalls` remains a pre-existing time-limit failure.

## Read More When

- Open [Plans/archive/locomotion-baselines-history.md](Plans/archive/locomotion-baselines-history.md) when you need Chapter 1 or older Chapter 8 snapshot detail.
- Open [Plans/unified-locomotion-roadmap/09-validation-debugging-and-tuning.md](Plans/unified-locomotion-roadmap/09-validation-debugging-and-tuning.md) when the task is baseline-refresh workflow or telemetry tooling.
- Open [Plans/foot-sliding-speed-envelope.plan.md](Plans/foot-sliding-speed-envelope.plan.md) when the task touches move-force, sprint multiplier, cadence, stride length, or planted-foot drift thresholds.
- Open [DEBUGGING.md](DEBUGGING.md) when the task is active investigation rather than known-red comparison.

---

## Current Baseline Reference (2026-03-17)

Use these artifacts first when checking whether a locomotion result is actually new.

Metric anchor artifacts:
- `TestResults/EditMode.xml` — `Logs/test_editmode_20260317_165338.log`
- `TestResults/PlayMode.xml` — `Logs/test_playmode_20260317_172301.log`

Full-gate reference artifacts:
- `TestResults/EditMode.xml` — `Logs/test_editmode_20260317_201024.log`
- `TestResults/PlayMode.xml` — `Logs/test_playmode_20260317_201133.log`
- `Logs/test_playmode_20260317_201830.log` — isolated `MovementQualityTests` confirmation

Current gate summary:
- EditMode: `119/119` passed.
- PlayMode C8 gate: `35/38` passed in the full 9-suite run; the remaining failures stay within the known-red or order-sensitive set below.
- Isolated `MovementQualityTests`: `3/4` passed; only `SustainedLocomotionCollapse_TransitionsIntoFallen` stays red in isolation.

Current metric anchors:
- GaitOutcome: 5 s displacement `11.30 m`, upper-leg peak rotation `60.5 deg`, stride sync `0.0%`.
- HardSnap 90-degree snap: recovery frame `56`, post-turn progress `7.63 m`, max stalled frames `4`.
- SpinRecovery: forward displacement `2.443 m`, yaw angular velocity at frame 150 `0.313 rad/s`.
- MovementQuality corner course: completed in `458` frames with `0` fallen frames.

Current focused foot-sliding gate:
- Method-level filter: `PhysicsDrivenMovement.Tests.PlayMode.FootSlidingTests.WalkForward_PlantedFeetDoNotSlide;PhysicsDrivenMovement.Tests.PlayMode.FootSlidingTests.SprintForward_PlantedFeetDoNotSlide`
- Locked thresholds: walk `< 0.35 m` max drift, sprint `< 0.80 m` max drift
- Honest envelope reference: `_moveForce = 150`, `_sprintSpeedMultiplier = 1.8`, `_stepFrequencyScale = 0.10`, `MaxStrideLength = 0.30`
- Use the method-level filter above because fixture-level `FootSlidingTests` also executes the explicit speed sweep.
- Detailed envelope analysis: [Plans/foot-sliding-speed-envelope.plan.md](Plans/foot-sliding-speed-envelope.plan.md)

## Known Active Reds

- `SustainedLocomotionCollapse_TransitionsIntoFallen`: stable known red. It still fails in isolation because the state never enters `Fallen` within `90` frames.
- `LapCourseTests.CompleteLap_WithinTimeLimit_NoFalls`: stable pre-existing pressure point. The lap can finish without falls but still misses the `4000`-frame gate.

## Known Order-Sensitive Pressure Points

- `WalkStraight_NoFalls`: can fail in the full suite even when the isolated `MovementQualityTests` slice returns to the known-red envelope.
- `TurnAndWalk_CornerRecovery`: currently a full-suite contamination risk, not the first sign of a new locomotion regression by default.
- `HardSnap90_AtFullSpeed_CharacterRecoversAndMakesProgress` and `SpinRecoveryTests.AfterFullSpinThenForwardInput_DisplacementRecoveredWithin2s`: historical mixed-slice contamination cases. Check isolation before treating them as new regressions.

## Current Behavior Expectations

- Extreme tilt or greater than `5 m/s` effective delta-v can force surrender and `Fallen`; moderate locomotion disturbances should not.
- Surrender-path floor dwell is roughly `1.5-3.0 s` by severity, with re-knockdowns extending the current dwell window.
- Surrender-path stand-up is procedural and typically resolves in about `1.5-2.5 s`, with a forced-stand safety net after repeated phase failures.

## History Archive

- Detailed Chapter 1 snapshots, Chapter 8 baseline tables, and the full knockdown expectation notes now live in [Plans/archive/locomotion-baselines-history.md](Plans/archive/locomotion-baselines-history.md).
