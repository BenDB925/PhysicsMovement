# Plan 08 — Auto-Sprint: Opus Review

**Reviewed:** 2026-03-25
**Reviewer context:** Full codebase exploration of PlayerMovement.cs (1,183 lines), all sprint consumers (LocomotionDirector, LegAnimator, ArmAnimator), existing sprint test suite (26+ tests), jump/landing/damping infrastructure, and DesiredInput contract.

---

## 1. Results-Based Testing (Most Important)

### `AutoSprint_WalkRampsToSprintAfterDelay`

The plan asserts `SprintNormalized < 0.5` at 80 frames and `SprintNormalized > 0.9` at 160 frames. This is **testing an internal variable**, not a player-observable outcome. If `SprintNormalized` returned 1.0 but `GetSprintMovementMultiplier()` was broken and always returned 1.0f, this test would pass while the feature is completely broken.

**Player-observable version:** Measure actual horizontal velocity. After 80 frames with move input, assert `horizontalSpeed < walkSpeedCap * 1.15` (still near walk). After 160 frames, assert `horizontalSpeed > walkSpeedCap * 1.5` (clearly in sprint territory). The speed is what the player experiences.

Even better: measure displacement over a known window. "Character covers X meters in the first 80 frames (walk pace) and Y meters in frames 120-200 (sprint pace), where Y/X > 1.5."

### `AutoSprint_StoppingResetsTimer`

Asserting `SprintNormalized < 0.3 within 5 frames of restart` — this is testing the blend curve, not the reset. The player-observable outcome is: "After stopping and restarting, character is at walk pace for at least 1 second." Measure horizontal speed in the 0-100 frames after restart and assert it stays below `walkSpeedCap * 1.15`.

### `AutoSprint_LandIntoRun_MaintainsSpeed`

"Speed > 85% of pre-jump sprint speed 10 frames after landing" — this is a good outcome-focused test. However, 85% is a guess. Current landing damping retains 75% per-frame over 0.08s (8 frames at 100Hz) — that's `0.75^8 ≈ 0.10`, meaning **only ~10% of horizontal speed survives**. If the plan skips damping, 85% is fine. If damping partially applies, 85% is too aggressive. **The threshold should be documented relative to the expected damping behavior, not guessed.**

A realistic threshold for "skip damping entirely": assert speed > 80% of pre-jump speed. For "reduced damping": assert speed > 50%.

### `AutoSprint_WalkJump_DoesNotLandAtSprintSpeed`

Asserting `SprintNormalized < 0.5 for first 30 frames after landing` is again checking the internal variable. The player-observable version: "Horizontal speed after a walk-jump landing stays below `walkSpeedCap * 1.15` for 50 frames." This would catch a bug where `SprintNormalized` is low but the character somehow gets sprint speed anyway.

### Missing Tests

These "broken auto-sprint" scenarios are not covered:

1. **Direction change resets sprint** — Player is sprinting forward, instantly reverses to backward. Does the timer reset? It should, because you're decelerating. The plan's input-magnitude check won't catch this since magnitude stays high during a direction reversal.

2. **Auto-sprint doesn't activate during stumble/recovery** — If the character is in a lean-suppressed state (high tilt), input is held but the character can barely move. Should the timer tick? The plan only checks input magnitude > 0.1, not CharacterState.

3. **Sprint state survives across multiple rapid jumps** — Sprint→jump→land→immediately jump again. Does the timer/sprint state persist correctly through the double bounce? The existing `SprintJump_TwoConsecutiveJumps_DoesNotFaceplant` test uses `SetSprintInputForTest(true)` which bypasses the timer entirely.

4. **Auto-sprint timer doesn't tick while airborne** — Should the timer pause in the air, or keep running? If it keeps running and you jump at 0.8s of movement, you could land already at sprint. If it pauses, you need post-landing continuation logic. **The plan doesn't specify.**

5. **Physics push doesn't trigger sprint** — Character standing still, gets shoved by a physics object. Move input is zero but character is moving. Timer shouldn't tick. This is actually safe per the plan (timer checks input magnitude), but worth a test to lock it in.

---

## 2. Root Cause vs Patchwork

### "Set timer to delay value on landing" is a hack

