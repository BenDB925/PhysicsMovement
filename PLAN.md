# Physics-Driven Movement Demo — Project Plan

## Project Summary

A multiplayer online brawler featuring **Gang Beasts–style active ragdoll movement**. Players control wobbly physics-driven characters that grab, punch, and throw each other. The game uses **Unity (Built-in 3D)** with **Netcode for GameObjects (NGO)** for networking, supports **2–4 players** in **peer-to-peer (host/client)** matches.

---

## Tech Stack

| Concern | Choice |
|---------|--------|
| Engine | Unity 6 (6000.3.9f1, Built-in Render Pipeline) |
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

### Phase 2E — Stability Hardening & Verification

> **Goal:** Lock in the new standing behavior with production-scene validation and stronger regression tests so future tuning doesn't reintroduce startup collapse.

| Task | Details |
|------|---------|
| 2.8 | **Code tidy and safety pass.** Keep debug tuning helpers but ensure they are safe by default (disabled in normal play), and align comments/docs with runtime behavior. |
| 2.9 | **Real-scene PlayMode validation.** Add a PlayMode test that loads `Arena_01` and validates the in-scene `PlayerRagdoll` startup behavior over time (not only synthetic test rigs). |
| 2.10 | **Long-run stability regression.** Add a longer-duration stability test (20–30 seconds of physics) tracking max tilt, min hips height, grounded/fallen frame counts, and ensuring recoverable behavior. |
| 2.11 | **State-transition stress test.** Strengthen push tests to assert explicit transitions (`Standing → Fallen → Standing` where appropriate) instead of tilt-only assertions. |
| 2.12 | **Test isolation hardening.** Ensure all tests restore global physics and collision settings (`Time.fixedDeltaTime`, solver iterations, layer collision masks) in teardown to avoid cross-test contamination. |
| 2.13 | **Repeatability sweep.** Add a repeated-run test scenario (same setup, multiple runs) to catch non-deterministic regressions in startup settle and recovery. |

**Deliverable:** Phase 2 has robust unit/integration/prefab/scene coverage and can detect regressions in startup standing behavior before manual QA.

---

### Phase 3 — Locomotion

> **Goal:** The player can move the character with WASD/stick input. The character lurches forward with physics forces and stays (mostly) upright while moving.

> **Execution model for agents:** Phase 3 is intentionally split into micro-sections so each section can be assigned with a single prompt like: **"Implement Phase 3B2"**.

| Task | Details |
|------|---------|
| 3.1 | **3A — Input asset.** Create `PlayerInputActions.inputactions` and generated wrapper class. |
| 3.2 | **3B1 — Movement scaffold.** Create `PlayerMovement.cs` component skeleton, input lifecycle, and component caching. |
| 3.3 | **3B2 — Movement forces.** Implement camera-relative movement force + speed cap + facing direction feed into `BalanceController`. |
| 3.4 | **3B3 — Movement wiring.** Add `PlayerMovement` to prefab/scene and validate runtime references (`Camera`, `BalanceController`, `Rigidbody`). |
| 3.5 | **3C1 — FSM API scaffold.** Add `CharacterStateType`, `CharacterState`, `CurrentState`, and `OnStateChanged` API. |
| 3.6 | **3C2 — FSM transitions.** Implement `Standing/Moving/Airborne/Fallen/GettingUp` transition rules. |
| 3.7 | **3C3 — Get-up behavior.** Add fallen timer, get-up delay/timeout, and upward recovery impulse. |
| 3.8 | **3D1 — Turning torque split.** Refactor balance control into separate upright torque (pitch/roll) and yaw torque. |
| 3.9 | **3D2 — Turning stabilization.** Tune/guard yaw behavior (no spin, no oscillation, safe zero-input handling). ✅ Complete (2026-02-22) |
| 3.10 | **3E1 — Leg animation core.** Add `LegAnimator.cs` with gait phase and upper/lower leg target rotations. |
| 3.11 | **3E2 — Leg settle behavior.** Add idle return-to-rest smoothing and fallen/get-up bypass behavior. |
| 3.12 | **3F1 — Jump impulse.** Add grounded/state-gated jump to `PlayerMovement`. |
| 3.13 | **3F2 — Airborne leg spring modulation.** In `LegAnimator`, subscribe to state changes and scale leg spring in air, restore on landing. |
| 3.14 | **3G1 — Camera follow script.** Create `CameraFollow.cs` with smoothed position/look behavior in `LateUpdate`. |
| 3.15 | **3G2 — Camera scene wiring.** Configure scene camera target and validate framing/jitter behavior. |
| 3.16 | **3T — Automated test pass.** Implement Phase 3 PlayMode/EditMode tests (cards below), then run EditMode + PlayMode sequentially and archive results in `TestResults/`. |

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

