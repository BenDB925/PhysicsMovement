# Plan 05 — Organic Movement & Gait Variation

**Status:** Active
**Current next step:** Finish Slice 4A by adding IdleSwayTests, tuning the prefab sway overrides if needed, and running the focused PlayMode filter `IdleSwayTests|OrganicGaitVariationTests|LateralPlacementNoiseTests|JumpTests|SprintJumpStabilityTests`.

Active slice checkpoint: `slice/05-4a-idle-sway` now contains the grounded-only idle sway force path in `LegAnimator.cs`, the new `IdleSwayTests.cs` outcome suite, and successful `dotnet build` checks for both `PhysicsDrivenMovement.Character.csproj` and `PhysicsDrivenMovement.Tests.PlayMode.csproj`. Focused Unity PlayMode execution and any prefab tuning are still pending.
| 2026-03-24 | 05-4a | GitHub Copilot | wip | Added the idle-sway force path in `LegAnimator`, authored `IdleSwayTests`, and verified both the character and PlayMode test assemblies compile on `slice/05-4a-idle-sway`. Focused Unity execution and final prefab tuning are still pending. |
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
- **Speed-linked tightening** — variation narrows at sprint (athletic, deliberate) and opens up at walk/idle (relaxed, lazy). All noise bounds lerp continuously with current speed — no discrete snapping between walk/sprint values.
- **Physics-first** — prefer signals already in the simulation (speed, foot reach deficit, tilt angle) over arbitrary timers. Where possible, apply perturbations and let the balance controller react naturally rather than scripting the result.
- **Subtle asymmetry** — the character has a consistent slight natural bias (left stride fractionally longer than right), subconscious rather than obvious
- **No test regressions** — `JumpTests|SprintJumpStabilityTests|JumpGapOutcomeTests` must stay green throughout
- **Local cosmetic only** — all organic variation is client-side cosmetic. No network sync. Remote players will see different foot placements; this is acceptable for the current phase.

---

## Pre-slice requirements (Slice 0 — must land before any other slice)

Before any noise is added, the foundation must exist:

1. **`_noiseSeed` serialized field** on `LegAnimator` (and `ArmAnimator` when reached). Agents use `new System.Random(_noiseSeed)` — never `UnityEngine.Random` which is global and non-deterministic.
2. **`_disableOrganicVariation` bool** (serialized, default false) — when true, all noise/variation is bypassed and the system behaves exactly as before this plan. This is the test seam. All new PlayMode tests for this plan set this flag to false; all *existing* tests must be unaffected because variation was never in the system before.
3. **Test seam method** `SetOrganicVariationSeedForTest(int seed)` — allows tests to set a known seed and get deterministic results.

These three items are the first commit of Slice 1.

---

## Compounding budget

Slices 1, 2, and 3 all modify foot placement. Worst case they apply simultaneously. The combined cap:
- **Total deviation from nominal landing position: ≤ 4cm in any direction**
- Each slice must check remaining budget before applying its offset (or agents must be given the combined formula upfront)

---

## Slices

### Slice 1 — Foundation + per-stride step angle noise
**File:** `LegAnimator.cs`

**Part A — Foundation (first commit):**
- Add `_noiseSeed` (serialized int, default 42), `_disableOrganicVariation` (serialized bool, default false), `SetOrganicVariationSeedForTest(int seed)` test seam
- Add `_organicRng` as a `System.Random` instance, re-seeded from `_noiseSeed` in `Awake`

**Part B — Step angle noise:**
- Add `_leftStepNoise` / `_rightStepNoise` floats regenerated on step trigger
- Walk bounds: ±8°, sprint bounds: ±4°. Lerp between based on `SprintNormalized`.
- Clamp final `effectiveStepAngle` to [30°, 90°] absolute — these are the safe bounds for the current simulation
- Noise bypassed when `_disableOrganicVariation = true`

**Tests:**
- Seed to known value. Run 20 steps at walk speed. Assert: stddev of effectiveStepAngle > 1°, max deviation from nominal < 8°.
- Repeat at sprint. Assert max deviation < 4°.
- Assert determinism: same seed → identical 20-step sequence.
- All 26 existing tests still green.

**Exit criteria:** Visibly varied stride length each step; sprint tighter; deterministic with known seed; all existing tests green.

---

