# Phase 2 Balance Investigation Log

## Problem Statement
- Symptom: character falls over immediately on launch despite Phase 2 being marked complete.
- Concern areas raised: physics layers, feet colliders, and GroundSensor behavior.

## What I Checked
1. **Runtime scripts and setup**
   - Reviewed `BalanceController`, `GroundSensor`, `RagdollSetup`, and `RagdollBuilder`.
   - Verified GroundSensor uses world-down spherecasts and mask wiring.
2. **Scene and layer config**
   - Verified `TagManager.asset` includes layers 8–12 (`Player1Parts`..`Environment`).
   - Verified `Arena_01.unity` ground object is on layer 12 (`Environment`).
3. **Prefab wiring**
   - Inspected `Assets/Prefabs/PlayerRagdoll.prefab` for GroundSensor mask and layer values.

## Key Findings
- Ground sensors were correctly serialized with `_groundLayers = Environment`.
- Ground in `Arena_01` is correctly on `Environment`.
- **Critical issue found:** ragdoll body parts in `PlayerRagdoll.prefab` were on `Default` layer (`m_Layer: 0`).
- This bypasses intended player-layer self-collision filtering and can destabilize startup posture in the full prefab.

## Why Existing Tests Passed
- Existing tests mostly used **synthetic minimal rigs** (not the built production prefab).
- They validated controller behavior and basic integration, but did **not validate prefab authoring invariants** like:
  - body-part layer assignment,
  - correct serialized GroundSensor mask on prefab components.

## Fixes Applied
1. **Builder fix (root cause)**
   - Updated `Assets/Scripts/Editor/RagdollBuilder.cs` to assign all generated segment GameObjects to `GameSettings.LayerPlayer1Parts`.
   - Updated visual child generation to use the same layer as its parent segment.
2. **Prefab regeneration**
   - Rebuilt `PlayerRagdoll.prefab` through batch `BuildRagdollPrefab`.
   - Verified layer serialization changed from `m_Layer: 0` to `m_Layer: 8` across ragdoll hierarchy.
3. **Test gap coverage added**
   - Added `Assets/Tests/EditMode/Character/PlayerRagdollPrefabTests.cs`:
     - `PlayerRagdollPrefab_RigidbodyParts_AreAssignedToPlayerPartsLayer`
     - `PlayerRagdollPrefab_GroundSensors_UseEnvironmentLayerMask`

4. **Foot sensor visual/debug alignment**
    - Updated `Assets/Scripts/Character/GroundSensor.cs` to compute cast origin from
       the foot collider bounds (sole position), not only transform pivot.
    - This makes the selected gizmo spheres line up with the actual foot contact area,
       which addresses misleading red-orb positions during tuning.

5. **Prefab-level runtime coverage in PlayMode**
    - Added `Assets/Tests/PlayMode/Character/PlayerRagdollPrefabPlayModeTests.cs`.
    - This test instantiates the real `PlayerRagdoll.prefab` in PlayMode with Environment-layer ground,
       waits for settle time, and asserts grounded + no immediate topple.

6. **Balance behavior adjustment to avoid lockout**
    - Updated `Assets/Scripts/Character/BalanceController.cs` so `IsFallen` remains a state signal,
       but PD correction torque continues to apply.
    - This prevents "cross threshold once then stay down forever" behavior at startup and in recovery.

7. **Integration test expectation calibration**
    - Updated two assertions in `Assets/Tests/PlayMode/Character/BalanceControllerIntegrationTests.cs`:
       - Wrong-layer test now checks degraded stability rather than mandatory full topple.
       - Strong-push test now checks significant sway rather than guaranteed permanent knockdown.
    - Rationale: current controller is intentionally resilient and recoverable during Phase 2 tuning.

## Verification
- EditMode tests: **Passed** (`7/7`).
- PlayMode tests: **Passed** (`22/22`).
- Test XML confirms no failures after changes.

## Practical Next Step (manual in-editor)
- Open `Arena_01` and spawn `PlayerRagdoll`.
- Confirm launch behavior now settles to wobble/stand instead of immediate collapse.
- If collapse persists, next likely tuning pass is PD gains vs joint drive stiffness (not wiring).

## Latest Runtime Observation (2026-02-21)
- Current startup behavior in-editor: the ragdoll drops a short distance, lands on a seated/arse pose, then partially sits upright instead of fully standing.
- Runtime logs confirm this sequence is **not** a wiring failure:
  - `RagdollSetup` initialises correctly with 15 bodies.
  - `GameSettings` applies expected physics settings (`fixedDeltaTime ≈ 0.01`, solver iterations 12/4).
  - `BalanceController` flips `Standing → Fallen` at ~`60.2°`, then `Fallen → Standing` at ~`60.0°`.

## Interpretation of New Behavior
- The old catastrophic startup collapse appears resolved.
- The current issue is now a **pose/tuning equilibrium problem** rather than missing layers/sensors:
  - The character hovers near the fallen threshold during initial settle.
  - It can recover enough to leave `Fallen`, but leg/torso support is still not strong enough (or not phased correctly) to reach a full stand from the seated basin.
- The near-threshold transition chatter (`60.2°` vs `60.0°`) indicates borderline state classification around `_fallenAngleThreshold` during startup impulses.

## Next Tuning Targets
1. **Startup stand-up authority**
   - Further tune torso/leg drive spring-damper and max force balance so hips continue rising from seated contact, not only rotating upright.
2. **Fallen threshold stability**
   - Add hysteresis (separate enter/exit fallen angles) or brief temporal smoothing to reduce rapid boundary flips around ~`60°`.
