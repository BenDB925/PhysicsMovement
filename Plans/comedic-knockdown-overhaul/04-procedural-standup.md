# Ch4: Procedural Stand-Up Sequence

## Goal
Replace the single magic 250N impulse with a multi-phase physics-driven stand-up that looks like the character is actually pushing himself off the ground. Each phase has a physical success condition. The sequence can fail (and re-enter knockdown), which is both realistic and funny.

## Current status
- State: Not started
- Current next step: Design phase state machine
- Blockers: Depends on Ch1 (joint spring profiles), Ch3 (GettingUp state entry)

## Current behavior
- `CharacterState.GettingUp` applies `Vector3.up * 250` as `ForceMode.Impulse` to hips
- If `IsFallen` clears and character is grounded → instant transition to Standing/Moving
- `_getUpTimeout` (3 s) safety net reverts to Standing if the impulse doesn't work
- Leg springs are boosted 2.25× during startup, which also catches the GettingUp phase
- Result: character pops upright unnaturally fast, or the impulse fails and timeout rescues it

## Design

### New component: `ProceduralStandUp`
A MonoBehaviour that drives a 4-phase state machine when `CharacterState` enters `GettingUp`.

### Phase overview

```
[Floor Dwell Ends]
        │
        ▼
   Phase 0: Orient Prone
   "Roll face-down if needed"
   Duration: 0.3–0.6 s
        │
        ▼
   Phase 1: Arm Push
   "Push upper body off ground"
   Duration: 0.5–0.8 s
        │
        ▼
   Phase 2: Leg Tuck
   "Pull knees under, get to crouch"
   Duration: 0.4–0.7 s
        │
        ▼
   Phase 3: Stand
   "Push up to full height"
   Duration: 0.3–0.5 s
        │
        ▼
   [Standing / Moving]
```

Total: ~1.5–2.6 s depending on physics success. ~2 s typical.

### Phase 0: Orient Prone
**Goal**: Get the torso face-down (belly on ground) so the push-up makes sense.

**Method**:
- Apply gentle torque to roll the hips toward a belly-down orientation
- Target: hips forward-vector points roughly toward the ground (dot product with Vector3.down > 0.5)
- Joint springs stay at limp profile (from Ch1)
- If already face-down (common after a forward fall): skip immediately

**Success condition**: `Vector3.Dot(hips.transform.forward, Vector3.down) > 0.5` OR timeout 0.6 s (proceed anyway — character might be on their side, arm push still works approximately)

**Failure**: Cannot fail — always advances (worst case, character does a weird sideways push-up, which is funny)

### Phase 1: Arm Push
**Goal**: Push the upper body off the ground, like a push-up.

**Method**:
- Ramp arm joint springs to 150% of normal (stiff arms)
- Apply upward force on the upper torso/chest rigidbody: `pushForce = _armPushForce * (1 - torsoHeightProgress)` where `torsoHeightProgress` = current chest height / target chest height for this phase (~0.25 m off ground)
- Slight forward lean torque to prevent falling backward
- Leg springs stay low — legs drag on the ground naturally

**Success condition**: Chest height > 0.2 m above ground contact point AND chest angular velocity is low (settled)

**Failure condition**: After 0.8 s, if chest height < 0.1 m → arm push failed (arms collapsed). Re-enter `Fallen` state with a short severity (0.2) knockdown — the "tried to get up and face-planted again" comedy beat.

**Tuning parameters**:
| Parameter | Default | Description |
|-----------|---------|-------------|
| `_armPushForce` | 180 N | Base upward force during push-up |
| `_armPushSpringMultiplier` | 1.5 | How stiff arms get during push |
| `_armPushTargetHeight` | 0.25 m | Chest height that counts as success |
| `_armPushTimeout` | 0.8 s | Max time before failure check |

### Phase 2: Leg Tuck
**Goal**: Pull the legs under the body, transitioning from push-up to crouching position.

**Method**:
- Ramp leg springs to 120% of normal
- Drive upper leg joints toward a tucked target rotation (~90° hip flexion)
- Drive lower leg joints toward ~100° knee flexion
- Maintain arm push force at reduced level (50%) to keep upper body propped
- Apply slight upward force on hips to assist the tuck: `_legTuckAssistForce`

**Success condition**: Hips height > 0.2 m AND at least one foot has ground contact (the character has "found the ground" with their legs)

**Failure condition**: After 0.7 s, if hips height < 0.12 m → tuck failed, re-enter Fallen with severity 0.15

