# Comedic Knockdown Overhaul

## Status
- State: Active
- Acceptance target: Character falls over naturally when situation is unrecoverable OR hit by external force, stays on the ground for a comedic beat, then physically stands back up through a staged sequence
- Current next step: Step 3 — CharacterState — Accept Surrender + KnockdownSeverity Utility
- Active blockers: None

## Quick Resume
- This plan overhauls falling, floor-dwell, and stand-up to be comedic and physics-driven
- Character should be sure-footed normally but embrace the fall when situations are hopeless or external forces demand it
- 15 atomic steps grouped under 5 chapters, each scoped for single-agent execution
- Chapter design docs live under `Plans/comedic-knockdown-overhaul/` for rationale and tuning reference

## Design Principles
1. **Sure-footed by default.** Existing recovery (PD torque, collapse detection, locomotion boosts) stays active and keeps the character upright under normal conditions.
2. **Surrender, don't fight.** When the situation is clearly unrecoverable — or an external hit demands it — stop resisting and let ragdoll physics do its thing. Flailing limbs, natural tumble.
3. **Let the comedy land.** Stay on the ground for ~2–2.5 s (tunable, scaled by severity). Don't rush the stand-up.
4. **Stand up physically.** Replace the magic impulse with a staged push-up-from-belly sequence. Each phase is physics-gated, and the whole thing can fail and restart.
5. **External forces matter.** A big enough hit should knock the character down regardless of internal balance state. Future brawler mechanics can layer damage on top.
6. **Knockdown severity.** A float 0–1 derived from impact/angle at surrender time scales floor duration and stand-up difficulty. Light stumble → short dwell; full slam → long dwell.

## Chapter design docs (rationale & tuning reference)
- Ch1 — Surrender Threshold: `Plans/comedic-knockdown-overhaul/01-surrender-threshold.md`
- Ch2 — External Impact Knockdown: `Plans/comedic-knockdown-overhaul/02-external-impact-knockdown.md`
- Ch3 — Knockdown Timer & Floor State: `Plans/comedic-knockdown-overhaul/03-knockdown-timer-floor-state.md`
- Ch4 — Procedural Stand-Up Sequence: `Plans/comedic-knockdown-overhaul/04-procedural-standup.md`
- Ch5 — Test & Tuning Pass: `Plans/comedic-knockdown-overhaul/05-test-tuning-pass.md`

---

## Execution Steps

Each step is scoped so one agent can complete it in a single session. Steps list
exactly which files to **read** (context) and **write** (output), plus a concrete
done-check. Steps within a chapter are sequential; chapters are sequential.

### Chapter 1 — Surrender Threshold (Steps 1–4)

#### Step 1: RagdollSetup — Joint Spring Profile API
| | |
|---|---|
| **Read** | `RagdollSetup.cs` (~430 lines) |
| **Write** | `RagdollSetup.cs` |
| **New LOC** | ~50–60 |
| **What to do** | Add a public method `SetSpringProfile(float armMultiplier, float legMultiplier, float torsoMultiplier, float blendDuration)` that lerps all ConfigurableJoint slerpDrive spring/damper values toward the target multipliers (relative to their current baseline) over `blendDuration` seconds. Store the baseline springs on `Awake`. Add a `ResetSpringProfile(float blendDuration)` that returns to baseline. Use a coroutine or FixedUpdate accumulator for the lerp — no Update dependency. |
| **Do NOT** | Touch any other file. Don't change existing joint drive values or collision setup. |
| **Design ref** | Ch1 §"Joint spring profile management", Ch4 §"Joint spring profile management" |
| **Done when** | EditMode compile passes. Calling `SetSpringProfile(0.25f, 0.25f, 0.25f, 0f)` immediately sets all drives to 25% of baseline. |
| **Status** | [x] |

