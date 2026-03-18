# Skill Adoption And Migration Scope

## Goal

Stand up an issue-driven agent workflow that reduces context bloat, preserves progress logs, and lets a fresh agent execute one bounded slice from the plan and issue context alone.

## Current status
- State: Proposed
- Current next step: Use the confirmed issue taxonomy and pilot decision to create issue templates, labels, and repo-local workflow customizations.
- Blockers: No issue templates exist yet, the repo only has default GitHub labels, and the external skills assume generic `./plans/` conventions that need adaptation to this repo's `Plans/README.md` workflow.

## Decisions
- 2026-03-18: Use a hybrid model, not a full issue-only model. Durable reference stays in repo files; PRDs, slice queues, bug tracking, and long-running execution state move to GitHub issues plus linked local plan docs.
- 2026-03-18: Do not import the entire `mattpocock/skills` repo. Install only the skills that reinforce this repo's desired workflow and adapt them to the current Unity-specific instructions and testing rules.
- 2026-03-18: Keep the repo's existing thin-entry routing surface (`.copilot-instructions.md`, `TASK_ROUTING.md`, `Plans/README.md`, `.github/instructions/`) and build the new workflow on top of it.
- 2026-03-18: Pilot the workflow on a new or bounded task first. Do not use a large locomotion regression as the first proof point because it would mix workflow risk with runtime uncertainty.
- 2026-03-18: Separate the workflow into distinct roles. Issue writing, debugging, and execution should be separate skills so each stage can run with a fresh context and a narrower objective.
- 2026-03-18: When an agent hits 3 failed hypotheses on the same unresolved symptom, treat that as a workflow transition, not a reason to keep looping. Split to a dedicated bug issue plus linked local bug sheet and preserve the handoff package there.
- 2026-03-18: Use a prefixed launch taxonomy. The default GitHub labels can remain, but the workflow itself should use explicit `type:*`, `mode:*`, `status:*`, and `area:*` labels.
- 2026-03-18: Reserve `mode:hitl` for work blocked on an explicit user decision, approval, or missing external input. Manual validation alone does not make a slice HITL.
- 2026-03-18: Use documentation slimming as the first pilot track, with `.copilot-instructions.md` as the recommended first AFK slice.

## Keep vs move

### Keep as durable files
- `CODING_STANDARDS.md`
- `ARCHITECTURE.md`
- `TASK_ROUTING.md`
- `DEBUGGING.md`
- `AGENT_TEST_RUNNING.md`
- `.copilot-instructions.md`
- `PLAN.md`
- `CONCEPT.md`
- `Plans/README.md`
- `.github/instructions/*`
- `.github/skills/debugging-workflow/SKILL.md`

### Move toward issues or linked child docs
- PRDs for new features or significant refactors
- Tracer-bullet implementation slices
- Bug investigations and repeated experiment logs
- Historical baseline snapshots and known-red histories that no longer need to live in a long-lived root doc
- Oversized parent-plan attempt summaries such as the active sprint-jump smoothing investigation

## Skill decisions

### Adopt now
- `write-a-prd`: Good fit for turning feature or refactor intent into a durable GitHub issue. Adapt it to repo-specific testing decisions and to avoid unstable file-path detail.
- `prd-to-plan`: High value. Adapt it so plans are written under `Plans/` using the repo's parent-plan and child-doc structure instead of a generic `./plans/` folder.
- `prd-to-issues`: High value. Use it to generate thin vertical slices with dependency links, AFK/HITL classification, and explicit acceptance criteria.
- `grill-me`: High value as a pressure-test gate before a PRD or plan is accepted. Keep it codebase-aware so it explores the repo instead of asking avoidable questions.
- `tdd`: High value, but it must inherit the repo's Unity rules: outcome-based PlayMode tests, focused verification, and the existing Phase A through F workflow.
- `triage-issue`: High value for regressions and bug intake. Adapt it to update the local plan or bug sheet alongside the created GitHub issue.

### Build as repo-local workflow skills
- `debug-stuck-issue`: New repo-local skill built from the existing debugging workflow plus `triage-issue`. It should enforce the attempt budget, mandatory bug sheet creation, handoff package, rejected-hypothesis logging, and root-cause-first rules.
- `execute-slice`: New repo-local execution skill that assumes the slice issue and local plan already exist, reads only that bounded context, and follows the repo's adapted `tdd` workflow.
- `write-slice-issue`: Either a thin wrapper around `prd-to-issues` or a repo-local prompt/skill for writing one narrow AFK or HITL work item at a time with the correct labels and link structure.

### Adopt later
- `improve-codebase-architecture`: Useful, but only after the issue-driven workflow is stable. It should be an opt-in architecture RFC generator, not a default source of issue churn.
- `request-refactor-plan`: Useful for larger refactors once issue templates and labels exist.
- `design-an-interface`: Useful for deeper module work, especially locomotion contracts, but it is not part of the core workflow.
- `ubiquitous-language`: Likely valuable once the issue workflow is in place, especially if term drift continues across locomotion, recovery, and knockdown work.
- `write-a-skill`: Useful for maintainers creating the next round of repo-specific skills, but not part of the day-to-day execution path.