### AGENT CARD: Phase 2E — Stability Hardening & Real Scene Coverage

**Context:** The character now stands, but this area has had heavy iteration and is vulnerable to regressions. We need a professional hardening pass focused on test quality and production-scene confidence.

**Dependencies:** Phase 2D complete.

**Instructions:**

1. **Add real-scene PlayMode test suite** at `Assets/Tests/PlayMode/Character/Arena01BalanceStabilityTests.cs`.
   - Load scene `Arena_01` in PlayMode (single mode).
   - Locate the active `BalanceController` from the in-scene `PlayerRagdoll` instance.
   - Fail with clear message if no instance is found.
   - Run for at least `2000` fixed frames (20s at 100 Hz).
   - Track and assert:
     - `maxTilt` remains below catastrophic threshold (e.g., `< 75°`),
     - `minHipsHeight` stays above seated-collapse floor (project-calibrated threshold),
     - `fallenFrameCount` remains bounded (recoverable behavior, not persistent down state),
     - final state has at least one grounded foot.

2. **Strengthen strong-force integration expectation** in `Assets/Tests/PlayMode/Character/BalanceControllerIntegrationTests.cs`.
   - Replace purely tilt-based “strong push” assertion with transition-aware checks:
     - must exhibit destabilisation,
     - must either enter `IsFallen` at least once or exceed a high-tilt threshold,
     - if recovery is expected under current tuning, verify it within a bounded window.
   - Keep threshold wording resilient to wobble-style gameplay (avoid overfitting to a single numeric outcome).

3. **Add repeatability regression test** in prefab or scene-level PlayMode tests.
   - Repeat spawn-settle-observe sequence at least 3 times in one test class.
   - Record per-run metrics (max tilt, min hips height, fallen frames).
   - Assert all runs remain within safety bounds.

4. **Enforce test hygiene** across Phase 2 PlayMode tests.
   - Snapshot and restore global physics settings and layer-collision toggles in setup/teardown.
   - Ensure each test cleans up spawned objects/scenes to prevent state bleed.

5. **Document known CI caveat** (project-open lock race) in test-running notes.
   - Explicitly note that batch test runs require no second Unity instance.
   - Include expected failure signature (`HandleProjectAlreadyOpenInAnotherInstance`) and operator action.

**Test criteria:**
- New `Arena_01` PlayMode tests pass reliably in clean batch runs.
- Existing Phase 2 tests remain green.
- Failures produce actionable messages (which metric exceeded threshold and by how much).

**Output:**
- `Assets/Tests/PlayMode/Character/Arena01BalanceStabilityTests.cs`
- Updated `BalanceControllerIntegrationTests.cs` (transition-aware strong-force assertions)
- Updated test-run notes documenting Unity lock caveat
- Passing EditMode + PlayMode results in `TestResults/`

---

### AGENT CARD: Phase 3A — Input Action Asset

**Context:** The character can stand. Set up input data only.

**Dependencies:** Phase 2 complete.

**Instructions:**
1. Create `Assets/Scripts/Input/PlayerInputActions.inputactions`.
2. Add action map `Player` with actions: `Move` (Vector2), `Jump` (Button), `Grab` (Button), `Punch` (Button).
3. Bindings:
   - Keyboard: `Move` = WASD composite, `Jump` = Space, `Grab` = Left Shift, `Punch` = Left Mouse Button.
   - Gamepad: `Move` = Left Stick, `Jump` = South (A), `Grab` = Left Trigger, `Punch` = Right Trigger.
4. Enable generated class (`PlayerInputActions`, namespace `PhysicsDrivenMovement.Input`).

**Done when:** Asset imports cleanly and generated wrapper class appears.

**Output:** `PlayerInputActions.inputactions`, generated `PlayerInputActions.cs`.

---

### AGENT CARD: Phase 3B1 — PlayerMovement Scaffold

**Context:** Add component skeleton without movement math yet.

**Dependencies:** Phase 3A, Phase 2C.

