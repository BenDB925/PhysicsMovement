# Plan 11b — Direct Collision Impact (replaces angular velocity approach)

## Problem

The angular velocity approach in Plan 11a is indirect and unreliable:
- Hit registers *after* the fact via `_rb.angularVelocity` — too late for fast impacts
- Only monitors the hips rigidbody — hits to arms/chest/legs are ignored
- Hammer phases through thin colliders and produces no angular spike at all

## Solution

Direct `OnCollisionEnter` detection on all ragdoll body parts.
When a significant collision impulse arrives on *any* body part, notify `BalanceController`
which immediately reduces balance correction and lets physics express the impact.

Strong hits → balance correction drops to near-zero for 0.5s → character goes sailing.
Weak hits → brief reduction, quick recovery.

---

## Architecture

### New component: `CollisionImpactReceiver.cs`

Lightweight MonoBehaviour. Attach to every ragdoll Rigidbody in the hierarchy
(Hips, Chest, UpperArm_L, UpperArm_R, UpperLeg_L, UpperLeg_R, etc.).

```csharp
public class CollisionImpactReceiver : MonoBehaviour
{
    private BalanceController _balance;

    private void Awake()
    {
        // Walk up the hierarchy to find BalanceController (lives on Hips).
        _balance = GetComponentInParent<BalanceController>();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (_balance == null) return;
        // Filter: ignore self-collisions (other player parts) and ground contacts.
        // Only respond to objects NOT on LayerPlayer1Parts and NOT on LayerEnvironment/LowerLeg.
        int otherLayer = collision.gameObject.layer;
        if (otherLayer == GameSettings.LayerPlayer1Parts ||
            otherLayer == GameSettings.LayerEnvironment ||
            otherLayer == GameSettings.LayerLowerLegParts)
            return;

        float impulse = collision.impulse.magnitude;
        _balance.ReceiveCollisionImpact(impulse, collision.impulse);
    }
}
```

### `BalanceController.cs` — add `ReceiveCollisionImpact`

Replace the angular-velocity-based impact yield detection with a direct method:

```csharp
public void ReceiveCollisionImpact(float impulseMagnitude, Vector3 impulseVector)
```

Inside:
- Ignore if `IsFallen`, `IsSurrendered`, or `_impactYieldActive`
- Ignore if `_impactYieldCooldownTimer > 0f` (cooldown)
- Ignore if `_impactYieldPostLandingSuppressTimer > 0f` (suppress during landing)
- Compute severity: `Mathf.Clamp01(impulseMagnitude / _impactYieldFullSeverityImpulse)`
  - `_impactYieldFullSeverityImpulse` = new field, default 15f (Newtons·seconds for a "full" hit)
- Compute yield scale: lerp from `_impactYieldStrengthScaleLight` to `_impactYieldStrengthScaleHeavy`
  based on severity
  - `_impactYieldStrengthScaleLight = 0.4f` (glancing blow — small reduction)
  - `_impactYieldStrengthScaleHeavy = 0.0f` (full hit — balance drops to zero, character sails)
- Compute duration: lerp from `_impactYieldDurationLight` to `_impactYieldDurationHeavy`
  - `_impactYieldDurationLight = 0.15f`
  - `_impactYieldDurationHeavy = 0.5f`
- Call `RampUprightStrength`, `RampHeightMaintenance`, `RampStabilization` to yield scale
- Set `_impactYieldTimer = duration`, `_impactYieldCooldownTimer = _impactYieldCooldown`
- Set `_impactYieldActive = true`

### Remove angular velocity detection

Remove the existing `impactYieldAngularSpeed` / `angularSpeedSpike` block from `FixedUpdate`.
Keep `_impactYieldTimer` / recovery ramp logic (still needed for the yield window expiry).
Keep `_impactYieldPostLandingSuppressTimer` (still needed — landing collisions would trigger).
Keep `_impactYieldCooldownTimer`.

### New serialized fields (replace existing impact yield fields with these):

