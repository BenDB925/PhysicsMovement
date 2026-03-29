# Plan 11a — Impact Yield

## Goal

When the character receives a large angular impulse (hit by a cube, sideswiped, etc.),
briefly reduce balance correction so the ragdoll physics can express the impact.
The character should visibly react — stagger, sail, tumble — then recover.

Right now: `UprightStrengthScale` fights impacts immediately. Character tanks everything.
After this: hit lands → correction briefly yields → ragdoll reacts → then recovers.

## Mechanism

In `BalanceController.FixedUpdate`, monitor `_rb.angularVelocity.magnitude` each frame.
When it spikes above threshold: ramp `UprightStrengthScale`, `HeightMaintenanceScale`,
and `StabilizationScale` down to a yield level for a short duration, then ramp back to 1.

All three ramp methods are already public:
- `RampUprightStrength(float targetScale, float duration)`
- `RampHeightMaintenance(float targetScale, float duration)`
- `RampStabilization(float targetScale, float duration)`

## Branch

Create branch `plan/11a-impact-yield` from `master`.

## Change: `BalanceController.cs`

### New serialized fields (add near the surrender/crumple fields):

```csharp
[Header("Impact Yield")]

[SerializeField, Range(1f, 20f)]
[Tooltip("Hips angular velocity (rad/s) that triggers impact yield. "
       + "Normal walking/turning peaks around 1-2 rad/s. "
       + "A solid cube hit produces 4-8+ rad/s.")]
private float _impactYieldAngularVelocityThreshold = 4f;

[SerializeField, Range(0f, 1f)]
[Tooltip("UprightStrength/HeightMaintenance/Stabilization scale during impact yield window. "
       + "0 = fully limp, 1 = no yield. 0.1-0.2 gives visible reaction while still recovering.")]
private float _impactYieldStrengthScale = 0.15f;

[SerializeField, Range(0.05f, 1f)]
[Tooltip("How long (seconds) the yield window lasts after an impact is detected.")]
private float _impactYieldDuration = 0.35f;

[SerializeField, Range(0.1f, 2f)]
[Tooltip("Minimum seconds between impact yield triggers. Prevents rapid re-triggering.")]
private float _impactYieldCooldown = 0.5f;
```

### New private fields:

```csharp
private float _impactYieldCooldownTimer;
private bool _impactYieldActive;
```

### Logic in FixedUpdate (add after the IsGrounded update, before upright torque application):

```csharp
// Impact yield: briefly reduce balance correction when a large angular impulse arrives.
// This lets the ragdoll physics express the hit before recovery kicks back in.
_impactYieldCooldownTimer = Mathf.Max(0f, _impactYieldCooldownTimer - Time.fixedDeltaTime);

if (!IsFallen && !IsSurrendered && _impactYieldCooldownTimer <= 0f)
{
    float angularSpeed = _rb.angularVelocity.magnitude;
    if (angularSpeed >= _impactYieldAngularVelocityThreshold)
    {
        RampUprightStrength(_impactYieldStrengthScale, 0.05f);    // snap down fast
        RampHeightMaintenance(_impactYieldStrengthScale, 0.05f);
        RampStabilization(_impactYieldStrengthScale, 0.05f);

        // Recovery ramp: schedule a return to 1.0 after yield duration.
        // Use a second ramp call chained via a coroutine-free timer approach:
        // set a timer; when it expires, ramp back up.
        _impactYieldCooldownTimer = _impactYieldCooldown;
        _impactYieldActive = true;
    }
}

// Return to full strength after yield duration expires.
// Detect yield completion by checking if all three scales have reached their target
// and the yield was active. Simplest: use a dedicated timer.
```

Actually — implement this with a single `_impactYieldTimer` float:

```csharp
private float _impactYieldTimer;
```

Full logic:

```csharp
_impactYieldCooldownTimer = Mathf.Max(0f, _impactYieldCooldownTimer - Time.fixedDeltaTime);

if (_impactYieldTimer > 0f)
{
    _impactYieldTimer -= Time.fixedDeltaTime;
    if (_impactYieldTimer <= 0f)
    {
        // Yield window expired — ramp back to full strength.
        RampUprightStrength(1f, 0.3f);
        RampHeightMaintenance(1f, 0.3f);
        RampStabilization(1f, 0.3f);
        _impactYieldActive = false;
    }
}

if (!IsFallen && !IsSurrendered && !_impactYieldActive
    && _impactYieldCooldownTimer <= 0f)
{
    float angularSpeed = _rb.angularVelocity.magnitude;
    if (angularSpeed >= _impactYieldAngularVelocityThreshold)
    {
        RampUprightStrength(_impactYieldStrengthScale, 0.05f);
        RampHeightMaintenance(_impactYieldStrengthScale, 0.05f);
        RampStabilization(_impactYieldStrengthScale, 0.05f);
        _impactYieldTimer = _impactYieldDuration;
        _impactYieldCooldownTimer = _impactYieldCooldown;
        _impactYieldActive = true;
    }
}
```

### Interaction guards

- **Surrender:** `!IsSurrendered` gate prevents yield from conflicting with surrender ramps
- **Fallen:** `!IsFallen` gate — no point yielding when already fallen
- **Ramp conflict:** The existing `CancelAllRamps` called from surrender will correctly
  override yield ramps if surrender fires during a yield window. This is correct behaviour.
