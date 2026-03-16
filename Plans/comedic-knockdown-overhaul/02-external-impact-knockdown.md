# Ch2: External Impact Knockdown

## Goal
Allow world objects (swinging hammers, thrown items, explosions, other players) to knock the character down regardless of internal balance state. A hard enough hit topples even a perfectly balanced character. This is the foundation for future brawler mechanics.

## Current status
- State: Not started
- Current next step: Design impact detection approach
- Blockers: None

## Current behavior
- External collisions push the ragdoll via Unity physics but the PD upright torque (kP=2000) fights to keep the character upright
- No system measures "how hard was I just hit?" to decide if a knockdown should occur
- Result: character tanks big impacts by brute-forcing upright torque, which looks unnatural

## Design

### New component: `ImpactKnockdownDetector`
A MonoBehaviour placed on the Hips rigidbody (or a central body part) that monitors incoming collision impulses.

### Detection approach
**Option A — OnCollisionEnter impulse magnitude (recommended)**:
- Unity's `Collision.impulse` gives the total impulse applied during the contact
- Compare `collision.impulse.magnitude / hipsRigidbody.mass` (effective delta-v) against a threshold
- Pros: simple, frame-accurate, works with any collider
- Cons: only captures the first contact frame; sustained pushes need separate handling

**Option B — Velocity delta sampling**:
- Sample hips velocity each `FixedUpdate`, detect sudden delta-v spikes
- Pros: catches sustained forces too
- Cons: harder to attribute to external vs. internal, more false positives from self-collision

**Recommendation**: Start with Option A for discrete impacts (hammer, projectile). Add Option B later if sustained pushes (bulldozer, conveyor belt) need to knock down too.

### Knockdown thresholds
| Parameter | Default | Description |
|-----------|---------|-------------|
| `_impactKnockdownDeltaV` | 5.0 m/s | Minimum effective delta-v to trigger instant knockdown |
| `_impactStaggerDeltaV` | 2.5 m/s | Minimum delta-v to trigger a stagger (feeds existing recovery, not instant knockdown) |
| `_impactCooldown` | 1.0 s | Ignore subsequent impacts for this long after a knockdown fires (prevents re-triggering during the same tumble) |
| `_impactDirectionWeight` | 0.7 | How much the impact direction (vs. up-vector) matters — a hit to the side of the head is worse than a hit straight down on the shoulders |

### Impact severity computation
```
rawDeltaV = collision.impulse.magnitude / rb.mass
directionFactor = Lerp(1.0, lateralComponent / rawDeltaV, _impactDirectionWeight)
effectiveDeltaV = rawDeltaV * directionFactor

if effectiveDeltaV >= _impactKnockdownDeltaV:
    → instant surrender (Chapter 1 pathway) with severity = Clamp01(effectiveDeltaV / (_impactKnockdownDeltaV * 2))
elif effectiveDeltaV >= _impactStaggerDeltaV:
    → apply angular impulse in hit direction, let existing recovery handle it
    → if recovery fails within timeout, Chapter 1 surrender kicks in naturally
```

### Integration with surrender system (Ch1)
- `ImpactKnockdownDetector` calls `BalanceController.TriggerSurrender(severity)` directly when the knockdown threshold is exceeded
- This bypasses the normal angle-based surrender check — you don't need to be tilting already
- The severity value feeds into Chapter 3's knockdown timer for floor dwell duration
- During `GettingUp` state (Ch4): impacts above `_impactKnockdownDeltaV * 0.6` re-trigger knockdown (you're vulnerable while standing up)

### Layer / tag filtering
- Ignore self-collisions (character's own body parts) — use a layer mask or check `collision.gameObject.GetComponentInParent<CharacterState>() == this`
- Ignore ground plane contacts
- Allow filtering by tag for future "non-damaging" objects that shouldn't knock down

### Future brawler hooks
The `ImpactKnockdownDetector` produces a `KnockdownEvent` struct:
```csharp
public struct KnockdownEvent
{
    public float Severity;           // 0–1
    public Vector3 ImpactDirection;  // world-space, normalized
    public Vector3 ImpactPoint;      // world-space contact point
    public float EffectiveDeltaV;    // m/s
    public GameObject Source;         // what hit us
}
```
This is consumed by the knockdown timer (Ch3) now, and can be consumed by a future damage/health system without changing the detection layer.

## Files to create / modify
| File | What changes |
|------|-------------|
| **New: `ImpactKnockdownDetector.cs`** | New MonoBehaviour. `OnCollisionEnter` handler, threshold checks, cooldown timer, severity computation, self-collision filtering. |
| **New: `KnockdownEvent.cs`** | Small struct for impact data. |
| `BalanceController.cs` | New public `TriggerSurrender(float severity)` method that Ch1's internal trigger and this external trigger both call. |
| `CharacterState.cs` | Accept knockdown events from the impact detector (same pathway as Ch1 surrender). |
| Prefab: `PlayerRagdoll` | Add `ImpactKnockdownDetector` component to Hips. |

## Acceptance criteria
- [ ] Launching a physics object at the character at ≥ 5 m/s delta-v knocks him down
- [ ] Smaller impacts (2.5–5 m/s) cause visible stagger but recovery can save it
- [ ] Self-collisions (arms hitting legs) do not trigger knockdown
- [ ] Ground contact does not trigger knockdown
- [ ] Impact during `GettingUp` at 60% threshold re-triggers knockdown
- [ ] `KnockdownEvent` struct is populated with correct source, direction, severity
- [ ] Cooldown prevents rapid re-triggering from multi-frame collision contacts

## Decisions
- (pending) Whether to use OnCollisionEnter vs. velocity sampling — recommendation is OnCollisionEnter first

## Progress notes
- 2026-03-16: Chapter spec written
