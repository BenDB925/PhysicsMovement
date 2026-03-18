# DEBUGGING.md — PhysicsDrivenMovementDemo Debugging Playbook

*Living document. Keep reusable debugging rules and repo patterns here. Put active attempt history in `Plans/` child docs or bug sheets.*

## Quick Load

- If the game is visibly wrong and tests are green, write or strengthen the failing outcome-based test first.
- If the symptom is not clear enough to test yet, add targeted logging before changing code.
- Keep the nearest task plan or roadmap chapter current, and split to a bug sheet once hypotheses, raw logs, or telemetry start to sprawl.
- Start with the player-visible outcome, then isolate one subsystem at a time until you find the first wrong layer.

## Read More When

- Use `.github/skills/debugging-workflow/SKILL.md` for the full investigation loop and task-record expectations.
- Use `AGENT_TEST_RUNNING.md` when you need unattended Unity test commands, result artifacts, or rerun guidance.
- Use `Plans/README.md` when the investigation needs a child doc or bug sheet.
- Use the repo patterns below when the symptom matches a known failure shape.

---

## Core Rules

1. Write or strengthen the failing test before code changes when the symptom is clear enough to test.
2. Prefer outcome-based assertions over internal flags, state transitions, or method-call checks.
3. Add only enough logging to separate the leading explanations, and sample every few fixed frames instead of every frame.
4. Fix the first layer where behavior becomes wrong instead of patching a downstream symptom.
5. Keep the stronger regression coverage after the fix and add reusable lessons back here.

## Investigation Record

- Treat the nearest task plan, roadmap chapter, or child doc as the canonical record.
- If none exists and the work will involve repeated experiments, create one under `Plans/` or the user-specified folder.
- Move the work into a linked bug sheet once raw logs, telemetry, or multiple hypotheses start cluttering the parent record.
- Store failed hypotheses and artifact paths in the local record, not in long-lived root docs or GitHub issue bodies.

## Default Loop

1. **Observe the symptom**
	- Reproduce it in the fastest trustworthy path: focused test, scene, or both.
	- Describe what the player would see, when it happens, and the conditions that trigger it.
2. **Decide whether the symptom is clear enough to test**
	- If yes, write the failing outcome-based test.
	- If no, instrument the run until the failure is specific enough to test.
3. **Write the strongest failing test you can**
	- Start with the broad player-visible outcome.
	- Add one or two narrower outcome assertions only when they help localize the broken layer.
4. **Isolate the layer**
	- Disable or bypass one suspect subsystem at a time.
	- Identify the first boundary where behavior diverges.
5. **Fix, verify, capture**
	- Fix the narrowest correct layer, rerun the focused regression slice, then widen only if the blast radius justifies it.
	- Keep the stronger test and add any reusable pattern back here.

## Keep / Remove Checks

Keep a new test when it:

- would have failed on the real bug
- measures displacement, stability, timing, contact, posture, or another external result
- runs long enough for the behavior to emerge
- uses thresholds tight enough to catch regressions without becoming flaky

Avoid or remove a test when it only proves:

- an internal flag flipped
- a state transition happened
- a setter, helper, or torque assignment ran
- an implementation detail changed without showing a player-visible effect

Keep logging when it:

- compares two to four quantities that separate the leading explanations
- includes frame or time plus state or phase
- can be gated cleanly behind a debug flag or define

Remove or disable logging when:

- it no longer changes the decision
- it prints every frame without separating hypotheses
- the same evidence is already covered by the regression test or task record

## Current Repo Patterns

### Tests pass but the game still looks wrong
- Most likely issue: the test proves intent or input application, not the gameplay outcome.
- Best next step: add the player-visible outcome assertion first, then one lower-level outcome check only if you need localization.

### Whole-system test fails but does not diagnose enough
- Most likely issue: the suite needs a smaller outcome discriminator, not a pivot to internal-field assertions.
- Best next step: keep the broad check and add one or two outcome assertions one layer down, such as distance plus limb clearance or recovery time plus tilt.

### You cannot tell what is wrong well enough to write the test
- Most likely issue: the failure mode is still ambiguous.
- Best next step: log position, height, tilt, velocity, grounded or contact state, authored state, or support progress until one hypothesis clearly wins.

### Landing smoothing never engages after a jump
- Most likely issue: authored airborne state and raw foot-ground edges diverge across the landing frames.
- Best next step: write a seam test around `IsGrounded` returning while the authored state still reports `Airborne`, then compare the exact landing-frame transitions before retuning damping.

### Recovery stays pinned longer than the visible disturbance
- Most likely issue: the runtime keeps re-entering the same recovery and resetting the timer from near-identical severity samples.
- Best next step: add a contract test around the recovery-state container and require a stronger same-situation signal before refreshing the active window.

### Step-up planning says yes, but the body still stalls
- Most likely issue: sensing and planning are no longer the problem; execution or support transfer is.
- Best next step: in the same failing outcome test, compare planned touchdown progress against the best grounded support height actually achieved before changing planner carry again.

### A feature only works when another system is disabled
- Most likely issue: the bug is at the integration boundary.
- Best next step: keep the real stack active in the regression test and diagnose the ownership or ordering conflict.

### A PlayMode fixture passes alone but degrades in a larger slice
- Most likely issue: leaked global physics state or scene bleed from a prior fixture.
- Best next step: restore global physics settings in setup or teardown and move prefab-backed fixtures into a fresh runtime-created active scene before instantiation.

*Living document. Add new reusable tactics only after they prove themselves more than once.*
