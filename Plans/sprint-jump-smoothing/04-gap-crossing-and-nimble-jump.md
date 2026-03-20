# Gap-Crossing And Nimble Jump Follow-Up

## Goal
Make the jump feel a bit more nimble without losing the grounded look that the earlier sprint-jump work protected. Prove the new capability with one standing short-gap outcome test and one sprint-gap outcome test, add slight midair WASD correction, and keep the landing stable enough that the character still feels like he has little legs rather than superhero hang time.

## Current status
- State: In progress
- Current next step: Slice 3 — sprint reach tuning
- Blockers: None.

## Decisions
- 2026-03-20: Reuse the `Plans/sprint-jump-smoothing/` tree because this is a direct jump-feel follow-up, not a new unrelated locomotion track.
- 2026-03-20: Start with a prefab-backed PlayMode harness instead of changing Arena or Museum builders. There is no authored gap terrain scenario today, and the first acceptance target is the jump capability itself.
- 2026-03-20: Treat the user's requested "moderate" gain as roughly a `20-25%` effective increase in controllable horizontal travel, but get there by shaping launch and carry plus slight air control rather than just inflating `_jumpForce`.
- 2026-03-20: Split the work into smaller one-shot slices. Standing reach, sprint reach, air control, yaw/facing, and airborne readability have different failure modes and should not land as one batch.
- 2026-03-20: Lock the exact standing and sprint gap widths in the tests before runtime tuning. "Moderate extra reach" is a design target, not an executable acceptance boundary by itself.
- 2026-03-20: Current intentional jumps have no translational airborne control. `PlayerMovement.ApplyMovementForces()` zeroes move force while `_recentJumpAirborne && !_balance.IsGrounded`; facing can still update, so the new slice should add a small capped correction window instead of full arcade steering.
- 2026-03-20: Because the user was unsure about timing assist, defer coyote time and jump-input buffering unless the gap tests show a real responsiveness problem after the core tuning lands.
- 2026-03-20: Treat airborne facing-rate changes as a contingency slice, not a default requirement. Only add them if bounded air translation makes yaw look too snappy.
- 2026-03-20: Standing reach must improve on its own. Do not count a sprint-only carry increase as success for the standing jump goal.
- 2026-03-20: Keep this bounded. Stop after gap tests, moderate jump tuning, slight air control, airborne arm polish, landing verification, and the nearby regression gate are green.
- 2026-03-20: **Height ceiling** — jump apex must not increase by more than 20% over the current baseline. Extra reach must come primarily from horizontal shaping, not a taller arc. Protects the "little legs, not superhero" feel.
- 2026-03-20: **Air-control authority** — midair WASD correction force capped at ≤ 15% of normal ground move force. Opposite-direction authority clamped harder (e.g. halved) versus same-direction or lateral trim. This is the testable boundary for Slice 5.
- 2026-03-20: **Standing horizontal launch mechanism** — add a small input-directed horizontal impulse at launch time (scaled by input magnitude, not sprint speed). Without this, standing reach can only grow by raising apex height, which violates the height ceiling.
- 2026-03-20: **Sprint carry mechanism** — use a velocity-preservation factor (reduce airborne drag / damping so more pre-jump horizontal speed survives the arc) rather than a separate sprint launch bonus. This keeps the grounded read because momentum is earned pre-jump.
- 2026-03-20: **Airborne movement suppression** lives in `PlayerMovement.ShouldSuppressLocomotion()` (L414-437), not directly in the `_recentJumpAirborne` flag. Agents modifying air control must change the suppression path, not just the flag.
- 2026-03-20: **Coyote time / input buffering revisit trigger** — if either gap test requires more than two tuning iterations to go green after the core runtime changes land, revisit whether a small input buffer or coyote window would solve the responsiveness problem more cleanly than further impulse tuning.
- 2026-03-20: **Landing gain retune policy** — if increased horizontal touchdown velocity causes `SprintJumpStabilityTests` regressions after Slice 3 or 4, retune `_jumpLandingGainBoost` and `_jumpPostLandingGraceDuration` in the same slice rather than splitting a new slice, since the root cause is the same arc change.

