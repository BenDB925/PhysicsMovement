# Sprint-Jump Smoothing Plan

## Status
- State: Active, Work Package 2 attempted but still red
- Acceptance target: Make repeated sprint -> jump -> land -> continue sprinting feel smooth enough for frequent player use, with `SprintJump_SingleJump_DoesNotFaceplant` and `SprintJump_TwoConsecutiveJumps_DoesNotFaceplant` green and no new regressions in the nearby sprint, jump, and recovery suites.
- Current next step: Debug why the director-owned touchdown stabilization window still fails to attenuate the first grounded landing frames strongly enough after the recent jump-state bridge changes. The next pass should compare authored `CharacterState = Airborne` against raw `IsGrounded` transitions in the fresh combined slice and then retune or re-seam the touchdown overlay rather than loosening thresholds.
- Active blockers: None.

## Quick Resume
- Latest focused verification on 2026-03-18 broadened to `JumpTests`, `LocomotionDirectorTests`, and `SprintJumpStabilityTests`: `36 passed, 3 failed, 39 total`.
- The remaining reds are now narrower but still unresolved: `FixedUpdate_WhenGroundedReturnsWhileStateIsStillAirborne_KeepsTouchdownLeanAttenuatedUntilLandingStabilizes`, `SprintJump_SingleJump_DoesNotFaceplant`, and `SprintJump_TwoConsecutiveJumps_DoesNotFaceplant`.
- The jump-path regressions that appeared during the investigation are materially better: late wind-up ground loss can still commit launch, recent jump launch can bridge `CharacterState` into `Airborne`, and low-priority `Slip`-style recovery no longer lingers indefinitely into the first jump. The unresolved problem is still the landing/posture budget after jump 1, not input gating.
- Latest sprint-jump outcome metrics are improved from the original baseline but still red: jump 1 peak torso tilt is `79.1` (target `< 45`), and jump 2 still does not consistently produce the required airborne excursion in the dedicated two-jump outcome test.

## Verified Artifacts
- `Plans/sprint-jump-stability-tests.md`: completed baseline test plan and fresh failure metrics.
- `TestResults/latest-summary.md`: latest combined digest (`36 passed, 3 failed, 39 total`) for `PhysicsDrivenMovement.Tests.PlayMode.JumpTests;PhysicsDrivenMovement.Tests.PlayMode.LocomotionDirectorTests;PhysicsDrivenMovement.Tests.PlayMode.SprintJumpStabilityTests`.
- `TestResults/PlayMode.xml`: authoritative NUnit artifact for the latest combined PlayMode run.
- `Logs/test_playmode_20260318_103717.log`: fresh PlayMode log backing the latest touchdown-window and sprint-jump outcome failures.
- `Assets/Tests/PlayMode/Character/SprintJumpStabilityTests.cs`: current regression harness and shared diagnostics helper.

## Primary Touchpoints
- `Assets/Scripts/Character/BalanceController.cs`: landing absorption, composite pelvis tilt, COM stabilization, upright damping, and surrender thresholds.
- `Assets/Scripts/Character/Locomotion/LocomotionDirector.cs`: sprint lean budget, recovery classification, recovery profiles, and telemetry.
- `Assets/Scripts/Character/PlayerMovement.cs`: jump gating, wind-up, launch, and wind-up abort path.
- `Assets/Scripts/Character/LegAnimator.cs`: airborne spring scaling and landing knee-bend absorption.
- `Assets/Tests/PlayMode/Character/SprintJumpStabilityTests.cs`: failing regression gate and best place to add landing-window diagnostics.

## Diagnosis Summary

### 1. Touchdown posture is still forward-biased, but the failing window is dominated by sprint lean plus recovery state rather than landing-absorb blend
Current touchdown posture is not just "land and recenter." `BalanceController` stacks:
- sprint lean from `LocomotionDirector` (`_maxSprintLeanDegrees = 8`)
- landing absorption lean (`_landingAbsorbLeanDeg = 3`)
- transient accel/decel lean
- local pelvis tilt from speed delta

