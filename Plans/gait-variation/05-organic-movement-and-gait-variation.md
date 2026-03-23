# Plan 05 — Organic Movement & Gait Variation

**Status:** Planning
**Branch prefix:** `slice/05-N-name`
**Watcher state file:** `C:\Users\Usuario\.openclaw\workspace\scripts\gait-watcher-state.json`
**Slice prompts dir:** `H:\Work\PhysicsDrivenMovementDemo\Plans\gait-variation\prompts\`

---

## Goal

The character currently moves like a well-balanced robot — every stride identical, every stop perfectly still. This plan adds organic variation so each stride feels slightly different, idle has life in it, and direction changes carry weight. Human-like but slightly exaggerated, never glitchy or limp-inducing.

---

## Design Principles

- **Bounded noise only** — every variation has a hard cap; can never cause a fall or look broken
- **Speed-linked tightening** — variation narrows at sprint (athletic, deliberate) and opens up at walk/idle (relaxed, lazy)
- **Physics-first** — prefer signals already in the simulation (speed, foot reach deficit, tilt angle) over arbitrary timers
- **Subtle asymmetry** — the character has a consistent slight natural bias (left stride fractionally longer than right), subconscious rather than obvious
- **No test regressions** — `JumpTests|SprintJumpStabilityTests|JumpGapOutcomeTests` must stay green throughout

---

## Slices

### Slice 1 — Per-stride step angle noise
**File:** `LegAnimator.cs`
**What:** Add a small per-step random variation to `effectiveStepAngle` each time a new step is triggered. Seeded at step acceptance so it's stable within a stride but different next time.
- Walk: ±8° variation
- Sprint: ±4° (tighter at speed — more deliberate)
- Implementation: keep a `_leftStepNoise` / `_rightStepNoise` float, regenerate on step trigger, apply as additive offset to `effectiveStepAngle` before passing to swing computation
- Must not cause foot to land in invalid position — clamp final angle within safe bounds

**Exit criteria:** Walking in circles shows visibly varied stride length each step; sprint looks tighter; no test regressions.

---

### Slice 2 — Lateral foot placement variation
**File:** `LegAnimator.cs`
**What:** Add a small per-step lateral offset to the step target landing position. Right now both feet land on exactly the same lateral line — too tightrope-walker.
- Walk: ±2.5cm lateral variation per step
- Sprint: ±1cm (barely any — sprinting is straight-line)
- Implementation: add `_leftLateralNoise` / `_rightLateralNoise` offset regenerated at step trigger, applied to `StepTarget.LandingPosition` in world space perpendicular to gait direction
- Must not cause feet to clip through each other or land outside capsule shadow

**Exit criteria:** Top-down view shows slightly varied foot track width each stride; never looks like a stagger.

---

### Slice 3 — Subtle natural asymmetry
**File:** `LegAnimator.cs`
**What:** Give the character a consistent slight natural bias — left stride is fractionally longer than right (like most real people). Consistent across all speeds, just a permanent small offset.
- Left step angle multiplier: 1.04× (4% longer)
- Right step angle multiplier: 1.0× (baseline)
- Serialized as `_leftStrideAsymmetry: 1.04` / `_rightStrideAsymmetry: 1.0` in prefab so it can be tuned or zeroed
- Should be invisible consciously but feel right subconsciously

**Exit criteria:** Character never looks like it has a limp; but watching closely you can see it's not a perfect mirror.

---

### Slice 4 — Idle weight shifting
**File:** `LegAnimator.cs` (and possibly `BalanceController.cs` for height target)
**What:** When `SmoothedInputMag` drops to near zero (standing still), introduce subtle idle behaviour:
- Slow lateral COM sway — gentle sine wave, ~0.8Hz, ±1.5cm lateral shift applied to balance controller height target or COM offset
- Occasional micro-adjustment step — if idle for >2s and sway reaches a threshold, trigger a tiny corrective foot plant (just a small re-plant, not a full step)
- Subtle breathing-like vertical bob — very small (~0.5cm amplitude), ~0.3Hz — blends in fully only when completely still
- All idle behaviours fade out within ~0.3s as soon as movement input returns — snap back to responsive

**Exit criteria:** Standing still has visible life; micro-adjustments look natural; movement input instantly overrides idle; no test regressions.

---

### Slice 5 — Arm swing variation & phase offset
**File:** `ArmAnimator.cs`
**What:** Right now arms are a perfect mirror of legs. Add:
- Per-stride amplitude variation: ±15% random amplitude modifier per arm per stride cycle (left/right independently)
- Slight phase drift: left arm phase offset drifts ±5° from perfect counter-phase over a slow 3s cycle, creating a gentle non-mechanical feel
- At sprint: amplitude variation tightens to ±5%, phase drift nearly zero (pumping arms, focused)
- Implementation: `ArmAnimator` already reads `LegAnimator.Phase` — add a `_leftArmAmplitudeScale` / `_rightArmAmplitudeScale` that slowly varies with a low-frequency noise signal

**Exit criteria:** Arms look natural and slightly independent; sprint looks athletic and tight; idle arm sway is subtle.

---

### Slice 6 — Momentum lean on direction change
**File:** `PlayerMovement.cs` or `BalanceController.cs`
**What:** When the character changes horizontal direction, the upper body (COM/facing target) lags slightly before committing to the new direction. Like a person's torso taking a fraction of a second to follow their feet.
- Implement as a low-pass filter on the facing/lean direction with a short time constant (~0.15s) — fast enough to feel responsive, slow enough to show the lag
- Stronger effect at higher speeds (more momentum to carry)
- Must not fight the balance controller's upright recovery — apply only to the facing lean direction, not to COM stabilization

**Exit criteria:** Sharp direction reversals show a brief body lean in the old direction before correcting; slow turns show nothing noticeable; no instability.

---

### Slice 7 — Tuning pass
**What:** Play with all the values. Observe the combined effect. Adjust magnitudes so the whole thing reads as "human, slightly exaggerated" not "glitchy" or "limp".
- Final prefab values locked in
- All 26 jump/sprint tests green
- Broader regression check: `MovementQualityTests|LapCourseTests` also green
- Document final tuned values in plan doc

**Exit criteria:** Benny plays it and says it feels good.

---

## Files likely modified

- `Assets/Scripts/Character/LegAnimator.cs` — slices 1, 2, 3, 4
- `Assets/Scripts/Character/ArmAnimator.cs` — slice 5
- `Assets/Scripts/Character/PlayerMovement.cs` or `BalanceController.cs` — slice 6
- `Assets/Prefabs/PlayerRagdoll_Skinned.prefab` — all slices (serialized values)
- `Assets/Tests/PlayMode/Character/` — adjustments if needed (slice 7)

---

## Test filter (must stay green throughout)
`"JumpTests|SprintJumpStabilityTests|JumpGapOutcomeTests"`

Slice 7 broader gate: `"JumpTests|SprintJumpStabilityTests|JumpGapOutcomeTests|MovementQualityTests|LapCourseTests"`

---

## Agent log

| Date | Slice | Agent | Result | Notes |
|------|-------|-------|--------|-------|
| — | — | — | — | Plan created |
