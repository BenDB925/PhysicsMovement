# Locomotion Baselines

Purpose: store concrete locomotion regression snapshots and focused test-run results without mixing them into general debugging policy.

## Quick Load

- This file is artifact history for locomotion regressions and verification slices, not the general debugging workflow.
- The main long-lived known reds are `WalkStraight_NoFalls` and `SustainedLocomotionCollapse_TransitionsIntoFallen`; mixed slices can also surface order-sensitive pressure points such as `TurnAndWalk_CornerRecovery`, `HardSnap90_AtFullSpeed_CharacterRecoversAndMakesProgress`, and `SpinRecoveryTests.AfterFullSpinThenForwardInput_DisplacementRecoveredWithin2s`.
- The Chapter 1 baseline snapshot and completion verification are the current reference points for pre-director parity and for distinguishing pre-existing failures from new regressions.
- Prefer the newest relevant section for the active chapter or verification pass rather than rereading the whole file on every task.

## Read More When

- Continue into the specific snapshot sections when the task needs metric-by-metric comparison against an earlier run.
- Continue into the completion verification sections when triaging whether a current failure is pre-existing, order-sensitive, or newly introduced.
- Continue into the artifact lists when you need the exact XML or log paths that back a baseline claim.

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

## Chapter 1 Step 6 Parity Refresh (2026-03-12)

Focused verification artifacts:
- `Logs/test_editmode_20260312_145407.log`
- `Logs/test_playmode_20260312_145449.log`
- `Logs/test_playmode_20260312_150230.log`
- `TestResults/EditMode.xml`
- `TestResults/PlayMode.xml`

Focused verification results:
- EditMode seam slice: `6/6` passed.
- Chapter 1 PlayMode gate: `114` total, `111` passed, `0` failed, `3` ignored.
- Baseline outcome slice: `12` total, `10` passed, `2` failed.

Current baseline comparison versus the initial Chapter 1 snapshot:
- GaitOutcome remains within the baseline envelope: springs stayed `1200 / 1200 / 1200 / 1200`, 5-second displacement measured `11.05 m` versus the original `11.32 m`, upper-leg peak rotation stayed `60.7 deg`, and the alternating-stride sync fraction remained `0.0%`.
- HardSnapRecovery improved versus the initial snapshot: the 90-degree snap now recovered by frame `159` with `7.14 m` post-turn progress and `60` max stalled frames, versus the original frame `220`, `5.95 m`, and `109`; the slalom run kept `0` fallen frames with stronger segment progress and comparable stall pressure.
- SpinRecovery matched the original feel after the PlayMode isolation fix: the refreshed slice measured `3.471 m` horizontal displacement and `0.299 rad/s` yaw angular velocity at frame `150`, close to the original `3.594 m` and `0.180 rad/s`. An isolated rerun in `Logs/test_playmode_20260312_145926.log` produced `3.116 m` and `0.306 rad/s`, confirming the earlier low slice metric was a test-order artifact rather than a locomotion regression.
- MovementQuality still holds the same two Chapter 1 red gates: `WalkStraight_NoFalls` remained incomplete at `600` frames with `151` fallen frames, and `SustainedLocomotionCollapse_TransitionsIntoFallen` still never entered `Fallen` within `90` frames.

Step 6 note:
- The first parity-refresh slice exposed a false spin-recovery regression because `MovementQualityTests` changed the global physics layer-collision matrix without restoring it before `SpinRecoveryTests` ran. Saving/restoring the matrix in both fixtures removed the suite-order distortion and made the baseline slice trustworthy again.

## Chapter 1 Completion Verification (2026-03-12)

Full-suite run artifacts:
- `TestResults/EditMode.xml` — `Logs/test_editmode_20260312_151607.log`
- `TestResults/PlayMode.xml` — `Logs/test_playmode_20260312_152410.log`

