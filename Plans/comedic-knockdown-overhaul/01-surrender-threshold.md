# Ch1: Surrender Threshold — "Embrace the Fall"

## Goal
Detect when the character is in an unrecoverable tilt and stop fighting it. Instead of boosting upright torque forever, release into a natural ragdoll fall after a short grace window. The character should look like he *tried* to stay up and then gave in.

## Current status
- State: In progress
- Current next step: Implement LocomotionDirector recovery-timeout → surrender handoff
- Blockers: None

## Current behavior
- `BalanceController` PD upright torque (kP=2000) is always active, even at extreme angles
- `LocomotionDirector` recovery situations (Stumble, NearFall) boost upright strength up to 1.8×
- `LocomotionCollapseDetector` catches pre-fall stumbles and triggers recovery — recovery has no fail timer
- Result: character fights to stay upright in absurd positions, rarely falls flat, and when it does the PD torque keeps trying to right it immediately

## Design

### Surrender trigger conditions (ANY of these)
1. **Extreme angle**: upright angle exceeds `_surrenderAngleThreshold` (~80–85°) — character is nearly horizontal, no saving this
2. **Recovery timeout**: active recovery situation (Stumble or NearFall) has been running for >`_surrenderRecoveryTimeout` (~0.8–1.0 s) without angle improving below 50° — we tried, it's not working
3. **Angular velocity blow-through**: upright angle > 65° AND angular velocity in the tilt direction > `_surrenderAngularVelocityThreshold` (~3 rad/s) — character is actively accelerating toward the ground, momentum wins

### Surrender response
When surrender fires:
1. **Kill upright torque**: Set `UprightStrengthScale` → 0 immediately. No more PD fight.
2. **Ramp down joint springs**: Over ~0.15 s, reduce all limb joint springs to 20–30% of normal values. This gives the flaily-but-not-rigid look — arms and legs go loose but aren't completely limp noodles.
3. **Disable height maintenance**: Set `HeightMaintenanceScale` → 0.
4. **Disable COM stabilization**: Set `StabilizationScale` → 0.
5. **Suppress gait**: Tell `LegAnimator` to stop driving phase — legs should flop naturally with gravity.
6. **Signal `CharacterState`**: Transition to `Fallen` with a `surrendered=true` flag (this tells Chapter 3's knockdown timer to begin the floor dwell rather than attempting instant recovery).
7. **Compute knockdown severity**: Capture `surrenderSeverity = f(uprightAngle, angularVelocity, hipsHeight)` at the moment of surrender (consumed by Chapter 3 for floor dwell duration).

### What stays active
- Gravity (obviously)
- Collision detection / ground contact
- Joint angle limits (so limbs don't clip through each other)
- Low residual joint damping (~10% of normal) to prevent infinite oscillation

### What does NOT change
- The existing 65° `IsFallen` threshold stays — it still marks the character as fallen for other systems
- Normal recovery (PD torque, recovery situations) still works for tilts below the surrender threshold — the character is still sure-footed in recoverable situations
- `LocomotionCollapseDetector` still detects pre-fall stumbles, but now hands off to surrender if recovery times out

## Files to modify
| File | What changes |
|------|-------------|
| `BalanceController.cs` | New `_surrenderAngleThreshold`, `_surrenderAngularVelocityThreshold` fields. New `IsSurrendered` property. In `FixedUpdate`: check surrender conditions, zero out torque scales on trigger. Joint spring ramp-down coroutine or lerp. |
| `LocomotionDirector.cs` | Add recovery timeout tracking. When active Stumble/NearFall exceeds `_surrenderRecoveryTimeout` without improvement, signal surrender. |
| `LocomotionCollapseDetector.cs` | Minimal change — collapse confirmation now feeds the LocomotionDirector timeout rather than extending recovery indefinitely. |
| `CharacterState.cs` | Accept `surrendered` flag on Fallen entry (consumed by Ch3). |
| `LegAnimator.cs` | Respond to surrender: zero phase drive, let joints go passive. |
| `RagdollSetup.cs` or new helper | Joint spring profile method: `SetLimpProfile(float t)` that lerps springs from current → limp over `t` seconds. |

## Acceptance criteria
- [ ] Character with upright angle > 85° for 2+ frames falls naturally (no PD torque fighting)
- [ ] Character in Stumble recovery for > 1 s with no improvement surrenders
- [ ] Limbs flail naturally during fall (not rigid, not completely limp)
- [ ] Normal recoverable tilts (< 70°) still recover as before — no regression
- [ ] `surrenderSeverity` float is computed and available for downstream consumption

## Decisions
- (pending)

## Progress notes
- 2026-03-16: Chapter spec written
- 2026-03-16: Verified the `RagdollSetup` spring-profile API was already present and passing EditMode validation.
- 2026-03-16: Implemented `BalanceController` surrender detection, severity capture, and limp-profile triggering. EditMode passed, `HardSnapRecoveryTests` + `BalanceControllerTests` passed, and the targeted `LocomotionDirectorTests` slice still shows persistent C3.2/C3.5 gait-phase reds unrelated to the surrender changes.
