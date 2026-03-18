# Copilot Instructions Slimming Pilot

## Goal

Prove the first documentation-slimming slice by turning `.copilot-instructions.md` into a thin routing file that points to deeper durable docs only when the task actually needs them.

## Current status
- State: Complete
- Current next step: Use PRD #1 as the parent for the next documentation-slimming slice, with `AGENT_TEST_RUNNING.md` still the leading candidate if priorities stay the same.
- Blockers: None currently. The workflow labels are now synced.

## Decisions
- 2026-03-18: Keep this slice narrow. `.copilot-instructions.md` is the pilot target; `AGENT_TEST_RUNNING.md` remains a likely follow-on slice unless a tiny supporting edit becomes unavoidable.
- 2026-03-18: The slimmed entry file should route agents into `CODING_STANDARDS.md`, `TASK_ROUTING.md`, `ARCHITECTURE.md`, `Plans/README.md`, `PLAN.md`, and `AGENT_TEST_RUNNING.md` only when the task actually needs those files.
- 2026-03-18: The parent plan plus this child doc remain the canonical resume surface; any future GitHub issues for this pilot should stay thin and point back here.

## Artifacts
- `.copilot-instructions.md`: pilot target for the thin entry-point rewrite.
- `Plans/issue-driven-agent-workflow.plan.md`: parent plan that tracks the overall workflow migration.
- `Plans/issue-driven-agent-workflow/03-document-surface-audit.md`: audit that justified this pilot.
- `Plans/issue-driven-agent-workflow/04-issue-model-and-pilot-decisions.md`: issue model and pilot acceptance target.
- `BenDB925/PhysicsMovement` labels: the launch taxonomy now exists remotely and matches `Tools/Sync-IssueWorkflowLabels.ps1`.
- `https://github.com/BenDB925/PhysicsMovement/issues/1`: live PRD for the documentation-slimming pilot.
- `https://github.com/BenDB925/PhysicsMovement/issues/2`: live slice issue for the `.copilot-instructions.md` routing rewrite.

## Launched issues

- PRD #1: `https://github.com/BenDB925/PhysicsMovement/issues/1`
- Slice #2: `https://github.com/BenDB925/PhysicsMovement/issues/2`
- The slice is intentionally AFK, labeled `status:ready`, and points back to this child doc plus the parent plan instead of carrying detailed execution history in the issue body.

## Progress notes
- 2026-03-18: Posted the prepared PRD and slice issues to `BenDB925/PhysicsMovement` as PRD #1 and slice #2, then replaced the local draft block with the live issue references.
- 2026-03-18: Synced the launch label set to `BenDB925/PhysicsMovement`, so the prepared PRD and slice issues can now be created without violating the label contract.
- 2026-03-18: User confirmed the next step should attempt the remote GitHub launch if auth works, commit the full workflow-documentation batch, and include `AGENT_TEST_RUNNING.md` only if it fits without widening the slice.
- 2026-03-18: Confirmed GitHub repo access through the GitHub toolset, but label creation is still blocked in this environment because the terminal has neither `gh` nor a GitHub token, and the available GitHub tools expose label reads but not label writes.
- 2026-03-18: Slimmed `.copilot-instructions.md` into a thin routing file. Left `AGENT_TEST_RUNNING.md` for a follow-on slice so this pilot stays one acceptance target.