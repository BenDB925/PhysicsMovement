# Issue-Driven Agent Workflow Migration

## Status
- State: Active
- Acceptance target: Stand up a workflow where durable reference docs stay in-repo, PRDs and execution slices live in GitHub issues, local plan files stay slim and resumable, stuck investigations split cleanly into bug issues plus bug sheets, and a fresh agent can execute one step from the plan and issue context alone.
- Current next step: Draft and launch the follow-on `DEBUGGING.md` documentation-slimming slice under PRD #1, unless priorities change.
- Active blockers: None currently.

## Quick Resume
- The documentation-slimming pilot now has live GitHub slice issues for `.copilot-instructions.md`, `AGENT_TEST_RUNNING.md`, `CODING_STANDARDS.md`, and `LOCOMOTION_BASELINES.md`, with child docs `06` through `09` serving as the canonical local resume surface.
- `LOCOMOTION_BASELINES.md` is now a current-baseline index, while the detailed Chapter 1 and Chapter 8 baseline history lives in `Plans/archive/locomotion-baselines-history.md`.
- The next useful restart point is a follow-on documentation-slimming slice for `DEBUGGING.md` under PRD #1 unless priorities change.

## Verified Artifacts
- `https://github.com/BenDB925/PhysicsMovement/issues/6`: live slice issue for the `LOCOMOTION_BASELINES.md` slimming pass.
- `Plans/issue-driven-agent-workflow/09-locomotion-baselines-slimming.md`: child doc for the fourth documentation-slimming slice, including the launched slice link and next restart point.
- `LOCOMOTION_BASELINES.md`: slimmed current-baseline index that now keeps only the latest baseline reference, active known reds, and first-open artifact links.
- `Plans/archive/locomotion-baselines-history.md`: archived Chapter 1 and Chapter 8 baseline detail moved out of the root read path.
- `https://github.com/BenDB925/PhysicsMovement/issues/4`: live slice issue for the `CODING_STANDARDS.md` slimming pass.
- `Plans/issue-driven-agent-workflow/08-coding-standards-slimming.md`: child doc for the third documentation-slimming slice, including the launched slice link and next restart point.
- `CODING_STANDARDS.md`: slimmed rulebook that now exposes `Quick Load` and `Read More When` while leaving detailed unattended test-running procedure in `AGENT_TEST_RUNNING.md`.
- `https://github.com/BenDB925/PhysicsMovement/issues/1`: live PRD for the documentation-slimming pilot.
- `https://github.com/BenDB925/PhysicsMovement/issues/3`: live slice issue for the `AGENT_TEST_RUNNING.md` slimming pass.
- `Plans/issue-driven-agent-workflow/07-agent-test-running-slimming.md`: child doc for the second documentation-slimming slice, including the launched slice link and next restart point.
- `AGENT_TEST_RUNNING.md`: slimmed test-running entry point that now defaults to the repo-primary unattended path.
- `https://github.com/BenDB925/PhysicsMovement/issues/2`: live slice issue for the `.copilot-instructions.md` entry-point rewrite.
- `Plans/issue-driven-agent-workflow/06-copilot-instructions-slimming-pilot.md`: child doc for the first documentation-slimming slice, including the launched PRD and slice links.
- `.copilot-instructions.md`: slimmed workspace entry point that now routes into deeper docs only when relevant.
- `BenDB925/PhysicsMovement` labels: `type:*`, `mode:*`, `status:*`, and initial `area:*` labels are now synced through `Tools/Sync-IssueWorkflowLabels.ps1`.
- `Plans/issue-driven-agent-workflow/05-launch-assets.md`: implementation record for the new templates, skills, routing shim, and label bootstrap.
- `.github/instructions/issue-driven-agent-workflow.instructions.md`: thin router for PRD, slice, bug, and workflow-asset tasks.
- `.github/ISSUE_TEMPLATE/prd.yml`: parent issue form for PRDs.
- `.github/ISSUE_TEMPLATE/slice.yml`: narrow slice issue form with explicit link and label contract.
- `.github/ISSUE_TEMPLATE/bug-escalation.yml`: dedicated stuck-issue escalation form.
- `Tools/Sync-IssueWorkflowLabels.ps1`: reproducible label bootstrap for the launch taxonomy.
- `Plans/issue-driven-agent-workflow/01-skill-adoption-and-migration-scope.md`: detailed skill adoption matrix, migration stance, issue model, and rollout phases.
- `Plans/issue-driven-agent-workflow/02-stuck-issue-debugging-workflow.md`: concrete escalation rules, handoff package, and skill-role split for stuck investigations.
- `Plans/issue-driven-agent-workflow/03-document-surface-audit.md`: keep/slim/archive recommendations for the current document set, including which files are worth keeping as thin entry points and which ones should be demoted or archived.
- `Plans/issue-driven-agent-workflow/04-issue-model-and-pilot-decisions.md`: confirmed launch taxonomy, AFK or HITL semantics, link rules, and the recommended first pilot slice.
- `Plans/README.md`: canonical task-record structure that this workflow must preserve.
- `.copilot-instructions.md`: current context-budget and task-record expectations that the new workflow must align with.