That total is clamped to `_totalPelvisTiltCapDeg = 15`, but the new landing-window telemetry changes the priority inside that stack: the failing 0.5 s window never raised `LandingAbsorbBlend` above `0`, while `DesiredLeanDegrees` stayed positive and both recovery blends were pegged at `1`. The first fix should therefore target touchdown sprint lean and recovery-time support behavior before changing `_landingAbsorbLeanDeg` in isolation.

### 2. The current second jump failure is a state rejection before wind-up, not a mid-wind-up abort
`SprintJumpStabilityTests` still waits for a grounded readiness window before pulsing jump 2, but the new jump telemetry shows that the request never enters wind-up on the failing run. `PlayerMovement.TryApplyJump()` rejects the second attempt with `state_not_jumpable:GettingUp` while `IsGrounded = true` and `IsFallen = true`, which means the system is still climbing out of the first landing collapse instead of being truly jump-ready. If jump 2 remains red after the landing smoothing work, inspect the `CharacterState` exit path from `Fallen`/`GettingUp` before widening test timing.

### 3. Recovery logic is reacting to the landing collapse and may be amplifying overshoot
`LocomotionDirector` classifies `Slip`, `NearFall`, and `Stumble` from support risk. While recovery is active, `BalanceController` reduces upright damping through `RecoveryKdBlend`, and COM damping can drop through `_snapRecoveryComDampScale = 0.4`. That tuning helps sharp-turn responsiveness, but it is suspicious during a sprint-jump landing where the desired outcome is quick recentering, not freer body swing.

### 4. Telemetry points to a real control problem, not a bad threshold
The latest telemetry run recorded two recovery windows and ended with `angle_above_ceiling` plus `recovery_surrendered`. This is useful because it means the system is already telling us the failure is severe enough to exhaust recovery. The first fix should target landing motion and support behavior, not loosen `FaceplantAngleThreshold`, `IsFallen`, or the sprint-jump test deadlines.

## Attempt Summary

### 1. Director-owned touchdown stabilization window
Tried: added a touchdown subphase in `LocomotionDirector` that attenuates sprint-derived lean for a short grounded landing window, scales down recovery-time damping reduction, and suppresses the recovery-added stabilization boost while touchdown stabilization is active.

Result: this improved some intermediate traces and removed several side regressions, but it is not yet strong or correctly timed enough to satisfy the direct touchdown seam test or the sprint-jump acceptance tests.

### 2. Recovery lifecycle tightening
Tried: changed `RecoveryState.Enter()` so re-entering the same recovery situation only refreshes the window when the new sample is actually stronger, instead of blindly resetting the timer every frame.

Result: this removed a long-running `Slip` recovery that had been surviving into jump 1 and polluting the landing with a full recovery profile. The improvement was real, but not enough to finish Work Package 2.

### 3. Intentional jump recovery suppression
Tried: in `LocomotionDirector`, suppressed low-priority `Slip`, `Reversal`, and `HardTurn` recovery during intentional jump wind-up/launch while preserving critical `NearFall`/`Stumble` handling.

Result: this stopped some jump-owned preload motion from being reclassified as generic recovery, but the landing still collapses later in the sequence.

### 4. Jump launch robustness and state bridging
Tried: in `PlayerMovement`, allowed an accepted jump wind-up to still commit launch after late transient ground loss; in `CharacterState`, added a short recent-launch airborne bridge keyed off upward hips velocity.

Result: this fixed earlier jump-path false negatives and removed a misleading class of `Airborne` failures, but the remaining red still comes from landing posture and support behavior rather than launch gating.

