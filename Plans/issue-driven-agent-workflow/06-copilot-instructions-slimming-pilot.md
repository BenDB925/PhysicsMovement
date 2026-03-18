# Copilot Instructions Slimming Pilot

## Goal

Prove the first documentation-slimming slice by turning `.copilot-instructions.md` into a thin routing file that points to deeper durable docs only when the task actually needs them.

## Current status
- State: Active
- Current next step: Create the workflow labels through an authenticated write path, then post the prepared PRD and slice issues for this pilot.
- Blockers: GitHub repo access works through the GitHub toolset, but this environment still has no label-creation path because `gh` is missing, no GitHub token is present in the terminal, and the available GitHub tools expose label reads but not label writes.

## Decisions
- 2026-03-18: Keep this slice narrow. `.copilot-instructions.md` is the pilot target; `AGENT_TEST_RUNNING.md` remains a likely follow-on slice unless a tiny supporting edit becomes unavoidable.
- 2026-03-18: The slimmed entry file should route agents into `CODING_STANDARDS.md`, `TASK_ROUTING.md`, `ARCHITECTURE.md`, `Plans/README.md`, `PLAN.md`, and `AGENT_TEST_RUNNING.md` only when the task actually needs those files.
- 2026-03-18: The parent plan plus this child doc remain the canonical resume surface; any future GitHub issues for this pilot should stay thin and point back here.

## Artifacts
- `.copilot-instructions.md`: pilot target for the thin entry-point rewrite.
- `Plans/issue-driven-agent-workflow.plan.md`: parent plan that tracks the overall workflow migration.
- `Plans/issue-driven-agent-workflow/03-document-surface-audit.md`: audit that justified this pilot.
- `Plans/issue-driven-agent-workflow/04-issue-model-and-pilot-decisions.md`: issue model and pilot acceptance target.

## Prepared issue drafts

### PRD draft
- Title: `[PRD] Documentation-slimming pilot for issue-driven workflow`
- Labels when created: `type:prd`, `area:workflow`, `area:docs`
- Goal: Prove the issue-driven workflow on a bounded documentation pilot by turning `.copilot-instructions.md` into a thin routing entry point and keeping detailed execution state in `Plans/`.
- Acceptance target:
  - `.copilot-instructions.md` becomes a thin entry point instead of a duplicated architecture and phase-status dump.
  - The local plan tree remains the canonical resume surface for this pilot.
  - Follow-on slice issues can execute from bounded context without old chat history.
- Non-goals: Do not widen this PRD into runtime behavior changes, broad architecture edits, or unrelated documentation cleanup.
- Local parent plan path: `Plans/issue-driven-agent-workflow.plan.md`
- Planned slices or placeholders:
  - Slice 1: slim `.copilot-instructions.md` into a routing entry point.
  - Slice 2: slim `AGENT_TEST_RUNNING.md` to the repo-primary unattended test path.
- Dependencies or blockers: Workflow labels still need a writable GitHub path in this environment.

### Slice draft
- Title: `[Slice] Slim .copilot-instructions.md into a true entry-point file`
- Labels when created: `type:slice`, `mode:afk`, `status:ready`, `area:workflow`, `area:docs`
- Parent issue: The PRD draft above.
- Acceptance target:
  - `.copilot-instructions.md` routes into `CODING_STANDARDS.md`, `TASK_ROUTING.md`, `ARCHITECTURE.md`, `Plans/README.md`, `PLAN.md`, and `AGENT_TEST_RUNNING.md` only when the task actually needs them.
  - The file no longer duplicates long systems tables, architecture snapshots, or phase-history inventory.
  - The parent plan and child doc capture the resumable execution detail.
- Local parent plan path: `Plans/issue-driven-agent-workflow.plan.md`
- Owning child-doc path: `Plans/issue-driven-agent-workflow/06-copilot-instructions-slimming-pilot.md`
- Blockers or dependencies: Workflow labels must exist before the issue can be created with the intended taxonomy.
- Definition of done:
  - `.copilot-instructions.md` is updated.
  - The parent plan and this child doc reflect the new state.
  - Verification confirms the routed files and resume surfaces exist.

## Progress notes
- 2026-03-18: User confirmed the next step should attempt the remote GitHub launch if auth works, commit the full workflow-documentation batch, and include `AGENT_TEST_RUNNING.md` only if it fits without widening the slice.
- 2026-03-18: Confirmed GitHub repo access through the GitHub toolset, but label creation is still blocked in this environment because the terminal has neither `gh` nor a GitHub token, and the available GitHub tools expose label reads but not label writes.
- 2026-03-18: Slimmed `.copilot-instructions.md` into a thin routing file. Left `AGENT_TEST_RUNNING.md` for a follow-on slice so this pilot stays one acceptance target.