## Artifacts
- `Assets/Scripts/Character/PlayerMovement.cs`: owns jump launch (`FireJumpLaunch` L695-730), airborne-movement suppression (`ShouldSuppressLocomotion` L414-437), and the safest place to add bounded air control. The new horizontal impulse and velocity-preservation factor also land here.
- `Assets/Scripts/Character/ArmAnimator.cs`: owns jump wind-up, launch, and airborne arm pose blending.
- `Assets/Scripts/Character/BalanceController.cs`: owns landing stability and jump-recovery posture support that must stay green while reach increases.
- `Assets/Scripts/Character/CharacterState.cs`: owns airborne classification and landing handoff, which remain nearby risk when jump carry or in-air control changes.
- `Assets/Tests/PlayMode/Character/JumpTests.cs`: existing jump lifecycle coverage for wind-up, launch, airborne, and landing.
- `Assets/Tests/PlayMode/Character/SprintJumpStabilityTests.cs`: existing regression gate for repeated jump landings and touchdown stability.
- `Assets/Tests/PlayMode/Character/AirborneSpringTests.cs`: nearby landing and airborne spring regression surface to keep honest if the jump arc changes.
- `Assets/Tests/PlayMode/Character/ArmAnimatorPlayModeTests.cs`: existing airborne arm-blend coverage to reuse if airborne readability tuning expands.
- `Assets/Tests/PlayMode/Utilities/PlayerPrefabTestRig.cs`: focused harness surface for a measured gap test without scene-authoring churn.

## Proposed slices
1. Gap harness and red tests
   - Add a focused PlayMode fixture `JumpGapOutcomeTests` using `PlayerPrefabTestRig` and two measured platforms (launch platform + far platform separated by the target gap).
   - Define reusable helpers: far-edge progress measurement, touchdown-platform ownership check, and `Fallen`-window duration assertion.
   - Lock the exact standing and sprint target gap widths as named constants before any runtime tuning.
   - Write the standing short-gap outcome test: assert a jump from rest clears the short gap, lands on the far platform within budget, and does not spend a long window in `Fallen` after touchdown. Expected: **red** before runtime tuning.
   - Write the sprint-gap outcome test: keep sprint build-up, launch timing, and touchdown budget explicit so sprint coverage does not silently inherit the standing profile. Expected: **red** before runtime tuning.
   - Exit: two red gap tests with locked widths, reusable helpers, no runtime changes.

2. Standing reach tuning
   - Add a small input-directed horizontal impulse in `FireJumpLaunch()` scaled by current input magnitude (not sprint speed) so standing jumps gain forward travel.
   - Tune the horizontal impulse magnitude to green the standing gap test.
   - Assert jump apex height does not exceed baseline + 20% (height ceiling). If it does, reduce vertical force and increase horizontal contribution.
   - Confirm `ShouldSuppressLocomotion()` still blocks full ground-speed movement during airborne frames — only the launch impulse adds horizontal reach, not in-flight drive.
   - If the standing gap test goes green but `SprintJumpStabilityTests` regress, retune `_jumpLandingGainBoost` or `_jumpPostLandingGraceDuration` in this same slice.
   - Exit: standing gap test green, apex height within budget, existing landing stability tests green.

3. Sprint reach tuning
   - Add a velocity-preservation factor: reduce airborne horizontal damping / drag so more pre-jump sprint speed survives through the arc. The mechanism lives in `ShouldSuppressLocomotion()` or a new airborne-drag path in `ApplyMovementForces()`.
   - Tune the factor to green the sprint gap test. Do not inflate `_jumpForce`.
   - Assert jump apex height remains within the 20% ceiling for sprint jumps as well.
   - Keep the sprint gain bounded so the character still reads heavy and does not outrun touchdown recovery.
   - If `SprintJumpStabilityTests` regress from faster horizontal touchdown, retune landing gains in this same slice.
   - Exit: sprint gap test green, standing gap test still green, apex height within budget, landing stability green.

4. Air-control force path
   - Add a midair WASD correction force in `PlayerMovement` gated on `_recentJumpAirborne && !IsGrounded`, bypassing the `ShouldSuppressLocomotion()` block for this limited force only.
   - Cap air-control authority at ≤ 15% of normal ground move force.
   - Clamp opposite-direction authority to roughly half of same-direction / lateral authority so the player can trim landing placement but not reverse travel.
   - Write a focused acceptance test: air input during a jump produces measurable lateral displacement (> some minimum) but total displacement from pure air input over one jump stays under a concrete ceiling.
   - Exit: air-control acceptance test green, both gap tests still green, landing stability green.

5. Air-control reversal clamp
   - Write a negative test: full opposite-direction air input during a jump does not reduce forward travel below a floor (e.g. ≥ 70% of zero-air-input travel distance).
   - If the negative test fails, tighten the opposite-direction clamp until it passes.
   - Exit: reversal-clamp test green, air-control test still green, both gap tests green.

6. Airborne facing contingency
   - **Trigger:** only execute if midair yaw rate exceeds 180 deg/s during Slice 4 or 5 testing, or if HITL review flags visible yaw snapping.
   - Add a separate in-air facing-rate cap (e.g. ≤ 120 deg/s) instead of bundling it into the air-control pass.
   - Keep this optional so the default implementation surface stays small.

7. Airborne readability
   - Tune airborne arm raise and balance pose only after distance and air-control behavior are stable.
   - Reuse or extend the current airborne arm tests in `ArmAnimatorPlayModeTests` rather than mixing visual-polish risk into movement tuning slices.

