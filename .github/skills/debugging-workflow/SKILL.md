---
name: debugging-workflow
description: "Use when debugging regressions or visibly broken behavior in PhysicsDrivenMovementDemo, especially when tests are passing but the game is still wrong, when you need to write failing outcome-based tests, add targeted logging, isolate subsystems, or turn a one-off investigation into a reusable debugging rule."
argument-hint: "Describe the symptom, expected behavior, what currently passes, and any logs or tests you already have."
user-invocable: true
disable-model-invocation: false
---

# Debugging Workflow

Use this skill when a feature is broken, flaky, visually wrong, or under-tested and you need a repeatable way to investigate it.

## Core Rules

1. If the behavior is wrong and tests are green, write or strengthen a test that fails for the observed symptom before changing code.
2. Prefer outcome-based tests over implementation-based tests. Measure what the system actually does in the world or over time.
3. If you cannot yet write the right failing test, add targeted logging before guessing.
4. Start with the whole feature the player experiences, then drill down one layer at a time.
5. Isolate one subsystem at a time so you can find the boundary where behavior diverges.
6. When a useful debugging pattern proves itself, update the repo root `DEBUGGING.md` so the process improves over time.
7. Keep the parent task record current. If the investigation gets long, move the detail into a linked bug sheet instead of letting the parent plan or chapter sprawl.

## Procedure

1. Set up the task record.
   - Find the parent plan or chapter doc for the work and treat it as the canonical entry point.
   - If none exists, create one under `Plans/` or the user-specified folder following `Plans/README.md`.
   - If the investigation grows past the rollover threshold, create a linked bug sheet and keep the parent summary current.
2. Observe the symptom precisely.
   - Reproduce it in the fastest trustworthy path: scene, focused test, or both.
   - Describe what is wrong in player-visible terms, including timing and conditions.
3. Decide whether the symptom is already clear enough to test.
   - If yes, write the failing test first.
   - If no, add instrumentation until the failure is clear enough to test.
4. Write the strongest failing test you can.
   - Start with an end-to-end or full-loop outcome assertion when multiple systems interact.
   - Measure displacement, recovery time, stability, contact state, progress, or other external results.
   - Only add narrower diagnostic assertions after the broad outcome check exists.
5. Add logging when the cause is unclear.
   - Log real runtime quantities that separate the main hypotheses.
   - Include frame count, time, state, or phase in the log line.
   - Sample every few fixed frames, not every frame.
6. Isolate the layer.
   - Temporarily disable or bypass one suspect subsystem at a time.
   - Identify the first layer where behavior becomes wrong.
7. Fix the narrowest correct layer.
   - Avoid patching downstream symptoms when the root cause is clearly upstream.
8. Verify and record.
   - Run focused verification first, then widen only if the impact is broader.
   - Keep the stronger test.
   - Update `DEBUGGING.md` when the investigation produces a reusable tactic, pattern, or test shape.

## Test Design Rules

- Ask: "Would this test have failed on the real bug?"
- Prefer full-system outcomes first, then lower-level outcome diagnostics if needed.
- Avoid tests that only prove an internal setter, flag, or method call happened.
- Use enough simulated time or frames for the behavior to emerge.
- Keep thresholds strong enough to catch regressions without making the test flaky.

## Quick Examples

- Stronger: character travels at least X meters in 10 seconds.
- Weaker: movement code assigned a target rotation or force value.
- Stronger: character regains upright pose within N frames.
- Weaker: recovery state was entered.
- Stronger diagnostic pair: character moves forward and a lower limb clears a minimum height threshold at least once.

## Logging Rules

- Log only the 2-4 quantities that can separate the leading explanations.
- Compare a bad run to a good run when possible.
- Gate logs behind a define or serialized debug flag when they may need to stay temporarily.
- Remove or disable noisy instrumentation once the bug is understood.

## Repo Guidance

- Read the repo root `DEBUGGING.md` for accumulated examples and known patterns.
- Read `Plans/README.md` when the debugging task needs a new parent plan, child work-package doc, or bug sheet.
- Follow the existing testing guidance when running Unity suites: use `Tools/Run-UnityTests.ps1` and do not run EditMode and PlayMode in parallel.