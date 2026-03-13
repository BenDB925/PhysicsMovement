# DEBUGGING.md — PhysicsDrivenMovementDemo Debugging Playbook

*Living document. Update this file whenever a debugging workflow, test pattern, logging tactic, or bug pattern proves useful more than once.*

## Quick Load

- If the game is visibly wrong and tests are green, write the failing outcome-based test first; if the failure mode is still unclear, add targeted logging before changing code.
- Keep the nearest task plan or roadmap chapter as the parent record, and split into a bug sheet once hypotheses, raw logs, or telemetry start to sprawl.
- Start with the broadest trustworthy outcome check, then add narrower outcome assertions only when they help localize the broken layer.
- Isolate one subsystem at a time and fix the first layer where behavior becomes wrong rather than patching a downstream symptom.

## Read More When

- Continue into the default debugging workflow when a regression needs a fresh end-to-end investigation path.
- Continue into the repo pattern sections when the symptom looks like green-tests/wrong-game, mixed-slice contamination, or another recurring failure shape.
- Continue into the checklists when you are adding new regression tests or instrumentation and need the keep/remove criteria.

---

## Core Rules

1. **If behavior is broken and tests are green, write the failing test first.** A bug that players can see should leave behind a test that can fail for the same reason.
2. **Prefer outcome-based tests over implementation-based tests.** Test what the system does in the world, not whether an internal field, setter, or helper method changed.
3. **If you cannot yet write the right failing test, add logging before guessing.** Unknown failure modes need evidence.
4. **Start broad, then drill down.** First prove the whole feature is broken. Then add smaller outcome checks only if they help localize the fault.
5. **Isolate one system at a time.** Disable or bypass subsystems temporarily to find the boundary where the bug appears.
6. **Record what worked.** When a new trick or pattern pays off, add it here so the next debugging pass starts higher.

---

## Investigation Record Rule

- Treat the nearest task plan or chapter doc as the parent record for the investigation.
- If no parent record exists and the debugging task is more than a one-shot check, create one under `Plans/` or the user-specified folder before the notes sprawl.
- Keep the parent record current with the active symptom, current next step, blockers, and links to any child work-package docs or bug sheets.
- Create a dedicated bug sheet once the investigation crosses the rollover thresholds in `Plans/README.md`, or whenever raw logs or telemetry would clutter the parent record.
- Put failed hypotheses and experiment outcomes in the bug sheet so future agents do not retry them blindly.

---

## Default Debugging Workflow

### Step 1 - Observe the symptom
Before changing code:
- Reproduce the issue in the fastest trustworthy path: scene, focused test, or both.
- Describe the visible behavior precisely: what is wrong, when it happens, and what the player would notice.
- If the issue looks like tuning rather than logic, try live Inspector changes first. If tuning fixes it, update the defaults in code and note the new baseline here.

### Step 2 - Decide whether you can already test it
- **If yes:** write or strengthen a failing test before changing code.
- **If no:** add targeted logging or instrumentation until the failure is clear enough to test.

### Step 3 - Write the right failing test
- Start with the broadest trustworthy outcome assertion you can support.
- Prefer end-to-end or full-loop tests when multiple runtime systems interact.
- If the broad test fails but does not localize the bug, add one or two narrower **outcome** assertions beneath it.
- Avoid tests that only assert internal intent, such as target rotations, flags, or setter calls, unless there is no measurable external effect.

Examples:
- **Strong:** "character travels at least X meters in 10 seconds"
- **Stronger diagnostic pair:** "character moves forward" plus "lower leg clears Y meters off the floor at least once"
- **Weak:** "LegAnimator wrote targetRotation Z"
- **Strong:** "character regains upright pose within N frames"
- **Weak:** "CharacterState entered GettingUp"

### Step 4 - Add logging when the cause is unclear
- Log real runtime quantities that separate the leading hypotheses.
- Include frame count, time, current state, or phase in each line.
- Sample every 5-10 FixedUpdate frames, not every frame.
- Compare a good run to a bad run when possible.
- Use gated logging (`DEBUG_*`, `[SerializeField] bool`, temporary defines) so it can be disabled cleanly after the investigation.

