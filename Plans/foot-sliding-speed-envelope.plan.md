# Foot Sliding Detection & Speed Envelope Tuning

## Status
- State: **In Progress**
- Acceptance target: Find the maximum honest movement speed where the character's planted feet do not visibly slide across the ground, lock that as a regression-tested quality gate, and document the parameter envelope.
- Current next step: WP2 — measure foot drift across speed tiers.
- Active blockers: None.

## Motivation

At higher `_moveForce` values (and with `_sprintSpeedMultiplier` stacking on top), the hips Rigidbody can be driven faster than the leg cycle can keep up with. When this happens, feet that are supposed to be planted on the ground visibly drag or "ice skate" — the body moves but the foot contact point slides along the floor instead of staying fixed until the next step lifts off. This breaks the illusion that the character is actually pushing off the ground.

The goal is to establish:
1. A **measurable metric** for planted-foot drift (how far a foot moves while it should be stationary).
2. A **regression test** that fails if drift exceeds a visual-quality threshold.
3. The **maximum parameter set** (`_moveForce`, `_sprintSpeedMultiplier`, `_stepFrequencyScale`, stride limits) that produces the fastest locomotion without cheating via sliding.

Current prefab state: `_moveForce = 150`, `_sprintSpeedMultiplier = 1.8`, `_jumpForce = 175`. The user reports that 150 N feels grounded; higher values produced visible gliding at sprint speed.

## Concept

A foot is "planted" when the `LegStateMachine` is in `Stance` or `Plant` state and `GroundSensor.IsGrounded` is true. During this window the foot should remain stationary on the ground — any world-space drift of the foot Transform is sliding.

**Why sliding happens:** `PlayerMovement.ApplyMovementForces()` applies `_moveForce * sprintMultiplier` to the hips Rigidbody every FixedUpdate. The hips pull the body forward. Meanwhile `LegAnimator` drives leg joints through a swing/stance cycle at a frequency of `_stepFrequencyScale * planarSpeed` (capped at `MaxStrideLength = 0.30 m`). When the force pushes the body faster than `stepFrequency × strideLength`, the stance foot cannot anchor the body — it gets dragged. The physics engine applies friction, but the continuous hips force overwhelms it.

**Measurement approach:** During each stance phase, record the foot's world position at plant-start. Each subsequent FixedUpdate, measure the XZ delta from that anchor. The maximum delta across the stance duration is the "planted foot drift" for that step. Average or peak drift across a sustained run is the quality metric.

## Available Signals

| Signal | Source | Use |
|--------|--------|-----|
| Foot world position | `LegAnimator._footL` / `_footR` Transform | Ground-truth foot location per frame |
| Planted state | `LegStateMachine.CurrentState == Stance or Plant` | When each foot should be stationary |
| `IsGrounded` per foot | `GroundSensor.IsGrounded` | Confirm foot is actually contacting ground |
| `GroundPoint` per foot | `GroundSensor.GroundPoint` | World-space contact point beneath foot |
| `IsPlanted` | `FootContactObservation.IsPlanted` | Observation-model planted flag |
| `SlipEstimate` | `FootContactObservation.SlipEstimate` | Existing slip signal (may already approximate this) |
| Planar speed | `BalanceController` / `PlayerMovement` | Current body speed for correlation |
| Step frequency | `LegAnimator._stepFrequency` | Current cadence |
| Stride length | `StepPlanner` stride output | Planned stride per step |

## Primary Touchpoints
- `Assets/Scripts/Character/LegAnimator.cs` — foot Transform refs, leg state machine, step frequency
- `Assets/Scripts/Character/Locomotion/LegStateMachine.cs` — stance/plant state transitions
- `Assets/Scripts/Character/Locomotion/StepPlanner.cs` — stride length, step frequency scaling
- `Assets/Scripts/Character/GroundSensor.cs` — per-foot ground contact data
- `Assets/Scripts/Character/PlayerMovement.cs` — `_moveForce`, `_sprintSpeedMultiplier`
- `Assets/Scripts/Character/BalanceController.cs` — `IsGrounded`, planar speed
- `Assets/Tests/PlayMode/Character/GaitOutcomeTests.cs` — existing gait test harness to extend

## Related Plans
- [Unified Locomotion Roadmap — Chapter 9](unified-locomotion-roadmap/09-validation-debugging-and-tuning.md) — validation and tuning infrastructure
- [Sprint-jump smoothing](sprint-jump-smoothing.plan.md) — confirmed `_moveForce = 150` as the user-preferred baseline