8. Landing and regression gate
   - Re-run `JumpTests`, `SprintJumpStabilityTests`, `AirborneSpringTests`, the new gap tests, and the air-control tests as a full regression sweep.
   - Add a height-budget assertion to the regression surface: measure peak apex height for both standing and sprint jumps and assert ≤ baseline + 20%.
   - If extra reach or air control reopens the earlier `Fallen`/`GettingUp` churn, stop and reopen the bug path instead of masking it with feel tuning.

## Scope risks to watch
- Keep standing reach, sprint reach, and air control acceptance separate. A single green gap-clear test is not enough to prove the jump feels more nimble in both profiles.
- Treat in-air yaw control as a follow-up risk, not assumed work. Translation and facing are separate knobs in the current runtime.
- Guard every runtime slice with the existing landing-stability baseline so the old touchdown/state-loop regressions do not sneak back in under "feel" tuning.
- If the fixed target gap widths need large revision after the first red run, revisit the acceptance target first instead of silently retuning the tests around the implementation.
- **Height creep** — every slice that changes the arc must re-check the ≤ 20% apex ceiling. If a slice greens its distance target but exceeds the height budget, reduce vertical contribution and increase horizontal before accepting.
- **Landing gain coupling** — increased horizontal speed at touchdown may stress `_jumpLandingGainBoost` (currently 3f) and `_jumpPostLandingGraceDuration` (currently 0.5s). Retune in the same slice that widens the arc, not as a separate follow-up, to avoid a stale regression window.
- **Air-control opposite-direction clamp** is its own failure mode (too loose = arcade reversal; too tight = no practical authority). Isolating it into Slice 5 keeps the debugging surface small.

## Exit criteria
- Two fixed-width gap outcome tests are green: one standing short gap and one sprint gap.
- Standing reach improves independently of sprint momentum (via input-directed horizontal launch impulse), and sprint reach improves via velocity preservation without requiring floaty hang time.
- Jump apex height stays within 20% of the pre-change baseline for both standing and sprint profiles.
- Midair WASD correction is active at ≤ 15% of ground force, with opposite-direction authority clamped harder; a negative test proves full reverse input cannot reduce forward travel below ~70% of the zero-input baseline.
- Wind-up, airborne arms, and landing still read physically motivated and grounded.
- Existing sprint-jump stability coverage (`SprintJumpStabilityTests`), `JumpTests`, `AirborneSpringTests`, and the nearest airborne/landing regressions stay green, and any reappearing state-loop bug is split out instead of absorbed into this slice.

## Progress notes
- 2026-03-20: User requested a more nimble but still grounded jump, wanted both standing and sprint gap coverage, liked adding more airborne arm balance, and asked whether midair control exists today.
- 2026-03-20: Current code check confirmed that intentional jumps disable translational airborne move force in `PlayerMovement.ApplyMovementForces()` while `_recentJumpAirborne && !_balance.IsGrounded`, so there is effectively no midair WASD control today beyond facing updates.
- 2026-03-20: Review against the live runtime and test tree tightened this plan into smaller one-shot slices. The main adjustment was to separate harness work, standing reach, sprint reach, bounded air control, optional in-air yaw limits, and airborne readability so failures stay diagnosable and agent-sized.
- 2026-03-20: **Plan review** identified five gaps and restructured slices:
  1. Original plan had no horizontal launch component — standing reach could only grow by raising apex. Added input-directed horizontal impulse at launch as the named mechanism.
  2. Sprint carry had no named mechanism — locked on velocity-preservation factor (reduce airborne drag).
  3. Air-control acceptance was vague — locked at ≤ 15% of ground force, opposite-direction halved, with a concrete reversal-clamp negative test.
  4. No height ceiling existed — added ≤ 20% apex increase constraint to protect grounded feel.
  5. Slices 1-2-3 merged into one "harness + red tests" slice (too thin individually); old Slice 6 split into "force path" + "reversal clamp" (too fat for one pass). Final count: 8 slices, all single-agent-session sized.
- 2026-03-20: Slice 1 implemented with JumpGapOutcomeTests (locked gap widths, platform harness helpers, red standing/sprint outcome tests).
- 2026-03-20: Slice 2 retry fixed the scenario geometry so the hips spawn on the launch platform lip instead of metres behind it, added the standing jump apex-height budget check, and tuned standing-only horizontal launch reach without leaking that shove into full sprint jumps.

## Agent log
- 2026-03-20 Slice 1: Committed gap harness and two red tests (e767276)
- 2026-03-20 Slice 2: First attempt failed — character spawned 10m from launch edge, progress 0.00m. Retry queued with spawn geometry fix.
- 2026-03-20 Slice 2 retry: Spawn geometry corrected; standing gap + apex budget + sprint landing stability slice now green, while sprint gap remains intentionally red for Slice 3.


