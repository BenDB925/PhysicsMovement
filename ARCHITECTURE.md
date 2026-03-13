# Architecture — PhysicsDrivenMovementDemo

> Living document. Update whenever a new system, assembly, or significant data flow is introduced.
> See `CODING_STANDARDS.md §7 Layer 5` for the update rules.

## Quick Load

- Runtime locomotion authority currently flows through `PlayerMovement` intent into `LocomotionDirector`, then out to `BalanceController` and `LegAnimator`, with `CharacterState` as the high-level safety label and `LocomotionCollapseDetector` as a watchdog input.
- The main shipped runtime assemblies are `Core`, `Character`, `Input`, and `Environment`; editor builders live separately under `PhysicsDrivenMovement.Editor`, and EditMode / PlayMode tests are split into their own assemblies.
- `RagdollBuilder` owns prefab composition for `PlayerRagdoll.prefab`, while `SceneBuilder` and `ArenaBuilder` generate `Arena_01.unity` and `Museum_01.unity` respectively.
- A live Unity MCP bridge is available for editor automation through the MCP stdio server plus Unity's `ws://localhost:8090/McpUnity` endpoint; use it for editor-state work and keep unattended regression verification on `Tools/Run-UnityTests.ps1`.
- When ownership boundaries or key collaborators change, keep this file, `TASK_ROUTING.md`, and `.copilot-instructions.md` aligned in the same slice.

## Read More When

- Continue into the system diagram if the task adds a new runtime system, scene-generation path, or assembly boundary.
- Continue into the key class sections if the task changes responsibilities, collaborators, or public seams for an existing system.
- Continue into the assembly structure if code is moving between runtime, editor, or test assemblies.

---

## 1 — High-Level System Diagram

```
┌────────────────────────────────────────────────────────────────────┐
│                           EDITOR TOOLS                             │
│  RagdollBuilder ──► PlayerRagdoll.prefab                          │
│  SceneBuilder   ──► Arena_01.unity                                │
│  ArenaBuilder   ──► Museum_01.unity                               │
│  PropBuilder / SkinnedRagdollBuilder ──► generated support assets │
└──────────────────────────────┬─────────────────────────────────────┘
                               │ (prefab / scene produced by)
                               ▼
┌────────────────────────────────────────────────────────────────────┐
│                          RUNTIME (HOST)                            │
│                                                                    │
│  GameSettings.Awake()   ─── configures Time, Physics, layers      │
│                                                                    │
│  RagdollSetup.Awake()   ─── disables neighbour collisions         │
│  BalanceController      ─── PD torque executor for body support   │
│  GroundSensor           ─── foot ground detection                 │
│  PlayerMovement         ─── input → AddForce on Hips + intent     │
│  LocomotionDirector     ─── desired input + observations →        │
│                           support / leg command frames            │
│  CharacterState         ─── FSM (Standing/Moving/Airborne/...)    │
│  LocomotionCollapseDetector ─ root stalled-collapse fall trigger   │
│  LegAnimator            ─── explicit leg-command executor         │
│  ArmAnimator            ─── counter-swing arm gait                │
│  FallPoseRecorder       ─── rolling fall pose NDJSON diagnostics  │
│  CameraFollow           ─── orbital third-person camera           │
│  HandGrabber ★          ─── FixedJoint grab mechanic              │
│  HitReceiver ★          ─── knockout on head collision            │
│                          FixedUpdate @ 100 Hz                     │
└───────────────────────────────┬────────────────────────────────────┘
                                │ (NGO NetworkRigidbody sync)
             ┌──────────────────┼──────────────────┐
             ▼                  ▼                  ▼
         Client 2           Client 3           Client 4
       (kinematic)        (kinematic)        (kinematic)

★ = not yet implemented
```

## 1.5 — Editor Automation Surface

- VS Code agents can drive the open Unity editor through the Unity MCP bridge documented in `UNITY_MCP.md`.
- Prefer that bridge for console inspection, script recompiles, menu-item execution, scene or prefab mutation, component wiring, material edits, and quick in-editor smoke tests.
- Keep unattended regression verification on `Tools/Run-UnityTests.ps1` so `TestResults/` remains the authoritative artifact source.