#### Step 2: BalanceController — Surrender Detection & TriggerSurrender
| | |
|---|---|
| **Read** | `BalanceController.cs` (~1100 lines), `RagdollSetup.cs` (just the new `SetSpringProfile` API) |
| **Write** | `BalanceController.cs` |
| **New LOC** | ~100–120 |
| **What to do** | 1) Add serialized fields: `_surrenderAngleThreshold` (80°), `_surrenderAngularVelocityThreshold` (3 rad/s), `_surrenderAnglePlusMomentumThreshold` (65°). 2) Add `bool IsSurrendered` public property (read-only). 3) Add `float SurrenderSeverity` public property (read-only, 0–1). 4) Add public `TriggerSurrender(float severity)` method that: sets `IsSurrendered = true`, stores severity, zeroes `UprightStrengthScale`, `HeightMaintenanceScale`, `StabilizationScale`, calls `RagdollSetup.SetSpringProfile(0.25f, 0.25f, 0.25f, 0.15f)` for limp ramp, and suppresses pelvis expression. 5) In `FixedUpdate`, after existing upright torque logic, add surrender condition checks: (a) angle > `_surrenderAngleThreshold` for 2+ consecutive frames, (b) angle > `_surrenderAnglePlusMomentumThreshold` AND angular velocity in tilt direction > `_surrenderAngularVelocityThreshold`. Either condition → call `TriggerSurrender(computedSeverity)`. 6) Add `ClearSurrender()` for use by stand-up (Ch4). 7) While `IsSurrendered`, skip all upright torque, height maintenance, COM stabilization in FixedUpdate. |
| **Severity formula** | `Clamp01((angle - 65) / 50) * 0.5 + Clamp01(angVel / 6) * 0.3 + Clamp01(1 - hipsHeight / _standingHipsHeight) * 0.2` |
| **Do NOT** | Change existing IsFallen thresholds (65°/55°). Don't touch LocomotionDirector yet. Don't modify CharacterState transitions. |
| **Design ref** | Ch1 §"Surrender trigger conditions", §"Surrender response" |
| **Done when** | EditMode compile passes. `TriggerSurrender(0.7f)` zeroes all torque scales and sets IsSurrendered = true. Calling `ClearSurrender()` restores them. |
| **Status** | [x] |

#### Step 3: CharacterState — Accept Surrender + KnockdownSeverity Utility
| | |
|---|---|
| **Read** | `CharacterState.cs` (~330 lines), `BalanceController.cs` (just `IsSurrendered`, `SurrenderSeverity` properties) |
| **Write** | `CharacterState.cs`, **new** `Assets/Scripts/Character/KnockdownSeverity.cs` |
| **New LOC** | ~60 CharacterState, ~35 KnockdownSeverity |
| **What to do** | 1) Create `KnockdownSeverity.cs`: a static utility class with `float ComputeFromSurrender(float uprightAngle, float angularVelocity, float hipsHeight, float standingHeight)` and `float ComputeFromImpact(float effectiveDeltaV, float knockdownThreshold)`. Both return 0–1. 2) In `CharacterState`: add `float KnockdownSeverityValue` public property (read-only). Add `bool WasSurrendered` public property. 3) Modify Fallen entry: when `BalanceController.IsSurrendered` is true at Fallen entry time, store `WasSurrendered = true` and `KnockdownSeverityValue = BalanceController.SurrenderSeverity`. 4) Do NOT change Fallen timing yet (keep existing `_getUpDelay` + `_knockoutDuration` — Ch3 replaces them). |
| **Do NOT** | Change Fallen→GettingUp timing. Don't modify any other state transitions. |
| **Design ref** | Ch1 §"Signal CharacterState", Ch3 §"Knockdown severity" |
| **Done when** | EditMode compile passes. When BalanceController.IsSurrendered is true at Fallen entry, CharacterState.WasSurrendered is true and KnockdownSeverityValue matches. |
| **Status** | [ ] |

#### Step 4: LocomotionDirector — Recovery Timeout → Surrender
| | |
|---|---|
| **Read** | `LocomotionDirector.cs` (~630 lines), `BalanceController.cs` (just `TriggerSurrender` API, `UprightAngle`) |
| **Write** | `LocomotionDirector.cs` |
| **New LOC** | ~50–60 |
| **What to do** | 1) Add serialized field `_surrenderRecoveryTimeout` (0.8 s). 2) Add serialized field `_surrenderRecoveryAngleCeiling` (50°) — angle must stay above this to count as "not improving". 3) Track: when a Stumble or NearFall recovery begins, start a timer. Each FixedUpdate while that recovery is active, if `BalanceController.UprightAngle < _surrenderRecoveryAngleCeiling`, reset timer (angle improved). If timer exceeds `_surrenderRecoveryTimeout`, call `BalanceController.TriggerSurrender(severity)` where severity is computed from current angle/angVel, and exit recovery. 4) Reset the timer when recovery situation changes or clears. |
| **Do NOT** | Change recovery classification thresholds (Stumble, NearFall, etc.). Don't change response profiles. Don't touch LocomotionCollapseDetector — it already feeds the director. |
| **Design ref** | Ch1 §"Surrender trigger conditions" condition 2 (Recovery timeout) |
| **Done when** | EditMode compile passes. A Stumble recovery running 0.8+ s with angle stuck above 50° triggers surrender. |
| **Status** | [ ] |

