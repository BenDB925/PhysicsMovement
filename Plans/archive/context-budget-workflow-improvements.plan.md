# Archived on 2026-03-18 during the legacy-plan cleanup pass. Slice 1 shipped; reopen this history only if a new routing slice is explicitly chosen.

# Context Budget Workflow Improvements

## Status
- State: Archived after Slice 1
- Acceptance target: Land the proposed context-budget workflow slices incrementally, starting with the summary-first doc pass.
- Current next step: None. Reopen this history only if the workflow needs another dedicated context-budget routing pass.
- Active blockers: None.

## Child docs
- [x] Token budget proposal: concrete wins, target files, and rollout order (`Plans/archive/context-budget-workflow-improvements/01-token-budget-proposal.md`)

## Work packages
1. [x] Inspect the current routing, heavy docs, and completion hook workflow.
2. [x] Draft a concrete token-budget proposal tied to real repo files.
3. [x] Recommend a rollout order that keeps risk low.
4. [x] Add Slice 1 `Quick Load` / `Read More When` headers to the initial heavy docs.

## Progress notes
- 2026-03-12: Reviewed the current routing surface (`TASK_ROUTING.md`, `.copilot-instructions.md`), the heavy long-lived docs (`ARCHITECTURE.md`, `DEBUGGING.md`, `LOCOMOTION_BASELINES.md`), the Unity test runner guidance, and the completion-review hook implementation.
- 2026-03-12: Wrote a concrete proposal that keeps the existing file-based workflow but borrows OpenViking's strongest ideas: layered summaries, deterministic drill-down, compact artifact digests, and resumable session summaries.
- 2026-03-12: Implemented Slice 1 by adding `Quick Load` and `Read More When` summary headers to `ARCHITECTURE.md`, `DEBUGGING.md`, `LOCOMOTION_BASELINES.md`, `AGENT_TEST_RUNNING.md`, and the first three unified-locomotion roadmap chapter docs.