---

## 2 — Assembly Structure

| Assembly | Path | Depends On | Notes |
|----------|------|------------|-------|
| `PhysicsDrivenMovement.Core` | `Assets/Scripts/Core/` | *(none)* | `GameSettings` singleton |
| `PhysicsDrivenMovement.Character` | `Assets/Scripts/Character/` | `Core` | Ragdoll physics scripts plus the locomotion contract and director command layer |
| `PhysicsDrivenMovement.Input` | `Assets/Scripts/Input/` | `Character` | Generated input wrapper |
| `PhysicsDrivenMovement.Environment` | `Assets/Scripts/Environment/` | *(none)* | Runtime room metadata for generated scenes |
| `PhysicsDrivenMovement.Editor` | `Assets/Scripts/Editor/` | `Core`, `Character` | Editor-only build tools |
| `PhysicsDrivenMovement.Tests.EditMode` | `Assets/Tests/EditMode/` | `Core`, `Character` | NUnit EditMode tests |
| `PhysicsDrivenMovement.Tests.PlayMode` | `Assets/Tests/PlayMode/` | `Core`, `Character` | NUnit PlayMode tests |

---

## 3 — Key Classes (Phase 0–1)

### `Core.GameSettings` — `Assets/Scripts/Core/GameSettings.cs`

| Concern | Detail |
|---------|--------|
| **What** | MonoBehaviour singleton; applies global physics configuration on Awake. |
| **Why** | Belt-and-suspenders guarantee that 100 Hz / 12 iterations are set even if ProjectSettings YAML was edited incorrectly. |
| **Public Surface** | Layer index constants (`LayerPlayer1Parts`…`LayerEnvironment`). |
| **Collaborators** | None — standalone setup. |
| **Phase** | 0 |

### `Character.RagdollSetup` — `Assets/Scripts/Character/RagdollSetup.cs`

| Concern | Detail |
|---------|--------|
| **What** | MonoBehaviour on ragdoll Hips; discovers ConfigurableJoint pairs and calls `Physics.IgnoreCollision` between each neighbour set. |
| **Why** | Prevents jitter artifacts at joints where collider geometry overlaps. |
| **Public Surface** | `AllBodies: IReadOnlyList<Rigidbody>` — consumed by balance, combat, and networking systems. |
| **Collaborators** | Built by `RagdollBuilder` (Editor); read by `BalanceController`, `HitReceiver` (future). |
| **Phase** | 1 |

### `Editor.RagdollBuilder` — `Assets/Scripts/Editor/RagdollBuilder.cs`

| Concern | Detail |
|---------|--------|
| **What** | Static editor class; `[MenuItem]` that procedurally builds `PlayerRagdoll.prefab`. |
| **Why** | Ensures a reproducible, data-driven ragdoll. Segment definitions (mass, shape, limits) live in a static `SegmentDef[]` table — edit there to tune. |
| **Public Surface** | `BuildRagdollPrefab()` (MenuItem). |
| **Collaborators** | Attaches `RagdollSetup`, `LocomotionDirector`, and the rest of the character runtime; reads `PhysicsMaterials/Ragdoll.physicsMaterial`. |
| **Phase** | 1 |

### `Character.GroundSensor` — `Assets/Scripts/Character/GroundSensor.cs`

| Concern | Detail |
|---------|--------|
| **What** | MonoBehaviour on each foot (`Foot_L`, `Foot_R`); performs a downward `SphereCast` every FixedUpdate and exposes `bool IsGrounded`. |
| **Why** | `BalanceController` needs to know when at least one foot is planted so it can modulate its upright torque. |
| **Public Surface** | `IsGrounded: bool` — read by `BalanceController`. |
| **Collaborators** | Read by `BalanceController`; attached by `RagdollBuilder` (Editor). |
| **Phase** | 2B |

### `Character.BalanceController` — `Assets/Scripts/Character/BalanceController.cs`