**Post-Ch1 gate:** Run EditMode compile. Run PlayMode test filter `HardSnapRecoveryTests;BalanceControllerTests;LocomotionDirectorTests` — existing tests should still pass (surrender only fires at extreme angles that tests don't hit).

---

### Chapter 2 — External Impact Knockdown (Steps 5–6)

#### Step 5: KnockdownEvent Struct + ImpactKnockdownDetector
| | |
|---|---|
| **Read** | `BalanceController.cs` (just `TriggerSurrender` API), `CharacterState.cs` (just `CurrentState` property). Skim `RagdollSetup.cs` for Hips rigidbody access pattern. |
| **Write** | **new** `Assets/Scripts/Character/KnockdownEvent.cs`, **new** `Assets/Scripts/Character/ImpactKnockdownDetector.cs` |
| **New LOC** | ~25 KnockdownEvent, ~180 ImpactKnockdownDetector |
| **What to do** | 1) `KnockdownEvent.cs`: public struct with fields `float Severity`, `Vector3 ImpactDirection`, `Vector3 ImpactPoint`, `float EffectiveDeltaV`, `GameObject Source`. 2) `ImpactKnockdownDetector.cs`: MonoBehaviour. Serialized: `_impactKnockdownDeltaV` (5 m/s), `_impactStaggerDeltaV` (2.5 m/s), `_impactCooldown` (1.0 s), `_impactDirectionWeight` (0.7), `_gettingUpThresholdMultiplier` (0.6). References: `Rigidbody _hipsRb` (assign in Awake via GetComponent or serialized), `BalanceController`, `CharacterState`. In `OnCollisionEnter`: filter self-collisions (check if other collider shares same root or is on character layers), filter ground (layer mask), compute `rawDeltaV = impulse.magnitude / mass`, compute `directionFactor`, compute `effectiveDeltaV`. If above knockdown threshold (adjusted lower during GettingUp state by `_gettingUpThresholdMultiplier`), call `BalanceController.TriggerSurrender(severity)` and raise a `KnockdownEvent`. If between stagger and knockdown thresholds, apply angular impulse in hit direction (let existing recovery handle it). Track cooldown timer to ignore rapid re-triggers. 3) Add `public event Action<KnockdownEvent> OnKnockdown` for future consumers. |
| **Do NOT** | Modify BalanceController or CharacterState — just call their existing public APIs. Don't add the component to the prefab yet (manual step). |
| **Design ref** | Ch2 full spec |
| **Done when** | EditMode compile passes. Component can be added to any GameObject with a Rigidbody. |
| **Status** | [ ] |

#### Step 6: Wire ImpactKnockdownDetector to Prefab
| | |
|---|---|
| **Read** | PlayerRagdoll prefab structure (inspect `Assets/Prefabs/`) |
| **Write** | Prefab modification (add ImpactKnockdownDetector to Hips GameObject) |
| **What to do** | Add `ImpactKnockdownDetector` component to the Hips bone on the PlayerRagdoll prefab. Wire the `BalanceController` and `CharacterState` references. Set default parameter values per Ch2 spec. |
| **Note** | This is a Unity Editor task — agent should open the prefab and add the component, or do it via script if MCP Unity is available. If not automatable, leave as a manual checklist item. |
| **Done when** | Prefab has the component with correct references. PlayMode tests still pass. |
| **Status** | [ ] |

**Post-Ch2 gate:** Run EditMode compile.

---

### Chapter 3 — Knockdown Timer & Floor State (Steps 7–8)

