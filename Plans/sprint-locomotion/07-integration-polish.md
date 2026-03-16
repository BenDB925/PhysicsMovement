# WP-7: Integration and Polish

## Goal
Validate full sprint lifecycle end-to-end: transitions, sustained sprint, turning while sprinting, and jumping from sprint. Update project documentation.

## Current status
- State: Not started
- Current next step: Wait for WP-1 through WP-6 to land
- Blockers: All prior WPs

## Scope

### 1. Transition tests
- Walk → sprint → walk: smooth acceleration and deceleration with no visual pop.
- Sprint start from standing: character leans, arms pump, stride widens within 0.5 s.

### 2. Sprint + turn
- Sprint while turning 90°: character should lean into the turn and not stumble/fall.
- Existing turn-recovery and snap-recovery systems should still function at sprint speed.

### 3. Sprint + jump
- Jump from sprint: should produce a longer forward arc.
- Landing from sprint-jump: character should recover to sprinting (not fall over).

### 4. Sustained sprint lap
- Use GhostDriver + WaypointCourseRunner to drive a full lap of Arena_01 at sprint speed.
- Assert: completes without falling, legs-behind-hips guard stays green.

### 5. Documentation updates
- Update `ARCHITECTURE.md` with sprint input, speed tier, and downstream parameter scaling.
- Update `TASK_ROUTING.md` if sprint introduces a new CharacterState or LocomotionDirector mode.
- Update `.copilot-instructions.md` if any routing changes.

## Tests — outcome-based

### T7-1: WalkToSprint_SmoothTransition
- **Setup**: Walk forward 2 s, start sprinting 3 s.
- **Assert**: No frame where CharacterState enters `Fallen`. Speed increases monotonically over ~0.5 s transition window.

### T7-2: SprintToWalk_SmoothTransition
- **Setup**: Sprint 3 s, release sprint, walk 2 s.
- **Assert**: Speed decreases from sprint cap to walk cap within 1 s. No `Fallen` state.

### T7-3: Sprint_90DegreeTurn_NoFall
- **Setup**: Sprint straight 2 s, turn 90° over 0.5 s, continue sprinting 2 s.
- **Assert**: Character does not enter `Fallen`. Displacement after the turn continues in the new direction.

### T7-4: Sprint_Jump_LandsUpright
- **Setup**: Sprint 2 s, jump, continue sprint input.
- **Assert**: Character enters `Airborne`, returns to `Moving` within 2 s. Does not enter `Fallen`.

### T7-5: Sprint_FullLap_Completes
- **Setup**: GhostDriver lap of Arena_01 at sprint speed.
- **Assert**: Lap completes (all waypoints reached). Character never enters `Fallen` for more than 80 consecutive frames. Legs-behind-hips guard (T6-2 thresholds) holds throughout.

### T7-6: AllExistingGaitTests_StillPass (regression)
- **Setup**: Run the full `GaitOutcomeTests` suite.
- **Assert**: All existing tests pass — proves sprint code does not regress walk behaviour.

## Decisions

## Artifacts

## Progress notes