**Tuning parameters**:
| Parameter | Default | Description |
|-----------|---------|-------------|
| `_legTuckSpringMultiplier` | 1.2 | Leg spring during tuck |
| `_legTuckAssistForce` | 80 N | Upward assist on hips during tuck |
| `_legTuckTargetHipAngle` | 90° | Target hip flexion |
| `_legTuckTimeout` | 0.7 s | Max time before failure check |

### Phase 3: Stand
**Goal**: Push from crouch to full standing height.

**Method**:
- Ramp leg springs to 200% of normal (powerful extension)
- Re-enable height maintenance with a ramp: 0 → 1.0 over 0.3 s
- Re-enable upright torque with a ramp: 0 → 1.0 over 0.4 s (slightly slower so legs lead)
- Re-enable COM stabilization with a ramp: 0 → 0.8 over 0.3 s
- Drive legs toward straight extension
- Resume gait phase in `LegAnimator` once hips exceed 80% of standing height

**Success condition**: Hips height > 90% of `_standingHipsHeight` AND `IsFallen == false` AND grounded

**Failure condition**: After 0.5 s, if `IsFallen == true` → stand failed, re-enter Fallen with severity 0.3 (heavier re-knockdown — "almost had it!")

**Completion**: Return all joint spring multipliers to 1.0 over 0.2 s. `CharacterState` transitions to Standing or Moving based on input.

### Stand-up failure comedy
Each failed stand-up attempt increments a `_standUpAttempts` counter. After `_maxStandUpAttempts` (default 3), skip straight to a forced stand (use a stronger version of the old impulse as safety net) to prevent infinite failure loops. The counter resets on successful stand.

### Interruption by external impact
During any phase, if `ImpactKnockdownDetector` fires (at the lowered GettingUp threshold from Ch2, 60% of normal):
- Abort the current phase
- Re-enter Fallen with severity from the new impact
- Reset `_standUpAttempts` counter (new knockdown, fresh attempts)

### Joint spring profile management
The stand-up sequence needs to smoothly transition joint springs across phases. Use a `JointSpringProfile` concept (could be a method on `RagdollSetup` or a new small helper):

```csharp
void SetSpringProfile(float armMultiplier, float legMultiplier, float torsoMultiplier, float blendTime)
```

This lerps all joint drives toward the target multipliers over `blendTime` seconds. Called at the start of each phase.

## Files to create / modify
| File | What changes |
|------|-------------|
| **New: `ProceduralStandUp.cs`** | Phase state machine (Orient → ArmPush → LegTuck → Stand). Per-phase force application, success/failure checks, timeout handling, attempt counter. ~250–350 lines. |
| `CharacterState.cs` | `GettingUp` state delegates to `ProceduralStandUp.Begin()`. Transitions back to Fallen on phase failure. Transitions to Standing/Moving on completion. Remove old `_getUpForce` impulse logic. |
| `BalanceController.cs` | Expose `SetUprightStrengthRamp(float target, float duration)` and similar for height/COM so the stand phase can smoothly re-enable balance. |
| `RagdollSetup.cs` | `SetSpringProfile(arm, leg, torso, blendTime)` method for cross-phase joint management. |
| `LegAnimator.cs` | Respond to `ResumeGait()` call from Phase 3 when hips height threshold is met. |

## Acceptance criteria
- [ ] Orient phase: character rolls to belly-down within 0.6 s
- [ ] Arm push: chest rises to 0.25 m off ground, arms visibly extend
- [ ] Leg tuck: legs pull under, at least one foot touches ground
- [ ] Stand: character reaches 90% standing height, balance re-engages smoothly
- [ ] Failed arm push → character falls back down, short re-knockdown, retries
- [ ] Failed stand → character falls back down, medium re-knockdown, retries
- [ ] After 3 failed attempts → forced stand (safety net)
- [ ] External impact during any phase → full knockdown reset
- [ ] Total stand-up time (no failures) is ~1.5–2.5 s
- [ ] Joint springs transition smoothly between phases (no sudden snapping)

## Open questions
- Should severity affect stand-up difficulty? (e.g., high severity → weaker arm push force, more likely to fail Phase 1). Leaning yes, but could be a follow-up.
- Should there be a brief "wobble" after Phase 3 completes where balance is still ramping up? Probably yes — small window of vulnerability adds realism.

## Decisions
- (pending)

## Progress notes
- 2026-03-16: Chapter spec written
