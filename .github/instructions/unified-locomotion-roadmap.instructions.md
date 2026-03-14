---
description: "Use when a task is explicitly driven by the unified locomotion roadmap, especially locomotion authority shifts, observation model work, leg-state migration, step planning, balance-support integration, recovery strategy, terrain robustness, or locomotion telemetry."
name: "Unified Locomotion Roadmap"
---
# Unified Locomotion Roadmap

Use this instruction only to route roadmap-guided locomotion work into the canonical plan under `Plans/`.

## Mandatory companion docs

- [CODING_STANDARDS.md](../../CODING_STANDARDS.md)
- [ARCHITECTURE.md](../../ARCHITECTURE.md)
- [TASK_ROUTING.md](../../TASK_ROUTING.md)
- [PLAN.md](../../PLAN.md)
- [Plans/README.md](../../Plans/README.md)
- [AGENT_TEST_RUNNING.md](../../AGENT_TEST_RUNNING.md)

## How to use this roadmap

1. Open the parent roadmap plan: [Plans/unified-locomotion-roadmap.plan.md](../../Plans/unified-locomotion-roadmap.plan.md).
2. Match the task to the routing table below, then read only the relevant chapter docs plus any dependencies they name.
3. Keep the touched chapter doc under `Plans/unified-locomotion-roadmap/` current as the work progresses. Update it when subtasks close, bugs or hypotheses appear, blockers or next steps change, or artifact locations move.
4. When durable ownership boundaries or reusable workflow rules change, update [ARCHITECTURE.md](../../ARCHITECTURE.md), [TASK_ROUTING.md](../../TASK_ROUTING.md), [.copilot-instructions.md](../../.copilot-instructions.md), and other long-lived docs in the same slice.
5. Avoid editing unrelated in-progress chapter docs for cross-cutting policy updates. Put those updates in shared long-lived docs or the roadmap parent plan instead.

## Parent plan

- [Unified Locomotion Roadmap parent plan](../../Plans/unified-locomotion-roadmap.plan.md)

## Routing by task

| If the task is about... | Read | Notes |
|---|---|---|
| LocomotionDirector, ownership cleanup, pass-through contracts, gait authority boundaries | [Chapter 1](../../Plans/unified-locomotion-roadmap/01-single-voice.md) | Foundation slice. Pair with the character runtime routing docs. |
| Support quality, contact confidence, planted-foot confidence, observation aggregation, hysteresis, locomotion debug draw | [Chapter 2](../../Plans/unified-locomotion-roadmap/02-world-model.md), then [Chapter 1](../../Plans/unified-locomotion-roadmap/01-single-voice.md) if contracts are involved | Read Chapter 1 when the observation work changes contracts or director inputs. |
| Per-leg state machines, transition reasons, asymmetric gait roles, fallback gait behavior | [Chapter 3](../../Plans/unified-locomotion-roadmap/03-leg-states.md), plus [Chapter 2](../../Plans/unified-locomotion-roadmap/02-world-model.md) if state transitions depend on new observations | Use Chapter 1 if the change also moves ownership. |
| Step targets, foothold planning, braking steps, catch-step placement, turn-specific foot placement | [Chapter 4](../../Plans/unified-locomotion-roadmap/04-step-planning.md), then [Chapter 2](../../Plans/unified-locomotion-roadmap/02-world-model.md) and [Chapter 3](../../Plans/unified-locomotion-roadmap/03-leg-states.md) | Step planning depends on both observation quality and explicit leg roles. |
| BalanceController as executor, support command interfaces, COM support policy, override cleanup | [Chapter 5](../../Plans/unified-locomotion-roadmap/05-body-support.md), plus [Chapter 1](../../Plans/unified-locomotion-roadmap/01-single-voice.md) | Read Chapter 4 too if support targets depend on planned steps. |
| Hard turns, reversals, slip handling, stumble recovery, catch steps, collapse boundaries | [Chapter 6](../../Plans/unified-locomotion-roadmap/06-recovery-and-catch-steps.md), plus [Chapter 5](../../Plans/unified-locomotion-roadmap/05-body-support.md) | Use the debugging workflow and outcome-based recovery tests. |
| Slopes, steps, uneven terrain, scene-builder alignment, ArenaRoom metadata, terrain-specific recovery | [Chapter 7](../../Plans/unified-locomotion-roadmap/07-terrain-and-contact-robustness.md) | Pair with the environment builder/runtime routing docs when scenes or room metadata change. |
| Motion style, pelvis and torso expression, arm-leg coordination, readable personality layers | [Chapter 8](../../Plans/unified-locomotion-roadmap/08-expressive-motion-and-feel.md) | Only start after control architecture is stable enough to protect readability. |
| Telemetry, dashboards, baseline captures, focused regression slices, failure triage workflow | [Chapter 9](../../Plans/unified-locomotion-roadmap/09-validation-debugging-and-tuning.md), plus [LOCOMOTION_BASELINES.md](../../LOCOMOTION_BASELINES.md) and [DEBUGGING.md](../../DEBUGGING.md) | This chapter runs continuously across the whole roadmap. |