**Instructions:**
1. Create `Assets/Scripts/Character/PlayerMovement.cs` in namespace `PhysicsDrivenMovement.Character`.
2. Add `[RequireComponent(typeof(Rigidbody))]`.
3. Add serialized fields: `_moveForce`, `_maxSpeed`, `_camera`.
4. Add private fields: `_rb`, `_balance`, `_inputActions`, `_currentMoveInput`.
5. In `Awake`, cache references and enable input actions.
6. In `OnDestroy`, dispose input actions.
7. Add public read-only property: `Vector2 CurrentMoveInput => _currentMoveInput;`.

**Done when:** Script compiles and can be attached to `Hips` without null-ref in `Awake`.

**Output:** `PlayerMovement.cs` scaffold.

---

### AGENT CARD: Phase 3B2 — Movement Force + Speed Cap

**Context:** Implement movement behavior.

**Dependencies:** Phase 3B1.

**Instructions:**
1. In `FixedUpdate`, read move input into `_currentMoveInput`.
2. If `_balance.IsFallen`, return.
3. Convert input to camera-relative world direction on XZ plane.
4. Apply `Rigidbody.AddForce(worldDir * _moveForce, ForceMode.Force)` only when horizontal speed is below `_maxSpeed`.
5. Call `_balance.SetFacingDirection(worldDir)` for non-trivial input magnitude (e.g., > 0.1).
6. Guard missing camera reference with a safe fallback (`Camera.main` once, or world-axis fallback).

**Done when:** WASD/stick moves character and speed stays near cap without hard velocity assignment.

**Output:** Updated `PlayerMovement.cs` with movement logic.

---

### AGENT CARD: Phase 3B3 — Movement Wiring

**Context:** Ensure scene/prefab wiring is deterministic.

**Dependencies:** Phase 3B2.

**Instructions:**
1. Add `PlayerMovement` to `Hips` in `PlayerRagdoll.prefab`.
2. Wire `_camera` in test scene (`Arena_01`) or define explicit runtime fallback.
3. Validate no duplicate movement components exist.
4. Confirm `BalanceController` + `Rigidbody` coexist on same object.

**Done when:** Play mode starts with no missing-reference warnings and movement responds immediately.

**Output:** Prefab/scene updated with movement wiring.

---

### AGENT CARD: Phase 3C1 — CharacterState API Scaffold

**Context:** Build state machine shell first.

**Dependencies:** Phase 3B2.

**Instructions:**
1. Create `Assets/Scripts/Character/CharacterState.cs`.
2. Define enum `CharacterStateType`: `Standing`, `Moving`, `Airborne`, `Fallen`, `GettingUp`.
3. Add `CurrentState` property and `OnStateChanged` event.
4. Add serialized fields: `_getUpDelay`, `_knockoutDuration`, `_getUpForce`.
5. Cache `BalanceController`, `PlayerMovement`, and `Rigidbody` in `Awake`.

**Done when:** Script compiles, starts in `Standing`, and API is consumable by other systems.

**Output:** `CharacterState.cs` skeleton + enum.

---

### AGENT CARD: Phase 3C2 — CharacterState Transitions

**Context:** Implement deterministic transition logic.

**Dependencies:** Phase 3C1.

**Instructions:**
1. Implement transition rules in `FixedUpdate` for `Standing/Moving/Airborne/Fallen/GettingUp`.
2. Use helper `ChangeState(newState)` to centralize event firing and entry logic.
3. Base transitions on `IsGrounded`, `IsFallen`, and `CurrentMoveInput.magnitude`.
4. Add hysteresis-friendly thresholds for movement input (e.g., 0.1 enter, 0.05 exit).

**Done when:** State transitions are observable in Inspector and event fires only on actual changes.

**Output:** Transition-complete `CharacterState.cs`.

---

### AGENT CARD: Phase 3C3 — Fallen Timer + GetUp

**Context:** Add timed recovery behavior.

**Dependencies:** Phase 3C2.

**Instructions:**
1. Track `_fallenTimer` only while grounded and in `Fallen`.
2. Enter `GettingUp` only after `_getUpDelay` and `_knockoutDuration` requirements are satisfied.
3. On entering `GettingUp`, apply upward impulse (`_getUpForce`) once.
4. Add recovery timeout safety (e.g., 3 seconds) back to `Standing`.

**Done when:** Character can enter `Fallen` then autonomously recover under normal ground conditions.

**Output:** Timed recovery completed in `CharacterState.cs`.

---

### AGENT CARD: Phase 3D1 — Split Upright vs Yaw Torque

**Context:** Refactor turning to avoid coupling yaw with pitch/roll stabilization.

**Dependencies:** Phase 3B2.

**Instructions:**
1. In `BalanceController`, replace combined torque logic with:
   - upright torque (pitch/roll axes)
   - yaw torque around world up
