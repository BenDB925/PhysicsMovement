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
│  BalanceController ★    ─── PD torque → upright pose              │
│  GroundSensor ★         ─── foot ground detection                 │
│  PlayerMovement ★       ─── input → AddForce on Hips              │
│  LegAnimator ★          ─── procedural walk cycle                 │
│  CharacterState ★       ─── FSM (Standing/Moving/Airborne/...)    │
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
