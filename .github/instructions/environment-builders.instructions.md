description: "Use when changing arena layout, museum rooms, generated scenes, prop builders, or environment data in PhysicsDrivenMovementDemo. Routes agents to ArenaRoom, ArenaBuilder, SceneBuilder, and scene assets."
name: "Environment Builder Routing"
applyTo: "Assets/Scripts/Editor/**/*.cs"
---
# Environment Builder Routing

- This file is scoped to the editor-side builders. Pair it with `environment-runtime.instructions.md` when runtime room metadata is involved.

- `ArenaBuilder` builds `Assets/Scenes/Museum_01.unity`; `SceneBuilder` is the older/simple scene path for `Arena_01.unity`.
- `ArenaRoom` is the runtime room metadata component used by the museum scene. Treat it as live runtime code, not editor-only scaffolding.
- Check the target scene asset before changing builder code so you know whether the work is for the physics prototype scene or the museum concept scene.
- `PropBuilder` and `SkinnedRagdollBuilder` are supporting editor tools; update architecture docs if their responsibilities change.