#### Step 7: CharacterState — Severity-Based Floor Dwell
| | |
|---|---|
| **Read** | `CharacterState.cs` (~330 lines + Step 3 additions), `KnockdownSeverity.cs` |
| **Write** | `CharacterState.cs` |
| **Modified LOC** | ~80 |
| **What to do** | 1) Add serialized: `_minFloorDwell` (1.5 s), `_maxFloorDwell` (3.0 s), `_reKnockdownFloorDwellCap` (1.5× `_maxFloorDwell`). 2) Replace the existing `_getUpDelay + _knockoutDuration` timing in Fallen state with: `floorDwellDuration = Lerp(_minFloorDwell, _maxFloorDwell, KnockdownSeverityValue)` when `WasSurrendered` is true. When `WasSurrendered` is false (existing angle-based fallen without surrender), keep original timing as fallback. 3) During floor dwell: ignore player movement input (don't clear `DesiredInput` — just don't transition to Moving). 4) Add re-knockdown handling: if a new `TriggerSurrender` arrives during Fallen, reset the dwell timer with `Max(currentSeverity, newSeverity)`, capped at `_reKnockdownFloorDwellCap`. 5) When dwell expires → transition to GettingUp as before (old impulse still fires — Ch4 replaces it). |
| **Do NOT** | Remove the old `_getUpForce` impulse yet — that's Step 11. Don't change GettingUp behavior. |
| **Design ref** | Ch3 §"Floor dwell duration", §"Re-knockdown during floor dwell" |
| **Done when** | EditMode compile passes. Surrendered fallen with severity 0.5 → dwell ≈ 2.25 s before GettingUp. Non-surrendered fallen → original timing preserved. |
| **Status** | [ ] |

