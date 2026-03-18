---
name: stuck-issue-debugging
description: "Use when a slice or regression investigation in PhysicsDrivenMovementDemo is stuck, especially after repeated failed hypotheses, when you need to enforce the three-hypothesis budget, split to a bug issue plus bug sheet, and announce the escalation clearly."
argument-hint: "Describe the symptom, expected behavior, current failing test or repro path, owning plan path, parent slice issue if any, and which hypotheses have already failed."
user-invocable: true
disable-model-invocation: false
---

# Stuck Issue Debugging

Use this skill when an investigation is no longer converging quickly and needs a durable, fresh-context handoff instead of more hidden chat history.

## Hard rules

1. Treat a stuck investigation as a workflow transition, not a private failure.
2. After 3 failed hypotheses on the same unresolved symptom, split to a dedicated bug issue and linked local bug sheet.
3. Always announce the split in chat with a short escalation summary.
4. Never relax thresholds, acceptance criteria, or behavior guarantees silently.
5. Keep the parent slice open only if unrelated acceptance work can still proceed independently.
6. Before most non-trivial investigation or escalation planning, use `vscode_askQuestions` if the expected behavior, symptom boundary, or escalation preference is not already explicit.
7. Update your task record as you go with failed hypotheses, artifact paths, and thoughts on next steps instead of keeping them in your head or in long-lived root docs or GitHub issue bodies.

## Investigation loop

1. Start from the bounded slice or symptom.
   - Confirm the parent issue, acceptance target, and local plan path.
   - Name the visible symptom in user-facing or workflow-facing terms.
   - Use `vscode_askQuestions` early if the expected behavior, repro boundary, or whether independent work may continue is unclear.
2. Strengthen the evidence.
   - Write or improve the strongest failing outcome-based test when possible.
   - If the failure is not yet testable, add targeted logging that separates the leading hypotheses.
3. Count failed hypotheses explicitly.
   - A failed hypothesis is a concrete explanation or fix direction that evidence ruled out.
   - Record each rejected hypothesis in the bug sheet or active child doc as it fails.
4. Fix only the narrowest correct layer.
   - Do not patch downstream symptoms when the root cause is still upstream.
   - Do not change a test just to get green unless the user explicitly approves a requirement change after a plain-English explanation.
5. Split cleanly when the budget is spent.
   - Create the bug sheet if it does not already exist.
   - Create the dedicated bug issue with the parent slice link.
   - Mark the parent slice `status:blocked` if the blocked symptom prevents completion.
   - Post the short escalation summary in chat.

## Mandatory handoff package

- visible symptom statement
- best failing test or focused reproduction path
- rejected-hypothesis log
- next best hypothesis
- artifact or telemetry links worth opening first
- parent slice issue link and local bug-sheet path

## Escalation summary shape

When the investigation splits, say all of the following in one short update:

- what symptom is still unresolved
- how many concrete hypotheses were ruled out
- where the new bug issue and bug sheet live
- whether the parent slice is now blocked or can continue on independent work

## Repo guidance

- Reuse the existing debugging rules in `.github/skills/debugging-workflow/SKILL.md` for test-first and logging-first behavior.
- Keep the parent plan current as soon as the investigation changes shape, not only at closeout.
- Keep long attempt history in the bug sheet under `Plans/`, not in the GitHub issue body.