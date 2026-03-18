# Fallen/GettingUp State Loop After Landing

## Symptom
During sprint-jump recovery, the player-state readout can churn `GettingUp -> Fallen -> GettingUp -> Fallen` while the character is trying to stand after landing, even without a new external hit.

## Reproduction or failing test
- Manual live-play report from 2026-03-18 after the focused sprint-jump slice had already gone green.
- Focused automated repro now exists on the current workspace state: `PhysicsDrivenMovement.Tests.PlayMode.SprintJumpStabilityTests` is `3 passed, 2 failed, 5 total` in `TestResults/latest-summary.md` / `TestResults/PlayMode.xml`.
- `SprintJump_SingleJump_DoesNotFaceplant` currently fails because the character enters `Fallen` after landing #1 even though the recorded `PeakTiltAfterJump1=38.9` and `RecoveryFrames1=192`.
- `SprintJump_TwoConsecutiveJumps_DoesNotFaceplant` currently fails with `PeakTiltAfterJump1=43.8`, `PeakTiltAfterJump2=93.6`, `RecoveryFrames1=30`, `RecoveryFrames2=20`, and `EverFallen=True`.
- Related prior telemetry from the sprint-jump investigation already showed `RequestRejected(state_not_jumpable:GettingUp)` while grounded and fallen during jump 2, which suggests the state handoff is still fragile after landing instability.

## Active hypotheses
- [ ] `CharacterState` is re-entering `Fallen` from the landing collapse while `GettingUp` has already started, without enough debounce or ownership between collapse detection and stand-up.
- [ ] `LocomotionDirector` recovery-active collapse deferral expires mid-stand-up and hands control back to raw fall detection too early.
- [ ] Stand-up success or failure, jump-readiness gating, and grounded-state sampling are reading mixed signals during the first post-landing recovery window, causing alternating state transitions instead of one durable handoff.

## Experiments
- 2026-03-18: User live-play report after the focused green slice -> visible player-state churn still occurs in some recoveries; no automated reproduction captured yet.
- 2026-03-18: WP3 hypothesis 1 -> add a touchdown-window-coupled landing support boost. Result: direct landing-support seam improved, but focused sprint-jump acceptance still failed later in the sequence.
- 2026-03-18: WP3 hypothesis 2 -> reduce the boost and apply it only while grounded. Result: direct seam still moved, but sprint-jump acceptance stayed red and the later collapse remained.
- 2026-03-18: WP3 hypothesis 3 -> decouple the landing support boost into its own `0.5 s` timer. Result: the first touchdown half-second no longer looked like the dominant failing window, but the later `Fallen -> GettingUp -> Fallen` churn still reproduced.
- 2026-03-18: Backed out the WP3 runtime/test experiment and reran `SprintJumpStabilityTests` to confirm the blocker still reproduces on the clean current workspace state.

## Evidence
- `Plans/sprint-jump-smoothing.plan.md`: earlier telemetry captured jump 2 rejected as `state_not_jumpable:GettingUp)` while grounded and fallen.
- `Logs/fall-pose-log.ndjson`: live-play landing trace already shows a post-touchdown tip into `Fallen` after the landing seam initially looks stable.
- `TestResults/latest-summary.md`: latest focused rerun is `3 passed, 2 failed, 5 total` for `PhysicsDrivenMovement.Tests.PlayMode.SprintJumpStabilityTests`.
- `TestResults/PlayMode.xml`: fresh metrics show the single-jump path can enter `Fallen` even with `PeakTiltAfterJump1=38.9`, while the repeated-jump path still reaches `PeakTiltAfterJump2=93.6` and `EverFallen=True`.
- `Logs/test_playmode_20260318_163433.log`: repeated re-entry sequence captured at `Moving -> Fallen` (`7.51 s`), `Fallen -> GettingUp` (`10.32 s`), then `GettingUp -> Fallen` at `11.13 s`, `14.04 s`, `26.23 s`, `29.14 s`, `41.43 s`, and `44.10 s`.

## Failed hypotheses
- A landing-only support boost tied directly to the existing touchdown window is sufficient to close the current sprint-jump failures.
- A weaker grounded-only version of that touchdown boost fixes the issue without exposing a deeper owner.
- A separate `0.5 s` touchdown support timer fixes the remaining failures by itself.

## Current conclusion
The direct touchdown seam is fixed, but the current focused rerun still falls over later in the sequence and repeatedly re-enters `Fallen` while `GettingUp` is already active. This is now a reproducible state-handoff bug, not just a landing-feel complaint, and touchdown-only boost tuning should stay paused until this owner is clear.

## Next step
1. Add targeted state-transition telemetry around `CharacterState`, `LocomotionDirector.IsRecoveryActive`, collapse-deferral expiry, and stand-up start/fail/completion for the first post-landing second.
2. Reproduce the churn in a focused PlayMode or manual capture and pin whether the re-entry is caused by collapse deferral, stand-up failure, or jump-readiness gating.
3. Only after the owner is clear, decide whether the fix belongs in `CharacterState`, `LocomotionDirector`, or the stand-up sequencing path, then return to the paused touchdown-feel follow-up if it is still needed.