# Legacy Plan Archive Pass

## Goal

Archive legacy plan files that still crowd the repo root or top-level `Plans/` surface, keep only thin durable root docs, and rewire the active docs that still need archived history.

## Current status
- State: Complete
- Current next step: Slim the remaining oversized active parent plans, starting with `Plans/comedic-knockdown-overhaul.plan.md`, unless priorities change.
- Blockers: None.

## Decisions
- 2026-03-18: Archive `AGENT_MOVEMENT_MIGRATION_PLAN.md` instead of keeping a root pointer. At 472 lines it had become a legacy migration diary, so the history was kept but removed from the live root surface.
- 2026-03-18: Archive `Plans/sprint-jump-stability-tests.md` as a frozen baseline record and keep `Plans/sprint-jump-smoothing.plan.md` as the live owner for sprint-jump runtime work.
- 2026-03-18: Archive `Plans/context-budget-workflow-improvements.plan.md`, its child proposal doc, and `Plans/task-completion-doc-review-hook.plan.md`; their shipped outcomes already live in the durable workflow docs and hooks.
- 2026-03-18: Keep the slim durable root docs `.copilot-instructions.md`, `TASK_ROUTING.md`, `ARCHITECTURE.md`, `CODING_STANDARDS.md`, `AGENT_TEST_RUNNING.md`, `DEBUGGING.md`, `LOCOMOTION_BASELINES.md`, `PLAN.md`, and `CONCEPT.md`. They still provide current routing, subsystem, or prototype intent and are no longer default-heavy reads.
- 2026-03-18: Keep this pass local. The user asked for immediate repo cleanup, so the plan tree was updated directly instead of pausing to launch a new GitHub slice issue first.

## Artifacts
- `Plans/archive/AGENT_MOVEMENT_MIGRATION_PLAN.md`: archived hard-turn migration history removed from the repo root.
- `Plans/archive/sprint-jump-stability-tests.md`: archived known-red sprint-jump baseline now referenced only from the active sprint-jump docs.
- `Plans/archive/context-budget-workflow-improvements.plan.md`: archived Slice 1 context-budget parent plan.
- `Plans/archive/context-budget-workflow-improvements/01-token-budget-proposal.md`: archived supporting proposal for the context-budget pass.
- `Plans/archive/task-completion-doc-review-hook.plan.md`: archived shipped hook plan.
- `Plans/sprint-jump-smoothing.plan.md`: live sprint-jump plan updated to reference the archived baseline.
- `Plans/unified-locomotion-roadmap/09-validation-debugging-and-tuning.md`: live roadmap chapter updated to point to the archived sprint-jump baseline.

## Progress notes
- 2026-03-18: Archived the obvious legacy plan surface under `Plans/archive/`, removed `AGENT_MOVEMENT_MIGRATION_PLAN.md` from the repo root, rewired the active sprint-jump docs to the archived baseline file, and kept only the slim durable root docs on the live read path.