| Concern | Detail |
|---------|--------|
| **What** | MonoBehaviour on the Hips; pure executor that applies PD torque every FixedUpdate to carry out the current `BodySupportCommand` — upright, yaw, height maintenance, and COM lean alignment. |
| **Why** | Active ragdolls need continuous corrective torque to counteract gravity and perturbations. All locomotion-specific support intent now comes exclusively from `LocomotionDirector` via `BodySupportCommand`; the controller no longer introduces independent locomotion heuristics. |
| **Public Surface** | `IsGrounded: bool`, `IsFallen: bool`, `StandingHipsHeight: float`, `SetFacingDirection(Vector3)` [Obsolete — kept for test compatibility]. The runtime support-command path is consumed internally from `LocomotionDirector`. |
| **Collaborators** | Reads `GroundSensor.IsGrounded`; consumes `BodySupportCommand` from `LocomotionDirector` (upright/yaw/stabilization strength, height maintenance scale, desired lean degrees); uses `CharacterState` only for fallen/get-up yaw suppression; feeds diagnostics through fallen/grounded state. |
| **Phase** | Unified locomotion roadmap C5 |

### `Character.PlayerMovement` — `Assets/Scripts/Character/PlayerMovement.cs`

| Concern | Detail |
|---------|--------|
| **What** | MonoBehaviour on Hips; reads player input, applies camera-relative horizontal forces and jump impulses, and snapshots the current desired locomotion intent for the director. |
| **Why** | Separates input-to-force translation from balance and gait execution concerns while exposing a neutral desired-input view for the locomotion coordinator and deferring collapse safety decisions to authoritative state labels. |
| **Public Surface** | `CurrentMoveInput: Vector2`, `CurrentDesiredInput`, `SetMoveInputForTest(Vector2)`, `SetJumpInputForTest(bool)` — test and coordination seams. |
| **Collaborators** | `CharacterState` (jump gate plus non-ambulatory locomotion suppression), `CameraFollow` (movement direction), `LocomotionDirector` (desired-input and jump-intent snapshot). |
| **Phase** | 3B |

### `Character.LocomotionDirector` — `Assets/Scripts/Character/Locomotion/LocomotionDirector.cs`

| Concern | Detail |
|---------|--------|
| **What** | MonoBehaviour on Hips; reads `PlayerMovement` desired input plus a shared Chapter 2 sensor snapshot, filters support confidence through serialized C2.3 hysteresis settings, seeds the locomotion world-model observation, converts that observation risk into body-support recovery plus upright/yaw/stabilization strength decisions, and publishes body-support plus leg-command frames each FixedUpdate. |
| **Why** | Establishes the first real runtime locomotion owner in the single-voice roadmap while keeping `BalanceController` and `LegAnimator` as explicit executors. |
| **Public Surface** | `HasCommandFrame: bool`, `IsPassThroughMode: bool`; internal snapshots for current desired input, shared sensor data, filtered observation, support command, and left/right leg commands. |
| **Collaborators** | Reads `PlayerMovement`, `BalanceController`, `CharacterState`, optional `LocomotionCollapseDetector`, optional `LegAnimator`, the shared `LocomotionSensorAggregator` helper over the Hips `Rigidbody` plus foot sensors, and the internal `SupportObservationFilter`; publishes support commands into `BalanceController` and leg command frames into `LegAnimator`, while treating raw collapse detection as observation/watchdog data rather than executor authority. |
| **Phase** | Unified locomotion roadmap C1.3-C2.5 |

### `Character.Locomotion Contracts, Sensor Aggregation, And Observation Filtering` — `Assets/Scripts/Character/Locomotion/*.cs`

