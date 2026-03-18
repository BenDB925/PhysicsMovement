# CODING_STANDARDS Slimming Slice

## Goal

Slim `CODING_STANDARDS.md` from a tutorial-style mandatory read into a tighter rulebook that keeps the Phase A-F workflow, self-review gate, documentation contract, and behavior-changing guardrails while routing low-frequency procedure elsewhere.

## Current status
- State: Complete
- Current next step: Use PRD #1 as the parent for the next documentation-slimming slice, with `LOCOMOTION_BASELINES.md` now the leading candidate unless priorities change.
- Blockers: None currently.

## Decisions
- 2026-03-18: Keep this slice narrow. Update `CODING_STANDARDS.md` itself plus the linked workflow records, and do not widen into unrelated durable docs unless a cross-reference would otherwise become incorrect.
- 2026-03-18: Add `Quick Load` and `Read More When` sections so agents can stop after the minimum rule surface on routine tasks.
- 2026-03-18: Keep the section numbering and the Section 7 documentation-layer headings stable where other durable docs already point back into this file.
- 2026-03-18: Route detailed test-running mechanics to `AGENT_TEST_RUNNING.md` and keep only the testing rules that change implementation or verification behavior in this file.

## Artifacts
- `CODING_STANDARDS.md`: target doc for the slimming pass.
- `Plans/issue-driven-agent-workflow.plan.md`: parent plan that tracks the documentation-slimming workflow migration.
- `https://github.com/BenDB925/PhysicsMovement/issues/1`: parent PRD for the documentation-slimming pilot.
- `https://github.com/BenDB925/PhysicsMovement/issues/4`: live slice issue for the `CODING_STANDARDS.md` slimming pass.

## Launched issues

- PRD #1: `https://github.com/BenDB925/PhysicsMovement/issues/1`
- Slice #4: `https://github.com/BenDB925/PhysicsMovement/issues/4`
- The slice is intentionally AFK, labeled `status:ready`, and points back to this child doc plus the parent plan instead of carrying detailed execution history in the issue body.

## Progress notes
- 2026-03-18: Launched `https://github.com/BenDB925/PhysicsMovement/issues/4` as the live AFK slice for this documentation pass, then closed retry duplicate `#5` so the remote queue keeps one canonical slice issue.
- 2026-03-18: Rewrote `CODING_STANDARDS.md` into a slimmer rulebook with `Quick Load` and `Read More When`, kept the Phase A-F workflow and documentation-layer contract, and removed the heavyweight tutorial duplication that the audit called out.
- 2026-03-18: User chose `CODING_STANDARDS.md` as the next documentation-slimming slice, requested remote GitHub launch if access works, asked to execute the slice in the same pass, and requested a closeout commit for the completed subtask.