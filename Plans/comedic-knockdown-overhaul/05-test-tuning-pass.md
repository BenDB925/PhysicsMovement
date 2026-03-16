# Ch5: Test & Tuning Pass

## Goal
Update existing tests that assume instant recovery, add new outcome-based tests for the full knockdown lifecycle, and perform a tuning pass so the numbers feel right in the arena scene.

## Current status
- State: Not started
- Current next step: Audit existing tests for knockdown assumptions
- Blockers: Depends on Ch1–Ch4 implementation

## Current behavior
- `HardSnapRecoveryTests` assert recovery within 200 frames (2 s) and max 150 consecutive fallen frames — these will break with the new 2+ s floor dwell
- `BalanceControllerTests` validate the 65°/55° fallen thresholds — these should still pass (surrender is separate and higher)
- No tests for external impact knockdown, floor dwell duration, or stand-up phases

## Design

### Existing test updates

| Test file | What changes |
|-----------|-------------|
| `HardSnapRecoveryTests.cs` | Relax "max consecutive fallen frames" to accommodate floor dwell. Add a separate assertion that stand-up *eventually* completes. Keep the "recovers from moderate tilt without falling" tests unchanged (they validate sure-footedness). |
| `BalanceControllerTests.cs` | No changes expected — the 65°/55° IsFallen thresholds are untouched. |
| `LocomotionDirectorTests.cs` | Any tests asserting "character never enters Fallen during recovery" may need severity-aware thresholds or scenario narrowing. |

### New tests

#### Surrender tests (Ch1)
| Test | Description |
|------|-------------|
| `Surrender_WhenAngleExceeds85Degrees_StopsUprightTorque` | Tilt hips to 85°+, verify upright torque drops to 0 within 2 frames |
| `Surrender_WhenRecoveryTimesOut_TriggersAfterTimeout` | Put character in Stumble recovery, hold angle at 60° for 1+ s, verify surrender fires |
| `Surrender_WhenAngleBelow70_DoesNotFire` | Moderate tilt + recovery → verify surrender never fires |
| `Surrender_JointSpringsRampDown_WithinExpectedTime` | After surrender, verify joint springs reach 20–30% within 0.15 s |

#### External impact tests (Ch2)
| Test | Description |
|------|-------------|
| `Impact_AboveKnockdownThreshold_TriggersKnockdown` | Launch a rigidbody at character at high velocity, verify Fallen state within a few frames |
| `Impact_BelowKnockdownAboveStagger_CausesStaggerNotKnockdown` | Medium-speed impact → verify character staggers but doesn't enter Fallen |
| `Impact_SelfCollision_DoesNotTriggerKnockdown` | Verify arm-to-leg contacts during normal gait never trigger impact knockdown |
| `Impact_DuringGettingUp_RetriggersKnockdown` | Hit character during stand-up → verify re-entry to Fallen |
| `Impact_DuringCooldown_IsIgnored` | Two rapid impacts → verify second is ignored within cooldown window |

#### Floor dwell tests (Ch3)
| Test | Description |
|------|-------------|
| `FloorDwell_LightSeverity_ShorterDuration` | Trigger low-severity knockdown, verify dwell ≈ 1.5–1.9 s |
| `FloorDwell_HeavySeverity_LongerDuration` | Trigger high-severity knockdown, verify dwell ≈ 2.7–3.0 s |
| `FloorDwell_InputIgnored_DuringDwell` | Apply move input during floor dwell, verify no movement response |
| `FloorDwell_ReHit_ResetsTimer_CappedAtMax` | Hit during dwell, verify timer resets but doesn't exceed cap |

#### Stand-up tests (Ch4)
| Test | Description |
|------|-------------|
| `StandUp_CompletesWithin3Seconds_FromFloor` | After floor dwell, verify standing within ~3 s |
| `StandUp_PhaseProgression_OrientThenPushThenTuckThenStand` | Verify phases advance in order |
| `StandUp_ArmPushFails_ReEntersKnockdown` | Weaken arm push force → verify fallen re-entry with short severity |
| `StandUp_ExternalImpactDuringPhase_FullReset` | Impact during arm push → verify full knockdown re-entry |
| `StandUp_After3Failures_ForcedStand` | Force 3 failures → verify safety net activates |
| `StandUp_BalanceRampsSmooth_NoPDSnap` | During Phase 3, verify upright torque ramps from 0 to 1 over ~0.4 s, not a step function |

### Test infrastructure
- Need a helper method to spawn a physics projectile aimed at the character (for impact tests)
- Need a helper to force the character to a specific tilt angle (for surrender tests) — may already exist in `BalanceControllerTests`
- Stand-up phase tests may need access to `ProceduralStandUp.CurrentPhase` — expose as a read-only property for test inspection

### Tuning pass
After tests are green, do a visual tuning pass in the arena scene:
1. Push character off a ledge → verify fall looks natural, floor dwell timing feels right
2. Spawn a fast-moving box into character → verify knockdown triggers, severity maps correctly
3. Watch 5+ stand-up sequences → verify arm push looks like a push-up, legs tuck naturally, no weird limb clipping
4. Try sprinting into a wall at full speed → verify collapse → surrender → floor dwell → stand-up flow
5. Adjust `_armPushForce`, `_legTuckAssistForce`, joint spring multipliers based on visual results
6. Verify that normal walking/turning/sprinting is unaffected (sure-footed default preserved)

### Baseline updates
Update `LOCOMOTION_BASELINES.md` with new expected behaviors:
- "Character MAY enter Fallen state during extreme tilts or external impacts"
- "Floor dwell duration: 1.5–3.0 s depending on severity"
- "Stand-up sequence: ~1.5–2.5 s"
- Remove any baselines that assume "character should never fall flat"

## Files to create / modify
| File | What changes |
|------|-------------|
| `HardSnapRecoveryTests.cs` | Relax fallen-frame thresholds |
| **New: `SurrenderTests.cs`** | Ch1 surrender trigger tests |
| **New: `ImpactKnockdownTests.cs`** | Ch2 external impact tests |
| **New: `FloorDwellTests.cs`** | Ch3 floor dwell timing tests |
| **New: `ProceduralStandUpTests.cs`** | Ch4 stand-up phase tests |
| `LOCOMOTION_BASELINES.md` | Updated baseline expectations |

## Acceptance criteria
- [ ] All existing tests pass (with updated thresholds where needed)
- [ ] All new tests pass
- [ ] Visual tuning pass completed — falls, dwell, and stand-up look natural and comedic
- [ ] Normal locomotion (walking, turning, sprinting) unaffected by changes
- [ ] `LOCOMOTION_BASELINES.md` updated

## Decisions
- (pending)

## Progress notes
- 2026-03-16: Chapter spec written