```csharp
[Header("Impact Yield")]

[SerializeField, Range(1f, 50f)]
[Tooltip("Impulse magnitude (N·s) that counts as a 'full' hit and drives maximum yield. "
       + "Scale this to match your hammer/obstacle mass × velocity.")]
private float _impactYieldFullSeverityImpulse = 15f;

[SerializeField, Range(0f, 1f)]
[Tooltip("Upright strength scale for a light glancing hit. 0.4 = mild stagger.")]
private float _impactYieldStrengthScaleLight = 0.4f;

[SerializeField, Range(0f, 1f)]
[Tooltip("Upright strength scale for a full heavy hit. 0 = character fully sails.")]
private float _impactYieldStrengthScaleHeavy = 0.0f;

[SerializeField, Range(0.05f, 0.3f)]
[Tooltip("Yield duration for a light hit (seconds).")]
private float _impactYieldDurationLight = 0.15f;

[SerializeField, Range(0.2f, 1f)]
[Tooltip("Yield duration for a full heavy hit (seconds).")]
private float _impactYieldDurationHeavy = 0.5f;

[SerializeField, Range(0.1f, 2f)]
[Tooltip("Minimum seconds between impact yield triggers.")]
private float _impactYieldCooldown = 0.4f;
```

Remove: `_impactYieldAngularVelocityThreshold`, `_impactYieldStrengthScale`, `_impactYieldDuration`

---

## Where to attach `CollisionImpactReceiver`

Add to `PlayerRagdoll.prefab` on these GameObjects (all have Rigidbody components):
- `Hips`
- `Chest` (or equivalent torso bone name — check prefab hierarchy)
- `UpperArm_L`, `UpperArm_R`
- `UpperLeg_L`, `UpperLeg_R`

Do NOT attach to `LowerLeg_L`, `LowerLeg_R` or foot GameObjects — ground collisions would
spam impacts every frame.

Use `GetComponentsInChildren<Rigidbody>()` in the Awake of a setup script if you want to
auto-attach rather than manually placing on each bone. But manual placement on the 5-6
key bodies is fine and more explicit.

---

## Hammer setup (for Benny to verify)

For the collision to register properly, the hammer needs:
- Rigidbody with `Collision Detection Mode = Continuous Dynamic` (prevents tunnelling)
- Layer: Default (layer 0) — this will collide with LayerPlayer1Parts (layer 8)
- Reasonable mass: 5-20kg will register impulses in the 5-30 N·s range at walking speed

If the hammer is kinematic (driven by animation/script), impulses are zero — use
`Collision Detection Mode = Continuous Speculative` instead.

---

## Branch

Create branch `plan/11b-collision-impact` from `master`.

## Files

- New: `Assets/Scripts/Character/CollisionImpactReceiver.cs`
- Modified: `Assets/Scripts/Character/BalanceController.cs`
- Modified: `Assets/Prefabs/PlayerRagdoll.prefab` (add CollisionImpactReceiver to 5-6 bones)

## What NOT to change

- Keep `_impactYieldPostLandingSuppressTimer` and `_impactYieldCooldownTimer`
- Keep `_impactYieldTimer` / recovery ramp logic
- Do NOT change the layer setup in `GameSettings`
- Do NOT modify `Run-UnityTests.ps1`
- Do NOT touch `LegAnimator` or `ArmAnimator`

## Verification

```
powershell -NoProfile -ExecutionPolicy Bypass -File "H:\Work\PhysicsDrivenMovementDemo\Tools\Run-UnityTests.ps1" -ProjectPath "H:\Work\PhysicsDrivenMovementDemo" -Platform PlayMode -MaxAttemptsPerPlatform 1 -Unattended -TestFilter "ImpactKnockdownTests|JumpTests|LandingRecoveryTests|SprintJumpStabilityTests"
```

All must pass. `ImpactKnockdownTests` should still pass — those apply forces directly
to the hips rigidbody which will trigger `OnCollisionEnter` on the `CollisionImpactReceiver`.

## Commit

`feat(11b): direct collision impact yield via CollisionImpactReceiver - replaces angular velocity approach`

## Manual verification

In editor: hit character with a heavy fast-moving cube/hammer.
- Glancing hit: brief stagger, quick recovery
- Direct full hit: character sails/stumbles, takes ~0.5s to recover balance
- Normal walking/running: no impact yield (no external collisions)
- Jump landing: no impact yield (post-landing suppress timer)

## On completion

Write `agent-slice-status.json`:
```json
{"slice":"11b-collision-impact","status":"pass","branch":"plan/11b-collision-impact","notes":"CollisionImpactReceiver on X bones, full severity impulse=15 N·s, tests pass"}
```

Send Telegram:
```powershell
$body = @{ chat_id = "8630971080"; text = "[plan-11b] Collision impact done. Hit with hammer to test - should sail on full hit. STATUS: pass/fail." } | ConvertTo-Json
Invoke-RestMethod -Uri "https://api.telegram.org/bot8733821405:AAHgYbzmTD7SiIrdkh_rVs7ujTxQtKdfpDg/sendMessage" -Method Post -Body $body -ContentType "application/json"
```