| Concern | Detail |
|---------|--------|
| **What** | Internal locomotion types now cover the command/observation contracts (`DesiredInput`, `FootContactObservation`, `SupportObservation`, `LocomotionObservation`, `BodySupportCommand`, `LegCommandOutput`, `LocomotionLeg`, `LegCommandMode`), the Chapter 3 leg-role contracts (`LegStateType`, `LegStateTransitionReason`, `LegStateFrame`), the new `LegStateMachine` per-leg controller, the Chapter 4 `StepTarget` step-planning contract, the Chapter 4 `StepPlanner` that computes world-space step targets from locomotion observations, the shared Chapter 2 sensor helpers (`SupportGeometry`, `LocomotionSensorSnapshot`, `LocomotionSensorAggregator`), and the C2.3 `SupportObservationFilter` that stabilizes planted/unplanted support confidence over time. |
| **Why** | Keeps one test-backed handoff boundary for director-owned commands while centralizing raw foot-contact, hips-motion, yaw-rate, support-geometry sampling, confidence filtering, explicit leg-state labels, step-target planning data, and the new left/right controller timing so support and gait decisions are expressed in locomotion language instead of repeated transform math or one-frame sensor noise. |
| **Public Surface** | Internal to `PhysicsDrivenMovement.Character`; neutral helpers such as `BodySupportCommand.PassThrough(...)`, `LegCommandOutput.Disabled(LocomotionLeg)`, `LegCommandOutput.State`, `LegCommandOutput.TransitionReason`, `LegStateMachine.SyncFromLegacyPhase(...)`, `StepTarget.Invalid`, and support-geometry classification methods seed the current migration slice without widening public APIs. |
| **Collaborators** | Sits between `GroundSensor`, `PlayerMovement`, `BalanceController`, `CharacterState`, `LocomotionCollapseDetector`, `LegAnimator`, and `LocomotionDirector`. |
| **Phase** | Unified locomotion roadmap C1.2-C4.2 |

### `Character.CharacterState` — `Assets/Scripts/Character/CharacterState.cs`

| Concern | Detail |
|---------|--------|
| **What** | MonoBehaviour on Hips; FSM with states `Standing / Moving / Airborne / Fallen / GettingUp`. |
| **Why** | Centralises high-level state labels so LegAnimator, ArmAnimator, BalanceController, and PlayerMovement can all read a single authoritative safety state without deriving gait strategy locally. |
| **Public Surface** | `CurrentState: CharacterStateType`, `OnStateChanged` event, `SetStateForTest(CharacterStateType)` — test seam. |
| **Collaborators** | `BalanceController` (IsGrounded, IsFallen), `PlayerMovement` (move input + jump gate), `LocomotionCollapseDetector` (watchdog fall signal), `LocomotionDirector` (observation snapshot), `LegAnimator` / `ArmAnimator` (subscribe to OnStateChanged). |
| **Phase** | 3C / unified locomotion roadmap C1.5 |

### `Character.LocomotionCollapseDetector` — `Assets/Scripts/Character/LocomotionCollapseDetector.cs`

| Concern | Detail |
|---------|--------|
| **What** | MonoBehaviour on Hips; detects the bounded strong-intent/no-progress/rear-support collapse regime that posture-only fallen thresholds miss during sharp-turn locomotion failures. |
| **Why** | Prevents the sustained hover-kick loop where feet trail behind the hips, progress collapses, and the FSM never enters `Fallen` because upright angle alone stays below the global fallen threshold. |
| **Public Surface** | `IsCollapseConfirmed: bool` — watchdog signal consumed by `CharacterState` and observed by `LocomotionDirector` snapshots; execution systems should react through state labels or director commands instead of reading it directly. |
| **Collaborators** | Reads `BalanceController.IsGrounded`, `PlayerMovement.CurrentMoveInput`, `PlayerMovement.CurrentFacingDirection`, and the shared `LocomotionSensorAggregator` support geometry over the ragdoll feet; feeds the safety layer rather than directly suppressing locomotion executors. |
| **Phase** | 3C / locomotion recovery hardening / unified locomotion roadmap C1.5 |

### `Character.LegAnimator` — `Assets/Scripts/Character/LegAnimator.cs`

| Concern | Detail |
|---------|--------|
| **What** | MonoBehaviour on Hips; consumes explicit left and right leg command frames, advances them through the internal `LegStateMachine` controllers for the Chapter 3 pass-through path, and applies UpperLeg and LowerLeg ConfigurableJoint `targetRotation` values while retaining a sinusoidal executor fallback. |
| **Why** | Ragdoll characters still need procedural joint targets, but locomotion intent ownership now lives in `LocomotionDirector` and the per-leg controller layer instead of inside a purely mirrored phase-only animator loop. |
| **Public Surface** | `Phase: float`, `SmoothedInputMag: float`, `StepAngleDegrees`, `KneeAngleDegrees` — read by `ArmAnimator` and diagnostics; internal command-frame seams are consumed by `LocomotionDirector`. |
| **Collaborators** | `CharacterState` (non-ambulatory state gate), `BalanceController` (defers leg joints via `_deferLegJointsToAnimator`), `LocomotionDirector` (publishes explicit command frames and requests pass-through planning during migration), `LegStateMachine` (left/right Chapter 3 state progression). |
| **Key design** | Supports both legacy local-frame swing and a world-space swing path. Current field default in code is `_useWorldSpaceSwing = false`. Angular velocity spin gate at 8 rad/s. Command frames apply immediately and suppress late writes on non-ambulatory frames to keep timing stable. |
| **Phase** | 3E |

