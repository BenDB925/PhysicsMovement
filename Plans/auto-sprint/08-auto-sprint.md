# Plan 08 — Auto-Sprint

**Status:** In Design — Opus review pending
**Current next step:** Opus review
**Branch prefix:** `slice/08-N-name`
**Slice prompts dir:** `H:\Work\PhysicsDrivenMovementDemo\Plans\auto-sprint\prompts\`

---

## Goal

Remove the sprint button entirely. The character walks when you first move, then automatically ramps up to sprint after ~1–1.5 seconds of sustained movement. Stopping resets the timer. Landing from a jump while already sprinting should flow directly into a run without any stumble pause — "land into a run" Super Mario style.

This makes movement feel more natural and less button-managey, and suits the obstacle course loop where you want to be at full speed approaching obstacles without holding an extra button.

---

## Root Cause / Background

Currently `_sprintHeld` is a raw bool polled from the Input System in `Update()`. `SprintNormalized` then blends 0→1 over `_sprintBlendDuration` when the button is held.

Auto-sprint replaces the input source: instead of a button, sprint activates based on how long the player has been continuously moving. The existing `SprintNormalized` blend, `_sprintSpeedMultiplier`, and all downstream consumers (`LocomotionDirector`, jump code, etc.) remain untouched — we're only changing what drives `_sprintHeld`.

"Land into a run" is the second part: currently landing applies horizontal damping and resets sprint state, causing a brief stumble. When already at sprint speed before a jump, landing should preserve that momentum and skip the walk ramp-up entirely.

---

## Approach

- **Slice 1 — Auto-sprint timer**: Replace `_sprintHeld = _inputActions.Player.Sprint.IsPressed()` with a timer. New fields: `_autoSprintDelay` (default 1.2s), `_autoSprintResetOnStop` (bool, default true). Timer counts up while move input magnitude > threshold, resets when stopped. Sets `_sprintHeld = true` when timer exceeds delay. Keep `SetSprintInputForTest` seam working — test override still forces `_sprintHeld` directly. Remove sprint button binding from Input Actions (or leave it wired but ignored).

- **Slice 2 — Land into a run**: When character lands (`_jumpLandingDetected`) and was at `SprintNormalized > 0.9` before leaving the ground, skip the horizontal damping window and preserve `_sprintHeld = true` immediately on touchdown. New field: `_landIntoRunSprintThreshold` (default 0.9). Gate: only when `SprintNormalized` was above threshold at jump launch (store as `_wasSprintingAtLaunch`).

- **Slice 3 — Tests**: Results-based tests measuring movement feel outcomes.

---

## Slices

### Slice 1 — Auto-Sprint Timer
**Goal:** Sprint activates automatically after sustained movement, no button required.

**Changes:**
- `PlayerMovement.cs`: Replace sprint button poll in `Update()` with auto-sprint timer logic. New serialized fields: `[SerializeField] float _autoSprintDelay = 1.2f`, `[SerializeField] float _autoSprintResetOnStopDelay = 0.1f` (small grace so brief decel doesn't kill sprint). Timer increments in `Update()` while move input magnitude > 0.1, resets when input drops to zero for longer than reset grace. `_sprintHeld = timer >= _autoSprintDelay`. `SetSprintInputForTest` still works — override takes precedence.
- Remove or comment out sprint button binding in `PlayerInputActions.inputactions` (or just stop polling it).

**Exit criteria:**
- Walking forward: character starts at walk speed, ramps to sprint after ~1.2s without touching sprint button
- Stopping and restarting resets the timer — character walks again briefly
- Existing tests using `SetSprintInputForTest(true)` still work correctly (override path unchanged)
- Full regression filter green

**Tests:** None — regression run is the gate.

---

### Slice 2 — Land Into a Run
**Goal:** Landing from a sprint-speed jump flows directly into a run. No stumble, no walk ramp.

**Changes:**
- `PlayerMovement.cs`: Store `_wasSprintingAtLaunch` bool when jump wind-up begins (set to `SprintNormalized > _landIntoRunSprintThreshold`). On `_jumpLandingDetected`, if `_wasSprintingAtLaunch`: skip horizontal damping window (`_jumpLandingDampingTimer = 0`), set auto-sprint timer to `_autoSprintDelay` immediately (so `_sprintHeld` stays true). New serialized field: `[SerializeField] float _landIntoRunSprintThreshold = 0.9f`.

**Exit criteria:**
- Sprint-jumping and landing continues at sprint speed without a walk dip
- Walk-jumping and landing still walks on touchdown (threshold not met)
- `SprintNormalized` does not drop below 0.8 during the landing frame when coming in at sprint speed
- Full regression filter green

**Tests:** None — regression run is the gate.

---

### Slice 3 — Tests & Regression Gate
**Goal:** Lock in both features with results-based tests.

**New test file: `Assets/Tests/PlayMode/Character/AutoSprintTests.cs`**

- `AutoSprint_WalkRampsToSprintAfterDelay` — apply move input, wait 80 frames (< delay), assert `SprintNormalized < 0.5`; wait another 80 frames (> delay), assert `SprintNormalized > 0.9`. Tests that sprint activates on time.
- `AutoSprint_StoppingResetsTimer` — ramp to sprint, stop input for 0.5s, restart input, assert `SprintNormalized < 0.3` within 5 frames of restart. Tests that the reset actually works.
- `AutoSprint_LandIntoRun_MaintainsSpeed` — sprint to a gap, jump, land; measure horizontal speed 10 frames after landing; assert speed > 85% of pre-jump sprint speed. Tests the "no stumble" outcome.
- `AutoSprint_WalkJump_DoesNotLandAtSprintSpeed` — walk-speed jump (use `SetSprintInputForTest(false)`, wait only 20 frames before jump); assert `SprintNormalized < 0.5` for first 30 frames after landing. Tests that the threshold gate works.

**Exit criteria:** All new tests pass. Full regression filter green (minus known pre-existing failures).

---

## Parameter Reference

*To be filled after slice 2 tuning.*

---

## Agent Log

*To be filled as slices complete.*

---

## Known Pre-Existing Test Failures (exclude from gate)

- `SustainedLocomotionCollapse_TransitionsIntoFallen`
- `CompleteLap_WithinTimeLimit_NoFalls`
- `TurnAndWalk_CornerRecovery`
- `LandingRecovery_SpringRampsGraduallyAfterLanding`
- `LandingRecovery_DampingDisabledWhenFactorIsOne`
- `SprintJump_TwoConsecutiveJumps_DoesNotFaceplant` (order-sensitive)
- `WalkStraight_NoFalls` (order-sensitive)