#### Step 8: ImpactKnockdownDetector + BalanceController — Floor Dwell Adjustments
| | |
|---|---|
| **Read** | `ImpactKnockdownDetector.cs` (~180 lines from Step 5), `BalanceController.cs` (just `IsSurrendered` check) |
| **Write** | `ImpactKnockdownDetector.cs`, `BalanceController.cs` (minor) |
| **Modified LOC** | ~30 total |
| **What to do** | 1) In `ImpactKnockdownDetector`: during `CharacterState.Fallen`, lower the knockdown re-trigger threshold to `_impactKnockdownDeltaV * 0.4` (character is vulnerable on the ground). 2) In `BalanceController.FixedUpdate`: ensure that while CharacterState is `Fallen` AND `IsSurrendered`, all torque/height/COM scales stay zeroed even if external code tries to set them (guard clause). |
| **Do NOT** | Add dazed twitches yet — that's optional flavor, can be a follow-up. Don't change floor dwell timing (that's in CharacterState). |
| **Design ref** | Ch3 §"Re-knockdown during floor dwell", §"Floor state behavior" |
| **Done when** | EditMode compile passes. Impact during Fallen at 40% threshold re-triggers knockdown. |
| **Status** | [ ] |

**Post-Ch3 gate:** Run EditMode compile. Run PlayMode `HardSnapRecoveryTests` — may need threshold relaxation if surrender now fires during extreme test scenarios. If tests fail, note which thresholds to update in Step 12.

---

### Chapter 4 — Procedural Stand-Up (Steps 9–11)

#### Step 9: BalanceController — Smooth Ramp Methods
| | |
|---|---|
| **Read** | `BalanceController.cs` (~1100 lines + Step 2 additions) |
| **Write** | `BalanceController.cs` |
| **New LOC** | ~50–60 |
| **What to do** | Add public methods: `RampUprightStrength(float targetScale, float duration)`, `RampHeightMaintenance(float targetScale, float duration)`, `RampStabilization(float targetScale, float duration)`. Each smoothly interpolates the respective scale from current value to target over `duration` seconds using a FixedUpdate accumulator (not coroutine — keep deterministic). Add a `CancelAllRamps()` that snaps to current target (for interruption by re-knockdown). Wire `ClearSurrender()` to call the ramp methods with target=1.0 and a configurable duration rather than instant restore. |
| **Do NOT** | Change surrender detection logic (Step 2). Don't change existing FixedUpdate torque application — just multiply by the ramped scale value. |
| **Design ref** | Ch4 §"Phase 3: Stand" (re-enable ramps), Ch1 §"ClearSurrender" |
| **Done when** | EditMode compile passes. `RampUprightStrength(1.0f, 0.4f)` smoothly restores upright torque over 0.4 s. |
| **Status** | [ ] |

#### Step 10: Create ProceduralStandUp Component
| | |
|---|---|
| **Read** | `BalanceController.cs` (API only: `ClearSurrender`, ramp methods, `UprightAngle`, `IsGrounded`, `StandingHipsHeight`), `RagdollSetup.cs` (API only: `SetSpringProfile`, `ResetSpringProfile`, Hips rigidbody access), `CharacterState.cs` (API only: `CurrentState`, state enum), `LegAnimator.cs` (skim for existing Fallen suppression pattern — agent needs to know LegAnimator auto-suppresses during Fallen/GettingUp, so ProceduralStandUp doesn't need to manage gait suppression directly) |
| **Write** | **new** `Assets/Scripts/Character/ProceduralStandUp.cs` |
| **New LOC** | ~280–350 |
| **What to do** | Create a MonoBehaviour with a 4-phase state machine (Inactive, OrientProne, ArmPush, LegTuck, Stand). Public: `void Begin(float severity)` (called by CharacterState on GettingUp entry), `bool IsActive`, `StandUpPhase CurrentPhase` (for test inspection), `event Action OnPhaseCompleted`, `event Action OnFailed`, `event Action OnCompleted`. Each phase per Ch4 spec: **Phase 0 (OrientProne):** gentle torque to roll belly-down, skip if already prone, always advances (0.3–0.6 s). **Phase 1 (ArmPush):** ramp arm springs to 150%, apply upward force on chest, success = chest height > 0.2 m, fail after 0.8 s if < 0.1 m → fire `OnFailed` with severity 0.2. **Phase 2 (LegTuck):** ramp leg springs to 120%, drive hip/knee flexion, assist force on hips, success = hips > 0.2 m + foot contact, fail after 0.7 s → `OnFailed` with severity 0.15. **Phase 3 (Stand):** ramp leg springs to 200%, call `BalanceController.RampUprightStrength(1.0, 0.4)` / `RampHeightMaintenance(1.0, 0.3)` / `RampStabilization(0.8, 0.3)`, success = hips > 90% standing height + !IsFallen + grounded, fail after 0.5 s → `OnFailed` with severity 0.3. Track `_standUpAttempts`; after `_maxStandUpAttempts` (3), do a forced stand (stronger impulse safety net). Expose all tuning parameters as serialized fields with defaults from Ch4 spec. |
| **Do NOT** | Modify any existing file. Don't manage gait suppression (LegAnimator handles that via CharacterState). Don't handle external impact interruption here (CharacterState will re-enter Fallen, which calls `OnFailed` implicitly). |
| **Design ref** | Ch4 full spec |
| **Done when** | EditMode compile passes. Component can be added to a GameObject. `Begin(0.5f)` starts the phase sequence. |
| **Status** | [ ] |

#### Step 11: CharacterState — Wire GettingUp to ProceduralStandUp
| | |
|---|---|
| **Read** | `CharacterState.cs` (~330 lines + Step 3/7 additions), `ProceduralStandUp.cs` (API only: `Begin`, `OnFailed`, `OnCompleted`) |
| **Write** | `CharacterState.cs` |
| **Modified LOC** | ~60–80 |
| **What to do** | 1) Add a serialized `ProceduralStandUp _proceduralStandUp` reference. 2) On GettingUp entry: if `WasSurrendered && _proceduralStandUp != null`, call `_proceduralStandUp.Begin(KnockdownSeverityValue)` instead of applying the old `_getUpForce` impulse. Subscribe to `OnFailed` (→ re-enter Fallen with the failure severity) and `OnCompleted` (→ transition to Standing/Moving). 3) Keep the old impulse path as fallback when `WasSurrendered` is false or `_proceduralStandUp` is null (backwards compat, non-surrender falls). 4) Keep `_getUpTimeout` as a safety net — if ProceduralStandUp hasn't completed or failed after timeout, force transition to Standing. 5) On re-entry to Fallen from stand-up failure: reset `WasSurrendered = true` with the new severity, which re-triggers floor dwell (Step 7 logic). |
| **Do NOT** | Delete the old `_getUpForce` field — it's the fallback. Don't modify ProceduralStandUp. |
| **Design ref** | Ch4 §"Integration", Ch3 §"State machine changes" |
| **Done when** | EditMode compile passes. Surrendered GettingUp → ProceduralStandUp drives recovery. Non-surrendered GettingUp → old impulse fallback. |
| **Status** | [ ] |