2. Add serialized fields `_kPYaw`, `_kDYaw`.
3. Keep airborne multiplier affecting only upright torque.
4. Keep zero-direction guard in `SetFacingDirection`.

**Done when:** Movement direction rotates hips smoothly without introducing roll instability.

**Output:** Updated `BalanceController.cs` torque model.

---

### AGENT CARD: Phase 3D2 — Yaw Stability Hardening

**Context:** Prevent spin/jitter regressions.

**Dependencies:** Phase 3D1.

**Instructions:**
1. Clamp/normalize yaw-target vectors before `SignedAngle`.
2. Add small dead zone for yaw error to prevent micro-oscillation near target.
3. Ensure behavior is stable with zero movement input (last valid facing retained).
4. Verify no NaN from normalization paths.

**Done when:** Character converges to facing target without oscillation or continuous spin.

**Output:** Hardened yaw behavior in `BalanceController.cs`.

---

### AGENT CARD: Phase 3E1 — LegAnimator Core Cycle

**Context:** Add procedural stepping first, no airborne spring scaling yet.

**Dependencies:** Phase 3C2.

**Instructions:**
1. Create `Assets/Scripts/Character/LegAnimator.cs` on `Hips`.
2. Cache four leg joints (`UpperLeg_L/R`, `LowerLeg_L/R`) and references to `PlayerMovement`, `CharacterState`.
3. Add serialized gait fields (`_stepAngle`, `_stepFrequency`, `_kneeAngle`).
4. In `FixedUpdate`, compute phase from move input magnitude and apply sinusoidal target rotations.
5. If state is `Fallen`/`GettingUp`, return legs to identity and skip gait.

**Done when:** Walking shows alternating leg swing and knee bend.

**Output:** `LegAnimator.cs` gait core.

---

### AGENT CARD: Phase 3E2 — Leg Settle & Idle Blend

**Context:** Smooth transition from moving to idle.

**Dependencies:** Phase 3E1.

**Instructions:**
1. Add smoothing path to blend upper/lower leg targets back to `Quaternion.identity` when idle.
2. Decay gait phase toward neutral when no input.
3. Ensure no abrupt snaps on quick move/stop toggles.

**Done when:** Legs settle naturally at idle with no visible popping.

**Output:** Updated `LegAnimator.cs` idle behavior.

---

### AGENT CARD: Phase 3F1 — Jump Impulse

**Context:** Add jump gate + impulse to movement.

**Dependencies:** Phase 3C3.

**Instructions:**
1. Add `_jumpForce` to `PlayerMovement`.
2. Cache `CharacterState` reference.
3. Allow jump only when:
   - Jump input pressed this frame,
   - state is `Standing` or `Moving`,
   - `_balance.IsGrounded` is true.
4. Apply `AddForce(Vector3.up * _jumpForce, ForceMode.Impulse)`.

**Done when:** Space/A triggers reliable single jump from grounded states only.

**Output:** Jump behavior in `PlayerMovement.cs`.

---

### AGENT CARD: Phase 3F2 — Airborne Leg Spring Scaling

**Context:** Legs should loosen in air and restore on landing.

**Dependencies:** Phase 3F1, Phase 3E2.

**Instructions:**
1. In `LegAnimator`, store baseline `slerpDrive` spring/damper for all leg joints.
2. Subscribe to `CharacterState.OnStateChanged` in `OnEnable`; unsubscribe in `OnDisable`.
3. On enter `Airborne`: apply spring multiplier (e.g., 0.2).
4. On exit `Airborne`: restore multiplier to 1.0.
5. Implement helper to reassign full `JointDrive` safely.

**Done when:** Legs visibly dangle more in air and return to normal stiffness after landing.

**Output:** Updated `LegAnimator.cs` spring modulation.

---

### AGENT CARD: Phase 3G1 — CameraFollow Script

**Context:** Add basic third-person follow implementation.

**Dependencies:** Phase 1.

**Instructions:**
1. Create `Assets/Scripts/Character/CameraFollow.cs`.
2. Add serialized fields: `_target`, `_distance`, `_height`, `_positionSmoothing`, `_lookSmoothing`.
3. In `LateUpdate`, smooth camera position and look rotation toward target.
4. Guard null target safely.

**Done when:** Script compiles and camera can follow a target in scene.

**Output:** `CameraFollow.cs`.

---

### AGENT CARD: Phase 3G2 — Camera Scene Wiring

**Context:** Wire scene camera and validate feel.

