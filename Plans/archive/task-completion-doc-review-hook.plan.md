# Archived on 2026-03-18 during the legacy-plan cleanup pass. The hook shipped and no live plan tracking is needed unless the behavior changes again.

# Task Completion Documentation Review Hook

## Status
- State: Archived (hook shipped)
- Acceptance target: Add a hook that notices user task-complete confirmations and forces a documentation and task-record review before the agent finishes.
- Current next step: None.
- Active blockers: None.

## Child docs
- None yet.

## Work packages
1. [x] Define the documentation review workflow and task record tree.
2. [x] Add completion-detection hook automation.
3. [x] Document the hook behavior for future agents.
4. [x] Validate the hook behavior with sample inputs.

## Progress notes
- 2026-03-12: Created the parent record for the completion-review hook task so the customization work has a canonical status entry point.
- 2026-03-12: Added a UserPromptSubmit plus Stop hook pair that records completion confirmations and blocks the first stop so the agent performs a documentation review pass.
- 2026-03-12: Validated the hook end-to-end with sample UserPromptSubmit and Stop payloads, including the one-time block and state cleanup behavior.