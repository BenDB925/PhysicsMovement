---
description: "Use when creating or updating GitHub PRDs, refactor RFCs, slice issues, stuck-bug escalations, launch labels, or workflow assets for the issue-driven execution model in PhysicsDrivenMovementDemo. Routes agents to the repo's issue templates, workflow skills, and plan-link rules."
name: "Issue-Driven Workflow Routing"
---
# Issue-Driven Workflow Routing

Use this instruction when the task is about GitHub issue workflow, issue templates, plan-to-issue links, stuck-issue handoffs, or the documentation-slimming pilot that proves this flow.

## Read first

- `Plans/issue-driven-agent-workflow.plan.md`
- `Plans/issue-driven-agent-workflow/04-issue-model-and-pilot-decisions.md`
- `Plans/README.md`

## Use the right asset

- Draft or update a PRD, refactor RFC, slice issue, or bug escalation: `.github/skills/issue-writing-workflow/SKILL.md`
- Investigate a stuck slice or narrow regression with the failed-hypothesis budget: `.github/skills/stuck-issue-debugging/SKILL.md`
- Implement one ready slice from bounded issue plus plan context: `.github/skills/execute-slice/SKILL.md`

## Hard workflow rules

- Before most planning or implementation work in this workflow, use `vscode_askQuestions` to confirm user preferences, scope, or tradeoffs in plain English unless the task is trivial or already fully explicit.
- One slice issue owns one narrow acceptance target, one parent issue, and one local plan or child doc.
- One bug issue owns one narrow unresolved symptom and one local bug sheet.
- Keep long attempt logs in `Plans/` docs or bug sheets, not issue bodies.
- Use exactly one of `mode:afk` or `mode:hitl` on slice issues.
- Reserve `mode:hitl` for real user-decision or external-input blockers, not ordinary manual validation.
- After 3 failed hypotheses on the same symptom, split to a dedicated bug issue and bug sheet instead of continuing hidden chat history.

## Launch assets

- Issue forms live under `.github/ISSUE_TEMPLATE/`.
- Label bootstrap lives at `Tools/Sync-IssueWorkflowLabels.ps1`.
- Keep `TASK_ROUTING.md` and the parent plan current when this workflow changes.