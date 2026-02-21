# Physics-Driven Movement Demo — Project Plan

## Project Summary

A multiplayer online brawler featuring **Gang Beasts–style active ragdoll movement**. Players control wobbly physics-driven characters that grab, punch, and throw each other. The game uses **Unity (Built-in 3D)** with **Netcode for GameObjects (NGO)** for networking, supports **2–4 players** in **peer-to-peer (host/client)** matches.

---

## Tech Stack

| Concern | Choice |
|---------|--------|
| Engine | Unity 2022.3 LTS+ (Built-in Render Pipeline) |
| Physics | PhysX (built-in 3D physics) |
| Networking | Unity Netcode for GameObjects (NGO) |
| Transport | Unity Transport + Unity Relay (optional, for NAT traversal) |
| Input | New Input System (per-player action maps) |
| Hosting | Peer-to-peer — one player is the host |

---

## Architecture Principles

1. **Physics runs on the host only.** Clients send input; the host simulates physics and replicates results. This avoids desynced ragdolls.
2. **Characters are always ragdolls.** There is no kinematic mode. Muscle forces (joint drives) keep the character upright; removing those forces *is* the ragdoll state.
3. **Input must feel responsive.** Clients apply local prediction for movement forces, but the host is authoritative. Start without prediction; add it later if latency is noticeable.
4. **Primitives first, art later.** All characters are built from capsules/boxes until the physics feel right.

---

## Phase Breakdown

Each phase is a self-contained chunk of work. Phases are sequential — each builds on the previous one. Sub-tasks within a phase can often be done in parallel.

---

### Phase 0 — Project Setup

> **Goal:** Empty Unity project with correct settings, folder structure, and packages installed.

| Task | Details |
|------|---------|
| 0.1 | Create a new Unity 3D project (Built-in RP, Unity 2022.3 LTS or newer). |
| 0.2 | Install packages: `com.unity.netcode.gameobjects`, `com.unity.inputsystem`, `com.unity.transport`. |
| 0.3 | Set up folder structure: `Assets/Scripts/{Character, Networking, Input, UI, Core}`, `Assets/Prefabs`, `Assets/Scenes`, `Assets/Materials`. |
| 0.4 | Configure physics settings: `Time.fixedDeltaTime = 0.01` (100 Hz physics), `Physics.defaultSolverIterations = 12`, `Physics.defaultSolverVelocityIterations = 4`. |
| 0.5 | Set up Layer Collision Matrix: create layers `Player1Parts`, `Player2Parts`, `Player3Parts`, `Player4Parts`, `Environment`. Disable self-layer collisions for each player layer. |
| 0.6 | Create a basic test scene with a flat ground plane and spawn points. |

**Deliverable:** A runnable project with an empty scene, correct physics settings, and all packages imported.

---

### Phase 1 — Ragdoll Skeleton

> **Goal:** A humanoid ragdoll built from primitive colliders and Rigidbodies, connected by ConfigurableJoints. It flops realistically when dropped.

| Task | Details |
|------|---------|
| 1.1 | **Design the body plan.** Define the segments: Hips (root), Torso, Head, Upper Arm L/R, Lower Arm L/R, Hand L/R, Upper Leg L/R, Lower Leg L/R, Foot L/R. Total: ~15 rigidbodies. |
| 1.2 | **Build the hierarchy in a prefab.** Hips is the root GameObject. Each segment is a child with: a `Rigidbody` (use realistic masses — torso ~8kg, limb ~2kg, head ~3kg), a `CapsuleCollider` or `BoxCollider` sized to approximate that body part. |
| 1.3 | **Connect segments with ConfigurableJoints.** Each child connects to its parent. Lock linear motion on all axes (X, Y, Z locked). Set angular motion to Limited on appropriate axes. Use SLERP drive mode. Set reasonable angle limits (e.g., elbow: 0–140° on one axis, locked on others; hip: ±60° on each axis). |
| 1.4 | **Add a `PhysicMaterial`** with moderate friction (dynamic: 0.4, static: 0.6) to all colliders. |
| 1.5 | **Self-collision filtering.** Write an initialization script (`RagdollSetup.cs`) that calls `Physics.IgnoreCollision()` between colliders that are direct neighbors (e.g., upper arm ↔ lower arm) to prevent jitter. |
| 1.6 | **Test.** Drop the ragdoll from height onto the ground plane. It should flop and settle naturally without exploding or jittering. Tweak masses, joint limits, and solver iterations until stable. |

**Deliverable:** A `PlayerRagdoll` prefab that can be dropped into a scene and flops realistically.

#### Joint Configuration Reference

```
Hips (root, no joint)
├── Torso (connected to Hips)
│   ├── Head (connected to Torso)
│   ├── UpperArm_L (connected to Torso)
│   │   └── LowerArm_L (connected to UpperArm_L)
│   │       └── Hand_L (connected to LowerArm_L)
│   ├── UpperArm_R (connected to Torso)
│   │   └── LowerArm_R (connected to UpperArm_R)
│   │       └── Hand_R (connected to LowerArm_R)
├── UpperLeg_L (connected to Hips)
│   └── LowerLeg_L (connected to UpperLeg_L)
│       └── Foot_L (connected to LowerLeg_L)
├── UpperLeg_R (connected to Hips)
│   └── LowerLeg_R (connected to UpperLeg_R)
│       └── Foot_R (connected to LowerLeg_R)
```

---

### Phase 2 — Balance Controller

> **Goal:** The ragdoll can stand upright passively using joint drive forces. It wobbles but doesn't fall over on flat ground.

| Task | Details |
|------|---------|
| 2.1 | **Create `BalanceController.cs`** attached to the Hips. This script reads the current rotation of the Hips rigidbody vs. the desired upright rotation (Quaternion.identity for world-up). |
| 2.2 | **Implement a PD controller** (Proportional-Derivative, skip the Integral term to avoid windup). Calculate torque = `kP * angleError - kD * angularVelocity`. Apply via `Rigidbody.AddTorque()` on the Hips. Start with `kP = 500`, `kD = 50`. |
| 2.3 | **Spine stiffness.** Set the SLERP drive spring/damper on the Torso→Hips ConfigurableJoint to hold the torso upright relative to hips. Spring ~300, Damper ~30. |
| 2.4 | **Leg stiffness.** Set SLERP drive on all leg joints to hold a "standing" pose (legs straight down). Spring ~200, Damper ~20. |
| 2.5 | **Ground contact detection.** Add a `GroundSensor.cs` script to each foot. Use a short SphereCast or `OnCollisionStay` to detect ground. Expose a `bool IsGrounded` property. The BalanceController should only apply full upright torque when at least one foot is grounded. |
| 2.6 | **Tuning UI.** Create a simple runtime debug panel (or use `[Range]` attributes in the Inspector) for kP, kD, joint springs, and joint dampers so they can be tuned without recompiling. |
| 2.7 | **Test.** The character should stand on flat ground and wobble slightly. Pushing it (apply force via debug key) should make it sway but recover. A strong enough push should knock it down. |

**Deliverable:** The ragdoll stands up on its own and recovers from light pushes.

---

### Phase 3 — Locomotion

> **Goal:** The player can move the character with WASD/stick input. The character lurches forward with physics forces and stays (mostly) upright while moving.