### Slice 2 — Lateral foot placement variation
**File:** `LegAnimator.cs`

**What:** Per-step lateral offset to `StepTarget.LandingPosition` perpendicular to gait direction. Regenerated at step trigger using `_organicRng`.
- Walk: ±2.5cm, sprint: ±1cm. Lerp based on `SprintNormalized`.
- Minimum inter-foot lateral distance: 8cm. If applying the offset would bring feet closer than 8cm, clamp it.
- Bypassed when `_disableOrganicVariation = true`
- Combined with Slice 1's angle offset, total foot displacement from nominal must not exceed 4cm

**Tests:**
- Seed to known value. Run 20 steps. Assert: lateral offsets vary, max |offset| ≤ 2.5cm at walk, ≤ 1cm at sprint.
- Assert: minimum simultaneous left/right foot lateral separation always ≥ 8cm.

**Exit criteria:** Top-down view shows varied foot track width; never looks like a stagger; feet never clip.

---

### Slice 3 — Subtle natural asymmetry
**File:** `LegAnimator.cs`

**What:** Consistent 4% longer left stride. Serialized as `_leftStrideAsymmetry: 1.04` / `_rightStrideAsymmetry: 1.0` in prefab (default 1.0 each in code). Applied as a multiplier to `effectiveStepAngle` before noise is added.
- Bypassed when `_disableOrganicVariation = true`
- Combined with Slice 1+2, total foot displacement still ≤ 4cm from nominal

**Tests:**
- Run 20 steps. Measure mean step angle left vs right.
- Assert: mean left / mean right within [1.02, 1.06].

**Exit criteria:** Not consciously noticeable as a limp; subconsciously feels real.

---

### Slice 4A — Idle lateral sway
**File:** `LegAnimator.cs` (COM offset injection) + read `BalanceController` deadzone before choosing amplitude

**What:** When `SmoothedInputMag` < 0.05 for > 0.5s, introduce a slow lateral COM sway.
- **Implementation approach (physics-first):** apply a tiny lateral perturbation force to the hips Rigidbody at `~0.8Hz` using a sine wave. Let the balance controller react naturally — micro-steps and weight shifting emerge from its normal recovery behaviour rather than being scripted.
- Amplitude: tuned so it stays **inside** the balance controller's upright correction deadzone (read `_surrenderAngleThreshold` from BalanceController before choosing — sway must not trigger surrender detection).
- Start value: ±0.8cm lateral force perturbation. Agent should read the deadzone and confirm this is safe before committing.
- Fade out: lerp perturbation amplitude to zero over 0.3s when `SmoothedInputMag` rises above 0.05.
- Bypassed when `_disableOrganicVariation = true`

**Tests:**
- Set input to zero. Wait 3 seconds (300 frames at 100Hz). Record hips lateral position at 10-frame intervals.
- Assert: position is not static (stddev > 0.2cm), peak-to-peak < 3cm.
- Then inject forward input. Assert: within 30 frames (0.3s), lateral displacement stddev drops below 0.2cm.
- Assert: character does not fall (state != Fallen) throughout entire test.

**Exit criteria:** Standing still has visible lateral sway; instant fade on input; no oscillation or jitter fighting the balance controller; no falls.

---

### Slice 4B — Idle vertical bob + micro-adjustment step
**File:** `LegAnimator.cs`

**What:**
- **Vertical bob:** very small (~0.4cm amplitude) ~0.3Hz vertical oscillation of hips height target. Applied as an additive offset to the height maintenance target only while `SmoothedInputMag < 0.05`. Fades out with same 0.3s ramp as 4A.
- **Micro-adjustment step:** if idle for > 2.5s AND the character has drifted > 1cm from its original idle position (due to 4A sway), trigger a tiny corrective re-plant on whichever foot is furthest from centre. This should look like the character briefly shifting weight.
- Both bypassed when `_disableOrganicVariation = true`
- Depends on Slice 4A being present — do not implement without it

**Tests:**
- Idle for 3s. Record hips Y position at 10-frame intervals.
- Assert: Y position varies (stddev > 0.1cm), range < 1cm total.
- Assert: at least one micro-step fires within 3s of idle (step count increments).
- Assert: no fall throughout.

**Exit criteria:** Subtle breathing-like feel when standing; micro-steps look like natural weight shifting; no test regressions.

---

