# Plan 11 — Opus Review

## 1. Lean Direction Calculation

`(Quaternion.Inverse(hipsRotation) * Vector3.up)` projected onto XZ **works when the hips
are roughly upright** but degrades exactly when you need it most — at extreme tilt. When
the hips are rotated 30°+ the local-up vector becomes progressively less meaningful as a
"direction of lean" because the projection shrinks and numerical noise dominates near the
gimbal singularity (hips fully horizontal → hips-up is in the XZ plane, projection is
the vector itself, no meaningful axis).

**More robust approach:** Use the world-space tilt vector directly:

```csharp
Vector3 hipsUp = hipsRb.rotation * Vector3.up;
Vector3 leanAxis = Vector3.Cross(Vector3.up, hipsUp);  // perpendicular to tilt plane
Vector3 leanDirection = Vector3.Cross(hipsUp, leanAxis).normalized;
```

Or simpler: take the horizontal displacement of hips-up from world-up:

```csharp
Vector3 hipsUp = hipsRb.rotation * Vector3.up;
Vector3 lean = new Vector3(hipsUp.x, 0f, hipsUp.z);  // horizontal component of hips-up
Vector3 leanDirection = -lean.normalized;              // opposite: direction to step toward
```

This is the XZ rejection of hips-up from world-up, negated. It points toward where the
character is falling and is valid at any tilt angle (degenerates only when perfectly
upright, which is the no-fire case anyway). Cheap, no inverse quaternion, no cross-product
chain. **Use this.**

---

## 2. Step Reach Boost

Temporarily mutating `_stepAngle` on LegAnimator is **fragile**. Problems:

- **Race condition:** If multiple systems call `TriggerCatchStep` or the angle is read
  between the mutation and the restore, the wrong value leaks.
- **Restore timing:** "Restore after one step completes" requires tracking which step was
  the boosted one. If the step is interrupted (surrender, airborne transition, another
  force-plant override), the restore may never fire or may restore at the wrong time.
- **Field ownership:** `_stepAngle` is a serialized Inspector field. Mutating it at runtime
  means the Inspector shows stale values and prefab override detection breaks.

**Better approach:** The existing `LegStateMachine.AdvanceCatchStep()` already has its own
phase-advancing logic. Pass urgency as a parameter through the existing observation or
command pipeline:

1. Add a float field `_catchStepUrgency` on LegAnimator (non-serialized, defaults to 0).
2. `TriggerCatchStep(leanDirection, urgency)` sets `_catchStepUrgency = urgency` and sets
   the appropriate `_forcePlantLeft/Right` flag (the existing mechanism).
3. In `LegExecutionProfileResolver` or the state-driven execution path, when
   `TransitionReason == StumbleRecovery`, read `_catchStepUrgency` and apply
   `+ urgency * 20f` locally to the computed swing angle. No global mutation.
4. LegStateMachine resets `_catchStepUrgency` to 0 on CatchStep exit.

This keeps the boost local to the catch step execution, doesn't touch serialized fields,
and plays nicely with the existing `_forcePlant` → `AdvanceCatchStep` pipeline.

---

## 3. Cooldown vs Continuous

**Recommendation: Continuous with cooldown** (as proposed), but with a re-arm gate.

| Mode | Pros | Failure modes |
|------|------|---------------|
| Continuous + cooldown | Handles sustained destabilisation (slope, persistent push) | Can produce stutter-stepping if cooldown is too short; looks panicky |
| Once-and-wait | Clean for single pushes; no stutter risk | Fails to recover from sustained destabilisation; character leans and topples if single step wasn't enough |

The dominant failure mode in gameplay is **sustained destabilisation** (slopes, collisions,
wonky terrain). Once-and-wait would let the character topple after the single catch step
fails to fully restore balance.

**Mitigations for continuous mode:**
- Cooldown of 0.25s is reasonable but should **increase with consecutive firing count**
  (e.g. 0.25 → 0.4 → 0.6s) to prevent rapid-fire stutter. Reset the counter when
  UprightAngle drops back below threshold.
- **Cap at 3 consecutive catch steps** before force-disabling for a full recovery window
  (~0.5s). If 3 rapid catch steps haven't saved the character, a 4th won't either — let
  the PD controller and existing recovery handle it or surrender.

---

## 4. Arm Brace Direction

**Brace toward the world-space lean direction**, not the character's facing direction.

Reasoning:
- A sideways push produces a sideways lean. Bracing toward facing direction would put arms
  forward while the character falls sideways — looks wrong.
- Humans instinctively reach toward the ground / fall direction, not their facing direction.

**However:** The existing ArmAnimator brace (driven by `LocomotionDirector.IsRecoveryActive`)
is **directionless** — it just dampens swing and tightens elbows. Adding directional arm
reach requires a different mechanism than the existing `_currentBraceBlend`.

Options:
1. **Lean-direction-weighted abduction:** Bias the abduction angle so the arm on the lean
   side extends more (e.g., falling left → left arm reaches outward/downward). This is
   simpler than full directional targeting and avoids fighting the swing axis constraints.
2. **Explicit forward reach via swing angle override:** Blend both arms forward (increased
   positive swing angle) with magnitude from lean direction projected onto the facing axis,
   and increased abduction from the lateral component. This gives directional brace without
   needing new joint axes.