| Task | Details |
|------|---------|
| 3.1 | **Input setup.** Create an Input Action Asset with a `Player` action map. Actions: `Move` (Vector2, WASD/left stick), `Jump` (Button), `Grab` (Button, placeholder), `Punch` (Button, placeholder). |
| 3.2 | **Create `PlayerMovement.cs`** attached to the Hips. In `FixedUpdate`, read the `Move` input vector. Convert to world-space direction relative to camera forward (project onto XZ plane). Apply a force to the Hips rigidbody: `rb.AddForce(moveDir * moveForce)`. Start with `moveForce = 300`. |
| 3.3 | **Speed capping.** Clamp the Hips' horizontal velocity to a max speed (e.g., 5 m/s) by reducing the applied force when near the cap, **not** by setting velocity directly. Use `Vector3.ClampMagnitude` on the horizontal component as a fallback. |
| 3.4 | **Turning.** Rotate the desired facing direction of the Hips toward the movement direction. Apply a torque around Y to turn, or adjust the balance controller's target rotation to face the move direction. |
| 3.5 | **Procedural leg movement.** Create `LegAnimator.cs`. When moving, oscillate the target rotation of the upper leg joints sinusoidally (simulating a walking cycle). The frequency should scale with speed. This makes the legs "walk" rather than drag. |
| 3.6 | **Jumping.** When grounded and Jump is pressed, apply an upward impulse to the Hips: `rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse)`. Temporarily reduce leg joint spring to let legs dangle in air. |
| 3.7 | **Falling & recovery state machine.** Create `CharacterState.cs` with states: `Standing`, `Moving`, `Airborne`, `Fallen`, `GettingUp`. Transitions: lose ground contact → `Airborne`; torso angle > 60° from upright → `Fallen`; in `Fallen` + grounded for 1s → `GettingUp` (boost joint drives + upward force on hips). `GettingUp` → `Standing` after uprighting. |
| 3.8 | **Camera.** Create a simple third-person follow camera (`CameraFollow.cs`) that tracks the Hips position with smoothing. For now, a single shared camera; split-screen or per-player camera comes in Phase 5. |
| 3.9 | **Test.** Run around the test scene. The character should lurch, wobble, fall over from sharp turns, and get back up. It should *feel* silly and physical. |

**Deliverable:** A controllable wobbly character that walks, jumps, falls, and recovers.

---

### Phase 4 — Grabbing & Combat

> **Goal:** Players can grab objects/other players and punch. Getting hit hard enough causes knockout (full ragdoll).

