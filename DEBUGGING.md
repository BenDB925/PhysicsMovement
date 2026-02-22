# DEBUGGING.md — Physics Character Debugging Playbook

*Hard-won lessons from building a ragdoll brawler. Read this before spinning up agents.*

---

## The Golden Rules

1. **Visual first, agent second.** Open the editor and look before writing code. Half the bugs are tuning, not logic.
2. **Outcome-based tests catch real bugs. Input-based tests don't.** "targetRotation was set" is not the same as "the leg moved."
3. **One system at a time.** Disable components to isolate. Is it the legs? Is it the balance? Is it both fighting?
4. **Real runtime data beats guessing.** If you've sent two agents and it's still broken, add logging and look at the numbers.

---

## Step-by-Step Debugging Protocol

### Step 1 — Look first
Before writing any code or sending any agent:
- Open the scene in Play Mode
- Move the character around
- Describe exactly what you see: which body part, which direction, what phase of movement

**Good:** "lower legs drag on the floor when moving, but first step looks correct"
**Bad:** "walking looks wrong"

### Step 2 — Try the Inspector
Many bugs are just wrong default values. Try live tuning before spinning up an agent:
- **Lean too much?** Try `BalanceController → P: 2000` (default was too low)
- **Legs not lifting?** Try `LegAnimator → Knee Angle: 55°`, `Step Angle: 50°`
- **Legs cycling wrong speed?** Try `Step Frequency Scale: 1.5–2.5`
- **Steps too snappy/floaty?** Try `Idle Blend Speed: 3–8`

If tuning fixes it → note the values and update the defaults in code. No agent needed.
If tuning doesn't fix it → it's a code bug. Move to Step 3.

### Step 3 — Isolate the system
Disable components one at a time to find the culprit:
- **Disable LegAnimator** — does the lean/issue go away? If yes: LegAnimator is causing it.
- **Disable BalanceController** — do the legs move correctly? If yes: BC is fighting LA.
- **Disable PlayerMovement** — does the character stand upright at rest? If no: BC upright gain is too weak.

The two systems most likely to fight each other: **BalanceController vs LegAnimator** (they both touch leg joints — BC must defer to LA).

### Step 4 — Add debug logging
If you can't figure it out visually, ask an agent to add a `DEBUG_GAIT` define or a `[SerializeField] bool _debugLog` flag, then log:

**For leg problems:**
```
LowerLeg_L world Y: {pos.y:F3}  |  gaitForward: {gaitForward}  |  cyclesPerSec: {rate:F2}
```

**For lean/balance problems:**
```
Hips pitch: {angle:F1}°  |  PD torque: {torque.magnitude:F0} Nm  |  velocity: {vel.magnitude:F2} m/s
```

Log every 10 FixedUpdate frames (not every frame — too spammy). Output goes to `Logs/debug_gait.txt` or just the Unity Console.

**Then:** Run the game, reproduce the bug, paste the log or share the file. Real numbers will tell you exactly what's happening.

### Step 5 — Write a failing test BEFORE the fix
Once you know what's broken, write a test that fails because of the bug:
- The test must use the **full component stack** (RagdollSetup → BalanceController → PlayerMovement → CharacterState → LegAnimator)
- BalanceController must be **fully active** — don't disable it via test seams, that hides the real problem
- Assert an **outcome** (world position, angular displacement) not an input (targetRotation value)
- If the test passes with the bug still present → the test is too weak, tighten the threshold

### Step 6 — Send the agent
Now write the agent brief with:
- Exact root cause (from Steps 1–4)
- Specific files to read (CODING_STANDARDS.md + .copilot-instructions.md first, always)
- The failing test as the acceptance criterion
- Commit message format
- `--timeout-seconds 1800` for anything touching PlayMode tests

---

## Known Bug Patterns

### "Legs dragging / not lifting"
**Most likely causes (in order):**
1. BalanceController overwriting leg joint SLERP drives every frame → fix: `_deferLegJointsToAnimator = true`
2. Wrong rotation axis — `Euler(x,0,0)` when joint needs Z axis (check `joint.axis` in RagdollSetup)
3. Spring too weak to beat gravity → lower leg spring needs ~1200 Nm/rad minimum
4. `RotationDriveMode` not set to Slerp — `targetRotation` only works with Slerp mode
5. Lower legs catching on the floor collider → disable lower leg/ground layer collision

