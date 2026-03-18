---
name: issue-writing-workflow
description: "Use when drafting or updating GitHub PRDs, refactor RFCs, slice issues, or bug escalations for PhysicsDrivenMovementDemo, especially when you need the correct `type:*`, `mode:*`, `status:*`, and `area:*` labels, one-issue-one-slice scope, and links back to `Plans/` docs."
argument-hint: "Describe the issue type, goal or symptom, acceptance target, linked plan path, parent issue link if any, and whether the slice is AFK or HITL."
user-invocable: true
disable-model-invocation: false
---

# Issue Writing Workflow

Use this skill to create thin GitHub issues that point to the real execution record in `Plans/`, instead of turning the issue body into a long plan.

## Gather these inputs first

- If the issue shape, scope, parent link, label choice, or ownership surface is not already explicit, use `vscode_askQuestions` before drafting instead of inferring silently.
- issue type: PRD, refactor RFC, slice, or bug escalation
- goal or visible symptom
- acceptance target
- local plan path
- parent issue link if this is a slice or bug issue
- child doc or bug-sheet path if this issue owns detailed execution
- execution mode if this is a slice: AFK or HITL
- blockers or dependencies
- smallest useful `area:*` label set

## Core rules

1. One issue should map to one durable purpose. Do not combine multiple slices, multiple unresolved bugs, or multiple parent goals into one issue.
2. The linked local plan doc is the canonical resume surface. The issue body is the queue, ownership, and handoff summary.
3. Slice issues must link to exactly one parent issue and exactly one local plan or child doc.
4. Bug issues must link back to exactly one parent slice issue and one local bug sheet.
5. Keep issue bodies short. If the body starts accumulating attempts, logs, or historical notes, move that detail into the linked plan doc or bug sheet.
6. Use exactly one `type:*` label. Slice issues also need exactly one `mode:*` label after creation.
7. Use `mode:hitl` only when the work is blocked on an explicit user choice, approval, permission, or missing external input.
8. Before most planning or issue-drafting work, use `vscode_askQuestions` to confirm user preferences or missing issue-shape details unless the task is trivial or the request is already fully explicit.

## Procedure

1. Clarify the issue shape.
   - Use `vscode_askQuestions` when the user has not already made the scope, issue type, parent link, mode, or labeling preference explicit.
   - Prefer short plain-English questions over silent assumptions.
2. Verify the local record exists.
   - Reuse the active parent plan when it already exists.
   - Create or update a child doc or bug sheet before the issue grows detailed execution notes.
3. Pick the correct issue shape.
   - PRD: parent initiative with goal, acceptance target, non-goals, local parent plan path, and planned slice list.
   - Refactor RFC: parent architecture or ownership cleanup with the same parent fields plus the design problem and proposed direction.
   - Slice: one narrow AFK or HITL execution step with one parent issue, one acceptance target, one local plan path, one owning child doc, blockers, and definition of done.
   - Bug escalation: one narrow unresolved symptom with parent slice link, best failing test or focused repro, failed hypotheses, next best hypothesis, and one local bug-sheet path.
4. Apply labels deliberately.
   - Always apply exactly one `type:*` label.
   - For slices, apply exactly one `mode:*` label and then the appropriate `status:*` label.
   - Add the smallest useful `area:*` label set; do not widen taxonomy casually.
5. Keep the issue body queue-friendly.
   - Summarize the latest truth.
   - Link the local plan artifact that holds the detail.
   - Do not paste long test logs, telemetry, or multi-attempt histories into the issue.
6. Close the loop.
   - If the user asked for drafting only, stop at a ready-to-post issue body.
   - If the user asked to create or update the issue, use GitHub tooling and keep the parent plan in sync in the same slice.

## Template contract

- PRD or refactor: goal, acceptance target, non-goals, local parent plan path, planned slices or checkpoints.
- Slice: parent issue, execution mode, acceptance target, local parent plan path, owning child doc, blockers, definition of done.
- Bug escalation: parent slice issue, visible symptom, best failing test or repro, failed hypotheses so far, next best hypothesis, local bug-sheet path.

## Repo guidance

- Use `.github/ISSUE_TEMPLATE/` as the starting structure instead of freehand issue bodies.
- Follow `Plans/README.md` for parent-plan, child-doc, and bug-sheet structure.
- Keep the active parent plan current when issue scope, blockers, or the next step changes.