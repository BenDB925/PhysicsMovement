---
description: "Use when changing runtime environment data or room metadata in PhysicsDrivenMovementDemo. Routes agents to ArenaRoom, related scenes, and the editor builders that author those scenes."
name: "Environment Runtime Routing"
applyTo: "Assets/Scripts/Environment/**/*.cs"
---
# Environment Runtime Routing

- `ArenaRoom` is live runtime metadata for the museum scene. Treat it as production scene data, not disposable scaffolding.
- Check whether the change targets `Assets/Scenes/Museum_01.unity` or `Assets/Scenes/Arena_01.unity` before touching data fields or assumptions.
- If a runtime room-data change requires scene regeneration, follow the builder path through `ArenaBuilder`, `SceneBuilder`, and supporting editor tools.
- Update architecture and routing docs if the environment runtime surface grows beyond `ArenaRoom`.