### `Character.ArmAnimator` — `Assets/Scripts/Character/ArmAnimator.cs`

| Concern | Detail |
|---------|--------|
| **What** | MonoBehaviour on Hips; drives UpperArm ConfigurableJoint `targetRotation` for counter-swing arm gait. LowerArm/Hand joints remain floppy. |
| **Why** | Separate from LegAnimator — arms will need independent behaviours (punch, grab, idle sway) in Phase 4. |
| **Public Surface** | None currently — internally reads `LegAnimator.Phase` and `LegAnimator.SmoothedInputMag`. |
| **Collaborators** | `LegAnimator` (phase/magnitude), `CharacterState` (state gate). |
| **Phase** | 3E4 |

### `Character.CameraFollow` — `Assets/Scripts/Character/CameraFollow.cs`

| Concern | Detail |
|---------|--------|
| **What** | MonoBehaviour on the scene Camera; orbital third-person follow with mouse/stick orbit, pitch clamp, SphereCast collision avoidance, and cursor lock. |
| **Why** | Camera must follow the ragdoll Hips smoothly — SmoothDamp position + look, not hard-snapping. |
| **Public Surface** | `_target: Transform` (serialized, auto-finds `PlayerMovement` if null). |
| **Collaborators** | Reads Hips transform position. `PlayerMovement` reads camera yaw for camera-relative movement. |
| **Phase** | 3G (delivered by Jermie) |

### `Character.FallPoseRecorder` — `Assets/Scripts/Character/FallPoseRecorder.cs`

| Concern | Detail |
|---------|--------|
| **What** | Optional MonoBehaviour on Hips that records line-delimited JSON pose samples around falls or manual investigator triggers. |
| **Why** | Corner and recovery regressions need exact knee/foot placement relative to the hips basis before and after a topple, without building a bespoke harness each time. |
| **Public Surface** | `TriggerRollingCapture(string)`, `IsCaptureActive`, `CompletedSessionCount`, `BufferedSampleCount`, `LogFilePath` |
| **Collaborators** | Reads `CharacterState` transitions, `BalanceController` grounded/fallen state, `PlayerMovement` input, and lower-leg / foot transforms. `MovementQualityTests` attaches and configures it automatically. |
| **Phase** | Diagnostics support |

---

### `Editor.SceneBuilder` — `Assets/Scripts/Editor/SceneBuilder.cs`

| Concern | Detail |
|---------|--------|
| **What** | Static editor class; builds the `Arena_01.unity` test scene with ground plane, lighting, spawn points, and a `GameSettings` object. |
| **Phase** | 0 |

---

### `Environment.ArenaRoom` — `Assets/Scripts/Environment/ArenaRoom.cs`

| Concern | Detail |
|---------|--------|
| **What** | Lightweight runtime room metadata component attached to generated museum room GameObjects. |
| **Why** | Allows gameplay systems to query room membership from world-space positions without hardcoding scene geometry. |
| **Public Surface** | `RoomName`, `RoomBounds`, `ContainsPoint(Vector3)`, `Initialise(string, Bounds)` |
| **Collaborators** | Authored by `ArenaBuilder`; intended for future room-aware hazards, scoring, and AI logic. |
| **Phase** | Concept/prototype support |

### `Editor.ArenaBuilder` — `Assets/Scripts/Editor/ArenaBuilder.cs`

