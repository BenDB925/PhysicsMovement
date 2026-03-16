# Ch3: Knockdown Timer & Floor State

## Goal
After surrender (Ch1) or external impact knockdown (Ch2), the character stays ragdolled on the ground for a comedic duration before attempting to stand. The floor dwell time scales with knockdown severity. Optionally add subtle dazed twitches for flavor.

## Current status
- State: Not started
- Current next step: Rework CharacterState Fallen/GettingUp transitions
- Blockers: Depends on Ch1 (surrender signal) and Ch2 (KnockdownEvent)

## Current behavior
- `CharacterState` enters `Fallen` and begins the stand-up attempt after only `_getUpDelay` (0.5 s) + `_knockoutDuration` (1.5 s) = 2 s total
- During this time, PD upright torque is still active (fighting to right the character)
- `_getUpForce` impulse fires immediately once timers expire
- Result: character barely touches the ground before magic-forcing back upright

## Design

### Knockdown severity
A `KnockdownSeverity` struct or utility computes a 0–1 value at the moment of knockdown:

| Source | Severity formula |
|--------|-----------------|
| Surrender (Ch1, internal) | `Clamp01((uprightAngle - 65) / 50) * 0.5 + Clamp01(angularVelocity / 6) * 0.3 + Clamp01(1 - hipsHeight / standingHeight) * 0.2` |
| External impact (Ch2) | `Clamp01(effectiveDeltaV / (_impactKnockdownDeltaV * 2))` |
| Combined (impact while already tilting) | `Max(internalSeverity, externalSeverity)` clamped to 1.0 |

### Floor dwell duration
```
floorDwell = Lerp(_minFloorDwell, _maxFloorDwell, severity)
```

| Parameter | Default | Description |
|-----------|---------|-------------|
| `_minFloorDwell` | 1.5 s | Light knockdown — quick pratfall |
| `_maxFloorDwell` | 3.0 s | Heavy knockdown — full face-plant, lies there |
| Default at severity 0.5 | ~2.25 s | The "couple seconds" sweet spot |

### Floor state behavior
During the entire floor dwell:
1. **Upright torque**: Stays at 0 (from Ch1 surrender)
2. **Height maintenance**: Stays at 0
3. **COM stabilization**: Stays at 0
4. **Joint springs**: Stay at limp profile (20–30% of normal, from Ch1)
5. **Gait phase**: Stopped — legs are passive
6. **Player input**: Ignored (no movement response). Camera still follows.

### Dazed twitches (optional, low priority)
During the last 30% of floor dwell, apply small random torque impulses to limbs:
- Magnitude: ±5–15 N·m on random axes
- Frequency: every 0.3–0.5 s on a random limb
- Purpose: subtle "trying to move" that sells the comedy without looking like a seizure
- Gated by a `_enableDazedTwitches` bool (default true, easy to disable)

### State machine changes
Rework the `Fallen` → `GettingUp` transition in `CharacterState`:

```
Standing/Moving/Airborne
    │
    ▼ (surrender signal OR impact knockdown)
  Fallen
    │ ← floorDwell timer starts here
    │ ← upright torque = 0, joints limp, input ignored
    │ ← optional dazed twitches in last 30%
    │
    ▼ (floorDwell timer expires)
  GettingUp ← this now means "procedural stand-up sequence" (Ch4)
    │
    ▼ (stand-up sequence completes)
  Standing/Moving
```

Remove the old `_getUpDelay` + `_knockoutDuration` + `_getUpForce` impulse logic. The `GettingUp` state now delegates to the `ProceduralStandUp` component (Ch4).

### Re-knockdown during floor dwell
External impacts (Ch2) during floor dwell that exceed `_impactKnockdownDeltaV * 0.4` (lower threshold — character is vulnerable on the ground):
- Reset the floor dwell timer
- Recompute severity (use max of current and new)
- This prevents stunlocking by capping total floor time at `_maxFloorDwell * 1.5`

## Files to modify
| File | What changes |
|------|-------------|
| `CharacterState.cs` | Rework `Fallen` state: add floor dwell timer, severity-based duration, remove old `_getUpDelay`/`_knockoutDuration`/`_getUpForce`. Transition to `GettingUp` only after dwell expires. Handle re-knockdown during dwell. |
| **New: `KnockdownSeverity.cs`** | Static utility or small struct: `Compute(angle, angularVel, hipsHeight, standingHeight)` and `ComputeFromImpact(effectiveDeltaV, threshold)`. |
| `BalanceController.cs` | Ensure upright torque stays zeroed during floor dwell (respect `CharacterState.CurrentState == Fallen`). |
| `ImpactKnockdownDetector.cs` (from Ch2) | Lower re-knockdown threshold during Fallen state. |

## Acceptance criteria
- [ ] After surrender, character stays on ground for severity-appropriate duration (1.5–3.0 s)
- [ ] Light knockdown (severity ~0.3): ~1.9 s floor dwell
- [ ] Heavy knockdown (severity ~0.9): ~2.85 s floor dwell
- [ ] Character is fully ragdolled during floor dwell (no upright torque, no height maintenance)
- [ ] Player input is ignored during floor dwell
- [ ] External re-hit during floor dwell resets timer (capped at _maxFloorDwell * 1.5)
- [ ] Floor dwell expiry transitions to GettingUp (Ch4)
- [ ] Dazed twitches visible in last 30% of dwell (when enabled)

## Decisions
- (pending)

## Progress notes
- 2026-03-16: Chapter spec written
