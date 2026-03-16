# WP-1: Sprint Input and Speed Tier

## Goal
Give the player a sprint action and make the character move significantly faster while it is held — without breaking existing walk behaviour.

## Current status
- State: Active
- Current next step: Add sprint speed and force scaling in `PlayerMovement`
- Blockers: None

## Scope

### 1. Input binding
- Add a `Sprint` button action to `PlayerInputActions` (keyboard: Left Shift, gamepad: Left Stick Press / L3).
- Wire button state into `PlayerMovement` the same way Jump is wired (poll in Update, consume in FixedUpdate).

### 2. Speed tier
- Add `_sprintSpeedMultiplier` field (default 1.8×, range 1–3) to `PlayerMovement`.
- When sprint is held **and** the character is in `Moving` state, effective max speed = `_maxSpeed * _sprintSpeedMultiplier`.
- Increase `_moveForce` proportionally (or add a separate `_sprintForceMultiplier`) so the character actually reaches the higher cap.

### 3. `SprintNormalized` output
- Expose a `public float SprintNormalized { get; }` on `PlayerMovement` (0 = walk, 1 = full sprint). This is the single value downstream systems (BalanceController, LegAnimator, ArmAnimator) will read.
- Ramp up/down smoothly over ~0.25 s so visual transitions are not instant.
- Propagate through `DesiredInput` if the LocomotionDirector needs it.

### 4. Test seam
- Add `SetSprintInputForTest(bool held)` on `PlayerMovement` matching the pattern of `SetMoveInputForTest` / `SetJumpInputForTest`.

## Tests — outcome-based

### T1-1: Sprint_ReachesHigherSpeed
- **Setup**: Arena_01, settle 1 s, hold forward + sprint for 5 s.
- **Assert**: Peak horizontal speed ≥ `_maxSpeed * 1.5` (proves the speed tier is active).
- **Regression guard**: Also assert existing `GaitOutcomeTests.WalkForward_MovesCharacter` threshold (0.85 m) still passes without sprint.

### T1-2: Sprint_DisplacementExceedsWalk
- **Setup**: Two runs — 5 s walk, 5 s sprint (same direction).
- **Assert**: Sprint displacement > walk displacement × 1.4.

### T1-3: SprintReleased_SpeedReturnsToWalkCap
- **Setup**: Hold sprint 3 s, release sprint, continue walking 2 s.
- **Assert**: Final horizontal speed ≤ `_maxSpeed + 0.5` (tolerance for decel curve).

### T1-4: Sprint_OnlyActiveDuringMoving
- **Setup**: Hold sprint with zero move input.
- **Assert**: Character does not accelerate; `SprintNormalized` stays ≈ 0.

## Decisions
- 2026-03-16: Button actions in `PlayerMovement` now sample in `Update` and latch into `FixedUpdate` so short presses are not missed between physics ticks.
- 2026-03-16: `Sprint` now binds to Left Shift and Gamepad Left Stick Press (`L3`). The dormant `Grab` binding remains unchanged for this slice and will need follow-up if grab gameplay is wired to keyboard input.

## Artifacts
- `Assets/Scripts/Input/PlayerInputActions.cs`: Added `Sprint` action to the hand-written input wrapper.
- `Assets/Scripts/Input/PlayerInputActions.inputactions`: Mirrored the `Sprint` action and bindings in the Input System asset.
- `Assets/Scripts/Character/PlayerMovement.cs`: Jump is now Update-latched, and sprint held state is captured for the current physics tick.
- `Assets/Tests/EditMode/Input/PlayerInputActionsTests.cs`: Regression coverage for the new Sprint action and bindings.

## Progress notes
- 2026-03-16: Completed scope item 1. Sprint input exists in both input definitions, and `PlayerMovement` now latches Jump and Sprint button state from `Update` into `FixedUpdate`.