- **Jump landing:** A hard landing may spike angular velocity. This is acceptable — a
  brief yield on hard landing actually looks good (character absorbs the impact). If it
  causes problems, add `!_playerMovement.IsRecentJumpAirborne` guard.

## Prefab values

Add to `PlayerRagdoll.prefab` (Hips → BalanceController):
- `_impactYieldAngularVelocityThreshold: 4` (tune down to 3 if hits still feel weak)
- `_impactYieldStrengthScale: 0.15`
- `_impactYieldDuration: 0.35`
- `_impactYieldCooldown: 0.5`

## What NOT to change

- Do NOT change `_kP`, `_kD` or any PD gains (already tuned to 1200/350 by Benny)
- Do NOT change surrender logic
- Do NOT modify Run-UnityTests.ps1
- Do NOT touch LegAnimator or ArmAnimator

## Verification

Run focused tests:
```
powershell -NoProfile -ExecutionPolicy Bypass -File "H:\Work\PhysicsDrivenMovementDemo\Tools\Run-UnityTests.ps1" -ProjectPath "H:\Work\PhysicsDrivenMovementDemo" -Platform PlayMode -MaxAttemptsPerPlatform 1 -Unattended -TestFilter "JumpTests|AirborneSpringTests|SprintJumpStabilityTests|LandingRecoveryTests|ImpactKnockdownTests"
```

All must pass. Pay attention to `ImpactKnockdownTests` — these test that the character
falls when hit hard. They should still pass; the yield makes falls easier, not harder.

If `LandingRecoveryTests` fail, add `!_playerMovement.IsRecentJumpAirborne` guard to
prevent yield triggering on jump landings.

## Manual verification

In editor: hit character with heavy cube.
- Should visibly stagger/react to the hit
- Should recover after ~0.35s
- Hard hits should still cause Fallen
- Normal walking/turning should NOT trigger yield (threshold at 4 rad/s)

## Commit

`feat(11a): impact yield - briefly reduce balance correction on large angular impulse`

## On completion

Write `agent-slice-status.json`:
```json
{"slice":"11a-impact-yield","status":"pass","branch":"plan/11a-impact-yield","notes":"threshold=X rad/s, yieldScale=X, duration=Xs, tests pass"}
```

Send Telegram:
```powershell
$body = @{ chat_id = "8630971080"; text = "[plan-11a] Impact yield done. Hit the character with a cube to test. STATUS: pass/fail." } | ConvertTo-Json
Invoke-RestMethod -Uri "https://api.telegram.org/bot8733821405:AAHgYbzmTD7SiIrdkh_rVs7ujTxQtKdfpDg/sendMessage" -Method Post -Body $body -ContentType "application/json"
```

## Execution Status

- State: Blocked
- Branches: `plan/11a-impact-yield` created from `master`; active handoff branch is `slice/11a-impact-yield`
- Best known implementation state: impact yield uses the 4 rad/s threshold, 0.15 yield scale, 0.35 s duration, a jump-landing guard, and tilt-direction spike filtering so yaw-only motion does not trigger it
- Current blocker: the required PlayMode gate still fails `LandingRecovery_RecoveryTimeImproved` on the slice, while `master` already fails `LandingRecovery_DampingDisabledWhenFactorIsOne`
- Current next step: instrument the sprint-jump setup path to capture any impact-yield activation or support-scale drop during the pre-jump warm-up documented in the linked bug sheet

## Quick Resume

- Focused coverage for the new feature passed with `PhysicsDrivenMovement.Tests.PlayMode.ImpactYieldTests`; see `Logs/test_playmode_20260329_101426.log`
- The exact verification command on the best-known slice state still fails 2 tests: `LandingRecovery_DampingDisabledWhenFactorIsOne` and `LandingRecovery_RecoveryTimeImproved`; see `Logs/test_playmode_20260329_101454.log`
- `master` reproduces only the first failure, so the unresolved slice-specific regression is `LandingRecovery_RecoveryTimeImproved`; see `Logs/test_playmode_20260329_100656.log`

## Verified Artifacts

- `Assets/Scripts/Character/BalanceController.cs`: impact-yield implementation with jump-landing guard and tilt-direction spike filter
- `Assets/Tests/PlayMode/Character/ImpactYieldTests.cs`: focused regression coverage for the feature
- `Logs/test_playmode_20260329_101426.log`: focused impact-yield PlayMode pass
- `Logs/test_playmode_20260329_101454.log`: exact slice-gate run on the best-known blocked state (`37 passed, 2 failed`)
- `Logs/test_playmode_20260329_100656.log`: exact gate on `master` (`38 passed, 1 failed`)
- `Plans/impact-yield/bugs/11a-landing-recovery-regression.md`: failed hypotheses, next hypothesis, and blocker handoff

## Failed Hypotheses

- 2026-03-29: Suppressing impact yield during `IsRecentJumpAirborne` would fully clear landing regressions. It removed 2 of 4 failures, but `LandingRecovery_DampingDisabledWhenFactorIsOne` and `LandingRecovery_RecoveryTimeImproved` still failed.
- 2026-03-29: Requiring a generic angular-speed spike would stop self-triggering during nominal locomotion. The slice-specific landing-recovery failure persisted.
- 2026-03-29: Raising the threshold to 5 rad/s would clear the remaining regression. It made the broader landing-recovery gate worse (`3 failed`), so the slice was restored to the 4 rad/s state.