**Post-Ch4 gate:** Run EditMode compile. Add `ProceduralStandUp` component to PlayerRagdoll prefab and wire the reference in CharacterState. Run PlayMode `HardSnapRecoveryTests` — old path should still work for non-surrender falls.

---

### Chapter 5 — Tests & Baselines (Steps 12–15)

#### Step 12: Update Existing Tests
| | |
|---|---|
| **Read** | `HardSnapRecoveryTests.cs` (~330 lines), `GetUpReliabilityTests.cs` (if it asserts recovery timing), `LocomotionDirectorTests.cs` (check for never-Fallen assertions) |
| **Write** | `HardSnapRecoveryTests.cs`, possibly `LocomotionDirectorTests.cs` |
| **Modified LOC** | ~40–60 |
| **What to do** | 1) In `HardSnapRecoveryTests`: increase `maxConsecutiveFallenFrames` from 150 to 250–300 to accommodate floor dwell. Keep "max 200 stalled frames" and "pre/post turn progress" assertions (sure-footedness must hold for moderate scenarios). Add a comment explaining why the threshold is higher. 2) In any test that asserts "character never enters Fallen during recovery": narrow the assertion to moderate-tilt scenarios (< 70°) or add a note that extreme tilts may now surrender. 3) Do NOT weaken assertions about sure-footed behavior in normal conditions. |
| **Design ref** | Ch5 §"Existing test updates" |
| **Done when** | EditMode compile passes. PlayMode run of updated tests passes. |
| **Status** | [ ] |

#### Step 13: Surrender + Impact Tests
| | |
|---|---|
| **Read** | `PlayerPrefabTestRig.cs` or `PlayModeSceneIsolation.cs` (test harness pattern), `BalanceController.cs` (test seam: `SetGroundStateForTest`), `BalanceControllerTests.cs` (example test structure) |
| **Write** | **new** `Assets/Tests/PlayMode/Character/SurrenderTests.cs`, **new** `Assets/Tests/PlayMode/Character/ImpactKnockdownTests.cs` |
| **New LOC** | ~200 SurrenderTests, ~250 ImpactKnockdownTests |
| **What to do** | **SurrenderTests:** 4 tests per Ch5 spec — (1) angle > 85° → upright torque drops to 0, (2) recovery timeout > 0.8 s with angle > 50° → surrender fires, (3) angle < 70° → surrender does NOT fire, (4) after surrender → joint springs at 20–30% within 0.15 s. Use the existing `PlayerPrefabTestRig` spawn pattern. Tilt character by applying torque impulse to hips. **ImpactKnockdownTests:** 5 tests per Ch5 spec — (1) high-velocity projectile → Fallen, (2) medium projectile → stagger not Fallen, (3) self-collision no trigger, (4) impact during GettingUp → re-Fallen, (5) rapid impacts within cooldown → second ignored. Spawn a kinematic rigidbody and set its velocity toward the character. |
| **Do NOT** | Create new test utilities unless absolutely needed — prefer using existing `PlayerPrefabTestRig` and `GhostDriver`. |
| **Design ref** | Ch5 §"Surrender tests", §"External impact tests" |
| **Done when** | EditMode compile passes. PlayMode run of new tests passes. |
| **Status** | [ ] |

