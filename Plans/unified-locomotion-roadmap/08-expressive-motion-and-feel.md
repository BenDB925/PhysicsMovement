# Chapter 8: Expressive Motion And Feel

Back to parent plan: [Unified Locomotion Roadmap](../unified-locomotion-roadmap.plan.md)

## Read this chapter when

- layering recognizable style onto already-stable locomotion control
- adding pelvis, torso, arm, and stride expression tied to movement state
- replacing the instant jump impulse with a visible crouch-and-spring launch sequence
- building style presets that change feel without destabilizing control

## Dependencies

- Read Chapters 5 through 7 first. Expression work only starts after control and terrain behavior are stable enough to protect readability.
- Jump rework (C8.5) depends on the existing jump gate in `PlayerMovement.TryApplyJump()`, `LegAnimator` airborne spring scaling, and `BalanceController` height-maintenance and posture torque.

## Objective

Layer character identity only after control architecture is stable. Replace abrupt mechanical transitions with visible body preparation so every movement looks motivated by the character's body rather than by invisible external forces.

## Status

- State: In progress.
- Current next step: C8.4c regression baseline refresh.
- Active blockers: None.
- Completed: C8.1a pelvis tilt driven by acceleration (speed-delta approach in BalanceController, verified against Arena01BalanceStabilityTests + FullStackSanityTests + HardSnapRecoveryTests + SpinRecoveryTests), C8.1b torso twist driven by gait phase (new TorsoExpression component on Hips, 1° amplitude at Torso ConfigurableJoint, verified against MovementQualityTests + FullStackSanityTests + Arena01BalanceStabilityTests + HardSnapRecoveryTests + SpinRecoveryTests), C8.1c lateral pelvis sway during single-support (0.02 m max COM shift toward stance foot in BalanceController STEP 3.6c, verified against MovementQualityTests + Arena01BalanceStabilityTests + FullStackSanityTests + HardSnapRecoveryTests + SpinRecoveryTests), C8.2a forward lean on movement start (5° transient forward tilt triggered by CharacterState.OnStateChanged Standing→Moving, decaying over 0.3 s in BalanceController STEP 3.10, verified against MovementQualityTests + FullStackSanityTests + Arena01BalanceStabilityTests + HardSnapRecoveryTests + SpinRecoveryTests), C8.2b backward settle on stop (3° transient backward lean triggered by Moving→Standing, decaying over 0.2 s via same transient lean system, verified against MovementQualityTests + FullStackSanityTests + Arena01BalanceStabilityTests), C8.2c reversal weight shift (0.035 m max transient COM offset toward stance foot on HardTurn/Reversal entry via new STEP 3.6d in BalanceController, decaying over 0.35 s, rising-edge detection on RecoverySituation, verified against HardSnapRecoveryTests + SpinRecoveryTests + MovementQualityTests + FullStackSanityTests + Arena01BalanceStabilityTests), C8.3a speed-reactive arm swing amplitude (AnimationCurve EaseInOut response curve on SmoothedInputMag in ArmAnimator, replacing linear ramp; restrained swing at slow walk, assertive at run; new PlayMode test SlowWalk_ProducesRestrainedArmSwing_ComparedToFullSpeed; verified against ArmAnimatorPlayModeTests 3/3 + FullStackSanityTests 1/1 + EditMode 119/119), C8.3b arm brace during recovery (ArmAnimator reads LocomotionDirector.IsRecoveryActive to ramp a brace blend 0→1; dampens arm swing by _braceSwingDampen 0.2 and adds _braceElbowBendBoost 20° elbow tuck; nullable test override on LocomotionDirector; new PlayMode test DuringRecovery_ArmSwingDampensAndElbowsTighten; verified against ArmAnimatorPlayModeTests 4/4 + StumbleStutterRegressionTests 4/4 + FullStackSanityTests 2/2), C8.3c arm raise during airborne (ArmAnimator subscribes to CharacterState.OnStateChanged for Airborne entry/exit; ramps airborne blend 0→1 at 6 units/s; applies outward abduction boost 25° + forward reach 10° + reduced elbow bend 8°; Slerp-blends between gait swing and airborne pose; new PlayMode test DuringAirborne_ArmsRaiseOutward_AndBlendBackOnLanding; verified against ArmAnimatorPlayModeTests 5/5 + JumpTests 7/7 + FullStackSanityTests 1/1 + EditMode 119/119), C8.4a expression amplitude caps (BalanceController: _totalPelvisTiltCapDeg 15° clamps composite tilt sum in STEP 4a; _totalComExpressionOffsetCap 0.06 m clamps combined sway + reversal COM offset via new STEP 3.6e; ArmAnimator: _maxSwingAngleDeg 60° clamps composed swing angle in STEP 4b, _maxElbowAngleDeg 55° clamps elbow bend after brace boost in STEP 3d, _maxAbductionAngleDeg 45° clamps airborne abduction in ApplyAirbornePose and ApplyArmSwingWithAirborneBlend; TorsoExpression: clamps smoothed twist to ±_twistMaxDeg in STEP 4b; verified against EditMode 119/119 + MovementQualityTests 3/4 (1 known pre-existing) + Arena01BalanceStabilityTests 2/2 + FullStackSanityTests 1/1 + ArmAnimatorPlayModeTests 5/5), C8.4b expression kill-switch during recovery or fallen (BalanceController: OnCharacterStateChanged sets _suppressPelvisExpression true on Fallen/GettingUp entry and zeroes all expression state, clears on Standing/Moving entry; ArmAnimator: _suppressExpression flag gates FixedUpdate early-out to rest pose during Fallen/GettingUp, clears airborne and brace blends; TorsoExpression: subscribe to CharacterState.OnStateChanged, _suppressTwist flag decays twist to zero during Fallen/GettingUp via smoothed lerp; verified against EditMode 119/119 + GetUpReliabilityTests pass + MovementQualityTests 3/4 (1 known pre-existing) + FullStackSanityTests 1/1 + Arena01BalanceStabilityTests 2/2 + ArmAnimatorPlayModeTests 5/5 + HardSnapRecoveryTests 2/2 + SpinRecoveryTests 2/2 + StumbleStutterRegressionTests 4/4 + JumpTests 7/7).
- Known pre-existing failures: `MovementQualityTests.SustainedLocomotionCollapse_TransitionsIntoFallen` and `TurnAndWalk_CornerRecovery` fail on baseline (confirmed without C8 changes).

