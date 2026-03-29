# 11a Landing-Recovery Regression

## Symptom

The impact-yield slice improves hit response and passes its dedicated PlayMode regression, but the required slice gate still adds one extra landing-recovery failure on top of a pre-existing `master` baseline red.

Visible symptom in workflow terms: the exact 11a PlayMode gate remains blocked by `LandingRecovery_RecoveryTimeImproved`, which does not fail on `master`.

## Reproduction or failing test

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "H:\Work\PhysicsDrivenMovementDemo\Tools\Run-UnityTests.ps1" -ProjectPath "H:\Work\PhysicsDrivenMovementDemo" -Platform PlayMode -MaxAttemptsPerPlatform 1 -Unattended -TestFilter "JumpTests|AirborneSpringTests|SprintJumpStabilityTests|LandingRecoveryTests|ImpactKnockdownTests"
```

Best-known slice-state result:

- `37 passed, 2 failed, 39 total`
- Failures: `LandingRecovery_DampingDisabledWhenFactorIsOne`, `LandingRecovery_RecoveryTimeImproved`
- Log: `Logs/test_playmode_20260329_101454.log`

Master baseline result:

- `38 passed, 1 failed, 39 total`
- Failure: `LandingRecovery_DampingDisabledWhenFactorIsOne`
- Log: `Logs/test_playmode_20260329_100656.log`

## Active hypotheses

- [ ] Impact yield still activates during nominal sprint-jump setup before the jump-ready window, dropping local support scales just long enough to invalidate `WaitForJumpReady()`.
- [ ] The right next probe is targeted logging around the sprint-ramp and jump-ready window: current support scales, impact-yield trigger state, tilt-direction angular speed, and `CurrentJumpPhase` every few fixed frames.

## Experiments

- 2026-03-29: Added the documented `!_playerMovement.IsRecentJumpAirborne` guard. Result: gate improved from `35 passed / 4 failed` to `37 passed / 2 failed`, but the slice-specific `LandingRecovery_RecoveryTimeImproved` failure remained.
- 2026-03-29: Added an angular-speed spike requirement so the trigger no longer fired on any sustained high angular speed. Result: dedicated `ImpactYieldTests` stayed green, but `LandingRecovery_RecoveryTimeImproved` still failed.
- 2026-03-29: Switched the trigger signal to the same tilt-direction pitch/roll angular velocity used by surrender. Result: no improvement to the remaining slice-specific landing-recovery failure.
- 2026-03-29: Raised the threshold from 4 to 5 rad/s. Result: the broader landing-recovery gate regressed further (`3 failed`), so the slice was restored to the 4 rad/s best-known state.

## Evidence

- `Logs/test_playmode_20260329_101426.log`: focused `ImpactYieldTests` pass on the best-known slice state.
- `Logs/test_playmode_20260329_101454.log`: exact slice gate on the best-known blocked state (`37 passed, 2 failed`).
- `Logs/test_playmode_20260329_100656.log`: exact gate on `master` (`38 passed, 1 failed`).
- `Logs/test_playmode_20260329_101937.log`: rejected threshold-5 tuning attempt (`36 passed, 3 failed`).

## Failed hypotheses

- Jump-landing suppression alone would restore the landing-recovery suite to green. Rejected by the surviving `LandingRecovery_RecoveryTimeImproved` slice-only failure.
- Generic angular-speed spike filtering was sufficient to distinguish hits from normal locomotion. Rejected because the same slice-only landing-recovery failure persisted.
- A 5 rad/s threshold would clear the extra regression without harming the rest of the gate. Rejected because the broader gate got worse, not better.

## Current conclusion

The slice is blocked on a remaining false-positive or timing interaction between impact yield and the sprint-jump setup path used by `LandingRecovery_RecoveryTimeImproved`. The broad gate also contains a pre-existing `master` baseline red in `LandingRecovery_DampingDisabledWhenFactorIsOne`, but that is not the new slice regression.

## Next step

Add targeted logging or a narrower diagnostic PlayMode test around the `LandingRecoveryTests.WaitForJumpReady()` setup window to capture whether impact yield ever lowers the local support scales during ordinary sprint buildup. Do not keep retuning thresholds blindly; the three-hypothesis budget is spent.