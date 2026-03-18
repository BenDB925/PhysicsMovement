# Stuck Issue Debugging Workflow

## Goal

Define a concrete workflow for when an agent cannot quickly solve a problem, so the investigation becomes a durable, fresh-context handoff instead of repeated context burn.

## Current status
- State: Proposed
- Current next step: Convert these rules into repo-local skills, issue templates, and plan-update behavior.
- Blockers: The repo does not yet have bug-specific issue templates, labels, or a dedicated debugging skill that enforces these rules.

## Decisions
- 2026-03-18: A stuck investigation is a workflow transition, not a private chat failure. After 3 failed hypotheses on the same unresolved symptom, the work must split into a dedicated bug issue plus a linked local bug sheet.
- 2026-03-18: The split must always be announced in chat with a short escalation summary.
- 2026-03-18: The local bug sheet is the detailed evidence log; the GitHub issue is the queue, assignment, and fresh-context entry point.
- 2026-03-18: One issue should represent one narrow symptom or one narrow vertical slice by default.
- 2026-03-18: Agents must not silently relax thresholds, acceptance criteria, or behavior expectations. They may propose a change only with a plain-English explanation of the current test, why it is failing, the proposed change, and what guarantee would be weakened or removed.
- 2026-03-18: The parent slice remains open when work is split. The unresolved thread is delegated to the bug issue rather than disappearing inside the parent slice.

## Default flow

1. Start from a bounded slice
   - The agent owns one narrow issue or one narrow symptom.
   - The parent plan and linked issue must already identify the current acceptance target.

2. Investigate with root-cause rules
   - Write or strengthen the best failing outcome-based test when possible.
   - If the failure is not yet testable, add targeted logging.
   - Log rejected hypotheses as they fail so the same idea is not retried blindly.

3. Count failed hypotheses
   - A failed hypothesis means the agent tried a concrete explanation or fix direction and the evidence ruled it out.
   - After 3 failed hypotheses on the same unresolved symptom, the work must split.

4. Split the work cleanly
   - Create a dedicated bug issue.
   - Create or update a local bug sheet under the owning plan.
   - Post a short chat escalation summary.
   - Link the parent slice, the bug issue, and the local bug sheet together.

5. Continue safely
   - The parent slice may remain open if there is unrelated acceptance work that can proceed independently.
   - The unresolved symptom is now owned by the bug issue and must not keep accumulating hidden chat-only history.

## Mandatory handoff package

Every split bug issue plus bug sheet must include:

- Visible symptom statement
- Best failing test or focused reproduction path
- Attempt log with rejected hypotheses
- Next best hypothesis
- Telemetry or artifact links that matter

## Hard rules

### Root-cause first
- Fix the narrowest correct layer.
- Do not change tests just to make them pass.
- Do not treat a green test as proof if the player-visible behavior is still wrong.

### No silent requirement drift
- Never loosen thresholds, acceptance criteria, or behavior guarantees silently.
- If a change is being proposed, explain in plain English:
  - what the current test asserts
  - why the current assertion is failing
  - what the proposed change is
  - what protection would be weakened or lost
- Wait for explicit user approval before making that change.

### No looped hypotheses
- Once a hypothesis is disproven, record it.
- If the same hypothesis fails twice in substance, do not keep retrying it under slightly different wording.
- Split the work instead of burning more context.

## Recommended skill split

### Issue-writing skill
- Purpose: create PRDs, slice issues, and bug issues with the right structure and links
- Inputs: parent PRD or parent slice, relevant local plan path, acceptance target
- Outputs: GitHub issue plus links back to local plan docs

### Debugging skill
- Purpose: investigate one unresolved symptom, maintain the bug sheet, enforce the attempt budget, and decide when to split
- Inputs: symptom, expected behavior, current failing test or reproduction, owning plan path
- Outputs: updated bug sheet, bug issue, escalation summary, next best hypothesis

### Execution skill
- Purpose: implement one bounded slice from issue plus plan context only
- Inputs: one ready issue, linked plan doc, linked artifacts
- Outputs: code change, focused verification, plan update, issue update

## Open implementation note

The user wants explicit notification when the workflow transitions into a stuck-issue path, but not necessarily a full stop on all work. The repo-local workflow should therefore:

- always announce the split in chat
- always create the durable handoff package
- allow the parent slice to stay open when genuinely independent work remains
- avoid pretending the bug was resolved just because the parent issue is still active