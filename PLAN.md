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

### AGENT CARD: Phase 2 — Balance Controller

**Context:** We have a ragdoll prefab that flops realistically. Now it needs to stand on its own.

**Dependencies:** Phase 1 prefab must be complete.

**Instructions:**
1. Create `BalanceController.cs` on the Hips rigidbody. Implement a PD controller that applies torque to keep hips upright. Use configurable kP (start 500) and kD (start 50).
2. Create `GroundSensor.cs` on each foot. Detect ground via short SphereCast downward. Expose `IsGrounded` bool.
3. Set SLERP drive springs on spine and leg joints to hold a standing pose.
4. Balance torque should only apply at full strength when at least one foot is grounded. In air, reduce to 20%.
5. All tuning parameters should be `[SerializeField]` with `[Range]` attributes.

**Test criteria:** Character stands upright on flat ground. Light pushes (apply 200N force via debug key) cause wobble but recovery. Strong pushes (800N+) knock it down.

**Output:** `BalanceController.cs`, `GroundSensor.cs`, updated prefab with configured joint drives.

---

### AGENT CARD: Phase 3 — Locomotion

**Context:** The ragdoll can stand. Now it needs to move based on player input.

**Dependencies:** Phase 2 balance system must be working.

**Instructions:**
1. Create an Input Action Asset (`PlayerInputActions.inputactions`) with Move (Vector2), Jump (Button), Grab (Button), Punch (Button).
2. Create `PlayerMovement.cs` on Hips. Apply horizontal force in the input direction (camera-relative). Cap speed at ~5 m/s.
3. Create `LegAnimator.cs` — oscillate upper leg joint targets sinusoidally when moving to simulate walking.
4. Implement jump: upward impulse when grounded, reduce leg spring in air.
5. Create `CharacterState.cs` state machine: Standing, Moving, Airborne, Fallen, GettingUp. Wire transitions based on ground contact and torso angle.
6. Create `CameraFollow.cs` — third-person camera that follows the Hips with smoothed position and rotation.

**Test criteria:** WASD moves the character. It wobbles and lurches but stays mostly upright. Jumping works. Falling off an edge causes ragdoll. Character gets back up after landing. Camera follows smoothly.

**Output:** Input asset, `PlayerMovement.cs`, `LegAnimator.cs`, `CharacterState.cs`, `CameraFollow.cs`.

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