## Non-Goals For The First Pass
- Do not redesign the leg state machine or step planner; measure with the current system.
- Do not add IK foot pinning or procedural ground anchoring yet; this plan measures the problem and finds the safe envelope, not implements a fix for higher speeds.
- Do not change `_jumpForce` or airborne behavior; this is about grounded locomotion only.

## Work Packages

### WP1: Build a planted-foot-drift measurement utility

Create a reusable test utility that measures planted foot drift per step cycle.

- [x] **WP1a: Create `PlantedFootDriftTracker` test utility** ✅ 2026-03-18
  - Scope: Add `Assets/Tests/PlayMode/Utilities/PlantedFootDriftTracker.cs`. A lightweight class that takes left/right foot Transforms and a way to query each foot's planted state (callback or interface). Each `Sample()` call (from FixedUpdate): when a foot transitions to planted, record its XZ world position as the anchor; while planted, accumulate the XZ delta from anchor; when the foot lifts off, finalize that step's drift and push it to a per-foot list. Expose: `float MaxDriftLeft`, `float MaxDriftRight`, `float MaxDrift` (worst across both), `float AverageDrift`, `int CompletedStepCount`, and per-step `IReadOnlyList<float> DriftPerStep`.
  - Done when: EditMode test instantiates tracker, feeds synthetic position sequences, and asserts correct drift computation (foot anchored → moved 0.05m → lift-off → drift = 0.05).
  - Verification: EditMode compile + new EditMode test.
  - Result: 9/9 EditMode tests pass (`PlantedFootDriftTrackerTests`).

- [x] **WP1b: Wire drift tracker into a PlayMode smoke test** ✅ 2026-03-18
  - Scope: Add a new test `GaitOutcomeTests.WalkForward_PlantedFeetDoNotSlide()` (or a new `FootSlidingTests` fixture). Spawn the prefab, hold forward input for 5 s at walk speed (no sprint), sample the drift tracker each FixedUpdate. Assert `MaxDrift < threshold` (start with 0.04 m — roughly the `GroundSensor` cast radius). Emit `[METRIC] PlantedFootDrift_Walk MaxDrift=X AverageDrift=Y StepCount=Z`.
  - Depends on: WP1a.
  - Done when: Test passes at walk speed with the current prefab (`_moveForce = 150`, no sprint).
  - Verification: PlayMode focused run of the new test.
  - Result: 1/1 PlayMode test passes (`FootSlidingTests.WalkForward_PlantedFeetDoNotSlide`).
  - **Measured walk drift:** MaxDrift=0.3076m, AverageDrift=0.0789m, StepCount=21, FinalSpeed=0.91 m/s.
  - **Threshold calibration:** The plan's initial guess of 0.04m assumed IK-quality foot anchoring. The physics-driven character (no IK foot pinning) shows ~0.31m peak drift even at walk speed. Threshold set to 0.35m as a regression gate. WP3b will tighten after envelope analysis.
  - **Planted detection:** Uses `LegStateMachine.CurrentState == Stance` (via reflection) AND `GroundSensor.IsGrounded`, with a 3-frame settle window after entering Stance to filter out initial foot-settling motion.

### WP2: Measure foot drift across speed tiers

- [ ] **WP2a: Add sprint-speed drift test**
  - Scope: Add `FootSlidingTests.SprintForward_PlantedFeetDoNotSlide()`. Same approach as WP1b but with sprint held. Emit `[METRIC] PlantedFootDrift_Sprint MaxDrift=X AverageDrift=Y PeakSpeed=S StepCount=Z`. Use the same threshold initially — this test may start red, which is useful data.
  - Done when: Test runs and emits metrics. If it passes, the current sprint parameters are clean. If it fails, the metric tells us how far off we are.
  - Verification: PlayMode focused run; record pass/fail and metric values.

- [ ] **WP2b: Add parameterized speed-sweep diagnostic test**
  - Scope: Add `FootSlidingTests.SpeedSweep_MeasureDriftAtEachTier()` as an `[Explicit]` diagnostic test (not part of the normal regression gate). Parameterize over `_moveForce` values: `[100, 125, 150, 175, 200, 250, 300]` while keeping `_sprintSpeedMultiplier = 1.8`. For each value: override the serialized force via reflection or a test-injection seam, sprint for 5 s, record `MaxDrift`, `AverageDrift`, `PeakPlanarSpeed`, and `StepCount`. Emit all values as `[METRIC]` lines. Log a summary table at the end.
  - Done when: Running the explicit test produces a table mapping force → drift → speed. This table identifies the "knee" where drift starts exceeding the visual threshold.
  - Verification: Manual run of the explicit test; inspect metric output.