## Primary touchpoints

- Assets/Scripts/Character/ArmAnimator.cs
- Assets/Scripts/Character/LegAnimator.cs
- Assets/Scripts/Character/BalanceController.cs
- Assets/Scripts/Character/TorsoExpression.cs
- Assets/Scripts/Character/PlayerMovement.cs
- Assets/Scripts/Character/CharacterState.cs
- Assets/Scripts/Character/Locomotion/LocomotionDirector.cs
- Optional style profile assets under Assets/ScriptableObjects/

## Work packages

Each unchecked sub-slice is intentionally small enough for one agent pass: make the change, run the focused verification, and update this chapter.

1. [ ] C8.1 Pelvis and torso expression:
    - [x] C8.1a Pelvis tilt driven by acceleration:
       - Scope: Add a small forward/backward pelvis tilt on the Hips joint `targetRotation` that tracks the character's current horizontal acceleration direction, blended by `SmoothedInputMag`. The tilt should lean the pelvis forward during acceleration and backward during deceleration.
       - Touchpoints: `BalanceController.cs` (add tilt offset to upright posture target).
       - Done when: A walking character visibly tilts the pelvis forward when speeding up and backward when slowing down, without affecting balance stability.
       - Verification: `MovementQualityTests`, `Arena01BalanceStabilityTests`.
    - [x] C8.1b Torso twist driven by gait phase:
       - Scope: Add a subtle counter-rotation twist to the Spine joint that opposes the current gait phase, so the upper body twists slightly against the stride. Read phase from `LegAnimator.Phase` and blend amplitude by `SmoothedInputMag`.
       - Touchpoints: New `TorsoExpression` component on the Hips GameObject (sets Torso ConfigurableJoint targetRotation).
       - Done when: The torso counter-rotates visibly during walking and returns to neutral at idle, without injecting angular instability.
       - Implementation notes: Amplitude limited to 1° default because the Torso SLERP spring (650) creates reaction torque on Hips that can destabilize yaw control at higher amplitudes. Higher values available via Inspector for experimentation.
       - Verification: `MovementQualityTests`, `FullStackSanityTests`.
    - [x] C8.1c Lateral pelvis sway during single-support:
       - Scope: Add a small lateral hip shift toward the stance leg during single-support phases. Read the current stance side from `LegAnimator` leg-state ownership and scale by speed.
       - Touchpoints: `BalanceController.cs` (lateral offset on height-maintenance or COM target).
       - Done when: The hips visibly shift toward whichever foot is planted, and the sway disappears at idle.
       - Implementation notes: 0.02 m default max offset applied via STEP 3.6c in COM stabilization. Detects single-support from GroundSensor per-foot IsGrounded. Smoothed at rate 8 to avoid jerky stance transitions. Gated on !IsFallen and SmoothedInputMag.
       - Verification: `MovementQualityTests`, `Arena01BalanceStabilityTests`.