### 5. Test and contract coverage added during the investigation
Tried: added direct contract coverage for `BodySupportCommand.ComDampingRecoveryBlend` and the new `RecoveryState.Enter()` refresh rule, direct PlayMode tests for touchdown stabilization and jump-recovery ownership in `LocomotionDirectorTests`, a sprinting jump airborne guard in `JumpTests`, and stronger airborne bookkeeping in `SprintJumpStabilityTests`.

Result: the direct seams are better protected now, and the remaining failures point back at the real runtime landing problem instead of earlier harness or bookkeeping gaps.

## Non-Goals For The First Pass
- Do not loosen the sprint-jump test thresholds first.
- Do not widen the test's second-jump readiness window as the primary fix.
- Do not start by raising fallen or surrender thresholds unless landing motion is already materially smoother and only a transient false positive remains.

## Work Packages

1. [x] Instrument the landing window and jump-2 abort path.
   - Add short-lived diagnostic output for the first `0.5 s` after landing: landing frame, peak-tilt frame, `DesiredLeanDegrees`, landing-absorb blend, total pelvis tilt, `RecoveryBlend`, `RecoveryKdBlend`, `IsGrounded`, and `CharacterState`.
   - Add jump-2 path metrics: jump accepted, wind-up entered, wind-up aborted reason, launch fired.
   - Result: `SprintJump_TelemetryCapture_LogsRecoveryEventsAroundLanding()` now records structured landing-window and jump-attempt telemetry. The latest bad run shows landing 1 entering the window already under `RecoveryBlend = 1` / `RecoveryKdBlend = 1`, never engaging `LandingAbsorbBlend`, peaking at sample `50/51` after surrender begins, and blocking jump 2 with `RequestRejected(state_not_jumpable:GettingUp)` before wind-up.

2. [ ] Rework the landing posture budget before touching thresholds.
   - Attempted on 2026-03-18: added a director-owned touchdown stabilization window plus touchdown-time scaling for sprint lean, COM damping reduction, and recovery-added stabilization.
   - Attempted on 2026-03-18: re-seamed touchdown arming so authored jump-state `Airborne` can open the window even when raw `IsGrounded` lags.
   - Current result: the direct touchdown seam test is still red, and the main sprint-jump outcome tests remain red (`PeakTiltAfterJump1 = 79.1`, `Airborne2 = false`), so Work Package 2 is still incomplete.
   - First candidate: attenuate sprint-derived `DesiredLeanDegrees` for a short touchdown window (`0.10-0.20 s`) and ramp it back in smoothly after the landing stabilizes.
   - Second candidate: if sprint lean attenuation is not enough, bypass or soften recovery-time damping reduction (`RecoveryKdBlend` / COM damping reduction) during the same grounded touchdown window.
   - Only revisit `BalanceController._landingAbsorbLeanDeg` after telemetry shows `LandingAbsorbBlend` actually participates in the failing landing window.
   - Keep the landing knee bend and height offset unless the new metrics show they also destabilize contact.
   - Done when jump 1 peak tilt drops materially and the landing still reads as intentional rather than rigid.

3. [ ] Add a landing-specific support profile instead of relying only on turn-recovery tuning.
   - Use the existing `BalanceController` ramp infrastructure or a small landing-specific multiplier to temporarily favor upright recentering and stable COM damping after `Airborne -> Standing/Moving`.
   - Verify whether recovery-time damping reduction should be bypassed during the touchdown window.
   - Done when the character can absorb the landing, recentre, and keep sprinting without the harsh forward dive.

4. [ ] Validate repeated action robustness.
   - Re-run the focused sprint-jump fixture and confirm jump 2 reaches `Airborne` and lands without entering `Fallen`.
   - If jump 2 still fails, inspect `PlayerMovement.TickJumpWindUp()` aborts and `CharacterState` transitions before changing test timing.
   - Consider adding a durable diagnostic metric such as `FramesUntilJumpReadyAfterLanding` if the failure still hides between state transitions.

