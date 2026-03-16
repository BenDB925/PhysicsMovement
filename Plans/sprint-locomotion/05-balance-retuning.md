# WP-5: Balance Retuning at Sprint Speed

## Goal
Confirm the BalanceController PD gains, COM stabilization, and height-spring hold up at sprint speed (~8–10 m/s). Retune only if testing reveals oscillation, over-damping, or collapse.

## Current status
- State: Not started
- Current next step: Run existing balance/gait tests at sprint speed to see if they pass
- Blockers: WP-1 (speed tier), WP-2 (lean) must be in place first

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
- **Assert**: CharacterState never enters `Fallen`. Hips height stays within ±0.1 m of target height (0.35 m) after initial ramp.

### T5-2: Sprint_HeightStableAtSpeed
- **Setup**: Sprint 5 s, sample hips Y every frame.
- **Assert**: Standard deviation of hips Y over the last 3 s < 0.05 m (no pumping).

### T5-3: Sprint_RecoveryFromMinorPerturbation
- **Setup**: Sprint 3 s, apply a small lateral impulse (50 N × 0.1 s), continue sprinting 3 s.
- **Assert**: Character recovers to upright within 1.5 s and does not fall.

## Decisions

## Artifacts

## Progress notes
