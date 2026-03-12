# Task Record Workflow

This folder is the default home for agent-authored execution records when the user does not name another folder.

The goal is to keep long-running work resumable without forcing future agents to reload a large chat transcript or repeat failed experiments.

## When to create or update a task record

- The user asks for a plan, roadmap slice, or task list.
- The user provides an existing plan document that should be kept current.
- The work will span multiple meaningful implementation or debugging steps.
- A debugging pass has moved beyond a one-shot check and now needs evidence, hypotheses, or repeated experiments.

If the user names a folder, use that folder. Otherwise create the task record under `Plans/`.

If a matching task record tree already exists, reuse it instead of creating a parallel tree.

Durable agent guidance belongs under `.github/instructions/`. If a roadmap or other long-running effort needs progress notes, checkpoints, or resumable execution state, keep the working plan under `Plans/` and leave any instruction file as a thin routing shim.

## Canonical structure

### 1. Parent plan file

Suggested path:

`Plans/<task-slug>.plan.md`

The parent plan is the canonical entry point.

A future agent should be able to resume from the parent plan's `Quick Resume` and `Verified Artifacts` sections before opening old chat history, raw logs, or child docs.

Keep it short and current:

- quick resume
- verified artifacts
- current status
- acceptance target
- current next step
- active blockers
- links to child work-package docs
- links to active or resolved bug sheets

The parent plan summarizes. It does not hold long logs, raw telemetry, or a full diagnostic diary.

### 2. Child work-package doc

Suggested path:

`Plans/<task-slug>/<nn>-<topic>.md`

Use one child doc per chapter, subsystem slice, or distinct subproblem.

Put the following here:

- scoped goal
- decisions and rationale
- artifact paths
- dated progress notes
- remaining follow-up work

### 3. Bug sheet

Suggested path:

`Plans/<task-slug>/bugs/<bug-slug>.md`

Use one bug sheet per active bug or investigation thread.

Put the following here:

- visible symptom
- failing test or reproduction path
- current hypothesis list
- experiments run
- evidence summary and artifact paths
- failed hypotheses
- current conclusion
- next step

## Hard rollover thresholds

Create a child work-package doc when any one of these becomes true:

- the parent file would exceed 200 lines
- a single parent section would exceed 80 lines
- a parent section needs more than 3 dated progress notes
- the parent section needs raw logs, telemetry, or more than one active subproblem

Create a bug sheet when any one of these becomes true:

- a work-package or debugging section would exceed 120 lines
- raw logs or telemetry would exceed 30 lines
- more than one active bug or hypothesis thread is being tracked
- the task now has a reproduced symptom or red test that needs repeated experiments

When in doubt, split early. A short linked doc is cheaper than a bloated parent plan.

## Update rules

- Every material child-doc update must be reflected in the parent plan in the same slice.
- Never leave a child doc or bug sheet unlinked.
- Parent plans summarize the latest truth. Child docs hold the detail.
- When a task is paused or complete, refresh the parent plan's `Quick Resume` and `Verified Artifacts` sections in the same slice.
- `Quick Resume` should be one paragraph or 3 bullets with the current truth, the most important verified outcome, and the next useful restart point.
- `Verified Artifacts` should list the one or two files or artifacts worth opening first on resume.
- Never paste long raw logs into the parent plan. Summarize them and link the artifact or bug sheet.
- When a bug is resolved or abandoned, update both the bug sheet and the parent plan with the outcome.
- If the user provided the initial plan document, treat it as the parent record unless they explicitly ask for a different top-level doc.
- If the user signals that the task is complete or paused, use that moment to review whether the parent plan, any child docs, and the core project docs need updates before ending the session.
- After a plan is complete and its durable outcomes have been promoted into long-lived docs, either archive it under `Plans/archive/` or delete it if it no longer adds resume value.
- Keep only active or recently paused plans at the top level of `Plans/`; old execution history should not crowd the default entry surface.

## Minimal templates

### Parent plan template

```md
# <Task Title>

## Status
- State: Active | Blocked | Complete
- Acceptance target:
- Current next step:
- Active blockers:

## Quick Resume
- <current truth>
- <most important verified result>
- <next useful restart point>

## Verified Artifacts
- <path>: <why this is the first file or artifact to open>
- <path>: <why this is the second file or artifact to open>

## Child docs
- [ ] <work package>: <one-line summary> (<path>)
- [ ] <active bug sheet>: <one-line summary> (<path>)

## Work packages
1. [ ] <work package>
2. [ ] <work package>
3. [ ] <work package>

## Progress notes
- YYYY-MM-DD: <short update>
```

### Child work-package template

```md
# <Work Package Title>

## Goal

## Current status
- State:
- Current next step:
- Blockers:

## Decisions
- YYYY-MM-DD: <decision and why>

## Artifacts
- <path>: <why it matters>

## Progress notes
- YYYY-MM-DD: <what changed>
```

### Bug sheet template

```md
# <Bug Title>

## Symptom

## Reproduction or failing test

## Active hypotheses
- [ ] <hypothesis>
- [ ] <hypothesis>

## Experiments
- YYYY-MM-DD: <experiment> -> <result>

## Evidence
- <artifact path>: <summary>

## Failed hypotheses
- <hypothesis>: <why it was ruled out>

## Current conclusion

## Next step
```