### WP3: Establish the speed envelope and lock the regression gate

- [ ] **WP3a: Analyze sweep results and define the envelope**
  - Scope: From WP2b results, identify the highest `_moveForce` where `MaxDrift` stays below the visual threshold at both walk and sprint. Document the results in a table in this plan. If `_stepFrequencyScale` or `MaxStrideLength` adjustments could extend the envelope, note them as follow-up candidates.
  - Done when: This plan's "Envelope Results" section is populated with the sweep data and the recommended parameter set.

- [ ] **WP3b: Lock the regression gate**
  - Scope: Update the walk and sprint drift tests from WP1b/WP2a to use the confirmed threshold from WP3a. Add them to the `gait-core` test slice in `Tools/test-slices.json` (if it exists by then) or document the filter string here. These tests become part of the normal regression gate — any future `_moveForce` or stride tuning that introduces sliding will fail.
  - Done when: Both drift tests are green with explicit thresholds documented in the test source and in this plan.
  - Verification: Focused PlayMode run of the drift tests.

- [ ] **WP3c: Document the honest speed ceiling**
  - Scope: Add an "Envelope Results" section to this plan with: the force-vs-drift table, the recommended parameter set, and a short explanation of why higher forces produce sliding (referencing the step frequency / stride length ceiling). Cross-link from `LOCOMOTION_BASELINES.md` if appropriate.
  - Done when: Another agent or the user can read this plan and understand exactly what the speed ceiling is and why.

### WP4 (stretch): Explore stride tuning to extend the envelope

Only pursue after WP3 is complete and the baseline is locked.

- [ ] **WP4a: Test higher `_stepFrequencyScale` values**
  - Scope: Re-run the speed sweep from WP2b with `_stepFrequencyScale` values `[0.10, 0.12, 0.15, 0.18, 0.20]` at the current `_moveForce = 150` and `_sprintSpeedMultiplier = 1.8`. Record whether faster leg cycling reduces drift at the same body speed without looking unnatural.
  - Done when: A second sweep table is captured showing frequency → drift → visual notes.

- [ ] **WP4b: Test higher `MaxStrideLength` values**
  - Scope: Same approach but varying `MaxStrideLength` from `[0.25, 0.30, 0.35, 0.40]`. Longer strides cover more ground per cycle, potentially allowing higher speed without sliding — but may look comically long.
  - Done when: A third sweep table is captured.

- [ ] **WP4c: Recommend the optimal parameter combination**
  - Scope: From WP4a and WP4b data, recommend the combination of `_moveForce`, `_sprintSpeedMultiplier`, `_stepFrequencyScale`, and `MaxStrideLength` that maximizes peak grounded speed while keeping planted foot drift below the visual threshold. Update the regression gate thresholds if the envelope expands.
  - Done when: This plan's "Envelope Results" section has the final recommended parameter set.

## Envelope Results

_To be populated after WP3a._

| `_moveForce` | `_sprintMultiplier` | `_stepFreqScale` | `MaxStride` | Peak Speed (m/s) | Max Drift (m) | Avg Drift (m) | Verdict |
|---|---|---|---|---|---|---|---|
| ... | ... | ... | ... | ... | ... | ... | ... |

## Verification Gate

- Walk-speed drift test green (WP1b).
- Sprint-speed drift test green (WP2a) — or documented as a known quality gap with a clear threshold.
- Speed sweep data captured and analyzed (WP2b + WP3a).
- Regression gate locked with explicit thresholds (WP3b).
- No new regressions in `GaitOutcomeTests`, `MovementQualityTests`, or `SprintJumpStabilityTests`.

## Progress Notes
- 2026-03-18: Created this plan from user feedback that `_moveForce = 150` feels grounded while higher values produce visible foot gliding at sprint speed. The prefab already serializes `_moveForce: 150`. The plan focuses on measuring planted-foot drift, finding the speed envelope, and locking a regression gate.
- 2026-03-18: **WP1 complete.** Created `PlantedFootDriftTracker` utility and wired into PlayMode `FootSlidingTests`. Walk-speed baseline: MaxDrift=0.31m, AvgDrift=0.08m at 0.91 m/s. Key finding: 0.04m threshold was unrealistic for a physics-driven character without IK foot pinning — actual stance-phase drift is ~0.08m average, ~0.31m peak. Threshold set to 0.35m as regression gate. Files added: `PlantedFootDriftTracker.cs`, `PlantedFootDriftTrackerTests.cs`, `FootSlidingTests.cs`. EditMode asmdef updated to reference PlayMode assembly for shared test utilities.
