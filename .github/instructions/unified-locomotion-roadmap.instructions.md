---
description: "Use when a task is explicitly driven by the unified locomotion roadmap, especially locomotion authority shifts, observation model work, leg-state migration, step planning, balance-support integration, recovery strategy, terrain robustness, or locomotion telemetry."
name: "Unified Locomotion Roadmap"
---
# Unified Locomotion Roadmap

Read this file first when the user asks for roadmap-guided locomotion work.

## Mandatory companion docs

- [CODING_STANDARDS.md](../../CODING_STANDARDS.md)
- [ARCHITECTURE.md](../../ARCHITECTURE.md)
- [TASK_ROUTING.md](../../TASK_ROUTING.md)
- `Plans/README.md`
- [AGENT_TEST_RUNNING.md](../../AGENT_TEST_RUNNING.md)

## How to use this roadmap

1. Match the task to the routing table below.
2. Read only the relevant chapter docs plus any dependencies they name. Do not load every chapter by default.
3. Keep the touched chapter doc current when progress, blockers, or artifact locations change.
4. When ownership boundaries change, update [ARCHITECTURE.md](../../ARCHITECTURE.md), [TASK_ROUTING.md](../../TASK_ROUTING.md), and [.copilot-instructions.md](../../.copilot-instructions.md) in the same slice.

## Target Runtime Authority Model

Input -> LocomotionDirector -> LegStateMachine + StepPlanner -> Actuators -> Safety Layer

1. Input says what the player wants.
2. LocomotionDirector decides what movement solution to run.
3. LegStateMachine and StepPlanner decide what each leg should do.
4. Actuators execute that decision.
   - LegAnimator executes leg targets.
   - BalanceController stabilizes body support.
   - ArmAnimator adds supportive counter motion.
5. Safety layer steps in only when the plan is failing.
   - LocomotionCollapseDetector
   - CharacterState fall/get-up transitions

## Shared work rules

1. Start from current behavior and record a baseline before refactoring.
2. Make authority changes in thin slices, not one large rewrite.
3. Keep feature-scoped verification green after each slice.
4. Prefer outcome-based PlayMode verification for locomotion behavior.
5. Run Unity tests sequentially through [Tools/Run-UnityTests.ps1](../../Tools/Run-UnityTests.ps1).

## Roadmap maintenance

1. Treat the chapter docs as a living execution record, not only a wishlist.
2. Update the relevant chapter when meaningful progress happens, including what was finished, what remains blocked, and where artifacts or notes were saved.
3. When a task produces durable outputs such as baseline summaries, regression notes, dashboards, or follow-up docs, add the destination path to the affected chapter.
4. Prefer short dated progress notes under the affected work package over long retrospective summaries elsewhere.
5. If reality diverges from the plan, update the plan text instead of leaving stale instructions in place.
6. If a chapter note would exceed the rollover thresholds in `Plans/README.md`, split the detail into a linked child doc instead of letting the chapter sprawl.
7. When a roadmap task turns into a deep bug investigation, create a linked bug sheet and keep the chapter doc as the short summary.

## Routing by task

| If the task is about... | Read | Notes |
|---|---|---|
| LocomotionDirector, ownership cleanup, pass-through contracts, gait authority boundaries | [Chapter 1](./unified-locomotion-roadmap/01-single-voice.md) | Foundation slice. Pair with the character runtime routing docs. |
| Support quality, contact confidence, planted-foot confidence, observation aggregation, hysteresis, locomotion debug draw | [Chapter 2](./unified-locomotion-roadmap/02-world-model.md), then [Chapter 1](./unified-locomotion-roadmap/01-single-voice.md) if contracts are involved | Read Chapter 1 when the observation work changes contracts or director inputs. |
| Per-leg state machines, transition reasons, asymmetric gait roles, fallback gait behavior | [Chapter 3](./unified-locomotion-roadmap/03-leg-states.md), plus [Chapter 2](./unified-locomotion-roadmap/02-world-model.md) if state transitions depend on new observations | Use Chapter 1 if the change also moves ownership. |
| Step targets, foothold planning, braking steps, catch-step placement, turn-specific foot placement | [Chapter 4](./unified-locomotion-roadmap/04-step-planning.md), then [Chapter 2](./unified-locomotion-roadmap/02-world-model.md) and [Chapter 3](./unified-locomotion-roadmap/03-leg-states.md) | Step planning depends on both observation quality and explicit leg roles. |
| BalanceController as executor, support command interfaces, COM support policy, override cleanup | [Chapter 5](./unified-locomotion-roadmap/05-body-support.md), plus [Chapter 1](./unified-locomotion-roadmap/01-single-voice.md) | Read Chapter 4 too if support targets depend on planned steps. |
| Hard turns, reversals, slip handling, stumble recovery, catch steps, collapse boundaries | [Chapter 6](./unified-locomotion-roadmap/06-recovery-and-catch-steps.md), plus [Chapter 5](./unified-locomotion-roadmap/05-body-support.md) | Use the debugging workflow and outcome-based recovery tests. |
| Slopes, steps, uneven terrain, scene-builder alignment, ArenaRoom metadata, terrain-specific recovery | [Chapter 7](./unified-locomotion-roadmap/07-terrain-and-contact-robustness.md) | Pair with the environment builder/runtime routing docs when scenes or room metadata change. |
| Motion style, pelvis and torso expression, arm-leg coordination, readable personality layers | [Chapter 8](./unified-locomotion-roadmap/08-expressive-motion-and-feel.md) | Only start after control architecture is stable enough to protect readability. |
| Telemetry, dashboards, baseline captures, focused regression slices, failure triage workflow | [Chapter 9](./unified-locomotion-roadmap/09-validation-debugging-and-tuning.md), plus [LOCOMOTION_BASELINES.md](../../LOCOMOTION_BASELINES.md) and [DEBUGGING.md](../../DEBUGGING.md) | This chapter runs continuously across the whole roadmap. |

## Recommended execution order

1. Chapter 1 only until authority boundaries are clean.
2. Chapters 2 and 3 in parallel slices.
3. Chapter 4 for real step planning.
4. Chapters 5 and 6 for support and recovery integration.
5. Chapter 7 terrain hardening.
6. Chapter 8 expression pass.
7. Chapter 9 runs continuously and scales with each chapter.

## First actionable sprint

1. Add LocomotionDirector in pass-through mode.
2. Add observation and command contracts without changing behavior.
3. Rewire PlayerMovement to emit desired intent only.
4. Keep LegAnimator and BalanceController as executors.
5. Prove parity with:
   - LegAnimatorTests
   - BalanceControllerTurningTests
   - HardSnapRecoveryTests
   - SpinRecoveryTests
   - MovementQualityTests

If this sprint does not preserve parity, stop and fix ownership boundaries before any feel tuning.