# AGENT_TEST_RUNNING Slimming Slice

## Goal

Slim `AGENT_TEST_RUNNING.md` into a thin, repo-primary guide that defaults to `Tools/Run-UnityTests.ps1` and demotes raw Unity CLI detail out of the normal read path.

## Current status
- State: Complete
- Current next step: Use PRD #1 as the parent for the next documentation-slimming slice, with `CODING_STANDARDS.md` now the leading candidate unless priorities change.
- Blockers: None currently.

## Decisions
- 2026-03-18: Keep this slice narrow. Update `AGENT_TEST_RUNNING.md` itself, record the slice in a dedicated child doc, and do not widen into `CODING_STANDARDS.md` or additional documentation cleanup unless a link would otherwise become incorrect.
- 2026-03-18: Rewrite `AGENT_TEST_RUNNING.md` around the repo script, focused `-TestFilter` usage, exit codes, artifact interpretation, and short troubleshooting. Remove the long raw CLI walkthrough and XML schema appendix from the default path.
- 2026-03-18: Keep the infrequent fallback as a short in-file section instead of creating another top-level appendix, so the always-read surface gets smaller instead of shifting sideways.

## Artifacts
- `AGENT_TEST_RUNNING.md`: target doc for the slimming pass.
- `Plans/issue-driven-agent-workflow.plan.md`: parent plan that tracks the documentation-slimming pilot.
- `https://github.com/BenDB925/PhysicsMovement/issues/1`: parent PRD for the documentation-slimming pilot.
- `https://github.com/BenDB925/PhysicsMovement/issues/3`: live slice issue for the `AGENT_TEST_RUNNING.md` slimming pass.

## Launched issues

- PRD #1: `https://github.com/BenDB925/PhysicsMovement/issues/1`
- Slice #3: `https://github.com/BenDB925/PhysicsMovement/issues/3`
- The slice is intentionally AFK, labeled `status:ready`, and points back to this child doc plus the parent plan instead of carrying detailed execution history in the issue body.

## Progress notes
- 2026-03-18: Launched `https://github.com/BenDB925/PhysicsMovement/issues/3` as the live AFK slice issue for this documentation pass, then updated the child doc and parent plan so the repo resume surface matches the remote queue state.
- 2026-03-18: User chose `AGENT_TEST_RUNNING.md` as the next documentation-slimming slice, requested remote GitHub launch if auth works, and asked for a commit at closeout.
- 2026-03-18: Verified the live `Tools/Run-UnityTests.ps1` flags plus the summary and parse wrapper entry points so the slimmed guide matches the actual unattended path and artifact layout.
- 2026-03-18: Rewrote `AGENT_TEST_RUNNING.md` around the repo-primary unattended path and left only a short manual fallback section.