### Skip for this repo
- `edit-article`: Not part of the core development workflow.
- `obsidian-vault`: Repo-external note workflow; not relevant.
- `migrate-to-shoehorn`: TypeScript-only.
- `scaffold-exercises`: Course-material workflow; not relevant.
- `setup-pre-commit`: Husky and Node-centric; not a good fit for this Unity repo as written.
- `git-guardrails-claude-code`: The repo already has strong guardrails and a VS Code hook surface. A guardrail hook could be added later, but this exact Claude-specific skill should not be copied directly.

## Target workflow

1. Intake
   - New feature or refactor starts as `write-a-prd`.
   - New bug starts as `triage-issue`.
2. Pressure test
   - Run `grill-me` against the proposed PRD or plan until the open branches are resolved.
3. Plan locally
   - Run `prd-to-plan` to create or update the local parent plan plus child docs under `Plans/`.
4. Slice the work
   - Run `prd-to-issues` to create AFK and HITL tracer-bullet issues with blocker links back to the parent PRD.
5. Execute one slice
   - A fresh agent takes one ready AFK issue, reads the linked local plan doc, and implements through the adapted `execute-slice` workflow.
6. Split stuck work cleanly
   - If the same unresolved symptom survives 3 failed hypotheses, run `debug-stuck-issue` to create a dedicated bug issue plus local bug sheet with the full handoff package.
   - The parent slice stays open, but the unresolved thread is explicitly delegated to the bug issue instead of living as hidden chat history.
6. Close out
   - Agent updates the issue, the parent plan, any child bug sheet, and only the durable docs that actually changed.

## Issue model

### Parent records
- PRD issue for a feature, initiative, or major refactor
- Bug issue for a concrete regression or failure report
- Refactor RFC issue for architecture-first cleanup work

### Local records
- Parent plan under `Plans/` for the canonical in-repo resume surface
- Child work-package docs for ongoing slices that need detailed notes
- Bug sheets for diagnostics, evidence, and failed hypotheses

### Execution records
- Tracer-bullet slice issues that are thin, demoable, and independently executable
- Each slice issue links back to the PRD and names the local plan file that owns the current execution record
- Dedicated bug issues for stuck symptoms or unresolved regressions; each bug issue links to the local bug sheet that contains attempts, rejected hypotheses, telemetry, and the next best hypothesis

## Initial label set

### Launch now
- `type:prd`
- `type:slice`
- `type:bug`
- `type:refactor`
- `mode:afk`
- `mode:hitl`
- `status:blocked`
- `status:ready`
- `area:workflow`
- `area:docs`

### Add when the workflow reaches runtime work
- `area:character`
- `area:environment`

Keep the first label set small. Do not add broad label taxonomies until the workflow proves useful in practice. The generic GitHub defaults can stay in place, but the workflow should route work through the prefixed labels above.

## Rollout phases

### Phase 1: Foundation
- Create GitHub issue templates for PRDs, bug triage, and tracer-bullet slices.
- Add the initial label set.
- Decide the canonical link convention between issues and local `Plans/` files.

### Phase 2: Repo-local customizations
- Add workspace-local skills or prompts for `write-a-prd`, `prd-to-plan`, `prd-to-issues`, `grill-me`, `tdd`, and `triage-issue`.
- Add repo-local workflow skills for stuck debugging and fresh-context execution.
- Adapt them to Unity testing, `Plans/README.md`, existing routing docs, and the repo's completion-review workflow.
- If needed, add a dedicated handoff agent that reads only the linked issue plus local plan surface.

### Phase 3: Pilot
- Choose one new or bounded task.
- Run the full workflow end to end: PRD or triage issue, plan file, slice issues, one AFK implementation slice, and closeout.
- Evaluate whether the implementation agent could work from the issue and plan context alone.

### Phase 4: Active-doc migration
- Split oversized parent plans into lean parents plus child bug sheets.
- Move stale experiment logs and historical baselines out of root docs into issues or linked child docs.
- Keep only current, durable reference in root-level docs.

### Phase 5: Steady-state rules
- Update routing docs and instructions so future agents default to the issue-driven workflow.
- Decide whether to add later-phase skills such as `improve-codebase-architecture`, `request-refactor-plan`, or `ubiquitous-language`.
- Archive or delete superseded execution records once their durable outcomes are promoted.

## Recommended pilot stance
- Start with a documentation-slimming slice, not a large locomotion regression.
- Recommended first slice: slim `.copilot-instructions.md` into a real entry-point file.
- Use the first pilot to prove the process, not to solve the hardest runtime problem in the repo.
- Only after the pilot works should active sprawling plans and baseline histories be migrated aggressively.