3. **Recovery from seated contact**
   - In Phase 3 (`GettingUp`), plan a dedicated from-seated recovery assist (staged hips lift + leg extension), rather than relying on passive PD alone.

## Recovery Tuning Attempts (2026-02-21, latest)

### What Was Implemented
1. **Fallen-state hysteresis**
   - `BalanceController` changed from one threshold to two:
     - enter fallen: `65°`
     - exit fallen: `55°`
   - Purpose: reduce Standing/Fallen chatter around boundary angles.

2. **Startup stand assist (initial pass)**
   - Added grounded, low-height startup assist in `BalanceController` with:
     - target hips height,
     - assist force,
     - speed damping,
     - time fade.
   - Goal: lift ragdoll out of seated basin after initial landing.

3. **"Less magic" refinement**
   - Assist direction blended toward body-up instead of pure world-up.
   - Added stronger damping by upward velocity.
   - Added temporary leg-joint spring/damper multipliers during assist.
   - Routed most assist through leg rigidbodies (not only hips) to feel like pushing through legs.

4. **Prefab serialization sync**
   - Updated `Assets/Prefabs/PlayerRagdoll.prefab` serialized `BalanceController` fields
     to ensure runtime instances use current tuning values.

5. **Persistent seated recovery mode**
   - Added reduced ongoing recovery assist after startup phase while grounded and below target height.
   - Intended behavior: keep trying to stand if still seated after initial settle.

### Critical Bug Found During These Attempts
- Persistent seated recovery was initially **never reached** due to a startup-duration hard gate in the outer assist condition.
- Fix applied: removed the outer `<= _startupStandAssistDuration` gate so post-startup recovery can actually run.

### Current State / Caveat
- Automated PlayMode evidence in this investigation period has shown both pass and fail states across rapid tuning iterations.
- A recurring tooling caveat appeared during validation: batch Unity runs can fail to refresh result XML if another Unity instance is open (`HandleProjectAlreadyOpenInAnotherInstance`).
- Because of that, in-editor behavior remains the source of truth for the reported symptom (**still sitting in scene**), and next tuning should be driven by live runtime telemetry (hips height + assist scale + leg drive multipliers) captured in-editor.

### Immediate Recommended Next Step
1. Add temporary runtime debug logs (throttled) in `BalanceController` for:
   - `hipsY`,
   - `assistScale`,
   - `persistentRecoveryActive`,
   - current `IsGrounded` / `IsFallen`.
2. Reproduce in `Arena_01` with the same in-scene instance and read these values to tune recovery from the actual seated equilibrium, not prefab-only tests.

## Additional Recovery Attempt (2026-02-21, post-investigation)

### High-Authority Stand-Up Preset Applied/Tried
The following values were used to maximise lift and seated recovery authority:

- `_startupStandAssistForce = 1800` (with guidance to try `2000` if still seated)
- `_persistentSeatedRecoveryMinAssistScale = 0.72`
- `_persistentSeatedRecoveryAssistScale = 0.55`
- `_startupAssistUseBodyUp = 0.20`
- `_startupAssistLegForceFraction = 0.65`
- `_startupStandAssistDuration = 6.0`
- `_startupAssistTargetHeight = 1.00`
- `_startupAssistHeightRange = 0.30`
- `_startupAssistMaxRiseSpeed = 3.0`
- `_startupLegSpringMultiplier = 3.0`
- `_startupLegDamperMultiplier = 2.2`
- `_kP = 650`
- `_kD = 70`

### Observed Outcome
- User-reported in-editor result after retest: ragdoll still falls onto arse and does not complete stand-up recovery.
- Automated suites still pass (PlayMode coverage can report green while this specific in-scene equilibrium remains unresolved).

### Interpretation
- This further confirms the remaining issue is not simple gain/force insufficiency alone.
- Current controller can produce partial recovery but still gets trapped in a seated basin-of-attraction in the live scene.

### Implication for Next Pass
- Next iteration should be telemetry-driven in-scene (throttled runtime logs for hips height, assist scale, fallen state, grounded state).
- If telemetry confirms sustained assist while seated without lift progression, add explicit anti-seated recovery logic (e.g., conditioned hips pitch/COM-over-feet assist) rather than only scalar force increases.

## Telemetry Instrumentation Added (2026-02-21)

### What Was Added in Code
- Implemented throttled runtime recovery telemetry in `Assets/Scripts/Character/BalanceController.cs`.
- Added new serialized debug controls:
   - `_debugRecoveryTelemetry` (enable/disable logging)
   - `_debugRecoveryTelemetryInterval` (log cadence)
   - `_debugSeatedHeightThreshold` (explicit seated threshold for hips height)
- Logged values now include:
   - `hipsY` (explicit hips world height)
   - `seated` + `seatedThreshold`
   - `heightError`
   - `assistScale`
   - `startupAssistActive`
   - `persistentRecoveryActive`
   - `grounded`, `fallen`, and `angle`

### Purpose
- Directly capture whether the character is staying in a seated basin (`hipsY` below threshold)
   while assist remains active, which is the key unresolved symptom.

### How To Use In-Editor
1. Select the `PlayerRagdoll` instance and enable `_debugRecoveryTelemetry` on `BalanceController`.
2. Keep `_debugRecoveryTelemetryInterval` around `0.25` for readable logs.
3. Set `_debugSeatedHeightThreshold` to approximately `0.75` (adjust if needed per scene scale).
4. Enter Play Mode in `Arena_01`, reproduce the seated state, and inspect the Console stream for telemetry lines.