**Dependencies:** Phase 3G1.

**Instructions:**
1. Attach `CameraFollow` to active scene camera.
2. Assign `Hips` transform as `_target`.
3. Resolve `AudioListener` conflicts if multiple cameras exist.
4. Validate framing during stand, move, jump, fall, and get-up.

**Done when:** Character remains in frame without visible jitter.

**Output:** Scene camera configured.

---

### AGENT CARD: Phase 3T-A — Movement Unit/Integration Tests

**Context:** Lock movement behavior against regressions.

**Dependencies:** 3B1–3B3.

**Instructions:**
1. Add PlayMode tests in `Assets/Tests/PlayMode/Character/PlayerMovementTests.cs`.
2. Validate:
   - no movement force when `IsFallen`,
   - movement force direction follows camera projection,
   - horizontal speed remains bounded near `_maxSpeed`.
3. Add clear assertion messages with measured values.

**Output:** `PlayerMovementTests.cs`.

---

### AGENT CARD: Phase 3T-B — State Machine Tests

**Context:** Ensure transition correctness.

**Dependencies:** 3C1–3C3.

**Instructions:**
1. Add PlayMode tests in `Assets/Tests/PlayMode/Character/CharacterStateTests.cs`.
2. Cover critical transitions:
   - `Standing ↔ Moving`,
   - grounded-loss to `Airborne`,
   - high tilt to `Fallen`,
   - `Fallen -> GettingUp -> Standing` with timer gates.
3. Validate `OnStateChanged` event sequence.

**Output:** `CharacterStateTests.cs`.

---

### AGENT CARD: Phase 3T-C — Turning Stability Tests

**Context:** Prevent future yaw regressions.

**Dependencies:** 3D1–3D2.

**Instructions:**
1. Add tests to existing `BalanceControllerTests.cs` or new `BalanceControllerTurningTests.cs`.
2. Assert yaw converges toward target direction within bounded time.
3. Assert no runaway spin under zero input.
4. Assert no NaN/Inf angular velocity components.

**Output:** Turning-focused balance tests.

---

### AGENT CARD: Phase 3T-D — Leg Animator Tests

**Context:** Verify gait output and safe bounds.

**Dependencies:** 3E1–3E2.

**Instructions:**
1. Add PlayMode tests in `Assets/Tests/PlayMode/Character/LegAnimatorTests.cs`.
2. Assert upper/lower leg `targetRotation` changes while moving.
3. Assert rotations return near identity at idle.
4. Assert only leg joints are modified (arms untouched).

**Output:** `LegAnimatorTests.cs`.

---

### AGENT CARD: Phase 3T-E — Jump + Airborne Spring Tests

**Context:** Validate jump gate and spring restoration.

**Dependencies:** 3F1–3F2.

**Instructions:**
1. Add PlayMode tests in `Assets/Tests/PlayMode/Character/JumpAndAirborneTests.cs`.
2. Assert jump occurs only from grounded `Standing/Moving`.
3. Assert leg spring multiplier applies in `Airborne` and restores after landing.
4. Assert no duplicate impulse from single button press frame.

**Output:** `JumpAndAirborneTests.cs`.

---

### AGENT CARD: Phase 3T-F — Arena_01 Locomotion Smoke

**Context:** Validate behavior in production scene, not just synthetic rigs.

**Dependencies:** 3B–3G complete.

**Instructions:**
1. Add `Assets/Tests/PlayMode/Character/Arena01LocomotionSmokeTests.cs`.
2. Load `Arena_01`, find active player rig, run 8–12 seconds.
3. Validate non-catastrophic locomotion metrics:
   - movement input produces displacement,
   - no sustained fallen lock,
   - camera keeps player in frame (coarse check).
4. Reuse physics/layer snapshot-restore hygiene pattern from Phase 2E tests.

**Output:** `Arena01LocomotionSmokeTests.cs`.

---

### AGENT CARD: Phase 3T-G — Test Execution & Artifacts

**Context:** Final verification gate for Phase 3.

**Dependencies:** 3T-A through 3T-F.

**Instructions:**
1. Run EditMode then PlayMode tests sequentially (never parallel Unity instances).
2. Save outputs to `TestResults/EditMode.xml` and `TestResults/PlayMode.xml` (+ logs).
3. If failing, include metric-driven failure summary and suspected subsystem.
4. Confirm lock-caveat handling follows `AGENT_TEST_RUNNING.md` guidance.

**Output:** Fresh `TestResults/` XML + logs and pass/fail summary.

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