#### Step 14: Floor Dwell + Stand-Up Tests
| | |
|---|---|
| **Read** | Same test harness files as Step 13, `ProceduralStandUp.cs` (`CurrentPhase` property for inspection) |
| **Write** | **new** `Assets/Tests/PlayMode/Character/FloorDwellTests.cs`, **new** `Assets/Tests/PlayMode/Character/ProceduralStandUpTests.cs` |
| **New LOC** | ~180 FloorDwellTests, ~280 ProceduralStandUpTests |
| **What to do** | **FloorDwellTests:** 4 tests — (1) light severity dwell ≈ 1.5–1.9 s, (2) heavy severity dwell ≈ 2.7–3.0 s, (3) input ignored during dwell, (4) re-hit resets timer capped at max. Trigger surrender via `BalanceController.TriggerSurrender(severity)` directly. Measure frame count in Fallen state. **ProceduralStandUpTests:** 6 tests — (1) completes within 3 s from floor, (2) phases advance in order (Orient→ArmPush→LegTuck→Stand), (3) arm push fail → re-knockdown with short severity, (4) impact during phase → full reset, (5) 3 failures → forced stand safety net, (6) upright torque ramps smoothly during Phase 3 (sample over frames, verify no step function). |
| **Do NOT** | Duplicate test harness code — reuse existing utilities. |
| **Design ref** | Ch5 §"Floor dwell tests", §"Stand-up tests" |
| **Done when** | EditMode compile passes. All new PlayMode tests pass. |
| **Status** | [ ] |

#### Step 15: Baselines Update + Full Regression
| | |
|---|---|
| **Read** | `LOCOMOTION_BASELINES.md`, `ARCHITECTURE.md`, `TASK_ROUTING.md` |
| **Write** | `LOCOMOTION_BASELINES.md`, `ARCHITECTURE.md`, `TASK_ROUTING.md` |
| **Modified LOC** | ~30–50 across all three |
| **What to do** | 1) `LOCOMOTION_BASELINES.md`: Add expected knockdown behaviors — "Character MAY enter Fallen during extreme tilts (>80°) or external impacts (>5 m/s delta-v)", "Floor dwell: 1.5–3.0 s scaled by severity", "Stand-up: ~1.5–2.5 s via ProceduralStandUp", "Normal locomotion unaffected (surrender only at extreme angles)". Remove/update any baselines saying "character should never fall flat". 2) `ARCHITECTURE.md`: Add entries for `ImpactKnockdownDetector`, `KnockdownSeverity`, `ProceduralStandUp`, `KnockdownEvent`. 3) `TASK_ROUTING.md`: Add routing for knockdown-related work. 4) Run full EditMode + PlayMode regression. All tests green. |
| **Design ref** | Ch5 §"Baseline updates" |
| **Done when** | Full EditMode + PlayMode test suite passes. Docs updated. |
| **Status** | [ ] |

---

## Dependency Graph

```
Step 1 (RagdollSetup springs)
  └→ Step 2 (BalanceController surrender)
       ├→ Step 3 (CharacterState + KnockdownSeverity)
       │    └→ Step 7 (Floor dwell timing)
       │         └→ Step 8 (Floor dwell adjustments)
       ├→ Step 4 (LocomotionDirector timeout)
       ├→ Step 5 (ImpactKnockdownDetector) → Step 6 (Prefab wire)
       └→ Step 9 (BalanceController ramps)
            └→ Step 10 (ProceduralStandUp)
                 └→ Step 11 (CharacterState wire GettingUp)

Steps 12–15 (tests/baselines) after Steps 1–11 are all complete.
Steps 12, 13, 14 can run in parallel if multiple agents are available.
```

## Progress notes
- 2026-03-16: Plan created from discussion. User wants sure-footed default, natural ragdoll falls, ~2–2.5 s floor dwell, staged stand-up, external force knockdowns, severity scaling.
- 2026-03-16: Restructured into 15 atomic agent-sized steps with explicit file scopes, LOC budgets, and done-checks.
- 2026-03-16: Verified Step 1 was already implemented in `RagdollSetup` on `master` and marked it complete after a fresh EditMode pass.
- 2026-03-16: Completed Step 2 in `BalanceController` with surrender thresholds, severity capture, `TriggerSurrender`/`ClearSurrender`, and local balance-scale gating. EditMode passed. `HardSnapRecoveryTests` + `BalanceControllerTests` passed in PlayMode. `LocomotionDirectorTests.FixedUpdate_WhenOneFootLosesContact_BreaksStrictHalfCyclePhaseMirror` and `...ConfidenceDrops_ConvergesTowardMirroredFallbackWithoutOneFrameSnap` remain red in the Chapter 1 slice and appear unrelated to the surrender seam.