| Concern | Detail |
|---------|--------|
| **What** | Static editor class that procedurally builds the museum concept scene `Museum_01.unity`. |
| **Why** | Keeps the museum layout reproducible and data-driven instead of hand-editing scene geometry. |
| **Public Surface** | `BuildMuseumArena()` (MenuItem). |
| **Collaborators** | Creates `ArenaRoom` components, `GameSettings`, room geometry, and spawn points. |
| **Phase** | Concept/prototype support |

### `Editor.PropBuilder` — `Assets/Scripts/Editor/PropBuilder.cs`

| Concern | Detail |
|---------|--------|
| **What** | Editor helper for generating environment props used by builder workflows. |
| **Why** | Centralises repeatable prop creation instead of embedding prop geometry directly in scene code. |
| **Public Surface** | Builder helpers consumed by editor-time scene generation. |
| **Collaborators** | Used alongside `ArenaBuilder` and other editor tools. |
| **Phase** | Concept/prototype support |

### `Editor.SkinnedRagdollBuilder` — `Assets/Scripts/Editor/SkinnedRagdollBuilder.cs`

| Concern | Detail |
|---------|--------|
| **What** | Editor helper for producing skinned/visual ragdoll variants on top of the physics rig. |
| **Why** | Separates visual-rig generation from the base primitive ragdoll builder. |
| **Public Surface** | Editor-only build entry points/helpers. |
| **Collaborators** | Works with `RagdollBuilder` and `RagdollMeshFollower` workflows. |
| **Phase** | Visual pipeline support |

## 4 — Data Flow: Ragdoll Initialisation

```
Application Start
      │
      ▼
GameSettings.Awake()
  ├── Time.fixedDeltaTime = 0.01f
  ├── Physics.defaultSolverIterations = 12
  ├── Physics.defaultSolverVelocityIterations = 4
  └── Physics.IgnoreLayerCollision(player layer i, player layer i) × 4
      │
      ▼
PlayerRagdoll prefab instantiated
      │
      ▼
RagdollSetup.Awake()
  ├── GetComponentsInChildren<Rigidbody>  → _allBodies[15]
  ├── GetComponentsInChildren<ConfigurableJoint> → 14 joints
  └── Physics.IgnoreCollision(colA, colB) × (neighbour pairs)
```

---

## 5 — Ragdoll Joint Hierarchy

```
Hips (root — RagdollSetup, Rigidbody, BoxCollider)
├── Torso         ← ConfigurableJoint → Hips
│   ├── Head      ← ConfigurableJoint → Torso
│   ├── UpperArm_L ← ConfigurableJoint → Torso
│   │   └── LowerArm_L ← ConfigurableJoint → UpperArm_L
│   │       └── Hand_L ← ConfigurableJoint → LowerArm_L
│   └── UpperArm_R ← ConfigurableJoint → Torso
│       └── LowerArm_R ← ConfigurableJoint → UpperArm_R
│           └── Hand_R ← ConfigurableJoint → LowerArm_R
├── UpperLeg_L ← ConfigurableJoint → Hips
│   └── LowerLeg_L ← ConfigurableJoint → UpperLeg_L
│       └── Foot_L ← ConfigurableJoint → LowerLeg_L
└── UpperLeg_R ← ConfigurableJoint → Hips
    └── LowerLeg_R ← ConfigurableJoint → UpperLeg_R
        └── Foot_R ← ConfigurableJoint → LowerLeg_R
```

Total: **15 Rigidbodies**, **14 ConfigurableJoints**, mass ≈ 49 kg.

---

## 6 — Physics Settings Reference

| Parameter | Value | Where set |
|-----------|-------|-----------|
| `Time.fixedDeltaTime` | 0.01 s (100 Hz) | `GameSettings.Awake()` + `TimeManager.asset` |
| `Physics.defaultSolverIterations` | 12 | `GameSettings.Awake()` + `DynamicsManager.asset` |
| `Physics.defaultSolverVelocityIterations` | 4 | `GameSettings.Awake()` + `DynamicsManager.asset` |
| Player layer self-collision | Disabled | `GameSettings.SetupPlayerLayerCollisions()` |
| Ragdoll neighbour collision | Disabled per pair | `RagdollSetup.DisableNeighboringCollisions()` |

---

*End of ARCHITECTURE.md — update this file when new systems are introduced.*