2. [ ] C8.2 Accel and decel body language:
    - [x] C8.2a Forward lean on movement start:
       - Scope: When `CharacterState` transitions from `Standing` to `Moving`, apply a brief forward-lean impulse to the Hips upright target (a few degrees pitched forward, decaying over ~0.3s). This makes the character look like they are pushing off.
       - Touchpoints: `BalanceController.cs` (transient offset on upright posture target, triggered by `CharacterState.OnStateChanged`).
       - Implementation notes: 5° default forward lean set via OnCharacterStateChanged event subscription. Decays linearly over 0.3 s via STEP 3.10 in FixedUpdate. Added to existing pelvis tilt as `totalPelvisTilt` in STEP 4. Transient lean system designed for reuse by C8.2b (backward settle) and C8.2c (reversal weight shift).
       - Done when: Starting to walk produces a visible forward dip that decays to normal posture within a few hundred milliseconds.
       - Verification: `MovementQualityTests`, `FullStackSanityTests`, `Arena01BalanceStabilityTests`, `HardSnapRecoveryTests`, `SpinRecoveryTests`.
    - [x] C8.2b Backward settle on stop:
       - Scope: When `CharacterState` transitions from `Moving` to `Standing`, apply a brief backward-lean offset (smaller than the start lean) so the character appears to brake. Decay over ~0.2s.
       - Touchpoints: `BalanceController.cs` (same transient posture mechanism as C8.2a, opposite direction).
       - Implementation notes: 3° default backward lean (negative transient) with 0.2 s decay. Reuses the same `_transientLeanDeg`/`_transientLeanTimer`/`_transientLeanDecay` mechanism from C8.2a. Wired alongside the Standing→Moving case in `OnCharacterStateChanged`.
       - Done when: Stopping from a walk produces a small backward rock before settling to idle.
       - Verification: `MovementQualityTests`, `FullStackSanityTests`.
    - [x] C8.2c Reversal weight shift:
       - Scope: When `LocomotionDirector` classifies a `SharpTurn` or reversal situation, temporarily shift the pelvis toward the plant foot and away from the prior movement direction. Read the situation tag already produced by the director.
       - Touchpoints: `BalanceController.cs` (short transient bias on COM stabilization offset, gated by director situation).
       - Implementation notes: 0.035 m default max lateral COM offset applied via new STEP 3.6d in COM stabilization block. Detects rising edge on `RecoverySituation` (HardTurn or Reversal entry, tracked via `_previousRecoverySituation`). Single-support: shifts toward stance foot. Both feet down: shifts backward from travel direction. Gated on !IsFallen, !_suppressPelvisExpression, and _reversalWeightShiftMaxOffset > 0. Decays linearly over 0.35 s. Cleared on TriggerSurrender alongside other expression state.
       - Done when: A 180-degree input reversal produces a visible weight transfer before the character re-accelerates. Existing `HardSnapRecoveryTests` and `SpinRecoveryTests` still pass.
       - Verification: `HardSnapRecoveryTests`, `SpinRecoveryTests`, `MovementQualityTests`.