**Quick test:** Select LowerLeg_L in hierarchy during Play Mode. Watch the local rotation in the Inspector. Is it changing? If yes → spring issue. If no → the targetRotation isn't being set or BC is overriding it.

### "Too much forward lean during movement"
**Most likely causes:**
1. `BalanceController._kP` too low — needs ~2000 to counteract move force (default 800 was insufficient)
2. Move force too high relative to upright correction — try `PlayerMovement.MoveForce: 150–200`
3. BC and LA fighting → leg forces destabilising the torso

**Quick test:** Set `P = 2000`, `Move Force = 150` in Inspector. If lean reduces significantly → just wrong defaults.

### "Tap dancing / legs cycling with no forward movement"
**Cause:** Step frequency was fixed (not velocity-scaled) — legs cycled at constant rate regardless of actual speed.
**Fix:** Use velocity-scaled gait — `effectiveCycles = max(minFreq, speed × _stepFrequencyScale)`.

### "Correct first step, then feet get left behind"
**Cause:** Body acceleration outpaces gait cycle. Fix: velocity-scaled frequency (above) or reduce Move Force.

### "Tests pass but visually broken"
**Cause:** Tests are isolating systems that fight each other in reality. 
**Fix:** Full-stack tests with BC fully active. If the test only passes with BC disabled → the fix is incomplete.

---

## Test Quality Checklist

Before committing any new test, ask:

- [ ] Would this test have caught the bug that prompted it?
- [ ] Is BC fully active during this test (not disabled via seam)?
- [ ] Am I asserting a world-space outcome (position, angle) not just a setter call?
- [ ] Have I simulated enough frames? (80+ for gait tests at 100Hz)
- [ ] Is the threshold tight enough to catch a regression but loose enough to not be flaky?

**The dragging-feet test:** `LowerLeg world Y > spawn Y + 0.05m at some point in 80 frames` — this is the canonical example. A broken joint scores ~0m, a working one scores 0.1–0.2m.

---

## Agent Brief Template

```
MANDATORY FIRST STEPS: Read CODING_STANDARDS.md, then .copilot-instructions.md, then AGENT_TEST_RUNNING.md, then [relevant scripts].

ROOT CAUSE: [exact diagnosis from debugging steps above]

FIX: [specific change — file, method, what to change]

ACCEPTANCE CRITERION: [the failing test that must now pass, with BC fully active]

Run tests: powershell -NoProfile -ExecutionPolicy Bypass -File "H:\Work\PhysicsDrivenMovementDemo\Tools\Run-UnityTests.ps1" -ProjectPath "H:\Work\PhysicsDrivenMovementDemo" -Platform PlayMode -MaxAttemptsPerPlatform 2 -Unattended

All [N] existing tests must pass.
Commit: '[description] (N passing)'
Report: [what changed, test count, what to look for visually]
```

---

## Inspector Quick Reference (Good Starting Values)

| Component | Field | Good Default | Notes |
|-----------|-------|-------------|-------|
| BalanceController | P | 2000 | 800 causes lean under move force |
| BalanceController | D | 200 | Keep P/D ratio ~10:1 |
| BalanceController | Defer Leg Joints To Animator | ✅ | Must be on when LegAnimator present |
| LegAnimator | Step Angle | 50° | Upper leg swing amplitude |
| LegAnimator | Knee Angle | 55° | Dial back once walking looks right |
| LegAnimator | Step Frequency Scale | 1.5 | Cycles per m/s |
| LegAnimator | Upper Leg Lift Boost | 15° | Extra upward bias on forward leg |
| LegAnimator | Idle Blend Speed | 5 | Transition speed at stop/start |
| LegAnimator | Use World Space Swing | ✅ | Must be on for correct stepping |
| PlayerMovement | Move Force | 150–200 | Higher values cause lean |
| RagdollSetup | Lower/Upper Leg Spring | 1200 | Minimum to beat gravity |

---

*Created 2026-02-22 after a full day of debugging. Update this file whenever a new bug pattern is solved.*