Option 2 composes better with the existing code but needs careful clamping to avoid arms
clipping the torso when lean is backward (character falls onto their back). **Suppress
directional arm brace when lean direction is more than 90° from facing** — that's a backward
fall and arms can't usefully brace.

---

## 5. New Component vs Existing

**A new component is the right call**, but not for the reasons stated.

The plan says "avoids touching the already-complex BalanceController and LegAnimator logic."
That's true, but the stronger reason is **separation of trigger policy from execution
mechanism**:

- `StumbleRecovery` owns the **trigger decision** (when to fire, cooldown, angle thresholds).
- `LegAnimator` owns the **execution** (how to step, CatchStep state machine).
- `ArmAnimator` owns the **visual expression** (brace pose blending).

This is the same pattern already used: `LocomotionDirector` makes recovery decisions,
`LegAnimator` executes catch steps, `ArmAnimator` reads `IsRecoveryActive`.

**Tradeoff risk:** Two independent systems can now trigger catch steps — `LocomotionDirector`
(via collapse detection / support risk) and `StumbleRecovery` (via angle threshold). These
must not fight. See §6.

If the intent is that `StumbleRecovery` **replaces** the angle-based part of the
LocomotionDirector's catch-step triggering, integrate it there instead. If it's **additive**
(fires at lower angles before the director kicks in), keep it separate but add mutual
awareness.

---

## 6. Risks and Failure Modes Not Covered

### 6a. Dual-trigger conflict
**Critical.** The existing system already triggers catch steps via `LocomotionDirector` →
`_forcePlantLeft/Right` when `IsLocomotionCollapsed || bothFeetFarBehind`. The plan adds a
second trigger via `StumbleRecovery` writing to the same `_forcePlant` flags. If both fire
in the same frame with different leg choices, the second write wins silently.

**Fix:** Either:
- Route `StumbleRecovery` through `LocomotionDirector` as a new `RecoverySituation` (e.g.
  `PostureTilt`), so the existing priority/cooldown logic arbitrates.
- Or have `StumbleRecovery` check `LocomotionDirector.IsRecoveryActive` and skip if the
  director is already managing a catch step.

### 6b. Surrender interaction
The plan gates on `UprightAngle < _fallenEnterAngleThreshold` (65°) but doesn't check
`IsSurrendered`. Surrender fires at 80° (extreme angle) or at 65° + angular velocity. If
surrender has already fired, balance torque is being ramped down — a catch step at this
point will try to plant a leg but the body has no torque to recover. The step becomes a
flailing gesture with no recovery value.

**Fix:** Skip catch step when `_balance.IsSurrendered` is true.

### 6c. Limbo dwell interaction
When the character is in the `Standing` state with forced limbo dwell (`_limboForcedDwellTimer > 0`),
it can still be tilted. A catch step during limbo dwell would conflict with the dwell logic
that's trying to hold the character in Standing before allowing Airborne transition.

Likely not critical (limbo dwell is short), but worth a guard comment.

### 6d. Existing arm brace already fires during recovery
`ArmAnimator` already reads `LocomotionDirector.IsRecoveryActive` and applies brace
(swing dampen + elbow tighten). The plan's `TriggerBracePose` adds a **second, independent
brace system** with its own timer and intensity. Both will be active simultaneously during
director-classified recovery events, leading to over-dampened arms (double brace).

**Fix:** Either:
- Make `TriggerBracePose` **replace** the existing brace blend (set `_currentBraceBlend`
  directly and suppress the director-driven path while the timer is active).
- Or route the plan's brace through the existing `_currentBraceBlend` by having
  `StumbleRecovery` signal recovery to `LocomotionDirector`, which in turn drives the
  existing arm brace path.

### 6e. Airborne edge case timing
The plan gates on "not Airborne" via CharacterState, but `_movingAirborneGuardFrames = 4`
means there's a 4-frame window where the character is ungrounded but still in Moving state.
A catch step during this window would try to plant a foot in midair. The `_forcePlant`
mechanism was designed for grounded idle micro-steps.

**Fix:** Also check `_balance.IsGrounded` directly, not just `CharacterState != Airborne`.

### 6f. No interaction with jump landing gains
`BalanceController._jumpLandingGainBoost` temporarily stiffens PD during landing. If a
catch step fires during this window (landing from a jump with forward tilt), the stiffened
PD + catch step may over-correct and cause a backward stumble.

Low risk but worth noting in tuning guidance.

---

## 7. Slice Ordering

**1→2→3 is correct** with one modification:

The slice ordering (legs → arms → tests) is logical — legs are the functional recovery,
arms are cosmetic, tests validate both.

**Split suggestion:** Extract "integrate with existing systems" from Slice 1 into a
**Slice 0.5 — Audit and decide trigger routing**. Before writing `StumbleRecovery.cs`,
decide:
- Does it go through `LocomotionDirector` as a new `RecoverySituation`, or is it
  independent?
- Does `TriggerBracePose` replace or stack with the existing brace?
- What's the priority when both the director and `StumbleRecovery` want a catch step?

This is a design decision, not an implementation task, but getting it wrong means
reworking Slices 1 and 2. A 30-minute design spike before Slice 1 saves that rework.

**Also:** Move Test 2 (`DoesNotFireWhenAirborne`) into Slice 1 as a guard test written
before the implementation — it's a critical safety constraint, not a polish item.