Full-suite results:
- EditMode: `18/18` passed.
- PlayMode: `200` total, `183` passed, `5` failed, `12` skipped.

Failed tests:
- `WalkStraight_NoFalls` — pre-existing (straight course incomplete at 600 frames, waypoint index 0).
- `SustainedLocomotionCollapse_TransitionsIntoFallen` — pre-existing (state never entered Fallen within 90 frames).
- `HardSnap90_AtFullSpeed_CharacterRecoversAndMakesProgress` — test-order contamination; passes in isolation (recovered at frame 203 vs 200 threshold, borderline).
- `SpinRecoveryTests.AfterFullSpinThenForwardInput_DisplacementRecoveredWithin2s` — test-order contamination; passes in isolation (0.504 m in full suite vs 3+ m isolated).
- `LapCourseTests.CompleteLap_WithinTimeLimit_NoFalls` — fails in isolation too (3 gates missed, 6000 frames); pre-existing pressure point and a Chapter 4 verification gate, not Chapter 1.

Isolated rerun of suspect tests (`SpinRecoveryTests` + `HardSnap90` + `LapCourseTests`):
- `Logs/test_playmode_20260312_152525.log`: `4` total, `3` passed, `1` failed (only `LapCourseTests`).
- Confirms `SpinRecoveryTests` and `HardSnap90` are clean when not preceded by contaminating fixtures.

Test-order sensitivity note:
- Full-suite ordering still causes cross-fixture contamination for spin and hard-snap tests despite per-fixture layer-collision matrix save/restore. The contamination vector is not in the matrix itself (which is properly saved/restored) but likely in residual physics-engine state (cached broadphase pairs or contact caches) that persists across test fixtures when many fixtures run in the same Unity process.

---

## Expected Knockdown Behaviors (Comedic Knockdown Overhaul)

Baseline expectations after the comedic knockdown overhaul (Steps 1–14):

- **Surrender threshold:** Character MAY enter Fallen during extreme tilts (>80°) or when recovery stalls with angle above 50° for more than 0.8 s. Normal locomotion (moderate tilts, standard turning) is unaffected — surrender only fires at extreme angles or prolonged failed recoveries.
- **External impact knockdown:** Hits delivering >5 m/s effective delta-v (direction-weighted) trigger immediate surrender and Fallen. Hits between 2.5–5 m/s apply stagger torque but do not force Fallen. During GettingUp the knockdown threshold is reduced to 60% (×0.6 multiplier). During Fallen it drops to 40% (×0.4 re-trigger).
- **Floor dwell:** 1.5–3.0 s scaled by knockdown severity (0–1). Light stumble-surrender → ~1.5 s; full slam → ~3.0 s. Re-knockdown during dwell resets the timer with max(current, new) severity, capped at 1.5× max dwell. Non-surrender falls retain original `_getUpDelay + _knockoutDuration` timing.
- **Procedural stand-up:** ~1.5–2.5 s via `ProceduralStandUp` (4 phases: OrientProne → ArmPush → LegTuck → Stand). Each phase is physics-gated and can fail, returning to Fallen with a short re-dwell. After 3 failures the safety-net forced stand fires. Only active for surrender-path falls; legacy impulse fallback serves non-surrender falls.
- **Joint spring profile:** On surrender, all joint drive springs ramp to 25% of baseline over 0.15 s (limp ragdoll). Stand-up phases ramp arm/leg springs independently (arm 150%, leg 120–200%). `ResetSpringProfile` restores baseline on completion.
- **Upright torque ramp:** `ClearSurrender` restores upright/height/stabilization scales via smooth `Ramp*` methods (0.3–0.4 s), not instant snap, to prevent stand-up pop.
- **Test coverage:** `SurrenderTests` (4), `ImpactKnockdownTests` (5), `FloorDwellTests` (4), `ProceduralStandUpTests` (6) — all PlayMode under `Assets/Tests/PlayMode/Character/`.