| Task | Details |
|------|---------|
| 4.1 | **Hand trigger zones.** Add a small `SphereCollider` (trigger) to each Hand. Create `HandGrabber.cs` that tracks overlapping rigidbodies. |
| 4.2 | **Grab mechanic.** On Grab input, if a hand trigger overlaps a valid target (another player's body part, or a grabbable object), create a `FixedJoint` at runtime between the Hand rigidbody and the target rigidbody. Store a reference to the joint. On Grab release, destroy the joint. |
| 4.3 | **Grab strength.** Set `FixedJoint.breakForce` to a finite value (e.g., 2000) so grabs can be broken by enough force. Stronger grab = higher break force (could be a stat). |
| 4.4 | **Arm stiffening on grab.** When grabbing, increase the SLERP drive spring on all arm joints (shoulder, elbow) so the arm holds firm. On release, restore original values. |
| 4.5 | **Punching.** On Punch input, temporarily: stiffen the punching arm joints, set arm joint target rotations to "extended forward" pose, apply a burst forward force to the hand rigidbody (e.g., `handRb.AddForce(facingDir * punchForce, ForceMode.Impulse)` with `punchForce = 50`). After 0.3s, restore arm to normal. |
| 4.6 | **Hit detection & knockout.** Create `HitReceiver.cs` on the Head. When the head receives a collision with relative velocity > threshold (e.g., 8 m/s), trigger knockout: set all joint drive springs to 0 for a duration (2–4 seconds). After knockout timer expires, restore joint drives (triggers `GettingUp` state). |
| 4.7 | **Headbutt.** Similar to punch but applies force to the head forward. Stiffen neck joint, impulse on head. |
| 4.8 | **Throwing.** If grabbing + move input in a direction + release grab, apply an impulse to the grabbed object in the throw direction. The force should be proportional to the character's current velocity. |
| 4.9 | **Test.** Two ragdolls in the scene (one AI-controlled or a second local player). Grab each other, punch, get knocked out, recover. |

**Deliverable:** Functional grab, punch, knockout, and throw mechanics.

---

### Phase 5 — Networking

> **Goal:** 2–4 players can connect over the network. Physics-driven characters are synchronized. Each player controls their own character.

| Task | Details |
|------|---------|
| 5.1 | **NetworkManager setup.** Add a `NetworkManager` to the scene. Configure the `UnityTransport` component. Set the `PlayerRagdoll` prefab as the Player Prefab. |
| 5.2 | **NetworkRigidbody on all parts.** Add `NetworkRigidbody` (from NGO) to every Rigidbody in the ragdoll prefab. On the **server/host**, rigidbodies are authoritative. On **clients**, they receive state updates and are kinematic. This is the key to syncing ragdolls. |
| 5.3 | **NetworkObject on root.** Add `NetworkObject` to the Hips (root). All child `NetworkRigidbody` components will be nested under this. |
| 5.4 | **Input transmission.** Modify `PlayerMovement.cs`: the owning client reads local input and sends it to the server via a `ServerRpc`. The server applies forces based on received input. Do **not** apply forces on the client — only the host simulates physics. |
| 5.5 | **Ownership & authority.** Ensure `PlayerMovement`, `HandGrabber`, etc. only execute their logic if `IsOwner` (for input reading) or `IsServer` (for physics application). |
| 5.6 | **Grab sync.** When a grab happens on the server, broadcast it via `ClientRpc` so clients can visually attach the joint (or just let `NetworkRigidbody` sync handle the resulting positions). |
| 5.7 | **Knockout sync.** Knockout state should be a `NetworkVariable<bool>`. When it changes, clients adjust joint drives locally for visual fidelity. |
| 5.8 | **Spawn system.** Create a `GameManager.cs` (NetworkBehaviour) that spawns each connecting player's ragdoll at a spawn point. Track connected players. |
| 5.9 | **Connection UI.** Simple UI: Host button, Join button + IP/port text field. Use `NetworkManager.Singleton.StartHost()` and `StartClient()`. Later, replace with Unity Relay for NAT traversal. |
| 5.10 | **Bandwidth optimization.** Configure `NetworkRigidbody` sync rates. Not every bone needs full-rate sync. Hips/Torso sync at high rate (30 Hz); extremities (hands, feet) can sync at lower rate (15 Hz) or be derived from joint constraints client-side. |
| 5.11 | **Test.** Two builds (or Editor + Build). Connect, see both characters, move independently, grab each other, punch. Verify ragdoll states are reasonably synced. |

**Deliverable:** Playable networked prototype with 2–4 synced ragdoll characters.

#### Networking Architecture Diagram

```
┌──────────────────────────────────────────────────┐
│                   HOST (Player 1)                │
│                                                  │
│  Local Input ──► ServerRpc ──► Apply Forces      │
│                                    │             │
│  Remote Inputs (P2,P3,P4) ────────►│             │
│       (via ServerRpc)              │             │
│                                    ▼             │
│                           Physics Simulation     │
│                              (100 Hz)            │
│                                    │             │
│                         NetworkRigidbody Sync     │
│                                    │             │
│               ┌────────────────────┼──────────┐  │
│               ▼                    ▼          ▼  │
│           Client P2           Client P3   Client P4
│         (kinematic)         (kinematic)  (kinematic)
└──────────────────────────────────────────────────┘
```

---

### Phase 6 — Game Loop & UI

> **Goal:** A complete game loop — lobby, countdown, round, win condition, restart.

| Task | Details |
|------|---------|
| 6.1 | **Lobby system.** `LobbyManager.cs`: players join, see a player list, host can start the game. Sync player names/colors via `NetworkVariable` or `NetworkList`. |
| 6.2 | **Round system.** `RoundManager.cs`: countdown (3-2-1), enable player input, track alive players. A player is "eliminated" when they fall off the map (Y < threshold) or are KO'd for too long. |
| 6.3 | **Win condition.** Last player standing wins. Display winner UI. After 5 seconds, return to lobby or start a new round. |
| 6.4 | **Hazards.** Add simple environmental hazards: kill zones (trigger volumes that eliminate on contact), moving platforms, swinging objects. |
| 6.5 | **HUD.** Minimal HUD: player name labels above heads (world-space Canvas), round timer, elimination feed. |
| 6.6 | **Spectator mode.** Eliminated players get a free-look camera until the round ends. |
| 6.7 | **Test.** Full game loop: connect → lobby → round → elimination → winner → restart. |

**Deliverable:** A playable game with a complete loop.

---

### Phase 7 — Polish & Juice

> **Goal:** Make it feel good. Sound, particles, visual feedback, better visuals.

| Task | Details |
|------|---------|
| 7.1 | **Character customization.** Swap primitive colors/materials per player. Later: simple mesh characters (low-poly humanoids). |
| 7.2 | **Impact effects.** Particle burst + sound on hard collisions (relative velocity > threshold). Screen shake on big hits. |
| 7.3 | **Sound design.** Footstep sounds (trigger on foot ground contact), punch whoosh, grab squelch, knockout bonk, crowd reactions. |
| 7.4 | **Level design.** 2–3 arena maps with different hazards and layouts. |
| 7.5 | **Main menu.** Title screen → Host/Join → Lobby → Game. |
| 7.6 | **Unity Relay integration.** Replace raw IP connection with Unity Relay for NAT traversal. Players share a join code instead of IP. |
| 7.7 | **Settings.** Resolution, fullscreen, audio volume, controls rebinding. |
| 7.8 | **Build & distribute.** Windows builds, test with friends over the internet. |

**Deliverable:** A polished, distributable prototype.

---

## Key Technical Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Joint instability / explosions | Characters fly apart | Fixed timestep 0.01s, high solver iterations, joint preprocessing enabled, careful mass ratios (max 5:1 between connected bodies) |
| Balance PID tuning | Character can't stand or is too stiff | Expose all parameters at runtime, use `[Range]` sliders, start with PD only (no integral), iterate extensively |
| NetworkRigidbody bandwidth | Lag / rubber-banding with 15 rigidbodies × 4 players | Reduce sync rates on extremities, compress quaternions, sync only where physics authority lives (host) |
| Host advantage in P2P | Host has zero latency | Acceptable for casual play; mitigate with Unity Relay for fair latency; consider dedicated server later |
| Client-side input delay | Movement feels sluggish for non-host players | Apply forces locally as prediction, reconcile with server state; start without prediction and see if it's tolerable at 2-4 player scale |
| Recovery animation | Getting up looks robotic | Blend joint drive restoration over time, apply staged forces (hips up first, then torso straighten), add randomness |

---

## File / Folder Structure

```
Assets/
├── Scenes/
│   ├── MainMenu.unity
│   ├── Lobby.unity
│   └── Arena_01.unity
├── Prefabs/
│   ├── PlayerRagdoll.prefab
│   ├── NetworkManager.prefab
│   └── Hazards/
├── Scripts/
│   ├── Character/
│   │   ├── RagdollSetup.cs          (Phase 1 — self-collision ignore, joint init)
│   │   ├── BalanceController.cs     (Phase 2 — PD upright torque)
│   │   ├── GroundSensor.cs          (Phase 2 — foot ground detection)
│   │   ├── PlayerMovement.cs        (Phase 3 — input → forces)
│   │   ├── LegAnimator.cs           (Phase 3 — procedural walk cycle)
│   │   ├── CharacterState.cs        (Phase 3 — state machine)
│   │   ├── HandGrabber.cs           (Phase 4 — grab mechanic)
│   │   └── HitReceiver.cs           (Phase 4 — knockout on impact)
│   ├── Networking/
│   │   ├── PlayerNetworkInput.cs    (Phase 5 — input RPCs)
│   │   ├── GrabSync.cs             (Phase 5 — grab replication)
│   │   └── GameManager.cs          (Phase 5 — spawn, connection)
│   ├── GameLoop/
│   │   ├── LobbyManager.cs         (Phase 6 — lobby)
│   │   ├── RoundManager.cs         (Phase 6 — round logic)
│   │   └── SpectatorCamera.cs      (Phase 6 — eliminated player cam)
│   ├── Input/
│   │   └── PlayerInputActions.inputactions
│   ├── UI/
│   │   ├── ConnectionUI.cs         (Phase 5 — host/join buttons)
│   │   ├── HUD.cs                  (Phase 6 — in-game UI)
│   │   └── MainMenuUI.cs           (Phase 7 — title screen)
│   └── Core/
│       └── GameSettings.cs         (constants, physics config)
├── Materials/
│   ├── PlayerColors/
│   └── Environment/
└── Audio/
    ├── SFX/
    └── Music/
```

---

## Agent Task Cards

Below is each task formatted as a standalone brief you can hand to an agent. Each card is self-contained with context, requirements, inputs, and expected outputs.

---

### AGENT CARD: Phase 0 — Project Bootstrap

**Context:** We are building a Unity multiplayer ragdoll brawler. This is the very first task.

**Instructions:**
1. Create a Unity project (3D, Built-in RP) or verify the existing one is set up.
2. Install packages via the Package Manager or `manifest.json`: `com.unity.netcode.gameobjects`, `com.unity.inputsystem`, `com.unity.transport`.
3. Create the folder structure shown in the File / Folder Structure section above.
4. Create `GameSettings.cs` in `Scripts/Core/` that sets `Time.fixedDeltaTime = 0.01f`, `Physics.defaultSolverIterations = 12`, `Physics.defaultSolverVelocityIterations = 4` in `Awake()`.
5. Set up the Layer Collision Matrix via script or document the manual steps.
6. Create a test scene (`Arena_01.unity`) with a ground plane (scale 20×1×20), basic lighting, and 4 spawn point empty GameObjects at corners.

**Output:** A runnable Unity project that opens without errors, with all packages imported and physics configured.

---

### AGENT CARD: Phase 1 — Ragdoll Skeleton

**Context:** We have a Unity project with correct physics settings (100 Hz fixed timestep, 12 solver iterations). We need to build the ragdoll character from primitives.

**Instructions:**
1. Build a humanoid ragdoll prefab (`PlayerRagdoll.prefab`) using the hierarchy and joint structure documented in Phase 1 above.
2. Use capsules for limbs, box for torso, sphere for head. Color with a default material.
3. Connect all segments with ConfigurableJoints using SLERP drive. Set sensible angle limits per joint.
4. Use realistic mass distribution totaling ~50kg.
5. Write `RagdollSetup.cs` that disables collisions between neighboring body parts on `Awake()`.
6. Add a `PhysicMaterial` with static friction 0.6, dynamic friction 0.4, friction combine mode = Average.

**Test criteria:** Drop the prefab from 3m height. It should land, flop, and settle without exploding, tunneling through the ground, or jittering. All joints should stay intact.

**Output:** A working `PlayerRagdoll.prefab` and `RagdollSetup.cs`.

---

### AGENT CARD: Phase 2A — Joint Drives (Prefab Update)

> **Why this is its own step:** `RagdollBuilder.ConfigureJoint()` currently sets joint *limits* only — no `JointDrive` (spring/damper) is set on any joint. The balance controller and leg animator both depend on drive forces being present. This step adds drives before any runtime scripts are written.

**Context:** The `PlayerRagdoll` prefab exists and flops correctly. All 14 `ConfigurableJoint` components have limits but zero spring/damper forces. We need to add SLERP drive values so joints resist displacement and can be controlled by downstream systems.

**Dependencies:** Phase 1 complete. `PlayerRagdoll.prefab` exists at `Assets/Prefabs/PlayerRagdoll.prefab`.

**Read first:**
- `Assets/Scripts/Editor/RagdollBuilder.cs` — understand `ConfigureJoint()`. You will add a `SetupDrives()` call inside it.
- `ARCHITECTURE.md §5` — the full joint hierarchy and GameObject names.

**Assembly / Namespace:** All editor code lives in `PhysicsDrivenMovement.Editor` namespace, `Assets/Scripts/Editor/`.

**Instructions:**

1. **Add a `JointDriveProfile` struct** near the top of `RagdollBuilder.cs` (still inside the class) containing `float Spring`, `float Damper`, `float MaxForce` fields. Add a corresponding field `JointDriveProfile DriveProfile` to `SegmentDef`.

2. **Populate default drive profiles in the `Segments` table** using these starting values (add to each `new SegmentDef(...)` call):

   | Segment(s) | Spring | Damper | MaxForce |
   |---|---|---|---|
   | Torso | 300 | 30 | 1000 |
   | Head | 150 | 15 | 500 |
   | UpperArm_L / UpperArm_R | 100 | 10 | 400 |
   | LowerArm_L / LowerArm_R | 80 | 8 | 300 |
   | Hand_L / Hand_R | 50 | 5 | 200 |
   | UpperLeg_L / UpperLeg_R | 200 | 20 | 800 |
   | LowerLeg_L / LowerLeg_R | 150 | 15 | 600 |
   | Foot_L / Foot_R | 80 | 8 | 300 |

3. **Add a `SetupDrives()` call at the end of `ConfigureJoint()`** that applies the drive profile to the joint's SLERP drive:
   ```csharp
   JointDrive slerpDrive = new JointDrive
   {
       positionSpring = seg.DriveProfile.Spring,
       positionDamper = seg.DriveProfile.Damper,
       maximumForce   = seg.DriveProfile.MaxForce
   };
   joint.slerpDrive = slerpDrive;
   joint.rotationDriveMode = RotationDriveMode.Slerp;
   ```
   Also set `joint.targetRotation = Quaternion.identity;` — this means "hold the joint at its rest angle" (the angle it was at when the joint was created). Do **not** set it to a world-space rotation. This is a critical Unity convention: `targetRotation` is relative to the joint's initial local orientation, not world space.

4. **Re-run the builder** via `Tools → PhysicsDrivenMovement → Build Player Ragdoll` to regenerate the prefab with drives applied.

5. **Verify:** Open the prefab in the Inspector. Select `Torso`. Confirm the `ConfigurableJoint` component shows `Rotation Drive Mode = Slerp` and `Slerp Drive` has non-zero spring/damper values.

**Test criteria:** Re-build the prefab, drop it from height in Play mode. It should still flop (drives aren't strong enough to hold it upright by themselves — that's correct for now; the balance controller does that). No joint explosions.

**Output:** Updated `RagdollBuilder.cs`, regenerated `PlayerRagdoll.prefab` with SLERP drives on all 14 joints.

---

### AGENT CARD: Phase 2B — Ground Sensor

**Context:** The prefab has joint drives. We now need feet to report ground contact so the balance controller can modulate its output.

**Dependencies:** Phase 2A complete.

**Read first:**
- `ARCHITECTURE.md §5` — note that `Foot_L` and `Foot_R` are the two foot GameObjects. Each is a `BoxCollider` (0.10 × 0.07 × 0.22 m).
- `Assets/Scripts/Character/RagdollSetup.cs` — note `AllBodies` public API; you won't need it here but awareness helps.

**Assembly / Namespace:** `PhysicsDrivenMovement.Character`, file at `Assets/Scripts/Character/GroundSensor.cs`.

**Instructions:**

1. **Create `GroundSensor.cs`** (MonoBehaviour). Attach to a foot GameObject (`Foot_L` or `Foot_R`). It needs no reference to the root.

2. **Expose these serialised fields** (all with `[Range]` attributes):
   - `[SerializeField, Range(0.02f, 0.2f)] float _castRadius = 0.06f` — SphereCast radius.
   - `[SerializeField, Range(0.01f, 0.3f)] float _castDistance = 0.12f` — how far below the foot centre to check.
   - `[SerializeField] LayerMask _groundLayers` — assign `Environment` layer (layer 12 per `GameSettings`).

3. **`bool IsGrounded` public property** backed by a private `_isGrounded` bool updated in `FixedUpdate`.

4. **In `FixedUpdate`**, cast a `Physics.SphereCast` from `transform.position` downward (`-transform.up` is not reliable on a foot that may tilt — use `Vector3.down` in world space). If the cast hits anything in `_groundLayers`, set `_isGrounded = true`, otherwise `false`.

5. **Visualise in `OnDrawGizmosSelected`**: draw a green (grounded) or red (airborne) wire sphere at the cast endpoint.

6. **Attach the component** to both `Foot_L` and `Foot_R` in the `PlayerRagdoll` prefab. Assign `_groundLayers` to the `Environment` layer.

**Test criteria:** In Play mode, with the prefab on the ground plane, both `GroundSensor` components show `IsGrounded = true` in the Inspector. Lift the prefab above the ground; `IsGrounded` becomes `false`.

**Output:** `GroundSensor.cs`, prefab updated with `GroundSensor` on both feet.

---

### AGENT CARD: Phase 2C — Balance Controller

> **Critical Unity gotcha — read carefully before writing any code:** `ConfigurableJoint.targetRotation` is specified in the *joint's local drive frame*, not world space. To drive a joint toward a pose in world space, you must transform your world-space target quaternion into drive space: `joint.targetRotation = Quaternion.Inverse(joint.transform.rotation) * worldTarget * initialJointRotation`. In practice for the Hips (which has no joint), we apply torque directly via `Rigidbody.AddTorque`, so this caveat applies only when you later set target rotations on child joints. For the Hips PD controller, work entirely with world-space `Rigidbody` angular quantities.

**Context:** SLERP drives exist; ground sensors report foot contact. Now add the PD torque controller that keeps the hips upright.

**Dependencies:** Phase 2A and 2B complete.

**Read first:**
- `Assets/Scripts/Character/RagdollSetup.cs` — `AllBodies` property; `BalanceController` should cache a reference to the `RagdollSetup` component.
- `ARCHITECTURE.md §5` — Hips is the root with no joint. Torso is a child of Hips.

**Assembly / Namespace:** `PhysicsDrivenMovement.Character`, file at `Assets/Scripts/Character/BalanceController.cs`.

**Instructions:**

1. **Create `BalanceController.cs`** (`[RequireComponent(typeof(Rigidbody))]`). Attach to Hips.

2. **Serialised fields** (all `[SerializeField]` with `[Range]`):
   - `[Range(0f, 2000f)] float _kP = 500f`
   - `[Range(0f, 200f)] float _kD = 50f`
   - `[Range(0f, 1f)] float _airborneMultiplier = 0.2f`
   - `[Range(0f, 90f)] float _fallenAngleThreshold = 60f` — torso world-up angle at which the character is considered fallen.

3. **Private fields:**
   - `Rigidbody _rb`
   - `GroundSensor _footL, _footR` — **found by name**: `transform.Find("UpperLeg_L/LowerLeg_L/Foot_L")` etc. Use `GetComponentsInChildren<GroundSensor>()` as a simpler alternative.
   - `Quaternion _targetFacingRotation = Quaternion.identity` — world-space target yaw. Initially forward. Exposed via a public setter (see below).

4. **Public API — define these now so Phase 3D can use them:**
   ```csharp
   /// <summary>Sets the world-space yaw direction the character should face.</summary>
   public void SetFacingDirection(Vector3 worldDirection) { ... }

   /// <summary>True when the torso's up-vector deviates more than _fallenAngleThreshold from world up.</summary>
   public bool IsFallen { get; private set; }

   /// <summary>True when at least one foot sensor reports ground contact.</summary>
   public bool IsGrounded { get; private set; }
   ```

5. **In `Awake`:** cache `_rb`, locate the two `GroundSensor` components.

6. **In `FixedUpdate`:**
   - Update `IsGrounded = _footL.IsGrounded || _footR.IsGrounded`.
   - Compute torso angle from world up: `float angle = Vector3.Angle(transform.up, Vector3.up)`. Set `IsFallen = angle > _fallenAngleThreshold`.
   - **PD torque:** compute the rotation error from current hips rotation to `_targetFacingRotation` (facing) combined with `Quaternion.identity` upright:
     ```csharp
     Quaternion targetRot = Quaternion.LookRotation(_targetFacingRotation * Vector3.forward, Vector3.up);
     Quaternion currentRot = _rb.rotation;
     Quaternion rotError   = targetRot * Quaternion.Inverse(currentRot);
     rotError.ToAngleAxis(out float angle, out Vector3 axis);
     if (angle > 180f) angle -= 360f;     // shortest path
     Vector3 torque = axis * (angle * _kP) - _rb.angularVelocity * _kD;
     float multiplier = IsGrounded ? 1f : _airborneMultiplier;
     _rb.AddTorque(torque * multiplier, ForceMode.Force);
     ```
   - If `IsFallen`, skip torque application (let the ragdoll lie flat; `GettingUp` state will handle recovery).

7. **`SetFacingDirection`** stores the direction as a yaw-only quaternion: `_targetFacingRotation = Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.z).normalized, Vector3.up)`. Guard against zero-length input.

**Test criteria:** Character stands upright on flat ground and wobbles but recovers. A 200 N debug-key force causes visible sway then correction. Increase force to 800 N and the character falls over (angle exceeds threshold, `IsFallen = true`).

**Output:** `BalanceController.cs` with defined public API, updated prefab.

---

### AGENT CARD: Phase 2D — Balance Tuning & Verification

**Context:** `BalanceController` and `GroundSensor` exist. This step adds a debug force key and an in-editor parameter panel so the balance feel can be iterated without re-compiling.

**Dependencies:** Phase 2C complete.

**Instructions:**

1. **Debug push key.** Add a temporary `DebugPushForce.cs` (MonoBehaviour, `Assets/Scripts/Character/`) that, in `Update`, listens for `KeyCode.P` (small push, 200 N) and `KeyCode.O` (large push, 800 N), and calls `GetComponent<Rigidbody>().AddForce(transform.forward * force)`. Mark the class with `#if UNITY_EDITOR` guards or a `[SerializeField] bool _enableDebugKeys` toggle.

2. **Runtime tuning.** `BalanceController` already has `[Range]` attributes — confirm they display correctly as sliders in the Inspector. No additional UI is needed at this stage.

3. **Verification checklist** (document results as a comment in `BalanceController.cs` under a `// TUNING LOG:` header):
   - Character stands still: ✓/✗
   - 200 N push → recovers within 3 s: ✓/✗
   - 800 N push → `IsFallen = true`: ✓/✗
   - Lift off ground → `IsGrounded = false`, balance torque reduces: ✓/✗

**Output:** `DebugPushForce.cs`, verified prefab, tuning log comment in `BalanceController.cs`.

---

### AGENT CARD: Phase 3A — Input Action Asset

**Context:** The character can stand. Before adding any movement logic, set up the input bindings in a Unity Input Action Asset. This card is entirely about data — no C# runtime logic.

**Dependencies:** Phase 2 complete. Package `com.unity.inputsystem` must be installed (it is, per Phase 0).

**Instructions:**

1. **Create `PlayerInputActions.inputactions`** at `Assets/Scripts/Input/PlayerInputActions.inputactions`.

2. **Action Map: `Player`** — add these actions:

   | Action | Type | Default Binding (KB) | Default Binding (Gamepad) |
   |---|---|---|---|
   | `Move` | Value / Vector2 | WASD composite | Left Stick |
   | `Jump` | Button | Space | South Button (A) |
   | `Grab` | Button | Left Shift | Left Trigger |
   | `Punch` | Button | Left Mouse Button | Right Trigger |

3. **Enable "Generate C# Class"** on the asset (tick the checkbox in the Inspector, set class name to `PlayerInputActions`, namespace to `PhysicsDrivenMovement.Input`). Save and let Unity generate the wrapper class.

4. **No MonoBehaviour needed yet.** The generated class will be instantiated by `PlayerMovement.cs` in Phase 3B.

**Output:** `PlayerInputActions.inputactions`, auto-generated `PlayerInputActions.cs` wrapper.

---

### AGENT CARD: Phase 3B — Player Movement (Force Application)

**Context:** Input bindings exist. Now apply horizontal forces to the Hips based on the `Move` input. No state machine, no leg animation — force application only.

**Dependencies:** Phase 3A complete. Phase 2C (`BalanceController`) complete.

**Read first:**
- `Assets/Scripts/Character/BalanceController.cs` — specifically the `IsGrounded`, `IsFallen` public properties.
- `Assets/Scripts/Input/PlayerInputActions.cs` (generated) — how to read the `Move` action.

**Assembly / Namespace:** `PhysicsDrivenMovement.Character`, file at `Assets/Scripts/Character/PlayerMovement.cs`.

**Instructions:**

1. **Create `PlayerMovement.cs`** (`[RequireComponent(typeof(Rigidbody))]`). Attach to `Hips`.

2. **Serialised fields:**
   - `[Range(0f, 1000f)] float _moveForce = 300f`
   - `[Range(1f, 20f)] float _maxSpeed = 5f`
   - `[SerializeField] Camera _camera` — assign the main camera in the prefab/scene.

3. **Private fields:** `Rigidbody _rb`, `BalanceController _balance`, `PlayerInputActions _inputActions`.

4. **In `Awake`:** cache `_rb`, `_balance`. Instantiate `new PlayerInputActions()` and call `.Enable()`.

5. **In `OnDestroy`:** call `_inputActions.Dispose()`.

6. **In `FixedUpdate`:**
   - Read `Vector2 rawInput = _inputActions.Player.Move.ReadValue<Vector2>()`.
   - If `_balance.IsFallen`, skip — do not apply movement forces while the character is on the ground.
   - Convert to world-space direction relative to camera: project the camera's forward/right vectors onto the XZ plane, normalise each, then `worldDir = (right * rawInput.x + forward * rawInput.y).normalized`.
   - **Speed cap:** read `Vector3 hVel = new Vector3(_rb.velocity.x, 0f, _rb.velocity.z)`. If `hVel.magnitude < _maxSpeed`, apply `_rb.AddForce(worldDir * _moveForce, ForceMode.Force)`. Otherwise apply zero (no friction fighting — let the balance controller's damping slow the character naturally).
   - **Facing:** call `_balance.SetFacingDirection(worldDir)` whenever `rawInput.magnitude > 0.1f`.

7. **`Vector2 CurrentMoveInput { get; }` public property** returning the latest raw input — needed by `LegAnimator` in Phase 3E.

**Test criteria:** In Play mode, WASD moves `Hips` in the input direction. Character does not exceed ~5 m/s. Turning via `SetFacingDirection` causes visible yaw rotation. No movement while `IsFallen = true`.

**Output:** `PlayerMovement.cs`, prefab updated (add component to Hips and wire `_camera`).

---

### AGENT CARD: Phase 3C — Character State Machine

**Context:** Force application works. Now formalise the character's states so that other systems (jump, get-up, leg animation, networking) can query and react to them cleanly.

**Dependencies:** Phase 3B complete. Phase 2C (`BalanceController`) complete.

**Read first:**
- `BalanceController.cs` — `IsFallen`, `IsGrounded`.
- `PlayerMovement.cs` — `CurrentMoveInput`.

**Assembly / Namespace:** `PhysicsDrivenMovement.Character`, file at `Assets/Scripts/Character/CharacterState.cs`.

**Instructions:**

1. **Define `CharacterStateType` enum** (same file, outside the class): `Standing`, `Moving`, `Airborne`, `Fallen`, `GettingUp`.

2. **Create `CharacterState.cs`** (MonoBehaviour). Attach to Hips.

3. **Public API:**
   ```csharp
   public CharacterStateType CurrentState { get; private set; }
   public event System.Action<CharacterStateType, CharacterStateType> OnStateChanged; // (from, to)
   ```

4. **Serialised fields:**
   - `[Range(0f, 5f)] float _getUpDelay = 1f` — seconds the character must be grounded and stationary while fallen before getting up.
   - `[Range(0f, 5f)] float _knockoutDuration = 3f` — minimum time spent in `Fallen` before `GettingUp` is allowed (used later by `HitReceiver`).

5. **Private fields:** `BalanceController _balance`, `PlayerMovement _movement`, `float _fallenTimer`, `float _getUpForce = 800f` (upward impulse applied to Hips when entering `GettingUp`).

6. **Transition table (implement in `FixedUpdate` as a switch):**

   | From | Condition | To |
   |---|---|---|
   | `Standing` | `!IsGrounded` | `Airborne` |
   | `Standing` | `CurrentMoveInput.magnitude > 0.1f` | `Moving` |
   | `Standing` | `IsFallen` | `Fallen` |
   | `Moving` | `!IsGrounded` | `Airborne` |
   | `Moving` | `CurrentMoveInput.magnitude <= 0.1f` | `Standing` |
   | `Moving` | `IsFallen` | `Fallen` |
   | `Airborne` | `IsGrounded && !IsFallen` | `Standing` |
   | `Airborne` | `IsGrounded && IsFallen` | `Fallen` |
   | `Fallen` | grounded for `_getUpDelay`s AND `_fallenTimer >= _knockoutDuration` | `GettingUp` |
   | `GettingUp` | `!IsFallen` | `Standing` |
   | `GettingUp` | timeout 3s (safety) | `Standing` |

7. **On entering `GettingUp`:** apply `GetComponent<Rigidbody>().AddForce(Vector3.up * _getUpForce, ForceMode.Impulse)` to help the character upright. Re-enable balance torque (it was implicitly skipped while `IsFallen`).

8. **On entering `Fallen`:** reset `_fallenTimer = 0`.

9. **While in `Fallen` and grounded:** increment `_fallenTimer += Time.fixedDeltaTime`.

10. Fire `OnStateChanged` whenever `CurrentState` changes.

**Test criteria:** Observe the Inspector while playing. State transitions correctly: push to fall → `Fallen` → waits ~1s → `GettingUp` → `Standing`. Jumping (covered in 3F) → `Airborne` → lands → `Standing`.

**Output:** `CharacterState.cs` with full FSM, `CharacterStateType` enum.

---

### AGENT CARD: Phase 3D — Turning

**Context:** The character moves forward but doesn't visually turn to face the movement direction. This phase wires `PlayerMovement.SetFacingDirection` into the balance controller's yaw torque.

**Note:** `PlayerMovement.cs` already calls `_balance.SetFacingDirection(worldDir)` (Phase 3B, step 6). This card verifies the yaw torque in `BalanceController` is correct and adds a dedicated yaw-only torque component separate from the uprighting torque.

**Dependencies:** Phase 3B complete.

**Read first:**
- `BalanceController.cs` — the current PD torque calculation in `FixedUpdate`. The existing implementation applies a single torque from a combined upright + facing quaternion. We need to split these into:
  1. **Upright torque** — corrects pitch and roll only.
  2. **Yaw torque** — corrects yaw toward `_targetFacingRotation`.

**Instructions:**

1. **Refactor `BalanceController.FixedUpdate`** to compute two separate torques and add them:

   ```csharp
   // ── Upright (pitch + roll) ──────────────────────────────────────────
   Vector3 hipsUp   = _rb.rotation * Vector3.up;
   Vector3 crossErr = Vector3.Cross(hipsUp, Vector3.up);          // pitch/roll axis
   float   dotErr   = Mathf.Clamp(Vector3.Dot(hipsUp, Vector3.up), -1f, 1f);
   float   angleErr = Mathf.Acos(dotErr) * Mathf.Rad2Deg;
   Vector3 uprightTorque = crossErr.normalized * (angleErr * _kP)
                         - new Vector3(_rb.angularVelocity.x, 0f, _rb.angularVelocity.z) * _kD;

   // ── Yaw (facing direction) ──────────────────────────────────────────
   Vector3 hipsForward = _rb.rotation * Vector3.forward;
   Vector3 targetFwd   = _targetFacingRotation * Vector3.forward;
   hipsForward.y = 0f; hipsForward.Normalize();
   targetFwd.y   = 0f; targetFwd.Normalize();
   float   yawErr    = Vector3.SignedAngle(hipsForward, targetFwd, Vector3.up);
   Vector3 yawTorque = Vector3.up * (yawErr * _kPYaw) - Vector3.up * (_rb.angularVelocity.y * _kDYaw);

   float mult = IsGrounded ? 1f : _airborneMultiplier;
   if (!IsFallen)
   {
       _rb.AddTorque(uprightTorque * mult, ForceMode.Force);
       _rb.AddTorque(yawTorque, ForceMode.Force);   // yaw always applies (turning in air should work)
   }
   ```

2. **Add two new serialised fields** to `BalanceController`:
   - `[Range(0f, 500f)] float _kPYaw = 200f`
   - `[Range(0f, 100f)] float _kDYaw = 30f`

3. **Remove the old combined torque block** (the `rotError.ToAngleAxis` block from Phase 2C). Replace entirely with the two-torque version above.

**Test criteria:** Walking in any direction causes the character's Hips forward vector to smoothly rotate toward the move direction within ~0.5s. No spinning or oscillation.

**Output:** Updated `BalanceController.cs` with separated upright and yaw torques.

---

### AGENT CARD: Phase 3E — Leg Animator

**Context:** The character moves but its legs drag. This phase adds a procedural walking cycle by sinusoidally oscillating the `UpperLeg` joint target rotations.

**Critical joint convention (read this):** All leg joints in the prefab use `axis = Vector3.right` as the primary axis (Angular X). `LowerLeg` joints use `lowAngX = -120°, highAngX = 0°` (knee bends backward). The `targetRotation` set on a `ConfigurableJoint` is in *joint drive space* — to rotate around the joint's X axis by angle `θ`, set `joint.targetRotation = Quaternion.AngleAxis(-θ, Vector3.right)` (the negation is because drive space is left-handed relative to the joint axis direction in Unity).

**Dependencies:** Phase 3B and 3C complete (`PlayerMovement.CurrentMoveInput`, `CharacterState.CurrentState`).

**Read first:**
- `RagdollBuilder.cs` segment table, specifically `UpperLeg_L`, `UpperLeg_R`, `LowerLeg_L`, `LowerLeg_R` entries and their `jointAxis` values.

**Assembly / Namespace:** `PhysicsDrivenMovement.Character`, file at `Assets/Scripts/Character/LegAnimator.cs`.

**Instructions:**

1. **Create `LegAnimator.cs`** (MonoBehaviour). Attach to Hips (same GO as `PlayerMovement`).

2. **Serialised fields:**
   - `[Range(0f, 60f)] float _stepAngle = 30f` — peak upper-leg angular swing (degrees away from rest pose).
   - `[Range(0.5f, 5f)] float _stepFrequency = 2f` — walk cycle frequency at max speed (Hz).
   - `[Range(0f, 60f)] float _kneeAngle = 20f` — additional bend in the knee during swing.

3. **Private fields:** cache `ConfigurableJoint` for `UpperLeg_L`, `UpperLeg_R`, `LowerLeg_L`, `LowerLeg_R`. Find them with `transform.Find("UpperLeg_L").GetComponent<ConfigurableJoint>()` etc. Also cache `PlayerMovement _movement` and `CharacterState _state`.

4. **In `FixedUpdate`:**
   - If `_state.CurrentState` is `Fallen` or `GettingUp`, set all four joints' `targetRotation = Quaternion.identity` and return (let drives return legs to rest).
   - Compute speed fraction: `float speedFrac = _movement.CurrentMoveInput.magnitude`. This is 0–1 for analogue sticks; for WASD it will be 0 or 1.
   - Accumulate phase: `_phase += _stepFrequency * speedFrac * Time.fixedDeltaTime * Mathf.PI * 2f`.
   - Left leg leads, right leg trails by π:
     ```csharp
     float swingL = Mathf.Sin(_phase)             * _stepAngle * speedFrac;
     float swingR = Mathf.Sin(_phase + Mathf.PI)  * _stepAngle * speedFrac;
     ```
   - Apply to upper legs: `_upperLegL.targetRotation = Quaternion.AngleAxis(-swingL, Vector3.right);`
   - Apply a partial knee bend when the leg is swinging backward (lifting for a step):
     ```csharp
     float kneeBendL = Mathf.Max(0f, -Mathf.Sin(_phase))             * _kneeAngle * speedFrac;
     float kneeBendR = Mathf.Max(0f, -Mathf.Sin(_phase + Mathf.PI))  * _kneeAngle * speedFrac;
     _lowerLegL.targetRotation = Quaternion.AngleAxis(-kneeBendL, Vector3.right);
     ```

5. **On `Standing` (no input):** lerp `_phase` toward 0 gradually and lerp all four joint `targetRotation` back to `Quaternion.identity` — this smooths the legs to the rest stance.

**Test criteria:** Moving with WASD causes visible alternating leg swing. Standing still causes legs to settle straight. Arms should be unaffected (this script only touches leg joints).

**Output:** `LegAnimator.cs`, prefab updated (add component to Hips).

---

### AGENT CARD: Phase 3F — Jump

**Context:** The character moves and walks. Now add jumping using the state machine from Phase 3C.

**Dependencies:** Phase 3C (`CharacterState`), Phase 3B (`PlayerMovement` and `_inputActions`).

**Instructions:**

1. **Add jump logic to `PlayerMovement.cs`** (do not create a new file — jump is part of movement):
   - Add `[Range(0f, 30f)] float _jumpForce = 15f` serialised field.
   - Cache a `CharacterState _state` reference in `Awake`.
   - In `FixedUpdate`, check `_inputActions.Player.Jump.WasPressedThisFrame()`. If true, `_state.CurrentState == CharacterStateType.Standing || CharacterStateType.Moving`, and `_balance.IsGrounded`: apply `_rb.AddForce(Vector3.up * _jumpForce, ForceMode.Impulse)`.

2. **Reduce leg spring in air.** In `LegAnimator.cs`, add a handler for `CharacterState.OnStateChanged`:
   - When entering `Airborne`: call a new method `SetLegSpringMultiplier(0.2f)` that scales the `slerpDrive.positionSpring` on all four leg joints by 0.2.
   - When leaving `Airborne`: call `SetLegSpringMultiplier(1.0f)` to restore.
   - Store the original spring values in `Awake` so the multiplier can be correctly applied.
   - Use a helper: `SetJointSpring(ConfigurableJoint joint, float spring, float damper)` that reconstructs and assigns a `JointDrive`.

**Test criteria:** Pressing Space while standing applies a visible upward impulse. Character briefly goes `Airborne` (visible in Inspector). Legs dangle slightly while in air. Landing returns to `Standing`.

**Output:** Updated `PlayerMovement.cs`, updated `LegAnimator.cs`.

---

### AGENT CARD: Phase 3G — Camera Follow

**Context:** The core character control is complete. Add a third-person follow camera. This has no physics dependency and can be implemented independently of 3A–3F.

**Dependencies:** Phase 1 (prefab exists, `Hips` is the root).

**Assembly / Namespace:** `PhysicsDrivenMovement.Character`, file at `Assets/Scripts/Character/CameraFollow.cs`.

**Instructions:**

1. **Create `CameraFollow.cs`** (MonoBehaviour). Attach to the Camera GameObject in the scene (not on the ragdoll).

2. **Serialised fields:**
   - `[SerializeField] Transform _target` — drag the `Hips` transform here.
   - `[Range(1f, 15f)] float _distance = 6f`
   - `[Range(0f, 10f)] float _height = 3f`
   - `[Range(1f, 30f)] float _positionSmoothing = 8f`
   - `[Range(1f, 30f)] float _lookSmoothing = 10f`

3. **In `LateUpdate`** (use `LateUpdate` so camera moves after all physics `FixedUpdate` and interpolation):
   - Desired camera position: `_target.position - _target.forward * _distance + Vector3.up * _height`. Use the Hips position not rotation for stability: `Vector3 desiredPos = _target.position + Vector3.back * _distance + Vector3.up * _height;`
   - Smooth position: `transform.position = Vector3.Lerp(transform.position, desiredPos, _positionSmoothing * Time.deltaTime)`.
   - Smooth look-at: `Quaternion desiredRot = Quaternion.LookRotation(_target.position - transform.position)`. `transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot, _lookSmoothing * Time.deltaTime)`.

4. **Assign in scene:** place Camera in the scene, attach `CameraFollow`, drag `Hips` to `_target`. Disable any existing `AudioListener` conflict if needed.

**Test criteria:** Camera follows the character while walking and jumping. No jitter. Character is always visible in frame.

**Output:** `CameraFollow.cs`, camera configured in scene.

---

### AGENT CARD: Phase 4 — Grabbing & Combat

**Context:** Player can move and fall. Now we add player interaction.

**Dependencies:** Phase 3 locomotion working.

**Instructions:**
1. Add trigger SphereColliders to each Hand. Create `HandGrabber.cs` — tracks overlapping rigidbodies, creates FixedJoint on grab input, destroys on release.
2. Set `FixedJoint.breakForce = 2000`. Stiffen arm joints while grabbing.
3. Punch mechanic: stiffen arm, set target rotation to extended, apply impulse to hand. 0.3s duration then restore.
4. Create `HitReceiver.cs` on Head — knockout when collision velocity > 8 m/s. Zero all joint springs for 3 seconds, then restore.
5. Throw: release grab + apply impulse in movement direction, scaled by character velocity.

**Test criteria:** Place two ragdolls. One can grab the other's limbs. Punching applies visible force. Head hits trigger knockout (character goes limp then recovers). Throws launch the grabbed player.

**Output:** `HandGrabber.cs`, `HitReceiver.cs`, updated arm/hand colliders.

---

### AGENT CARD: Phase 5 — Networking

**Context:** Single-player ragdoll brawler is working. Now we make it multiplayer using Unity NGO.

**Dependencies:** Phases 0–4 complete and tested locally.

**Instructions:**
1. Add `NetworkObject` to the Hips root. Add `NetworkRigidbody` to every Rigidbody in the prefab.
2. Create `NetworkManager` prefab with `UnityTransport`. Set PlayerRagdoll as the player prefab.
3. Refactor `PlayerMovement.cs`: clients read input and send via `ServerRpc`. Server applies forces. Guard all force-applying code with `IsServer` checks. Guard input reading with `IsOwner`.
4. Create `PlayerNetworkInput.cs` to encapsulate input RPCs (move vector, jump, grab, punch as a compressed struct).
5. Create `GameManager.cs` (NetworkBehaviour): handle player spawning at spawn points on connect, track connected players.
6. Create `ConnectionUI.cs`: Host button, Join button, IP input field. Wire to `NetworkManager.StartHost()` / `StartClient()`.
7. Sync grab/knockout state via `NetworkVariable<bool>` and `ClientRpc`s.

**Test criteria:** Build + Editor test. Two players connect, see each other, move independently, grab and punch each other. Ragdoll states sync visibly (knockout flop visible to both).

**Output:** Networked prefab, `PlayerNetworkInput.cs`, `GameManager.cs`, `ConnectionUI.cs`, refactored movement/combat scripts.

---

### AGENT CARD: Phase 6 — Game Loop

**Context:** Multiplayer works. Now we need a game loop.

**Dependencies:** Phase 5 networking working.

**Instructions:**
1. `LobbyManager.cs`: player list synced via `NetworkList`, host "Start Game" button, transition to arena scene.
2. `RoundManager.cs`: 3-2-1 countdown → enable input → track alive players → last alive wins. Eliminate players who fall below Y threshold or are KO'd for >6 seconds.
3. Winner display UI, 5-second timer, then back to lobby.
4. Add at least one hazard (moving platform or kill zone trigger).
5. `SpectatorCamera.cs`: free-look camera for eliminated players.
6. Basic HUD with player name labels (world-space Canvas) and elimination feed.

**Test criteria:** Full loop: host → join → lobby → start → fight → elimination → winner → lobby. Spectating works.

**Output:** `LobbyManager.cs`, `RoundManager.cs`, `SpectatorCamera.cs`, `HUD.cs`, UI canvases.

---

### AGENT CARD: Phase 7 — Polish

**Context:** Game is playable. Now make it presentable.

**Dependencies:** Phase 6 game loop working.

**Instructions:**
1. Player color selection in lobby. Apply material colors to ragdoll parts.
2. Impact particles on hard collisions. Punch whoosh particles.
3. Sound effects: punch, grab, footsteps, knockout, crowd cheer on elimination.
4. At least 2 arena maps with different layouts/hazards.
5. Main menu scene with Host/Join flow.
6. (Optional) Unity Relay integration — replace IP input with relay join codes.
7. Settings screen: resolution, volume, fullscreen.
8. Windows build pipeline.

**Output:** Polished, buildable game ready for friend testing.

---

## Quick Reference: Key Numbers

| Parameter | Value | Rationale |
|-----------|-------|-----------|
| Fixed timestep | 0.01s (100 Hz) | Ragdoll joint stability |
| Solver iterations | 12 | Joint constraint accuracy |
| Velocity solver iterations | 4 | Velocity constraint accuracy |
| Character total mass | ~50 kg | Realistic enough for force tuning |
| Balance kP | 500 (start) | Proportional gain for uprighting |
| Balance kD | 50 (start) | Damping to prevent oscillation |
| Move force | 300 N | Horizontal locomotion |
| Jump force | 15 N·s (impulse) | Vertical jump |
| Max speed | 5 m/s | Horizontal speed cap |
| Punch force | 50 N·s (impulse) | Hand impulse |
| Knockout threshold | 8 m/s relative velocity on head | Collision speed to trigger KO |
| Knockout duration | 3 seconds | Time with zero joint drives |
| Grab break force | 2000 N | FixedJoint break threshold |
| Sync rate (core) | 30 Hz | Hips/torso NetworkRigidbody |
| Sync rate (extremities) | 15 Hz | Hands/feet NetworkRigidbody |