## Child docs
- [x] Skill adoption and migration scope: adoption matrix, issue model, rollout phases (`Plans/issue-driven-agent-workflow/01-skill-adoption-and-migration-scope.md`)
- [x] Stuck issue debugging workflow: escalation rules, mandatory handoff package, and threshold-change policy (`Plans/issue-driven-agent-workflow/02-stuck-issue-debugging-workflow.md`)
- [x] Document surface audit: keep/slim/archive recommendations and thin-entry-point strategy (`Plans/issue-driven-agent-workflow/03-document-surface-audit.md`)
- [x] Issue model and pilot decisions: launch labels, AFK or HITL semantics, link rules, and recommended first pilot slice (`Plans/issue-driven-agent-workflow/04-issue-model-and-pilot-decisions.md`)
- [x] Launch assets: issue templates, workflow skills, routing shim, and label bootstrap (`Plans/issue-driven-agent-workflow/05-launch-assets.md`)
- [x] Copilot instructions slimming pilot: local execution record plus launched PRD and slice for the first documentation slice (`Plans/issue-driven-agent-workflow/06-copilot-instructions-slimming-pilot.md`)
- [x] AGENT_TEST_RUNNING slimming slice: local execution record plus launched slice for the second documentation pass (`Plans/issue-driven-agent-workflow/07-agent-test-running-slimming.md`)
- [x] CODING_STANDARDS slimming slice: local execution record plus launched slice for the third documentation pass (`Plans/issue-driven-agent-workflow/08-coding-standards-slimming.md`)
- [x] LOCOMOTION_BASELINES slimming slice: local execution record plus launched slice for the fourth documentation pass (`Plans/issue-driven-agent-workflow/09-locomotion-baselines-slimming.md`)

## Work packages
1. [x] Define the issue model: PRD issue, local plan file, tracer-bullet slice issues, bug issues, refactor RFCs, AFK/HITL semantics, blocker links, and the stuck-issue split rules.
2. [x] Add repo-local workflow customizations adapted to this Unity repo and its `Plans/` conventions, including separate issue-writing, debugging, and execution skills.
3. [ ] Shrink the always-read document surface by converting heavy mandatory docs into thin entry points plus appendices or skills.
4. [x] Pilot the workflow on one real, bounded task to prove that a fresh agent can execute from the plan and issues without prior chat context.
5. [ ] Migrate active execution history out of oversized parent plans and baseline docs into linked child docs or GitHub issues.
6. [ ] Trim long-lived documentation back to durable reference only and document the steady-state workflow.

## Progress notes
- 2026-03-18: Slimmed `LOCOMOTION_BASELINES.md` into a current baseline index, moved the detailed baseline history to `Plans/archive/locomotion-baselines-history.md`, launched slice #6 under PRD #1, and updated the local plan tree so `DEBUGGING.md` is now the leading documentation-slimming candidate.
- 2026-03-18: Slimmed `CODING_STANDARDS.md` into a tighter rulebook, launched slice #4 under PRD #1, closed retry duplicate #5, and updated the local plan tree so `LOCOMOTION_BASELINES.md` is now the leading documentation-slimming candidate.
- 2026-03-18: Slimmed `AGENT_TEST_RUNNING.md` to the repo-primary unattended test path, launched slice #3 under PRD #1, and updated the local plan tree so the next documentation-slimming candidate is now `CODING_STANDARDS.md`.
- 2026-03-18: Posted the prepared documentation-slimming pilot issues to `BenDB925/PhysicsMovement` as PRD #1 and slice #2, then updated the local plan tree so the GitHub queue state and repo resume surface match.
- 2026-03-18: Synced the launch label set to `BenDB925/PhysicsMovement` after locating `gh.exe` and reusing its authenticated token for `Tools/Sync-IssueWorkflowLabels.ps1`. Issue creation is now unblocked.
- 2026-03-18: Slimmed `.copilot-instructions.md` into a thin routing file, created the pilot child doc, and prepared the matching PRD plus slice issue drafts locally. Remote launch is now blocked only on label creation.
- 2026-03-18: Updated the workflow assets and workspace guidance so `vscode_askQuestions` is the default before most non-trivial planning or implementation work unless the task is already fully explicit.
- 2026-03-18: Attempted to create the launch labels remotely. The environment does not have `gh` or a GitHub token, so the sync is still blocked pending auth; the bootstrap script now supports either path.
- 2026-03-18: Implemented the launch assets for the issue-driven workflow: four GitHub issue forms, three repo-local workflow skills, a routing instruction, a reproducible label-sync script, and the supporting plan/task-routing updates.
- 2026-03-18: Reviewed the external `mattpocock/skills` catalog and mapped the relevant skills against the repo's current planning, routing, and debugging surface.
- 2026-03-18: Audited local documentation and customization files. Durable references are already reasonably well-scoped; the main problem is progress and experiment history accumulating in parent plans and baseline docs.
- 2026-03-18: Wrote the initial migration plan with a recommendation to adopt a focused subset of skills instead of importing the full catalog wholesale.
- 2026-03-18: Refined the plan around stuck-issue handling. User preference is now explicit: one issue per narrow slice, split to a dedicated bug issue plus bug sheet after 3 failed hypotheses, always announce the split in chat, and never relax thresholds or acceptance criteria silently.
- 2026-03-18: Added a document-surface audit. Main conclusion: the repo does not have too many documents, but it does have too many heavyweight docs treated as mandatory; the right fix is to keep thin entry points, archive execution-heavy history, and demote detailed procedures into appendices or skills.
- 2026-03-18: Confirmed the launch issue model and pilot policy. User preference is now explicit: use a prefixed label taxonomy, reserve `mode:hitl` for user-decision blockers, and start the pilot on documentation slimming with `.copilot-instructions.md` as the recommended first slice.