The plan says: on sprint-speed landing, `set auto-sprint timer = _autoSprintDelay` so `_sprintHeld` stays true immediately. This works but creates a hidden coupling: the landing code now knows about the auto-sprint timer's implementation. If the timer logic changes (e.g., you add a distance requirement), the landing bypass breaks silently.

**Cleaner model:** Separate the concepts. The auto-sprint timer determines *when sprint activates organically*. The land-into-run feature determines *whether sprint persists through a landing*. These are different questions.

Introduce `_preserveSprintOnLanding` (bool, set at launch if sprinting) that directly sets `_sprintHeld = true` on touchdown, bypassing the timer entirely. The timer doesn't need to be manipulated — the held state is just forced. This is closer to how `SetSprintInputForTest` already works and is less fragile.

### Sprint reset on fall/knockdown

The plan doesn't address this. Currently `IsSprintSpeedTierActive()` gates on `CharacterState == Moving`. If the character falls (`CharacterState == Fallen`), sprint is already gated off. But the *timer* would keep ticking if input is held during a ragdoll tumble. On recovery, if the timer exceeds `_autoSprintDelay`, sprint would instantly activate. **The timer should reset when CharacterState leaves Moving/Standing**, or at minimum when entering Fallen.

### 0.1s reset grace

0.1s (10 frames at 100Hz) is fine for "player briefly released stick while readjusting." But quick direction changes (e.g., sharp 90° turn) maintain input magnitude above 0.1 — the timer wouldn't reset at all. This is probably correct for the game feel, but consider that a full 180° reversal with sustained input would keep the sprint timer running, and then the character would be sprinting backwards. This interacts with facing direction slew — is that desirable?

Actually, checking the code: `IsSprintSpeedTierActive()` requires `CharacterState == Moving` and `_sprintHeldThisPhysicsStep`. Sprint force applies via `GetSprintMovementMultiplier()` in `ApplyMovementForces()`, which applies force in the camera-relative world direction. So sprinting backwards is possible with current code if you hold back. Auto-sprint doesn't change this, but it does make it more likely since the player can't release a sprint button to slow down.

**Suggestion:** The plan should acknowledge that auto-sprint means the player loses the ability to walk on demand. If this is intentional (it sounds like it is), note it as a design decision. If not, consider a "walk toggle" input as a replacement.

---

## 3. Slice Sizing

### Can slices 1 and 2 merge?

No. They touch the same file (PlayerMovement.cs) but different concerns:
- Slice 1 changes how `_sprintHeld` is determined (input source replacement). This is a clean substitution with a tight blast radius.
- Slice 2 changes landing behavior (damping skip, sprint state preservation on touchdown). This touches the jump/landing state machine.

Merging would exceed 2-3 files and create a debugging nightmare if either feature introduces a regression. Keep them separate.

### Should Input Actions change be in slice 1?

**No.** Remove the binding only after tests confirm auto-sprint works. The existing `PlayerInputActionsTests.SprintAction_UsesShiftAndLeftStickPressBindings` will fail immediately if you remove the binding. Remove the binding + update those EditMode tests in slice 3, after everything is proven green.

Actually, even slice 3 is wrong for this. The binding removal should be its own commit at the end of slice 3 (or a tiny slice 4) because:
1. It's an Input Actions asset change, not a C# change
2. It breaks EditMode tests that verify binding existence
3. It's irreversible in the sense that reverting auto-sprint also needs reverting the Input Actions

---

## 4. Gaps

### Sprint state during wind-up and airborne

This is the biggest gap in the plan. Currently:

1. **Wind-up:** `IsSprintSpeedTierActive()` checks `CharacterState == Moving`. During wind-up the state is still Moving (it doesn't change until airborne). So sprint force is still applied during wind-up frames. The auto-sprint timer would still be ticking since input is held. No problem here.

2. **Airborne:** `CharacterState` transitions to Airborne. `IsSprintSpeedTierActive()` returns false. `GetSprintNormalizedTarget()` returns 0. `SprintNormalized` starts blending down. **But** the airborne velocity preservation system (`ApplyRecentJumpAirborneVelocityPreservation`) maintains 90% of launch speed regardless of `SprintNormalized`.

3. **The problem:** During airborne, `SprintNormalized` decays toward 0 over 0.25s. If the jump lasts > 0.25s (most do), `SprintNormalized` reaches 0 before landing. The plan stores `_wasSprintingAtLaunch` to handle this, which is correct. But `SprintNormalized` reaching 0 also affects:
   - **LegAnimator:** Gait parameters snap to walk values mid-air
   - **ArmAnimator:** Arm swing snaps to walk values mid-air
   - **LocomotionDirector:** Sprint lean goes to 0 (this is already handled — `jump-airborne sprint lean scale = 0`)

   On landing, even if sprint is preserved, `SprintNormalized` needs 0.25s to blend back up. During those 25 frames, the character is at sprint speed but with walk animations. **This will look wrong.**

**Fix:** When `_wasSprintingAtLaunch` is true and airborne, either freeze `SprintNormalized` at its launch value, or reduce the blend rate to 0 during airborne. On landing with sprint preserved, `SprintNormalized` should resume from its frozen value, not from 0.

### `SprintRampFrames = 500` in existing tests

Tests like `SprintJumpStabilityTests` use `SetSprintInputForTest(true)` and wait 500 frames. This bypasses the auto-sprint timer entirely (override path). These tests are safe — the plan correctly identifies this. **But** any *new* test that doesn't use `SetSprintInputForTest` and expects sprint after N frames will need to account for the auto-sprint delay (120 frames) + blend duration (25 frames) = ~145 frames minimum.

### Minimum movement distance vs time

The plan uses input magnitude > 0.1 as the timer gate. This means:
- Character pushed against a wall, input held: timer ticks, sprint activates after 1.2s, character sprints into wall. Not great but not terrible.
- Character in a tight space, oscillating input: timer could accumulate. With the 0.1s reset grace, very rapid micro-stops won't reset it.
- Character on a conveyor belt (physics push) with no input: timer doesn't tick. Safe.

**Suggestion:** Gate the timer on `CharacterState == Moving` in addition to input magnitude. This catches the wall case (character would be in Standing state if not actually moving) and the recovery case (Fallen state blocks timer).

### UI/feedback

Auto-sprint is invisible. The plan should note whether any visual/audio feedback is needed. Consider:
- Subtle camera FOV increase at sprint (common in games with auto-sprint)
- Sprint dust/footfall audio change
- None — if the movement feel difference is clear enough already

This is a design decision, not a code gap. But the plan should mention it.

---

## 5. Ordering

### Slice 2 depends on slice 1

Yes. The "set auto-sprint timer to delay value on landing" logic requires the timer to exist. More fundamentally, the `_wasSprintingAtLaunch` flag's value depends on how sprint is driven. If using the button, the player might not be holding sprint at launch. With auto-sprint, sprint at launch is deterministic (held input for > 1.2s).

**Slice 2 cannot be standalone.** It must follow slice 1.

### Should the sprint button be kept?

**Remove it.** A debug option adds complexity and test surface for zero gameplay value. Developers can use `SetSprintInputForTest(true)` in the editor console if needed. The binding should go in slice 3 as described above.

---

## 6. Risks

### `SetSprintInputForTest(false)` tests

Tests that call `SetSprintInputForTest(false)` and expect walk speed are safe because:
1. `_overrideSprintInput = true` prevents the auto-sprint timer from overwriting `_sprintHeld`
2. `_sprintHeld = false` directly → `SprintNormalized` blends to 0

No regression risk here.

### `CurrentSprintHeld` consumers

No external consumers. `CurrentSprintHeld` is internal to `PlayerMovement` and is only used in tests via the public `SprintNormalized` path. Safe.

### Auto-sprint timer edge cases

**Physics push without input:** Safe — timer checks input magnitude.
**Controller disconnect:** Input magnitude goes to 0 → timer resets. On reconnect, timer starts fresh. Safe.
**Pause/unpause:** If `Time.timeScale = 0`, `Update()` still runs but `deltaTime = 0`. Timer wouldn't tick. On unpause, timer resumes. Probably fine.

---

## 7. Physics Correctness

### `_jumpLandingDampingTimer = 0` to skip damping

The damping timer is checked in a standalone block ([PlayerMovement.cs](Assets/Scripts/Character/PlayerMovement.cs#L546-L563)):

```csharp
if (_jumpLandingDampingTimer > 0f)
{
    _jumpLandingDampingTimer -= dt;
    // ... apply damping
}
```

Setting it to 0 cleanly skips the entire block. No other code depends on the damping window running — `_jumpLandingDetected`, `_jumpPostLandingGraceTimer`, and `_recentJumpAirborne` are all managed independently. **This is safe.**

### BUT: `_jumpPostLandingGraceTimer` still suppresses movement force

The plan skips damping but doesn't address the landing movement force ramp. In `ApplyMovementForces()` ([line 1138](Assets/Scripts/Character/PlayerMovement.cs#L1138)), while `_jumpPostLandingGraceTimer > 0`:

```csharp
float landingProgress = 1f - (_jumpPostLandingGraceTimer / _jumpPostLandingGraceDuration);
activeMoveForce *= Mathf.Clamp01(landingProgress);
```

This ramps movement force from 0 → full over 0.65s. Even with damping skipped, the character can't re-accelerate for 0.65s. Combined with the airborne velocity preservation system maintaining speed, this might be fine (speed is preserved passively, not via force application). But the character will decelerate naturally during those 0.65s due to physics damping.

**The plan should explicitly decide:** either (a) also reduce `_jumpPostLandingGraceDuration` for sprint landings so force comes back faster, or (b) accept the 0.65s coast and document that the character maintains momentum via inertia, not force.

### `MinimumSprintReachVelocityPreservationFactor` interaction

This constant (0.85f) and its acceleration (28 m/s²) govern airborne velocity preservation. They're applied during airborne and don't interact with landing at all — they stop being relevant once the character is grounded. The land-into-run feature only acts at the moment of landing. **No interaction.**

---

## 8. Anything Else

### PlayerMovement.cs is 1,183 lines — auto-sprint adds ~30-40 more

This is above the 500-line preference and approaching the 600-line hard refactor ceiling noted in repo conventions. The plan doesn't address this. Consider whether the auto-sprint timer logic should live in a small helper class (`AutoSprintTimer`) that PlayerMovement delegates to, or whether this is acceptable growth. Given the timer is ~15 lines of logic and the rest is field declarations, I'd say it's acceptable, but flag it.

### The auto-sprint timer should tick in FixedUpdate, not Update

The plan says "Timer increments in `Update()`." The sprint held state is consumed in `FixedUpdate()` via `_sprintHeldThisPhysicsStep`. If the timer ticks in `Update()` (variable rate), there's a frame-rate-dependent timing inconsistency: at 240fps the timer ticks 2.4x faster than at 100fps if using `Time.deltaTime`. **Use `FixedUpdate` or accumulate using `Time.fixedDeltaTime` for deterministic behavior.** This matches the physics-driven philosophy of the project.

Actually — the current sprint input polling is in `Update()` and then latched in `FixedUpdate()`. The timer could tick in `Update()` with `Time.deltaTime` (matching the input polling cadence) and then the resulting `_sprintHeld` gets latched in `FixedUpdate()` as before. This maintains the existing input→physics latch pattern. But the timing of "1.2s of sustained movement" will vary by framerate. For a physics-driven game running at 100Hz fixed step, I'd recommend ticking the timer in `FixedUpdate()` alongside the sprint state consumption for consistency.

### `_autoSprintResetOnStopDelay` naming

The plan defines `_autoSprintResetOnStopDelay = 0.1s` but the field name is confusing. "Reset on stop delay" could mean "how long after stopping does the reset happen" or "the delay before reset triggers." Call it `_autoSprintResetGraceSeconds` or `_autoSprintStopGraceWindow` — something that clearly communicates "you have this many seconds of zero input before the timer resets."

---

# Revised Plan

Below is the revised plan incorporating the above findings.

---

# Plan 08 — Auto-Sprint (Revised)

**Status:** In Design — Opus review complete
**Current next step:** Slice 1 implementation
**Branch prefix:** `slice/08-N-name`
**Slice prompts dir:** `H:\Work\PhysicsDrivenMovementDemo\Plans\auto-sprint\prompts\`

---

## Goal

Remove the sprint button entirely. The character walks when you first move, then automatically ramps up to sprint after ~1.2 seconds of sustained movement. Stopping resets the timer. Landing from a jump while already sprinting flows directly into a run — "land into a run."

<!-- OPUS: Added design decision: player loses walk-on-demand. This is intentional for the obstacle-course format. If walk-on-demand is needed later, add a "walk toggle" input. -->

**Design decision:** Auto-sprint means the player cannot choose to walk during sustained movement. This is intentional — the obstacle course format wants full speed approaching obstacles without an extra button. If a walk mode is needed later, add a dedicated "walk toggle" input rather than reverting to sprint-hold.

---

## Root Cause / Background

Currently `_sprintHeld` is a raw bool polled from the Input System in `Update()`. `SprintNormalized` then blends 0→1 over `_sprintBlendDuration` when the button is held.

Auto-sprint replaces the input source: instead of a button, sprint activates based on how long the player has been continuously moving. The existing `SprintNormalized` blend, `_sprintSpeedMultiplier`, and all downstream consumers (`LocomotionDirector`, `LegAnimator`, `ArmAnimator`, jump code) remain untouched.

"Land into a run" is the second part: currently landing applies horizontal damping (75% retain per-frame over 0.08s) and movement force is suppressed for 0.65s via `_jumpPostLandingGraceTimer`. Together these bleed nearly all sprint momentum. When already at sprint speed before a jump, landing should preserve that momentum.

---

## Approach

- **Slice 1 — Auto-sprint timer**: Replace `_sprintHeld = _inputActions.Player.Sprint.IsPressed()` with timer-based logic. Timer ticks in `FixedUpdate` for deterministic behavior. Gate on move input magnitude AND `CharacterState` not being Fallen/recovery. Sprint button binding remains in Input Actions until slice 3.

<!-- OPUS: Timer in FixedUpdate, not Update, for physics-time consistency. Added CharacterState gate so the timer doesn't accumulate during tumbles. Sprint button binding stays until tests are green. -->

- **Slice 2 — Land into a run**: Store `_wasSprintingAtLaunch` at wind-up. On landing, if sprinting at launch: skip damping, reduce `_jumpPostLandingGraceTimer` to a shorter sprint-landing value, and set `_sprintHeld = true` directly (don't manipulate the timer). Freeze `SprintNormalized` decay during airborne when launched at sprint.

<!-- OPUS: Don't hack the timer — set _sprintHeld directly (same pattern as SetSprintInputForTest). Also freeze SprintNormalized during airborne to prevent walk-animation snap mid-jump, and reduce the movement force suppression window for sprint landings. -->

- **Slice 3 — Tests & cleanup**: Results-based tests measuring velocity outcomes. Remove sprint button binding from Input Actions. Update affected EditMode tests.

<!-- OPUS: Binding removal moved to slice 3 after tests prove the feature works. -->

---

## Slices

### Slice 1 — Auto-Sprint Timer

**Goal:** Sprint activates automatically after sustained movement, no button required.

**Changes:**
- `PlayerMovement.cs`:
  - New serialized fields: `[SerializeField] float _autoSprintDelay = 1.2f`, `[SerializeField] float _autoSprintStopGraceWindow = 0.1f`  <!-- OPUS: Renamed from _autoSprintResetOnStopDelay for clarity -->
  - New private state: `float _autoSprintTimer`, `float _autoSprintStopGraceTimer`
  - In `FixedUpdate`, after snapshotting move input: <!-- OPUS: Moved from Update to FixedUpdate for deterministic timing -->
    - If `_overrideSprintInput` is true, skip (test seam preserved)
    - If move input magnitude > 0.1 AND `CharacterState` is not Fallen: <!-- OPUS: Added CharacterState gate to prevent timer ticking during ragdoll tumble -->
      - Reset `_autoSprintStopGraceTimer = _autoSprintStopGraceWindow`
      - Increment `_autoSprintTimer += Time.fixedDeltaTime`
    - Else if `_autoSprintStopGraceTimer > 0`: decrement grace timer
    - Else: reset `_autoSprintTimer = 0`
    - Set `_sprintHeld = _autoSprintTimer >= _autoSprintDelay`
  - Stop polling `_inputActions.Player.Sprint.IsPressed()` in `Update()` (remove/comment the line; leave the binding in Input Actions)
  - `SetSprintInputForTest` unchanged — override still forces `_sprintHeld` directly and skips timer logic

**Exit criteria:**
- Walking forward: character starts at walk speed, ramps to sprint after ~1.2s
- Stopping and restarting resets the timer — character walks again briefly
- Existing tests using `SetSprintInputForTest(true/false)` still work (override bypasses timer)
- Auto-sprint does not activate during stumble recovery (CharacterState != Moving)
- Full regression filter green

**Tests:** None — regression run is the gate.

---

### Slice 2 — Land Into a Run

**Goal:** Landing from a sprint-speed jump flows directly into a run.

**Changes:**
- `PlayerMovement.cs`:
  - New serialized field: `[SerializeField] float _landIntoRunSprintThreshold = 0.9f`
  - New serialized field: `[SerializeField] float _sprintLandingGraceDuration = 0.15f` <!-- OPUS: Shorter than the 0.65s default, so movement force comes back faster for sprint landings -->
  - New private state: `bool _wasSprintingAtLaunch`, `float _sprintNormalizedAtLaunch`
  - At wind-up start (`FireJumpLaunch` or wind-up entry):
    - `_wasSprintingAtLaunch = SprintNormalized >= _landIntoRunSprintThreshold`
    - `_sprintNormalizedAtLaunch = SprintNormalized`
  - In `UpdateSprintNormalized()`: if `_recentJumpAirborne && _wasSprintingAtLaunch`, freeze `_sprintNormalized` at `_sprintNormalizedAtLaunch` instead of blending toward 0 <!-- OPUS: Prevents walk-animation snap during sprint jumps. LegAnimator and ArmAnimator stay at sprint parameters mid-air -->
  - On `_jumpLandingDetected`, if `_wasSprintingAtLaunch`:
    - Skip horizontal damping: `_jumpLandingDampingTimer = 0` (safe — no other code depends on damping window)
    - Reduce movement force suppression: `_jumpPostLandingGraceTimer = _sprintLandingGraceDuration` instead of `_jumpPostLandingGraceDuration` <!-- OPUS: Ground force comes back in 0.15s instead of 0.65s so the character doesn't decelerate from physics damping alone -->
    - Set `_sprintHeld = true` directly (not via timer manipulation) <!-- OPUS: Cleaner decoupling — landing code doesn't need to know about timer internals -->
    - Set `_autoSprintTimer = _autoSprintDelay` so the timer is already "past threshold" and sprint persists naturally after the forced hold <!-- OPUS: Kept timer sync but as a secondary step, not the primary mechanism -->

**Exit criteria:**
- Sprint-jumping and landing continues at sprint speed without a walk dip
- Walk-jumping and landing still walks on touchdown (threshold not met)
- Mid-air animations stay at sprint parameters during sprint jumps (no walk-stride snap)
- Full regression filter green

**Tests:** None — regression run is the gate.

---

### Slice 3 — Tests & Cleanup

**Goal:** Lock in both features with results-based tests. Remove sprint button binding.

**New test file: `Assets/Tests/PlayMode/Character/AutoSprintTests.cs`**

All tests use horizontal velocity measurements, not `SprintNormalized` checks: <!-- OPUS: Every test rewritten to measure player-observable outcomes (velocity, displacement) instead of internal SprintNormalized -->

- **`AutoSprint_WalkRampsToSprintAfterDelay`** — Apply move input via `SetMoveInputForTest`. After 80 frames (~0.8s), measure horizontal speed and assert < `walkMaxSpeed * 1.15` (walk pace). After 200 frames (~2.0s, well past 1.2s delay + 0.25s blend), measure horizontal speed and assert > `walkMaxSpeed * 1.5` (sprint pace). Uses `_maxSpeed` and `_sprintSpeedMultiplier` to derive thresholds from actual config rather than magic numbers.

- **`AutoSprint_StoppingResetsTimer`** — Ramp to sprint (200 frames with input). Remove input for 30 frames (0.3s, well past 0.1s grace). Reapply input. Assert horizontal speed < `walkMaxSpeed * 1.15` for the first 80 frames after restart. Tests that the reset produces observable walk-speed behavior.

- **`AutoSprint_LandIntoRun_MaintainsSpeed`** — Sprint-ramp for 200 frames, record pre-jump horizontal speed. Jump, wait for landing. Measure horizontal speed 15 frames after landing. Assert speed > `preJumpSpeed * 0.80`. <!-- OPUS: 80% threshold accounts for some natural physics deceleration even without explicit damping. Document the reasoning. -->

- **`AutoSprint_WalkJump_DoesNotLandAtSprintSpeed`** — Use `SetSprintInputForTest(false)`. Move for 30 frames (walk only), jump, land. Assert horizontal speed < `walkMaxSpeed * 1.15` for 50 frames after landing. Tests that the sprint threshold gate works.

- **`AutoSprint_DirectionReversal_DoesNotInstantSprint`** <!-- OPUS: New test — catches the direction-reversal edge case where input magnitude stays high but the character should decelerate --> — Sprint-ramp for 200 frames forward. Reverse input to backward. Assert horizontal speed drops below `walkMaxSpeed * 1.3` within 100 frames of reversal (character is decelerating, not sprinting backwards immediately). Note: the timer may not reset since input magnitude stays high, but the character's actual velocity should show the deceleration/turnaround.

- **`AutoSprint_RapidJumpChain_MaintainsSprint`** <!-- OPUS: New test — covers the double-jump chain case --> — Sprint-ramp for 200 frames. Jump, land, immediately jump again. Measure horizontal speed after second landing. Assert speed > `preJumpSpeed * 0.70`. Tests that sprint state chains correctly across rapid jumps.

**Cleanup:**
- Remove sprint button binding from `PlayerInputActions.inputactions` (delete binding, not the action — in case it's referenced elsewhere) <!-- OPUS: Deferred to slice 3 after tests confirm feature works -->
- Update `PlayerInputActionsTests.SprintAction_UsesShiftAndLeftStickPressBindings` — either remove the test or change assertion to verify no bindings

**Exit criteria:** All new tests pass. Full regression filter green (minus known pre-existing failures). Sprint button binding removed.

---

## Open Questions for Design

<!-- OPUS: Consolidated questions surfaced during review that need design decisions before implementation -->

1. **Airborne timer behavior:** Should the auto-sprint timer pause while airborne, or keep ticking? Current plan doesn't specify. Recommendation: **pause the timer while airborne** — it only ticks when `CharacterState == Moving/Standing` and move input is held. This prevents "jump at 0.8s, land already sprinting" scenarios.

2. **Visual feedback:** Auto-sprint has no tactile feedback (no button press). Should there be any visual/audio cue? Camera FOV shift, footfall sound change, sprint dust particles? Recommendation: defer to post-slice-3 polish pass.

3. **Walk-on-demand:** Is there any scenario where the player needs to walk slowly on purpose (e.g., precision platforming near an edge)? If so, a "walk toggle" (crouch button?) may be needed. Recommendation: don't address in this plan; add as a separate plan if needed after playtesting.

---

## Parameter Reference

| Parameter | Default | Location | Purpose |
|-----------|---------|----------|---------|
| `_autoSprintDelay` | 1.2f | PlayerMovement | Seconds of sustained movement before sprint activates |
| `_autoSprintStopGraceWindow` | 0.1f | PlayerMovement | Seconds of zero input before timer resets |
| `_landIntoRunSprintThreshold` | 0.9f | PlayerMovement | Minimum `SprintNormalized` at launch to qualify for land-into-run |
| `_sprintLandingGraceDuration` | 0.15f | PlayerMovement | Shortened post-landing force ramp for sprint landings (default is 0.65s) |
| `_sprintBlendDuration` | 0.25f | PlayerMovement (existing) | Ramp time for SprintNormalized 0→1 (unchanged) |
| `_jumpLandingHorizontalDampingFactor` | 0.75f | PlayerMovement (existing) | Landing damping retain factor (skipped for sprint landings) |

---

## Risk Register

<!-- OPUS: Explicit risk tracking -->

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| `SetSprintInputForTest(false)` tests see sprint after 1.2s of movement | Low | High | Override path sets `_overrideSprintInput = true`, preventing timer from writing `_sprintHeld` |
| SprintNormalized decay causes walk-animation snap mid-air during sprint jumps | High (guaranteed without fix) | Medium | Freeze `_sprintNormalized` during airborne when `_wasSprintingAtLaunch` |
| Landing force ramp (0.65s) bleeds sprint momentum despite damping skip | High (guaranteed without fix) | Medium | Use shorter `_sprintLandingGraceDuration` (0.15s) for sprint landings |
| Timer ticks during ragdoll tumble, causing instant sprint on recovery | Medium | Low | Gate timer on `CharacterState` not being Fallen |
| PlayerMovement.cs exceeds 500-line preference | Already there (1,183 lines) | Low | Auto-sprint adds ~30 lines. Flag but don't refactor in this plan. |

---

## Agent Log

*To be filled as slices complete.*

---

## Known Pre-Existing Test Failures (exclude from gate)

*Carry forward from original plan.*