### Step 5 - Isolate the layer
- Temporarily disable or bypass one suspect subsystem at a time.
- Ask which layer should absorb the fix: input, state machine, balance, gait, collision, environment data, or tests.
- If the issue disappears only when a neighboring system is removed, the bug is likely at the boundary between them.

### Step 6 - Fix the narrowest correct layer
- Fix the first layer where the behavior becomes wrong.
- Do not patch symptoms downstream if the source is clearly upstream.
- If the fix is "just a better default," keep the stronger regression test anyway.

### Step 7 - Verify and capture the learning
- Run focused verification first, then widen coverage if the change crosses system boundaries.
- Keep outcome-based assertions in place after the fix; they are the long-term protection.
- When the investigation reveals a reusable tactic, threshold, or failure pattern, update this document and any relevant instructions.

---

## Test Design Checklist

Before keeping a new test, ask:

- [ ] Would this test have failed on the real bug I just saw?
- [ ] Am I asserting an outcome in world space or over time, not just an implementation detail?
- [ ] If multiple systems interact here, am I exercising the real stack rather than a convenient seam?
- [ ] Have I simulated enough frames or seconds for the behavior to appear?
- [ ] Is the threshold tight enough to catch regressions but loose enough to avoid flakiness?
- [ ] If I added a narrower diagnostic assertion, is there still a broader whole-system assertion above it?

---

## Logging Checklist

Before adding logs, ask:

- [ ] Which competing explanations am I trying to separate?
- [ ] Which 2-4 numbers will actually distinguish them?
- [ ] Am I logging infrequently enough to keep the output readable?
- [ ] Can I compare against a known-good run?
- [ ] Is the logging gated so it can stay in the codebase safely or be removed cleanly?

---

## Current Repo Patterns

### "Tests pass but the game still looks wrong"
**Most likely issue:** the tests are proving that inputs were applied, not that the feature actually worked.

**Fix:** add an outcome-based test at the level the player would notice first, then add one lower-level outcome assertion only if you need localization.

### "Whole-system test is too broad to diagnose"
**Best next step:** keep the broad outcome assertion, then add one or two smaller outcome checks one layer down.

Examples:
- movement distance + limb clearance
- recovery time + hips tilt
- jump apex + grounded transition

### "I cannot tell what is wrong well enough to write the test"
**Best next step:** add targeted logging before changing code.

Useful physics-style signals:
- position / height
- tilt / angle error
- velocity / angular velocity
- grounded or contact state
- state machine state
- forces / torques / drive strength
- progress through a course or task

### "A feature only works when another system is disabled"
**Most likely issue:** the bug lives at the integration boundary, not fully inside either system in isolation.

**Fix:** keep the real stack active in the regression test and diagnose the ownership or ordering conflict.

### "A PlayMode fixture passes alone but degrades inside a multi-fixture slice"
**Most likely issue:** a previous test leaked global Unity physics state such as the layer-collision matrix, fixed timestep, or solver settings, or left the next prefab-backed fixture instantiating into an authored scene that it did not expect.

**Fix:** save and restore global physics settings in `SetUp`/`TearDown`, and for prefab-backed PlayMode fixtures switch the active test world to a fresh runtime scene via `SceneManager.CreateScene(...)` plus `SceneManager.SetActiveScene(...)` before instantiating the rig. Do not use `EditorSceneManager.NewScene(...)` during PlayMode setup. If the remaining red is a locomotion outcome assertion, prefer hips-relative limb lead over raw world-position crossing so whole-body translation does not dominate the metric.

---

## Repo-Specific Examples

### Walking / locomotion
Prefer:
- distance travelled in a time window
- whether the character stays upright while moving
- whether limbs clear a minimum threshold during gait

Avoid as the only assertion:
- exact `targetRotation` values
- exact phase accumulator values
- whether a movement method was called

### Recovery / balance
Prefer:
- upright recovery within N frames
- hips or torso tilt below a threshold after stabilization
- standing height or grounded state regained

Avoid as the only assertion:
- torque was applied
- a state transition fired

### Jump / airborne behavior
Prefer:
- leaves the ground
- reaches an apex
- returns to grounded state in a stable posture

Avoid as the only assertion:
- jump input flag consumed
- impulse method invoked

---

*This is a living document. When a clever debugging approach, log shape, or stronger regression test proves itself, add it here.*
