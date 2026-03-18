---
name: execute-slice
description: "Use when implementing one ready AFK or HITL slice in PhysicsDrivenMovementDemo from bounded issue and plan context only, especially when a fresh agent should be able to execute without loading broad repo history or old chat transcripts."
argument-hint: "Describe the slice issue, linked parent plan path, owning child doc path, acceptance target, blockers, and the smallest relevant verification path."
user-invocable: true
disable-model-invocation: false
---

# Execute Slice

Use this skill to implement one bounded issue from the issue body plus linked plan context, while keeping context load and scope creep under control.

## Entry contract

Before implementation, confirm all of the following:

- one ready slice issue exists
- the issue links to exactly one parent issue
- the issue links to exactly one local parent plan or child doc
- the acceptance target is narrow and concrete
- blockers or dependencies are explicit

If any of those are missing, fix the issue or plan surface first instead of guessing.

Before most non-trivial planning or implementation work, use `vscode_askQuestions` to confirm user preferences, scope boundaries, verification priorities, or tradeoffs unless the task is already fully explicit.

## Context-budget rules

1. Start with the slice issue, the linked child doc, and the parent plan summary.
2. Read only the durable docs that the slice actually needs.
3. Do not load older chat history, broad roadmap chapters, or unrelated baseline history unless the slice proves it needs them.
4. If the slice is documentation work, keep durable docs thin and point to the deeper reference instead of duplicating it.

## Implementation loop

1. Understand the slice.
   - Restate the acceptance target and definition of done in concrete terms.
   - Confirm whether the slice is AFK or HITL and why.
   - Ask short plain-English questions before presuming on approach, scope, or preferred verification when those choices are not already explicit.
2. Follow the repo workflow.
   - Use the Phase A through F loop from `CODING_STANDARDS.md`.
   - Prefer focused verification first.
   - Keep the linked plan doc current as soon as subtasks, blockers, or artifact paths change.
3. Keep scope narrow.
   - Fix the root cause for this slice, not unrelated backlog.
   - If the slice needs another independent acceptance target, spin a new issue instead of widening the current one.
4. Handle blockers honestly.
   - If the work now depends on a user decision or external input, move it to `mode:hitl` and say exactly what is missing.
   - If the same unresolved symptom survives 3 failed hypotheses, stop looping and use the stuck-issue debugging workflow.
5. Close out cleanly.
   - Update the issue summary, parent plan, and child doc in the same slice.
   - Keep only durable outcomes in long-lived docs.

## Verification rules

- Prefer the smallest trustworthy verification path for the touched slice.
- For runtime work, use outcome-based tests over implementation-only checks.
- For documentation or workflow work, verify the links, file paths, label contract, and resume surface instead of treating file creation alone as sufficient.
- Record the verification result where a fresh agent will look first: the child doc and parent plan.

## Repo guidance

- Use `Plans/README.md` as the source of truth for parent-plan, child-doc, and bug-sheet structure.
- Keep GitHub issues as thin queue and handoff surfaces. Do not let them become execution diaries.
- When the slice is complete, make the parent plan's next step obvious for the next agent.