5. [ ] Run the nearby regression gate before calling the motion stable.
   - Focused required gate: `SprintJumpStabilityTests`, `JumpTests`, `SprintBalanceOutcomeTests`, `SprintLeanOutcomeTests`.
   - Nearby confidence slice: `HardSnapRecoveryTests`, `SpinRecoveryTests`, and `MovementQualityTests`.
   - Treat the long-lived `MovementQualityTests` reds as baseline, but stop if the sprint-jump fix creates any new red or obvious metric regression in the other suites.

## Recommended Implementation Order
1. Instrument peak-tilt and jump-2 abort timing.
2. Attenuate sprint lean during the immediate touchdown window.
3. Soften or bypass recovery-time damping reduction during the same grounded touchdown window if needed.
4. Revisit landing forward lean only if later telemetry shows `LandingAbsorbBlend` actually contributing during the failing landing.
5. Add landing-specific support and damping only if the posture-budget fix is not enough.

## Verification Gate
- Keep `SprintJump_SingleJump_DoesNotFaceplant` and `SprintJump_TwoConsecutiveJumps_DoesNotFaceplant` as the primary success criteria.
- Preserve the softer recovery test as a secondary signal: the landing should be both stable and smooth, not merely eventually upright.
- After the motion is materially better, refresh the sprint-jump baseline notes in `Plans/sprint-jump-stability-tests.md` and add the new artifact paths here.
- If behavior expectations change meaningfully, refresh `LOCOMOTION_BASELINES.md` after the fix is verified.

## Progress Notes
- 2026-03-18: Created this handoff plan from the fresh sprint-jump baseline (`3 passed, 2 failed, 5 total`). Current diagnosis favors landing posture stacking and post-landing wind-up aborts over test timing or simple threshold problems.
- 2026-03-18: Completed Work Package 1. Added `LandingWindowTelemetrySample` / `JumpTelemetryEvent` runtime diagnostics plus landing-window logging in `SprintJumpStabilityTests`. Focused verification remains `3 passed, 2 failed, 5 total` (`TestResults/latest-summary.md`, `TestResults/PlayMode.xml`, `Logs/test_playmode_20260318_082451.log`), but the new data narrows the fix: landing 1 starts its grounded window with `DesiredLeanDegrees = 6.44`, `TotalPelvisTilt = 7.39`, `RecoveryBlend = 1`, `RecoveryKdBlend = 1`, `LandingAbsorbBlend = 0`, then peaks at sample `50/51` with `UprightAngle = 84.56` after surrender has already started; jump 2 never reaches wind-up because the request is rejected as `state_not_jumpable:GettingUp` while grounded and fallen.
- 2026-03-18: Attempted Work Package 2 across the landing, recovery, jump, and state seams. Runtime changes now include a director-owned touchdown stabilization window, touchdown-time recovery damping/stabilization scaling, same-situation recovery refresh tightening, low-priority recovery suppression during intentional jump sequences, late-ground-loss launch commit, and a recent-launch airborne bridge from `PlayerMovement` into `CharacterState`. Test coverage was widened with new contract tests in `LocomotionContractsTests`, targeted PlayMode guards in `JumpTests` and `LocomotionDirectorTests`, and stronger airborne bookkeeping in `SprintJumpStabilityTests`.
- 2026-03-18: Latest combined verification is still red but materially narrower: `36 passed, 3 failed, 39 total` in `PhysicsDrivenMovement.Tests.PlayMode.JumpTests;PhysicsDrivenMovement.Tests.PlayMode.LocomotionDirectorTests;PhysicsDrivenMovement.Tests.PlayMode.SprintJumpStabilityTests` (`TestResults/latest-summary.md`, `TestResults/PlayMode.xml`, `Logs/test_playmode_20260318_103717.log`). Remaining failures are the direct touchdown seam test plus the two sprint-jump acceptance tests. Current evidence says jump launch/airborne classification is no longer the dominant blocker; the next pass should focus on why touchdown stabilization still does not reclaim enough posture/support budget on the first landing after jump 1.