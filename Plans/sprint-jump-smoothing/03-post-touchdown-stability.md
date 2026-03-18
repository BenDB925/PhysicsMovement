# Post-Touchdown Stability Follow-Up

## Goal
Translate the now-green sprint-jump regression slice into better live-play feel without reopening threshold tuning. Keep the touchdown seam stable while making the first `~0.5 s` after landing feel more planted and less likely to tip into `Fallen`.

## Current status
- State: Blocked
- Current next step: Resume this touchdown-feel slice only after `Plans/sprint-jump-smoothing/bugs/fallen-getting-up-state-loop.md` identifies the owner of the later `Fallen`/`GettingUp` churn on the current workspace state.
- Blockers: `SprintJumpStabilityTests` is back to `3 passed, 2 failed, 5 total` on the user-approved workspace state, and the dominant failure now occurs later than the first touchdown half-second.

## Decisions
- 2026-03-18: Treat the remaining complaint as a touchdown feel/stability slice, not a test-threshold problem. Do not loosen fallen thresholds or widen jump timing first.
- 2026-03-18: Track the observed `Fallen`/`GettingUp` oscillation separately in a bug sheet so the landing-feel task stays thin and executable.
- 2026-03-18: Treat the dirty prefab jump-force change (`Assets/Prefabs/PlayerRagdoll_Skinned.prefab: _jumpForce = 175`) as intentional while debugging this follow-up.
- 2026-03-18: Stop after three failed WP3 support-boost hypotheses and record the blocker instead of leaving partial runtime/test changes in the workspace.

## Artifacts
- `Plans/sprint-jump-smoothing.plan.md`: parent summary, focused gate status, and work-package ordering.
- `Plans/sprint-jump-smoothing/bugs/fallen-getting-up-state-loop.md`: active bug sheet for the player-state churn reported after landing recovery attempts.
- `TestResults/latest-summary.md`: latest focused rerun (`3 passed, 2 failed, 5 total`) on the current workspace state.
- `TestResults/PlayMode.xml`: authoritative NUnit artifact for that focused rerun.
- `Logs/test_playmode_20260318_163433.log`: fresh PlayMode log showing the current `Fallen -> GettingUp -> Fallen` re-entry loop.
- `Logs/test_playmode_20260318_151251.log`: historical focused green slice backing the last accepted Work Package 2 baseline.
- `Logs/fall-pose-log.ndjson`: live-play pose capture of the late post-touchdown collapse that motivated the touchdown seam work.

## Open follow-up
1. Work the linked state-loop bug first, because the later handoff failure now dominates the focused rerun.
2. Once that owner is clear, return to a touchdown-only support or upright-strength bias that lasts about `0.5 s` after landing and eases out smoothly.
3. Re-run the repeated sprint-jump slice and capture `FramesUntilJumpReadyAfterLanding` or equivalent if jump 2 still depends on a narrow readiness window.
4. Run the nearby sprint, jump, and recovery confidence slice only after the landing feel is materially better and the later state churn is controlled.

## Progress notes
- 2026-03-18: User feedback after the green focused slice says the direct faceplant regression is fixed, but the first half-second after touchdown still feels too wobbly in live play.
- 2026-03-18: The next slice is therefore a feel-oriented touchdown support boost with eased fade-out, not another touchdown-seam or threshold pass.
- 2026-03-18: The reported `GettingUp -> Fallen -> GettingUp` churn is tracked separately in the linked bug sheet so the landing-feel work can stay focused.
- 2026-03-18: Tried three WP3 hypotheses on the user-approved `jumpForce = 175` workspace state: a touchdown-window-coupled support boost, a smaller grounded-only variant, and a separate `0.5 s` support timer. None kept the focused sprint-jump gate green, so the runtime/test experiment was backed out.
- 2026-03-18: Fresh focused verification after that rollback is `3 passed, 2 failed, 5 total` in `PhysicsDrivenMovement.Tests.PlayMode.SprintJumpStabilityTests` (`TestResults/latest-summary.md`, `TestResults/PlayMode.xml`, `Logs/test_playmode_20260318_163433.log`). The single-jump failure now enters `Fallen` even with `PeakTiltAfterJump1=38.9`, and the repeated-jump failure still reaches `PeakTiltAfterJump2=93.6` with `EverFallen=True`, so the later state-handoff bug is the active blocker.