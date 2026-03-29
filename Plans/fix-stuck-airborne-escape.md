# Fix: Stuck Airborne Escape — Geometry Nudge

## Problem

The character can get stuck in Airborne state indefinitely when resting on geometry
(cowboy on rocks, knees on platform edge, feet dangling). The existing `_airborneLimboTimer`
(1.5s) transitions to Standing but applies no physics nudge, so the character immediately
returns to Airborne and loops forever.

Two bugs found:
1. `_balanceController.UprightAngle * Mathf.Rad2Deg < 45f` — UprightAngle is already
   in degrees, multiplying by Rad2Deg makes the threshold ~2.6° instead of 45°.
   Fix: remove the `* Mathf.Rad2Deg`.
2. No physics nudge on escape — character snaps to Standing then falls straight back.
   Fix: apply a `ComputePenetration`-based nudge + small upward impulse on escape.

## Branch

Create `fix/stuck-airborne-escape` from `master`.

## File: `CharacterState.cs`

Path: `Assets/Scripts/Character/CharacterState.cs`

### Fix 1 — correct UprightAngle comparison (one-liner):

Find:
```csharp
bool likelyStuck = hipSpeed < 0.15f && _balanceController.UprightAngle * Mathf.Rad2Deg < 45f;
```

Replace with:
```csharp
bool likelyStuck = hipSpeed < 0.15f && _balanceController.UprightAngle < 45f;
```

### Fix 2 — apply geometry nudge on limbo escape:

Find the limbo escape block:
```csharp
if (_airborneLimboTimer >= 1.5f)
{
    _airborneLimboTimer = 0f;
    _limboForcedDwellTimer = 0.4f;
    nextState = CharacterStateType.Standing;
}
```

Replace with:
```csharp
if (_airborneLimboTimer >= 1.5f)
{
    _airborneLimboTimer = 0f;
    _limboForcedDwellTimer = 0.4f;
    nextState = CharacterStateType.Standing;
    ApplyLimboEscapeNudge();
}
```

### New private method `ApplyLimboEscapeNudge`:

Add after the FixedUpdate / state machine method:

```csharp
/// <summary>
/// Applies a physics nudge to escape geometry when the character is stuck in Airborne limbo.
/// Uses ComputePenetration to find overlapping colliders and pushes away from them.
/// Falls back to a small upward impulse if no penetration is found.
/// </summary>
private void ApplyLimboEscapeNudge()
{
    if (_rb == null) return;

    const float NudgeForce = 3f;        // upward impulse strength
    const float SeparationForce = 4f;   // separation impulse per overlapping collider
    const float CheckRadius = 0.6f;     // sphere radius to find nearby colliders

    // Find all colliders overlapping a sphere around the hips.
    Collider[] nearby = Physics.OverlapSphere(_rb.position, CheckRadius,
        Physics.AllLayers, QueryTriggerInteraction.Ignore);

    Vector3 separationDir = Vector3.zero;
    int separationCount = 0;

    Collider[] selfColliders = GetComponentsInChildren<Collider>();

    foreach (Collider other in nearby)
    {
        // Skip self colliders.
        bool isSelf = false;
        foreach (Collider self in selfColliders)
        {
            if (other == self) { isSelf = true; break; }
        }
        if (isSelf) continue;

        // Skip triggers.
        if (other.isTrigger) continue;

        // Compute penetration direction.
        Collider hipsCollider = _rb.GetComponent<Collider>();
        if (hipsCollider == null) continue;

        if (Physics.ComputePenetration(
            hipsCollider, _rb.position, _rb.rotation,
            other, other.transform.position, other.transform.rotation,
            out Vector3 dir, out float dist))
        {
            separationDir += dir * dist;
            separationCount++;
        }
    }

    if (separationCount > 0)
    {
        // Push away from overlapping geometry.
        _rb.AddForce(separationDir.normalized * SeparationForce, ForceMode.Impulse);
    }

    // Always add a small upward impulse to help clear the geometry.
    _rb.AddForce(Vector3.up * NudgeForce, ForceMode.Impulse);
}
```

**Note on `_rb`:** Check that `_rb` is already cached in `CharacterState`. If not, cache it
in `Awake` with `TryGetComponent(out _rb)` on the same GameObject as `BalanceController`
(the Hips). Look at how other references are cached in the file and follow the same pattern.

## What NOT to change

- Do NOT change the 1.5s limbo timer duration
- Do NOT change `_limboForcedDwellTimer = 0.4f`
- Do NOT touch BalanceController, LegAnimator, or PlayerMovement
- Do NOT modify Run-UnityTests.ps1

## Verification

```
powershell -NoProfile -ExecutionPolicy Bypass -File "H:\Work\PhysicsDrivenMovementDemo\Tools\Run-UnityTests.ps1" -ProjectPath "H:\Work\PhysicsDrivenMovementDemo" -Platform PlayMode -MaxAttemptsPerPlatform 1 -Unattended -TestFilter "JumpTests|AirborneSpringTests|GaitOutcomeTests"
```

All must pass.

## Manual verification

In editor: get the character into the cowboy-on-rocks position (straddle a rock).
After ~1.5s, character should receive a nudge and pop free rather than staying stuck.

## Commit

`fix: correct UprightAngle comparison in limbo detection + add geometry nudge on escape`

## On completion

Write `agent-slice-status.json`:
```json
{"slice":"fix-stuck-airborne","status":"pass","branch":"fix/stuck-airborne-escape","notes":"UprightAngle Rad2Deg bug fixed, geometry nudge applied on escape"}
```

Send Telegram:
```powershell
$body = @{ chat_id = "8630971080"; text = "[fix] Stuck airborne escape: STATUS pass/fail. Cowboy-on-rocks should self-rescue after 1.5s now." } | ConvertTo-Json
Invoke-RestMethod -Uri "https://api.telegram.org/bot8733821405:AAHgYbzmTD7SiIrdkh_rVs7ujTxQtKdfpDg/sendMessage" -Method Post -Body $body -ContentType "application/json"
```
