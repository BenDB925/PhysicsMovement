# Architecture — PhysicsDrivenMovementDemo

> Living document. Update whenever a new system, assembly, or significant data flow is introduced.
> See `CODING_STANDARDS.md §7 Layer 5` for the update rules.

---

## 1 — High-Level System Diagram

```
┌────────────────────────────────────────────────────────────────────┐
│                           EDITOR TOOLS                             │
│  RagdollBuilder ──► PlayerRagdoll.prefab                          │
│  SceneBuilder   ──► Arena_01.unity                                │
└──────────────────────────────┬─────────────────────────────────────┘
                               │ (prefab / scene produced by)
                               ▼
┌────────────────────────────────────────────────────────────────────┐
│                          RUNTIME (HOST)                            │
│                                                                    │
│  GameSettings.Awake()   ─── configures Time, Physics, layers      │
│                                                                    │
│  RagdollSetup.Awake()   ─── disables neighbour collisions         │
│  BalanceController      ─── PD torque → upright + yaw pose        │
│  GroundSensor           ─── foot ground detection                 │
│  PlayerMovement         ─── input → AddForce on Hips              │
│  CharacterState         ─── FSM (Standing/Moving/Airborne/...)    │
│  LegAnimator            ─── procedural walk cycle (sinusoidal)    │
│  ArmAnimator            ─── counter-swing arm gait                │
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

---

## 2 — Assembly Structure

| Assembly | Path | Depends On | Notes |
|----------|------|------------|-------|
| `PhysicsDrivenMovement.Core` | `Assets/Scripts/Core/` | *(none)* | `GameSettings` singleton |
| `PhysicsDrivenMovement.Character` | `Assets/Scripts/Character/` | `Core` | Ragdoll physics scripts |
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
| **Collaborators** | Attaches `RagdollSetup`; reads `PhysicsMaterials/Ragdoll.physicsMaterial`. |
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
| **What** | MonoBehaviour on the Hips; applies a PD torque every FixedUpdate to keep the character upright and facing the desired direction. |
| **Why** | Active ragdolls need a continuous corrective torque to counteract gravity and perturbations; a PD controller is the standard technique (no integral windup risk). |
| **Public Surface** | `IsGrounded: bool`, `IsFallen: bool`, `SetFacingDirection(Vector3)` — consumed by `PlayerMovement` and `CharacterState` (Phase 3). |
| **Collaborators** | Reads `GroundSensor.IsGrounded`; forces applied to Hips `Rigidbody`. |
| **Phase** | 2C |

### `Character.PlayerMovement` — `Assets/Scripts/Character/PlayerMovement.cs`

| Concern | Detail |
|---------|--------|
| **What** | MonoBehaviour on Hips; reads player input and applies camera-relative horizontal forces and jump impulses. |
| **Why** | Separates input-to-force translation from balance/animation concerns. |
| **Public Surface** | `CurrentMoveInput: Vector2`, `SetMoveInputForTest(Vector2)`, `SetJumpInputForTest(bool)` — test seams. |
| **Collaborators** | `BalanceController.SetFacingDirection`, `CharacterState` (jump gate), `CameraFollow` (movement direction). |
| **Phase** | 3B |

### `Character.CharacterState` — `Assets/Scripts/Character/CharacterState.cs`

| Concern | Detail |
|---------|--------|
| **What** | MonoBehaviour on Hips; FSM with states `Standing / Moving / Airborne / Fallen / GettingUp`. |
| **Why** | Centralises state logic so LegAnimator, ArmAnimator, and BalanceController can all read a single authoritative state. |
| **Public Surface** | `CurrentState: CharacterStateType`, `OnStateChanged` event, `SetStateForTest(CharacterStateType)` — test seam. |
| **Collaborators** | `BalanceController` (IsGrounded, IsFallen), `PlayerMovement` (move input), `LegAnimator` / `ArmAnimator` (subscribe to OnStateChanged). |
| **Phase** | 3C |

### `Character.LegAnimator` — `Assets/Scripts/Character/LegAnimator.cs`

| Concern | Detail |
|---------|--------|
| **What** | MonoBehaviour on Hips; drives UpperLeg and LowerLeg ConfigurableJoint `targetRotation` values in a sinusoidal walk cycle based on move input. |
| **Why** | Ragdoll characters need procedural leg animation — no keyframe rigs, just physics-driven joint targets. |
| **Public Surface** | `Phase: float`, `SmoothedInputMag: float` — read by `ArmAnimator` for counter-swing. |
| **Collaborators** | `PlayerMovement` (input), `CharacterState` (state gate), `BalanceController` (defers leg joints via `_deferLegJointsToAnimator`). |
| **Key design** | Local-space swing only (`_useWorldSpaceSwing = false`). Angular velocity spin gate at 8 rad/s. Phase resets on restart/sharp turn to prevent leg snap. |
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

---

### `Editor.SceneBuilder` — `Assets/Scripts/Editor/SceneBuilder.cs`

| Concern | Detail |
|---------|--------|
| **What** | Static editor class; builds the `Arena_01.unity` test scene with ground plane, lighting, spawn points, and a `GameSettings` object. |
| **Phase** | 0 |

---

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
