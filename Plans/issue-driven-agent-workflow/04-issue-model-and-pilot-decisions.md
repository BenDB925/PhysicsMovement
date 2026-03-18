# Issue Model And Pilot Decisions

## Goal

Lock the initial GitHub issue taxonomy, label set, AFK or HITL semantics, and first pilot slice so repo-local customizations can be implemented without guessing.

## Current status
- State: Proposed, decisions confirmed
- Current next step: Turn these decisions into issue templates, label creation, and repo-local workflow customizations.
- Blockers: The repository still needs issue templates, launch labels, and the repo-local skills or prompts that will enforce this workflow.

## Decisions
- 2026-03-18: Use a prefixed startup label taxonomy instead of leaning on generic GitHub defaults. The default labels can remain in the repo, but the workflow should route work through explicit `type:*`, `mode:*`, `status:*`, and `area:*` labels.
- 2026-03-18: Reserve `mode:hitl` for slices that are blocked on an explicit user decision, approval, or missing external input. Manual editor checks or subjective feel validation alone do not make a slice HITL.
- 2026-03-18: Use documentation slimming as the first pilot track because it is bounded, low-risk, and directly tests the context-budget goal of the workflow.
- 2026-03-18: Start that pilot with slimming `.copilot-instructions.md` into a true entry-point file. This is the strongest first slice because the document is both high-impact and explicitly called out as the first cleanup target in the document-surface audit.
- 2026-03-18: Keep the launch taxonomy intentionally small. Add more `area:*` labels only when the pilot proves that the workflow is worth carrying into wider runtime work.

## Confirmed issue taxonomy

### Parent issues
- `type:prd`: feature or initiative entry point that owns the goal, acceptance target, non-goals, and the linked local parent plan.
- `type:refactor`: architecture-first cleanup issue where the design or ownership discussion matters as much as the implementation.
- `type:bug`: standalone regression intake, or a dedicated bug spawned from a stuck slice after the failed-hypothesis budget is exhausted.

### Execution issues
- `type:slice`: one narrow AFK or HITL work item with one owner, one acceptance target, and one linked local execution record.
- Escalated bug issues stay separate from slice issues. A slice may be blocked by a bug issue, but the slice should not absorb the bug diary into itself.

### Local records
- Parent plan under `Plans/`: the canonical in-repo resume surface.
- Child work-package doc: the detailed execution record for an active slice.
- Bug sheet: the detailed evidence log for a stuck symptom or active regression thread.

## Launch label set

### Create now
- `type:prd`
- `type:slice`
- `type:bug`
- `type:refactor`
- `mode:afk`
- `mode:hitl`
- `status:ready`
- `status:blocked`
- `area:workflow`
- `area:docs`

### Add later, when the workflow reaches runtime work
- `area:character`
- `area:environment`

## AFK and HITL semantics

### `mode:afk`
- The agent can advance the slice without waiting for a user decision.
- Focused implementation and verification can run from repo context, MCP tooling, or terminal access.
- A manual follow-up check may still be recommended at closeout, but it is not the blocking condition for the slice.

### `mode:hitl`
- The slice cannot be completed correctly without the user's explicit choice, approval, or out-of-band input.
- Use this mode when the agent needs a policy decision, a requirement tradeoff approval, credentials or permissions, or a human sign-off that changes what work is allowed.
- Do not mark a slice HITL only because it may benefit from a quick visual check or editor validation.

## Link and ownership rules

1. Every slice issue must link to exactly one parent issue and exactly one local plan or child-doc path.
2. Every bug issue must link back to the parent slice issue and to one local bug sheet.
3. When a slice becomes blocked by a dedicated bug issue, the slice keeps its scope, gains `status:blocked`, and names the blocking bug explicitly.
4. Parent plans stay as the canonical resume surface. GitHub issues hold queue state, ownership, and the concise handoff summary.
5. No issue should become a surrogate long-form plan. If the body starts accumulating attempt logs, split that detail into the linked plan child doc or bug sheet.

## Template field contract

### PRD or refactor issue
- Goal
- Acceptance target
- Non-goals
- Local parent plan path
- Planned slice list or placeholders

### Slice issue
- Parent issue
- Mode (`mode:afk` or `mode:hitl`)
- Acceptance target
- Local plan path
- Owning child-doc path
- Blockers or dependencies
- Definition of done

### Bug issue
- Parent slice issue
- Visible symptom
- Best failing test or focused reproduction path
- Failed hypotheses so far
- Next best hypothesis
- Local bug-sheet path

## Recommended pilot slice

### Pilot track
- Documentation slimming

### First slice
- Slim `.copilot-instructions.md` into a true routing file that points to `CODING_STANDARDS.md`, `TASK_ROUTING.md`, `Plans/README.md`, and `AGENT_TEST_RUNNING.md` only when relevant.

### Why this is the right pilot
- It exercises the hybrid workflow without mixing in runtime debugging uncertainty.
- It is small enough that a fresh agent should be able to execute it from the issue plus plan context alone.
- It directly tests the workflow's stated goal: reducing mandatory context load while preserving durable knowledge.

### Pilot acceptance target
- `.copilot-instructions.md` becomes a thin entry point rather than a duplicated architecture or phase-status dump.
- The slice can be written as a narrow AFK issue with a linked local child doc.
- A fresh agent should be able to execute the slice from the issue body plus linked plan without needing the old chat transcript.

## Next implementation step

Turn this decision set into:

1. issue templates for PRDs, slices, and bug escalations
2. the launch label set
3. repo-local workflow customizations that enforce the link and handoff rules above