3. [ ] C8.3 Arm-leg coordination:
    - [x] C8.3a Speed-reactive arm swing amplitude:
       - Scope: Scale `ArmAnimator` swing amplitude non-linearly with horizontal speed so arms swing more assertively at higher speeds and stay close to the body at slow walks. Currently amplitude is a linear scale of `SmoothedInputMag` — add a response curve (e.g., smoothstep or AnimationCurve).
       - Touchpoints: `ArmAnimator.cs`.
       - Done when: Slow walking shows restrained arms; running shows wide assertive swings. No arm NaN or physics instability.
       - Implementation notes: Added serialized `_swingAmplitudeCurve` (AnimationCurve, default EaseInOut 0→0→1→1) that remaps SmoothedInputMag before computing effectiveScale. At slow walk (0.25 input) the curve outputs ~0.10, at full speed outputs 1.0, giving a visible suppression at low speeds. Clamped to [0,1] with null-safety fallback to linear. Added PlayMode test `SlowWalk_ProducesRestrainedArmSwing_ComparedToFullSpeed` asserting slow/fast peak ratio < 0.35.
       - Verification: `ArmAnimatorPlayModeTests` 3/3, `FullStackSanityTests` 1/1.
    - [x] C8.3b Arm brace during recovery:
       - Scope: When `LocomotionDirector` signals active recovery (stumble, catch-step), temporarily reduce arm swing amplitude and pull elbows inward (increase elbow bend) so the character looks like they're bracing. Read recovery state from the director or `CharacterState`.
       - Touchpoints: `ArmAnimator.cs`, `LocomotionDirector.cs` (nullable test override on IsRecoveryActive).
       - Done when: During a stumble recovery the arms visibly tighten rather than swinging freely. Normal swing resumes after recovery ends.
       - Implementation notes: Added serialized `_braceSwingDampen` (0.2), `_braceElbowBendBoost` (20°), `_braceBlendSpeed` (10) fields. Caches LocomotionDirector via TryGetComponent in Awake. In STEP 3c of FixedUpdate, ramps `_currentBraceBlend` toward 1 when IsRecoveryActive, toward 0 otherwise. Multiplies effectiveScale by Lerp(1, dampen, blend) and adds elbow-bend boost × blend. LocomotionDirector.SetRecoveryActiveForTest(bool?) added as true override (not OR) so tests can suppress or force recovery independent of the real classifier. PlayMode test uses reversed order (brace first, normal second) to avoid SmoothedInputMag ramp bias.
       - Verification: `ArmAnimatorPlayModeTests` 4/4, `StumbleStutterRegressionTests` 4/4, `FullStackSanityTests` 2/2.
    - [x] C8.3c Arm raise during airborne:
       - Scope: When `CharacterState` enters `Airborne`, blend arm targets toward a raised-outward pose (partial abduction + slight forward reach) to simulate instinctive balance-seeking in the air. Blend out on landing.
       - Touchpoints: `ArmAnimator.cs` (subscribe to `CharacterState.OnStateChanged`, same pattern as `LegAnimator`'s airborne spring scaling).
       - Done when: Jumping produces visible arm raise, landing blends arms back to walk swing within ~0.3s.
       - Implementation notes: Added serialized _airborneAbductionBoost (25°), _airborneForwardReach (10°), _airborneElbowBend (8°), _airborneBlendSpeed (6 units/s). Caches CharacterState in Awake, subscribes to OnStateChanged in OnEnable/OnDisable (same pattern as LegAnimator). Sets _isAirborne flag on Airborne entry/exit. FixedUpdate ramps _currentAirborneBlend via MoveTowards. When blend > 0 during gait, ApplyArmSwingWithAirborneBlend Slerps between gait swing pose and airborne raised-outward pose. When idle and airborne, ApplyAirbornePose applies the pose directly. PlayMode test DuringAirborne_ArmsRaiseOutward_AndBlendBackOnLanding verifies arm abduction increases during airborne and blend decays on landing.
       - Verification: `ArmAnimatorPlayModeTests` 5/5, `JumpTests` 7/7, `FullStackSanityTests` 1/1.

4. [ ] C8.4 Protect readability:
    - [x] C8.4a Expression amplitude caps:
       - Scope: Add per-layer amplitude cap fields (pelvis tilt max, torso twist max, arm raise max, sway max) so that no expressive offset can exceed a safe angular or positional bound. Wire them as serialized fields on the relevant components.
       - Touchpoints: `BalanceController.cs`, `ArmAnimator.cs`.
       - Done when: Every expressive offset is clamped before being applied to joint targets. No single expression layer can push the character past safe physics bounds.
       - Implementation notes: BalanceController: added `_totalPelvisTiltCapDeg` (15°, Range 0–30) to clamp the composite sum of accel tilt + transient lean + director lean in STEP 4a; added `_totalComExpressionOffsetCap` (0.06 m, Range 0–0.15) in new STEP 3.6e to clamp the combined lateral offset from pelvis sway + reversal weight shift (captures `preExpressionFeetCenter` before STEP 3.6c and magnitude-clamps the delta). ArmAnimator: added `_maxSwingAngleDeg` (60°) in STEP 4b, `_maxElbowAngleDeg` (55°) in STEP 3d, `_maxAbductionAngleDeg` (45°) in ApplyAirbornePose and ApplyArmSwingWithAirborneBlend. TorsoExpression: added Mathf.Clamp in STEP 4b clamping `_smoothedTwistDeg` to ±`_twistMaxDeg`. All caps use default values well above normal operating range so behaviour is unchanged — they fire only if inspector values or additive composition exceed safe bounds.
       - Verification: EditMode 119/119, MovementQualityTests 3/4 (1 known pre-existing), `Arena01BalanceStabilityTests` 2/2, `FullStackSanityTests` 1/1, `ArmAnimatorPlayModeTests` 5/5.
    - [x] C8.4b Expression kill-switch during recovery or fallen:
       - Scope: Suppress all expressive offsets (pelvis expression, torso twist, accel lean, arm raise) when `CharacterState` is `Fallen` or `GettingUp`. Expression should not fight recovery torques.
       - Touchpoints: `BalanceController.cs`, `ArmAnimator.cs`, `TorsoExpression.cs`.
       - Implementation notes: BalanceController: OnCharacterStateChanged sets _suppressPelvisExpression true on Fallen/GettingUp entry and zeroes all pelvis expression state (tilt, sway, transient lean, reversal shift), clears suppression on Standing/Moving entry; runs after CharacterState's GettingUp entry logic (which may call ClearSurrender), so re-asserts suppression through the entire recovery window. ArmAnimator: added _suppressExpression flag; FixedUpdate early-outs to rest pose and zeroes airborne/brace blends during Fallen/GettingUp; OnCharacterStateChanged also clears _isAirborne on Fallen entry to prevent stale airborne state. TorsoExpression: added CharacterState subscription (OnEnable/OnDisable pattern matching ArmAnimator); _suppressTwist flag gates FixedUpdate to smoothly decay twist to zero during Fallen/GettingUp rather than snapping, preserving joint stability.
       - Done when: Expressive layers are zeroed during fallen/getting-up and resume cleanly when `Standing` or `Moving` is reached.
       - Verification: EditMode 119/119, GetUpReliabilityTests pass, MovementQualityTests 3/4 (1 known pre-existing), FullStackSanityTests 1/1, Arena01BalanceStabilityTests 2/2, ArmAnimatorPlayModeTests 5/5, HardSnapRecoveryTests 2/2, SpinRecoveryTests 2/2, StumbleStutterRegressionTests 4/4, JumpTests 7/7.
    - [ ] C8.4c Regression baseline refresh:
       - Scope: Run the full PlayMode and EditMode verification gate after all C8.1–C8.3 slices land. Capture updated `LOCOMOTION_BASELINES.md` values. Confirm no degradation in walk-straight, turn-corner, terrain, or recovery outcomes.
       - Touchpoints: `LOCOMOTION_BASELINES.md`, test result artifacts.
       - Done when: All existing regression tests pass with the full expression stack enabled, and baseline numbers are documented.
       - Verification: Full PlayMode + EditMode gate.

5. [ ] C8.5 Physically-motivated jump (crouch-and-spring):
    - [ ] C8.5a Jump state machine — wind-up phase:
       - Scope: Replace the instant `AddForce` jump with a multi-phase sequence. When jump is pressed (and the existing Standing/Moving + Grounded gate passes), enter a new `JumpWindUp` phase instead of immediately applying force. During wind-up (~0.15–0.25s): lower the `BalanceController` height-maintenance target (crouch the hips down by ~15–20%), increase knee bend targets on both legs, and suppress gait phase advancement so the legs hold a braced stance.
       - Touchpoints: `PlayerMovement.cs` (new jump phase enum and timing), `BalanceController.cs` (temporary height target reduction API), `LegAnimator.cs` (temporary knee-bend override and gait suppression during wind-up).
       - Done when: Pressing jump visibly crouches the character for a short wind-up before any upward force, and the character does not leave the ground during wind-up.
       - Verification: `JumpTests` (update existing tests to account for delayed launch), `FullStackSanityTests`.
    - [ ] C8.5b Jump state machine — launch phase:
       - Scope: At the end of wind-up, apply the upward impulse (same `AddForce` magnitude, or slightly increased to compensate for the brief crouch) and simultaneously drive leg extension targets toward full straighten (knees push to ~0° bend over ~0.1s). Transition to `Airborne` only after the launch impulse is applied.
       - Touchpoints: `PlayerMovement.cs` (launch trigger at wind-up completion), `LegAnimator.cs` (leg-straighten drive during launch window, overriding the normal swing/stance targets for a few frames).
       - Done when: The character springs upward with visible leg extension, and the launch impulse fires at the end of the crouch, not at the moment of input.
       - Verification: `JumpTests`, `FullStackSanityTests`.
    - [ ] C8.5c Jump arm coordination:
       - Scope: During the wind-up phase, pull arms slightly back and down (shoulder extension). During the launch phase, swing arms forward and up to amplify the visual thrust. Blend back to the C8.3c airborne arm pose over ~0.2s after launch.
       - Touchpoints: `ArmAnimator.cs` (new jump-phase arm target blending, reading the jump phase from `PlayerMovement` or a shared jump-state accessor).
       - Done when: Arms visibly pull back during crouch and thrust forward on launch. Seamless transition into the airborne arm pose.
       - Verification: `JumpTests`, `FullStackSanityTests`.
    - [ ] C8.5d Landing absorption:
       - Scope: When transitioning from `Airborne` to `Standing`/`Moving` (landing detected by `IsGrounded` re-establishing), apply a brief landing-squat: lower hips height target for ~0.15s, increase knee bend momentarily, and apply a small forward-tilt on the pelvis to absorb impact visually. Blend back to normal posture over ~0.2s.
       - Touchpoints: `BalanceController.cs` (transient height + tilt on landing event), `LegAnimator.cs` (transient knee-bend boost on landing).
       - Done when: Landing from a jump shows visible knee compression and hip drop before returning to normal stance. Walking or stopping after landing is not disrupted.
       - Verification: `JumpTests` (add landing-specific assertions), `MovementQualityTests`, `FullStackSanityTests`.
    - [ ] C8.5e Jump test suite update:
       - Scope: Update all existing `JumpTests` to account for the delayed impulse (wind-up frames before force appears). Add new test cases: wind-up crouch is visible (hips lower during wind-up), launch produces upward velocity only after wind-up completes, landing absorption lowers hips briefly, and jump cancel (if input is released during wind-up — decide policy: either always commit or allow cancel).
       - Touchpoints: `Assets/Tests/PlayMode/Character/JumpTests.cs`.
       - Done when: Jump test suite covers the full wind-up → launch → airborne → land → absorb lifecycle with measurable assertions at each phase.
       - Verification: `JumpTests` (full suite green), `FullStackSanityTests`.

## Verification gate

- Assets/Tests/PlayMode/Character/ArmAnimatorPlayModeTests.cs
- Assets/Tests/PlayMode/Character/JumpTests.cs
- Assets/Tests/PlayMode/Character/MovementQualityTests.cs
- Assets/Tests/PlayMode/Character/FullStackSanityTests.cs
- Assets/Tests/PlayMode/Character/Arena01BalanceStabilityTests.cs
- Assets/Tests/PlayMode/Character/HardSnapRecoveryTests.cs
- Assets/Tests/PlayMode/Character/SpinRecoveryTests.cs
- Assets/Tests/PlayMode/Character/StumbleStutterRegressionTests.cs
- Assets/Tests/PlayMode/Character/GetUpReliabilityTests.cs

## Exit criteria

- Motion style is recognizable, but control reliability remains intact.
- Jump shows a visible crouch → spring → airborne → land-absorb sequence driven by the character's legs, not an invisible upward force.
- No regression in walk, turn, terrain, recovery, or balance outcomes.