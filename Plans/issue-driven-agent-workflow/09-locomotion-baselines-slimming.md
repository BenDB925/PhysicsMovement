# LOCOMOTION_BASELINES Slimming Slice

## Goal

Slim `LOCOMOTION_BASELINES.md` into a current baseline index that keeps only the latest reference snapshot, active known reds, and first-open artifact links while pushing old chapter history out of the root read path.

## Current status
- State: Complete
- Current next step: Use PRD #1 as the parent for the next documentation-slimming slice, with `DEBUGGING.md` now the leading candidate unless priorities change.
- Blockers: None currently.

## Decisions
- 2026-03-18: Keep this slice narrow. Rewrite `LOCOMOTION_BASELINES.md` into a summary-first index, move the detailed historical sections into a linked archive file, and avoid widening into roadmap chapter edits or new baseline-capture tooling.
- 2026-03-18: Treat the 2026-03-17 Chapter 8 regression baseline refresh as the current metric anchor and the 2026-03-17 Chapter 8 completion verification as the current full-gate reference.
- 2026-03-18: Keep the current knockdown behavior expectations in the slim root doc, but demote the longer baseline-history prose and chapter-by-chapter metric tables into the archive.

## Artifacts
- `LOCOMOTION_BASELINES.md`: slimmed current-baseline index for routine regression comparison.
- `Plans/archive/locomotion-baselines-history.md`: archived historical Chapter 1 and Chapter 8 baseline detail moved out of the root read path.
- `Plans/issue-driven-agent-workflow.plan.md`: parent plan tracking the documentation-slimming workflow.
- `https://github.com/BenDB925/PhysicsMovement/issues/1`: parent PRD for the documentation-slimming pilot.
- `https://github.com/BenDB925/PhysicsMovement/issues/6`: live slice issue for the `LOCOMOTION_BASELINES.md` slimming pass.

## Launched issues

- PRD #1: `https://github.com/BenDB925/PhysicsMovement/issues/1`
- Slice #6: `https://github.com/BenDB925/PhysicsMovement/issues/6`
- The slice is intentionally AFK, labeled `status:ready`, and points back to this child doc plus the parent plan instead of carrying detailed baseline history in the issue body.

## Progress notes
- 2026-03-18: Launched `https://github.com/BenDB925/PhysicsMovement/issues/6` as the live AFK slice for this documentation pass, then updated the child doc and parent plan so the repo resume surface matches the remote queue state.
- 2026-03-18: Rewrote `LOCOMOTION_BASELINES.md` into a current-baseline index, moved the detailed Chapter 1 and Chapter 8 history into `Plans/archive/locomotion-baselines-history.md`, and verified the archive link, artifact paths, and live issue references after the rewrite.
- 2026-03-18: User chose `LOCOMOTION_BASELINES.md` as the next documentation-slimming slice, requested the full slice in one pass, preferred continuing locally if remote launch failed, and asked for a closeout commit.
