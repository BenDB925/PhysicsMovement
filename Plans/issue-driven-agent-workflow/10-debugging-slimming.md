# DEBUGGING Slimming Slice

## Goal

Slim `DEBUGGING.md` into a compact debugging playbook and pattern index aligned with `.github/skills/debugging-workflow/SKILL.md`, keeping durable rules and repo-specific failure patterns while moving repeated process language out of the default read path.

## Current status
- State: Complete
- Current next step: Start the follow-on active-history migration slice, with `AGENT_MOVEMENT_MIGRATION_PLAN.md` as the leading candidate unless priorities change.
- Blockers: None currently.

## Decisions
- 2026-03-18: Keep this slice narrow. Rewrite `DEBUGGING.md` itself, keep the root doc as a compact playbook and pattern index, and do not widen into unrelated documentation cleanup.
- 2026-03-18: Align the root doc with `.github/skills/debugging-workflow/SKILL.md` so the skill owns the longer procedural loop while `DEBUGGING.md` keeps the quick-load rules, task-record contract, and recurring repo patterns.
- 2026-03-18: Keep the repo-specific failure patterns that still have resume value, but compress the repeated checklists and workflow prose so the root doc stays near the audit target.

## Artifacts
- `DEBUGGING.md`: slimmed debugging playbook and pattern index.
- `.github/skills/debugging-workflow/SKILL.md`: debugging skill that now owns the fuller reusable procedure for investigation passes.
- `Plans/issue-driven-agent-workflow.plan.md`: parent plan tracking the workflow migration and next restart point.
- `https://github.com/BenDB925/PhysicsMovement/issues/1`: parent PRD for the documentation-slimming pilot.
- `https://github.com/BenDB925/PhysicsMovement/issues/7`: live slice issue for the `DEBUGGING.md` slimming pass.

## Launched issues

- PRD #1: `https://github.com/BenDB925/PhysicsMovement/issues/1`
- Slice #7: `https://github.com/BenDB925/PhysicsMovement/issues/7`
- The slice is intentionally AFK, labeled `status:ready`, and points back to this child doc plus the parent plan instead of carrying detailed execution history in the issue body.

## Progress notes
- 2026-03-18: Launched `https://github.com/BenDB925/PhysicsMovement/issues/7` as the live AFK slice for this documentation pass, then updated the child doc and parent plan so the repo resume surface matches the remote queue state.
- 2026-03-18: Rewrote `DEBUGGING.md` into a shorter Quick Load / Read More / playbook shape, aligned it with `.github/skills/debugging-workflow/SKILL.md`, and verified the updated skill and plan references after the rewrite.
- 2026-03-18: User asked to handle the literal next plan step plus execute the `DEBUGGING.md` slimming slice in the same pass, preferred a normal repo-style commit message, and requested a closeout commit for the completed step.