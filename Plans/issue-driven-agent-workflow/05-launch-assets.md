# Launch Assets

## Goal

Create the first durable repo-local assets for the issue-driven workflow: issue templates, workflow skills, a routing shim, and launch-label bootstrap support.

## Current status
- State: Complete
- Current next step: Synchronize the launch labels once GitHub auth is available, then open the first documentation-slimming parent issue and slice issue for `.copilot-instructions.md`.
- Blockers: Repo-local asset creation is complete. Remote label sync still needs either `gh` or a GitHub token in this environment.

## Decisions
- 2026-03-18: Implement the launch step as reusable repo assets, not one-off issue text, so future agents can start from the same structure.
- 2026-03-18: Keep the execution guidance generic for any bounded slice, while using the documentation-slimming pilot as the first concrete proving ground.
- 2026-03-18: Add four issue templates instead of three so `type:refactor` is also backed by a concrete form instead of only a label.
- 2026-03-18: Add an idempotent PowerShell label-sync script even when direct GitHub label creation is attempted, so the launch taxonomy stays reproducible.
- 2026-03-18: Keep the workflow split into three repo-local skills: issue writing, stuck debugging, and slice execution.
- 2026-03-18: For planning and implementation work in this workflow, prefer `vscode_askQuestions` in plain English before presuming on user preferences or tradeoffs; trivial or fully explicit tasks may skip it.

## Artifacts
- `.github/ISSUE_TEMPLATE/prd.yml`: parent issue form for PRDs.
- `.github/ISSUE_TEMPLATE/refactor.yml`: parent issue form for architecture-first cleanup or ownership RFCs.
- `.github/ISSUE_TEMPLATE/slice.yml`: narrow AFK or HITL slice issue form with explicit link and label contract.
- `.github/ISSUE_TEMPLATE/bug-escalation.yml`: stuck-issue escalation form for dedicated bug issues.
- `.github/instructions/issue-driven-agent-workflow.instructions.md`: thin routing shim for this workflow.
- `.github/skills/issue-writing-workflow/SKILL.md`: repo-local issue-writing workflow guidance.
- `.github/skills/stuck-issue-debugging/SKILL.md`: enforced stuck-investigation and escalation workflow.
- `.github/skills/execute-slice/SKILL.md`: bounded execution workflow for fresh-context slice implementation.
- `Tools/Sync-IssueWorkflowLabels.ps1`: reproducible launch-label sync script for the prefixed taxonomy.

## Progress notes
- 2026-03-18: Confirmed the rollout scope with the user: implement the full launch step, stop at reusable assets, try direct GitHub label creation if auth is available, and keep execution guidance generic.
- 2026-03-18: Added issue forms, workflow skills, the routing instruction, and the idempotent label bootstrap script.
- 2026-03-18: Attempted remote label creation. The environment has neither `gh` nor a GitHub token available, so the sync remains blocked pending auth, but the script now supports either path.
- 2026-03-18: Tightened the workflow assets and workspace guidance so `vscode_askQuestions` is the default before most planning or implementation work unless the task is trivial or already fully explicit.
- 2026-03-18: Updated the parent workflow plan and task routing so future agents can discover the new workflow assets from the existing repo entry points.