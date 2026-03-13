# Chapter 6: Turn Recovery, Stumbles, And Catch Steps

Back to parent plan: [Unified Locomotion Roadmap](../unified-locomotion-roadmap.plan.md)

## Quick Load

- C6.1, C6.2, and C6.3 are complete. C6.4 and C6.5 remain.
- The director now classifies situations (HardTurn, Reversal, Slip, NearFall, Stumble), maps them to typed response profiles (strength, duration, kD blend), and gates entry/exit through a `RecoveryTransitionGuard` with debounce, cooldown, and ramp-in.
- Leg smoothing fix (commit `adc5096`) was applied between C6.2 and C6.3 to resolve a jerky-stride visual regression caused by Chapter 3 state-driven leg targeting.
- Verification baseline entering C6.4: EditMode 81/81, PlayMode 82/86 (3 ignored, 1 known fixture-order flake `CatchStepWithStumbleRecoveryReason`).

## Read More When

- Continue into the work packages when changing situation classification, response profiles, transition guard behavior, or collapse boundaries.
- Continue into the verification gate when running regression checks after recovery changes.
- Continue into the dependency notes when the change also alters observation inputs or support command contracts.

## Read this chapter when

- making hard turns, reversals, slips, or near-falls first-class situations
- defining explicit recovery strategies and timeout windows
- deciding where the collapse boundary belongs after recovery strategies fail

## Dependencies

- Read Chapter 5 first so recovery behavior is layered on top of the support-command model.
- Read Chapter 4 too when recovery steps depend on explicit foothold planning.

## Objective

Make hard locomotion cases first-class behaviors, not threshold accidents.

## Primary touchpoints

- Assets/Scripts/Character/LocomotionCollapseDetector.cs
- Assets/Scripts/Character/CharacterState.cs
- Assets/Scripts/Character/LegAnimator.cs
- Assets/Scripts/Character/Locomotion/LocomotionDirector.cs
- Assets/Scripts/Character/Locomotion/RecoverySituation.cs
- Assets/Scripts/Character/Locomotion/RecoveryState.cs
- Assets/Scripts/Character/Locomotion/RecoveryResponseProfile.cs
- Assets/Scripts/Character/Locomotion/RecoveryTransitionGuard.cs

## Work packages

1. C6.1 Situation classifier:
   - Add explicit situations: HardTurn, Reversal, Slip, NearFall, CatchStepNeeded, and Stumble.
   - 2026-03-13: Complete. Added `RecoverySituation` enum (None, HardTurn, Reversal, Slip, NearFall, Stumble) and `RecoveryState` readonly struct under `Assets/Scripts/Character/Locomotion/`. `LocomotionDirector.ClassifyRecoverySituation()` maps observation signals to named situations with priority ordering (Stumble > NearFall > Slip > Reversal > HardTurn). Director advances `RecoveryState` via `Enter()` / `Tick()` / expiry lifecycle. 8 new EditMode tests. Verification: EditMode 73/73, PlayMode 67/70 (3 ignored, 0 failed). Commit `9495992`.
2. C6.2 Dedicated responses:
   - Map each situation to a recovery strategy and timeout window.
   - 2026-03-13: Complete. Added `RecoveryResponseProfile` readonly struct with `UprightStrengthMultiplier`, `YawStrengthMultiplier`, `StabilizationStrengthMultiplier`, `KdBlendMultiplier`, `DurationMultiplier`. Five static factory methods (`ForHardTurn`, `ForReversal`, `ForSlip`, `ForNearFall`, `ForStumble`) with increasing strength tuning. Director now resolves per-situation profile and applies multipliers to `EmitObservationDrivenCommands` recovery blend and kD blend paths. Recovery command factory adds `ResponseProfile` field. 5 new EditMode tests. Verification: EditMode 78/78, PlayMode 67/70 (3 ignored, 0 failed). Commit `484575a`.
   - 2026-03-13: Leg smoothing fix applied between C6.2 and C6.3. `LegAnimator` now smooths `_resolvedUpperLegAngle` and `_resolvedKneeAngle` via `Mathf.MoveTowards` with serialized `_targetAngleSmoothingSpeed` (default 720 deg/s) to prevent abrupt frame-to-frame jumps at state boundaries. Commit `adc5096`.
3. C6.3 Recovery transitions:
   - Define clean entry and exit rules so recovery does not oscillate every few frames.
   - 2026-03-13: Complete. Added `RecoveryTransitionGuard` (sealed class) under `Assets/Scripts/Character/Locomotion/` with entry debounce, exit cooldown, and ramp-in blend. High-priority situations (NearFall, Stumble) debounce at half length. Exit cooldown blocks same-or-lower-priority re-entry but allows escalation. Integrated into `LocomotionDirector` with serialized fields: `_recoveryEntryDebounceFrames=3`, `_recoveryExitCooldownFrames=20`, `_recoveryRampInFrames=8`. Active recovery bypasses the guard for direct extension/upgrade so the ramp-in counter advances uninterrupted. 8 new EditMode tests. PlayMode turn-risk test adjusted with debounce override. Verification: EditMode 81/81, PlayMode 82/86 (3 ignored, 1 known fixture-order flake). Commit `b7b6b40`.
4. C6.4 Collapse boundary:
   - Keep LocomotionCollapseDetector as last resort when strategy fails, not first-line controller.
   - Status: Not started. Next work package.
   - Design notes: CharacterState currently treats `isLocomotionCollapsed` identically to `isFallen` for Standing/Moving/Airborne→Fallen transitions. The goal is to defer collapse-triggered Fallen when the director has active recovery, with a hard ceiling so truly unrecoverable situations still fall. LocomotionDirector should expose `IsRecoveryActive` for this purpose.
5. C6.5 Expressive outcomes:
   - Ensure visible problem-solving behavior before falling back to Fallen and GetUp.
   - Status: Not started.

## Verification gate

- Assets/Tests/EditMode/Character/LocomotionContractsTests.cs
- Assets/Tests/PlayMode/Character/LocomotionDirectorTests.cs
- Assets/Tests/PlayMode/Character/HardSnapRecoveryTests.cs
- Assets/Tests/PlayMode/Character/SpinRecoveryTests.cs
- Assets/Tests/PlayMode/Character/StumbleStutterRegressionTests.cs
- Assets/Tests/PlayMode/Character/GetUpReliabilityTests.cs

## Exit criteria

- Hard turns and stumble events show deterministic, test-backed recovery paths.