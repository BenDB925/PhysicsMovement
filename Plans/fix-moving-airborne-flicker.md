# Investigation & Fix: Moving→Airborne flicker during normal walking

## Problem

During normal walking the character briefly transitions Moving→Airborne→Moving.
This is visible as an animation/state flicker and feels wrong — the character is never
truly airborne, just briefly losing ground contact mid-stride.

## Root cause (already diagnosed)

`CharacterState.cs` — the `Moving` case in the state machine transitions to `Airborne`
the instant `shouldBeAirborne` is true (line ~241). There is no dwell guard.

`GroundSensor.cs` already has `_groundedExitDelay = 0.03f` (3 frames @ 100Hz) to debounce
brief sensor dropouts. But during fast walking, the foot can legitimately lose contact for
exactly those 3 frames mid-stride, causing `IsGrounded` to briefly flip false, which
immediately triggers Moving→Airborne.

The fix: add a short dwell guard to the Moving→Airborne transition so the character must
be consistently ungrounded for N frames before transitioning. Jump launches should be
unaffected because they set `ShouldTreatJumpLaunchAsAirborne = true` directly (independent
of ground sensor).

## Files to read first

- `Assets/Scripts/Character/CharacterState.cs` — state machine, Moving case ~line 238
- `Assets/Scripts/Character/PlayerMovement.cs` — `ShouldTreatJumpLaunchAsAirborne` property
- `Assets/Scripts/Character/GroundSensor.cs` — `_groundedExitDelay` field for reference
- `Assets/Tests/PlayMode/Character/JumpTests.cs` — jump tests must still pass
- `Assets/Tests/PlayMode/Character/AirborneSpringTests.cs` — airborne spring tests must still pass

## Branch

Create branch `fix/moving-airborne-flicker` from `master`.

## Change required

### `CharacterState.cs`

Add a new serialized field:
```csharp
[SerializeField, Range(0, 10)]
[Tooltip("Frames Moving must be consistently ungrounded before transitioning to Airborne. "
       + "Filters brief ground contact loss during fast walking without delaying real jumps.")]
private int _movingAirborneGuardFrames = 4;
```

Add a private counter field:
```csharp
private int _movingUngroundedFrames;
```

In the `Moving` case of the state machine, replace the bare `shouldBeAirborne` check with:

```csharp
case CharacterStateType.Moving:
    if (isFallen || collapseTriggersfall)
    {
        nextState = CharacterStateType.Fallen;
        _movingUngroundedFrames = 0;
    }
    else if (shouldBeAirborne)
    {
        _movingUngroundedFrames++;
        if (_movingUngroundedFrames >= _movingAirborneGuardFrames
            || _playerMovement.ShouldTreatJumpLaunchAsAirborne)
        {
            nextState = CharacterStateType.Airborne;
            _movingUngroundedFrames = 0;
        }
    }
    else
    {
        _movingUngroundedFrames = 0;
    }
    ...
```

Keep all existing logic after this (wantsMove, etc.) unchanged.

**Important:** `ShouldTreatJumpLaunchAsAirborne` bypasses the frame guard entirely — jump
launches must transition to Airborne immediately without waiting for the guard to expire.

## Verification

Run the following focused tests — all must pass:

```
powershell -NoProfile -ExecutionPolicy Bypass -File "H:\Work\PhysicsDrivenMovementDemo\Tools\Run-UnityTests.ps1" -ProjectPath "H:\Work\PhysicsDrivenMovementDemo" -Platform PlayMode -MaxAttemptsPerPlatform 1 -Unattended -TestFilter "JumpTests|AirborneSpringTests|AutoSprintTests|SprintJumpStabilityTests"
```

Expected: all pass. If any jump test fails (character not entering Airborne when expected),
the guard is too long — reduce `_movingAirborneGuardFrames` default or verify
`ShouldTreatJumpLaunchAsAirborne` bypass is working.

## What NOT to change

- Do NOT change `GroundSensor._groundedExitDelay` — leave at 0.03f
- Do NOT add a guard to the `Standing→Airborne` transition — not needed (idle → airborne
  is rare and already has `_limboForcedDwellTimer`)
- Do NOT modify `Run-UnityTests.ps1`
- Do NOT add a guard to `Airborne→Moving` — that direction is already handled by
  `_airborneLimboTimer`

## Commit

`fix: add Moving→Airborne frame guard to suppress walking ground flicker`

## On completion

Write `agent-slice-status.json`:
```json
{"slice":"fix-moving-airborne-flicker","status":"pass","branch":"fix/moving-airborne-flicker","notes":"_movingAirborneGuardFrames=4, jump bypass confirmed working"}
```

Send Telegram:
```powershell
$body = @{ chat_id = "8630971080"; text = "[fix] Moving->Airborne flicker: STATUS pass/fail. Guard=X frames. Jump tests: pass/fail." } | ConvertTo-Json
Invoke-RestMethod -Uri "https://api.telegram.org/bot8733821405:AAHgYbzmTD7SiIrdkh_rVs7ujTxQtKdfpDg/sendMessage" -Method Post -Body $body -ContentType "application/json"
```
