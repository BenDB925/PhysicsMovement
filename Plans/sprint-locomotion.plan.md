# Sprint Locomotion

## Status
- State: Active
- Acceptance target: Character sprints at higher speed with visible forward lean, pumping arms, longer stride — and existing walk gait never regresses to legs dragging behind hips
- Current next step: Finish WP-1 Arena_01 sprint speed outcomes so the speed tier has the same scene-level coverage now added for WP-2, then continue to WP-3 arm behaviour
- Active blockers: None

## Quick Resume
- WP-2 is complete: sprint lean now ramps through `LocomotionDirector` and no longer gets damped by recovery-only turn-lean attenuation on stable straight runs.
- New PlayMode sprint-lean coverage lives in `SprintLeanOutcomeTests` plus a focused `LocomotionDirectorTests` ramp check; the dedicated sprint-lean slice passed 4/4.
- Remaining open sprint validation is still WP-1's missing Arena_01 speed/displacement outcomes; after that, the next visual slice is WP-3 sprint arms.

## Verified Artifacts
- [Assets/Scripts/Character/Locomotion/LocomotionDirector.cs](Assets/Scripts/Character/Locomotion/LocomotionDirector.cs): Sprint lean now stays independent from recovery-only turn attenuation while still ramping from `SprintNormalized`
- [Assets/Tests/PlayMode/Character/SprintLeanOutcomeTests.cs](Assets/Tests/PlayMode/Character/SprintLeanOutcomeTests.cs): Arena_01 acceptance coverage for sprint lean increase, release, and Fallen-state safety
- [Plans/sprint-locomotion/02-forward-lean.md](Plans/sprint-locomotion/02-forward-lean.md): Completed WP-2 record with the runtime decision and verification results

## Child docs
- [ ] WP-1: Sprint input and speed tier ([Plans/sprint-locomotion/01-input-and-speed.md](Plans/sprint-locomotion/01-input-and-speed.md))
- [x] WP-2: Forward lean during sprint ([Plans/sprint-locomotion/02-forward-lean.md](Plans/sprint-locomotion/02-forward-lean.md))
- [ ] WP-3: Sprint arm behaviour ([Plans/sprint-locomotion/03-sprint-arms.md](Plans/sprint-locomotion/03-sprint-arms.md))
- [ ] WP-4: Sprint gait — stride and cadence ([Plans/sprint-locomotion/04-sprint-gait.md](Plans/sprint-locomotion/04-sprint-gait.md))
- [ ] WP-5: Balance retuning at sprint speed ([Plans/sprint-locomotion/05-balance-retuning.md](Plans/sprint-locomotion/05-balance-retuning.md))
- [ ] WP-6: Legs-behind-hips regression guard ([Plans/sprint-locomotion/06-legs-behind-hips-guard.md](Plans/sprint-locomotion/06-legs-behind-hips-guard.md))
- [ ] WP-7: Integration and polish ([Plans/sprint-locomotion/07-integration-polish.md](Plans/sprint-locomotion/07-integration-polish.md))

## Work packages

### 1. Sprint input and speed tier
Add a sprint button to `PlayerInputActions` (Left Shift / Gamepad Left Stick press), expose a sprint speed multiplier on `PlayerMovement`, increase `_moveForce` proportionally while sprint is held, and surface a `SprintNormalized` (0–1) value that downstream systems can read.

### 2. Forward lean during sprint
Use `SprintNormalized` in `BalanceController` to tilt the upright target forward. Walk lean stays at the current ~0–3° pelvis expression range; sprint pushes 5–10° of intentional forward lean. The lean must release smoothly when sprint ends.

### 3. Sprint arm behaviour
Scale `ArmAnimator._armSwingAngle` and `_elbowBendAngle` upward during sprint so arms pump more aggressively. At full sprint the swing should roughly double (40°+) and elbows should bend tighter (~30–40°) to match a natural sprint posture.

### 4. Sprint gait — stride and cadence
Scale `LegAnimator._stepAngle` and `_upperLegLiftBoost` with sprint intensity so the character takes visibly longer, higher strides. Optionally increase gait frequency slightly, but the primary visual cue should be stride length, not just faster leg cycling.

### 5. Balance retuning at sprint speed
At ~8–10 m/s the existing PD gains may over-damp or oscillate. Test at sprint speed and adjust `kP`/`kD`, height-spring, and COM-stabilization strength if needed. This may be a no-op if the existing gains hold — validate before changing.

### 6. Legs-behind-hips regression guard
Add dedicated outcome tests that detect legs dragging behind the hips during both walk and sprint. These tests run throughout the plan to catch regressions introduced by any work package.

### 7. Integration and polish
Full end-to-end PlayMode test: walk → sprint → walk transition, sustained sprint lap, sprint + turn, sprint + jump. Clean up any doc or architecture file updates.

## Test strategy overview

Every work package includes at least one outcome-based PlayMode test. Tests assert on world-space observables (displacement, joint rotation, body tilt, foot position relative to hips) — never on internal state. New tests extend the existing `GaitOutcomeTests` suite or create a sibling `SprintOutcomeTests` file using the same scene-load and GhostDriver patterns.

**Regression baseline**: all existing `GaitOutcomeTests` must remain green after every work package.

## Progress notes
- 2026-03-16: Plan created. Seven work packages scoped from codebase analysis.
- 2026-03-16: WP-1 scope item 1 completed. `Sprint` input is bound in both input definitions, and `PlayerMovement` now Update-latches Jump/Sprint button state before physics consumption.
- 2026-03-16: WP-2 completed. `LocomotionDirector` sprint lean now stays independent from recovery-only turn attenuation, and new PlayMode coverage (`SprintLeanOutcomeTests` plus a director ramp test) passed on the focused sprint-lean slice 4/4. A broader nearby slice passed 29/30 with one pre-existing unrelated `LocomotionDirectorTests` fallback red still failing in isolation.
