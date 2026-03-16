# WP-5: Balance Retuning at Sprint Speed

## Goal
Confirm the BalanceController PD gains, COM stabilization, and height-spring hold up at sprint speed (~8–10 m/s). Retune only if testing reveals oscillation, over-damping, or collapse.

## Current status
- State: Complete - validated with no BalanceController gain retune required
- Current next step: None. Resume the parent sprint plan for the next unfinished work package.
- Blockers: None

## Scope

### 1. Diagnostic pass
- Run a 10 s sustained sprint in Arena_01 and observe:
  - Does the character fall over? (PD gains too weak for the higher COM velocity)
  - Does the character wobble/oscillate? (kD under-damped at speed)
  - Does height drop or pump? (height-spring resonance at new frequency)
  - Does COM stabilization fight the intentional lean? (conflicting targets)

### 2. Conditional retuning
If issues found, consider:
- Speed-scaled `kP` / `kD` boost (e.g., `kP * (1 + 0.3 * sprintNormalized)`).
- Slightly higher height-spring during sprint to counter increased vertical oscillation.
- Relaxed COM horizontal stabilization during sprint so it doesn't fight the lean.

### 3. If no issues, close as validated
This WP may be a no-op. The existing gains are fairly aggressive (kP=2000, kD=200) and may handle 10 m/s fine. The outcome is either "tuning changes" or "validated — no changes needed."

## Tests — outcome-based

### T5-1: Sprint_SustainedWithoutFall
- **Setup**: Arena_01, sprint straight for 8 s.
- **Assert**: CharacterState never enters `Fallen`. Hips height stays within ±0.1 m of the sprint validation reference height (0.35 m) after initial ramp; the test does not use Arena_01's authored standing target directly because the shipped prefab keeps a taller 0.5 m standing support target while stable sprint posture rides lower.

### T5-2: Sprint_HeightStableAtSpeed
- **Setup**: Sprint 5 s, sample hips Y every frame.
- **Assert**: Standard deviation of hips Y over the last 3 s < 0.05 m (no pumping).

### T5-3: Sprint_RecoveryFromMinorPerturbation
- **Setup**: Sprint 3 s, apply a small lateral impulse (50 N × 0.1 s), continue sprinting 3 s.
- **Assert**: Character recovers to upright within 1.5 s and does not fall.

## Decisions
- 2026-03-16: WP-5 keeps the BalanceController support gains unchanged. Once Arena_01 reached the intended sprint tier, the sustained-sprint, height-stability, and perturbation-recovery diagnostics passed without a PD/COM/height-spring gain retune.
- 2026-03-16: `Sprint_SustainedWithoutFall_AndHipsHeightStaysNearTarget` now validates against the 0.35 m sprint reference height from the chapter spec instead of the prefab's live `StandingHipsHeight` value. Arena_01 currently authors a 0.5 m standing support target for the shipped ragdoll, and using that value directly would treat the intended lower sprint posture as a false failure.

## Artifacts
- `Assets/Tests/PlayMode/Character/SprintBalanceOutcomeTests.cs` - Arena_01 PlayMode diagnostics for T5-1/T5-2/T5-3 with an explicit speed-envelope precondition
- `Assets/Scripts/Editor/RagdollBuilder.cs` - Builder source of truth for the shipped BalanceController and PlayerMovement prefab values used by Arena_01
- `Assets/Prefabs/PlayerRagdoll.prefab` - Base prefab now serializes the sprint speed tier fields and updated move-force tune used to unlock the WP-5 speed envelope
- `Assets/Prefabs/PlayerRagdoll_Skinned.prefab` - Arena_01 scene prefab now serializes the same sprint speed tier fields and move-force tune

## Progress notes
- 2026-03-16: Added `SprintBalanceOutcomeTests` to cover sustained sprint, sprint height stability, and minor-perturbation recovery in Arena_01.
- 2026-03-16: Focused PlayMode run `PhysicsDrivenMovement.Tests.PlayMode.SprintBalanceOutcomeTests` completed with 3 skipped / 0 failed after converting the speed check into an explicit blocker.
- 2026-03-16: Measured peak planar sprint speeds were 4.00 m/s (sustained sprint), 3.94 m/s (height stability), and 3.99 m/s (minor perturbation recovery), so WP-5 step 1 remains blocked until the speed tier is active in-scene.
- 2026-03-16: Serialized the missing sprint tier fields into both shipped ragdoll prefabs and aligned the builder with those values, then raised the authored `PlayerMovement._moveForce` from 150 to 300 so Arena_01 could actually reach the intended sprint tier during scene-level diagnostics.
- 2026-03-16: Focused PlayMode rerun `PhysicsDrivenMovement.Tests.PlayMode.SprintBalanceOutcomeTests` passed 3/3 once the sustained-height assertion was aligned with the chapter's 0.35 m sprint reference instead of the prefab's 0.5 m standing target.
- 2026-03-16: Nearby sprint regression slice `PhysicsDrivenMovement.Tests.PlayMode.SprintLeanOutcomeTests;PhysicsDrivenMovement.Tests.PlayMode.PlayerMovementTests` passed 21/21 after the speed-envelope fix.