### Slice 5 — Arm swing variation & phase offset
**File:** `ArmAnimator.cs`

**What:** Add `_armNoiseSeed` / `_armOrganicRng` (same pattern as LegAnimator). Per-stride amplitude modifier:
- Walk: ±15% amplitude variation per arm per stride cycle, independently
- Sprint: ±5%
- Left arm phase offset drifts ±5° from counter-phase over a 3s sine cycle — creates gentle non-mechanical feel
- All bypassed when `_disableOrganicVariation = true` (add this field to ArmAnimator too, or read from LegAnimator)

**Tests:**
- Run 20 strides. Record arm swing amplitude per stride per arm.
- Assert: amplitude varies (stddev > 1°), within ±15% of nominal at walk, ±5% at sprint.
- Assert: left arm phase occasionally differs from pure counter-phase by > 1°.

**Exit criteria:** Arms look natural and slightly independent; sprint looks athletic and tight.

---

### Slice 6 — Momentum lean on direction change
**File:** `BalanceController.cs` — specifically the lean target / desired tilt angle computation. NOT the COM stabilization forces. The low-pass filter should apply to the cosmetic lean direction used for visual tilt, feeding a separate `_momentumLeanOffset` that is added to the lean target but does NOT affect the COM stabilization or upright recovery torques.

**What:** When horizontal velocity direction changes, the visual lean direction lags behind using a low-pass filter with ~0.15s time constant. Stronger at higher speeds (scale by `speed / maxSpeed`). Apply only to the visual lean, not to physics forces.
- Must NOT touch `_kP`, `_kD`, COM stabilization, or upright recovery threshold
- Must NOT create a feedback loop: lean offset must be read-only by the physics system
- If no clean separation point exists in BalanceController, implement as a post-physics cosmetic rotation on a visual-only transform instead — do not modify the physics path

**Tests:**
- Run at sprint. Reverse input. Record lean angle for 60 frames after reversal.
- Assert: lean continues in original direction for at least 10 frames (0.1s).
- Assert: lean returns to < 5° from neutral within 50 frames (0.5s).
- Assert: character does not fall (no Fallen state) within 150 frames after reversal.

**Exit criteria:** Sharp reversals show brief body lag; slow turns unaffected; zero instability.

---

### Slice 7 — Regression gate + parameter summary
**File:** Tests only + doc update

**What (agent task):**
- Run full regression: `JumpTests|SprintJumpStabilityTests|JumpGapOutcomeTests|MovementQualityTests|LapCourseTests`
- Fix any failures introduced by slices 1–6
- Output a summary table of all new serialized parameters and their current prefab values
- Update this plan doc with final values

**Human task (Benny):** Play test and adjust prefab values. The agent provides the table; the human tunes.

**Exit criteria:** All regression tests green. Summary table written. Benny plays it and adjusts values to taste.

---

## Files modified

- `Assets/Scripts/Character/LegAnimator.cs` — slices 1–4B
- `Assets/Scripts/Character/ArmAnimator.cs` — slice 5
- `Assets/Scripts/Character/BalanceController.cs` — slice 6
- `Assets/Prefabs/PlayerRagdoll_Skinned.prefab` — all slices
- `Assets/Tests/PlayMode/Character/` — new tests per slice

---

## Test filter (must stay green throughout)
Core: `"JumpTests|SprintJumpStabilityTests|JumpGapOutcomeTests"`
Slice 7 gate: `"JumpTests|SprintJumpStabilityTests|JumpGapOutcomeTests|MovementQualityTests|LapCourseTests"`

---

## Agent log

| Date | Slice | Agent | Result | Notes |
|------|-------|-------|--------|-------|
| 2026-03-23 | Plan | Opus review | — | Critical gaps identified; plan revised |
| 2026-03-24 | 05-3 | OpenClaw main | fail | Added serialized stride asymmetry fields, prefab defaults, and new StrideAsymmetryTests. Target slice tests improved from 27/31 to 30/31 green after isolating organic-noise tests and softening the asymmetry path, but SprintJumpStabilityTests still regresses (Landing #1 peak tilt 55.9° > 50° in TwoConsecutiveJumps). |

**Current next step:** Investigate why stride asymmetry is still leaking into sprint-jump landing posture and neutralise that path without losing the walk-speed left/right ratio coverage.
