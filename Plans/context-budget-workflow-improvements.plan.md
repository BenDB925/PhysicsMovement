# Context Budget Workflow Improvements

## Status
- State: In Progress
- Acceptance target: Land the proposed context-budget workflow slices incrementally, starting with the summary-first doc pass.
- Current next step: Decide whether to implement Slice 2 context-budget routing rules now that Slice 1 summary headers are in place.
- Active blockers: None.

## Child docs
- [x] Token budget proposal: concrete wins, target files, and rollout order (`Plans/context-budget-workflow-improvements/01-token-budget-proposal.md`)

## Work packages
1. [x] Inspect the current routing, heavy docs, and completion hook workflow.
2. [x] Draft a concrete token-budget proposal tied to real repo files.
3. [x] Recommend a rollout order that keeps risk low.
4. [x] Add Slice 1 `Quick Load` / `Read More When` headers to the initial heavy docs.

## Progress notes
- 2026-03-12: Reviewed the current routing surface (`TASK_ROUTING.md`, `.copilot-instructions.md`), the heavy long-lived docs (`ARCHITECTURE.md`, `DEBUGGING.md`, `LOCOMOTION_BASELINES.md`), the Unity test runner guidance, and the completion-review hook implementation.
- 2026-03-12: Wrote a concrete proposal that keeps the existing file-based workflow but borrows OpenViking's strongest ideas: layered summaries, deterministic drill-down, compact artifact digests, and resumable session summaries.
- 2026-03-12: Implemented Slice 1 by adding `Quick Load` and `Read More When` summary headers to `ARCHITECTURE.md`, `DEBUGGING.md`, `LOCOMOTION_BASELINES.md`, `AGENT_TEST_RUNNING.md`, and the first three unified-locomotion roadmap chapter docs.