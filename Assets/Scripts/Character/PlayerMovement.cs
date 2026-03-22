using System;
using System.Collections.Generic;
using PhysicsDrivenMovement.Input;
using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Tracks the multi-frame jump sequence introduced in C8.5.
    /// </summary>
    public enum JumpPhase
    {
        None = 0,
        WindUp = 1,
        Launch = 2
    }

    /// <summary>
    /// Converts player input into locomotion forces for the active ragdoll hips body.
    /// This component belongs to the Character locomotion system and is responsible for
    /// camera-relative movement, speed limiting, jump impulse, and forwarding facing
    /// direction intent to <see cref="BalanceController"/> while latching transient button
    /// input in Update for the next physics step.
    /// Jump is only permitted when the character is grounded and in the
    /// <see cref="CharacterStateType.Standing"/> or <see cref="CharacterStateType.Moving"/>
    /// state; a one-frame consume prevents repeated impulses while the button is held.
    /// Lifecycle: caches dependencies in Awake, samples input in Update, and applies
    /// movement and jump forces in FixedUpdate.
    /// Collaborators: <see cref="BalanceController"/>, <see cref="CharacterState"/>,
    /// <see cref="Rigidbody"/>, <see cref="PlayerInputActions"/>.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class PlayerMovement : MonoBehaviour
    {
        private const int JumpTelemetryCapacity = 64;
        private const float MinimumSprintReachVelocityPreservationFactor = 0.85f;
        private const float MinimumSprintReachVelocityPreservationAcceleration = 28f;
        private const float MinimumSprintReachPostLandingGraceDuration = 0.7f;

        [SerializeField, Range(0f, 2000f)]
        private float _moveForce = 300f;

        [SerializeField, Range(0f, 20f)]
        private float _maxSpeed = 5f;

        [SerializeField, Range(1f, 3f)]
        [Tooltip("Multiplier applied to walk max speed and move force while sprint is held and the character is in Moving state.")]
        private float _sprintSpeedMultiplier = 1.8f;

        [SerializeField, Range(0.01f, 1f)]
        [Tooltip("Seconds for SprintNormalized to blend between walk and sprint while sprint becomes active or inactive.")]
        private float _sprintBlendDuration = 0.25f;

        [SerializeField, Range(0f, 500f)]
        [Tooltip("Impulse magnitude applied to the Hips Rigidbody on a valid jump. " +
                 "Jump is only allowed from Standing or Moving state while grounded.")]
        private float _jumpForce = 100f;

        [SerializeField, Range(0f, 3000f)]
        [Tooltip("Small input-directed horizontal launch impulse added on jump fire. " +
                 "Uses current input magnitude instead of sprint speed so standing jumps gain reach " +
                 "without turning airborne frames into full locomotion.")]
        private float _jumpLaunchHorizontalImpulse = 2600f;

        [Header("Jump Airborne Carry")]
        [SerializeField, Range(0f, 1f)]
        [Tooltip("Minimum fraction of the earned pre-jump horizontal speed to preserve while the character is in the intentional jump airborne window. " +
                 "Keeps sprint jumps carrying their run-up momentum without inflating jump height or adding full midair drive.")]
        private float _jumpAirborneVelocityPreservationFactor = 0.9f;

        [SerializeField, Range(0f, 40f)]
        [Tooltip("Maximum horizontal speed correction applied per second while preserving recent jump carry. " +
                 "Caps the anti-damping assist so sprint jumps stay heavy and touchdown recovery remains readable.")]
        private float _jumpAirborneVelocityPreservationAcceleration = 32f;

        [Header("Jump Air Control")]
        [SerializeField, Range(0f, 0.15f)]
        [Tooltip("Fraction of the normal grounded move force that may be applied as airborne correction during an intentional jump. " +
                 "Keeps midair WASD limited to landing trim instead of full steering.")]
        private float _jumpAirControlForceFraction = 0.15f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Additional multiplier applied when airborne input opposes the captured jump travel direction. " +
                 "Clamps reverse steering harder than same-direction or lateral trim so jumps cannot be meaningfully reversed in midair.")]
        private float _jumpAirControlOppositeDirectionMultiplier = 0.5f;

        [Header("Jump Wind-Up (C8.5)")]
        [SerializeField, Range(0f, 0.5f)]
        [Tooltip("Duration of the crouch wind-up before the jump impulse fires. " +
                 "During this time the character crouches and braces legs.")]
        private float _jumpWindUpDuration = 0.2f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Normalized wind-up progress after which a transient loss of grounding still commits the jump. " +
             "Keeps sprint-jump preload from aborting when the crouch briefly unweights the feet near takeoff.")]
        private float _jumpWindUpGroundLossCommitProgress = 0.85f;

        [SerializeField, Range(0f, 0.2f)]
        [Tooltip("Metres to lower the hips height-maintenance target during wind-up. " +
                 "Creates the visible crouch before launch.")]
        private float _jumpCrouchHeightOffset = 0.07f;

        [SerializeField, Range(0f, 90f)]
        [Tooltip("Extra knee-bend degrees applied to both legs during wind-up " +
                 "so the character visibly loads the spring.")]
        private float _jumpWindUpKneeBendBoost = 25f;

        [SerializeField, Range(0f, 0.3f)]
        [Tooltip("Duration of the leg-straighten window after the jump impulse fires. " +
                 "During this time both legs drive toward full extension.")]
        private float _jumpLaunchDuration = 0.1f;

        [Header("Jump State Bridge")]
        [SerializeField, Range(0f, 0.3f)]
        [Tooltip("Seconds after launch during which CharacterState may classify the jump as airborne " +
             "even if one foot sensor is still coasting grounded.")]
        private float _jumpAirborneStateGraceDuration = 0.12f;

        [SerializeField, Range(0f, 5f)]
        [Tooltip("Minimum upward hips velocity required during the recent-launch grace window " +
             "for CharacterState to treat the jump as airborne.")]
        private float _jumpAirborneStateVelocityThreshold = 0.1f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Seconds after landing (IsGrounded becomes true) during which " +
             "IsRecentJumpAirborne stays true. Gives downstream systems a window " +
             "to suppress surrender / boost upright torque through the landing impact.")]
        private float _jumpPostLandingGraceDuration = 0.65f;

        [SerializeField, Range(0f, 1080f)]
        [Tooltip("Maximum rate at which movement input may rotate the facing target sent to BalanceController. " +
                 "0 disables the slew limit and forwards the raw heading immediately.")]
        private float _maxFacingTurnRateDegPerSecond = 540f;

        [Header("Lean-Proportional Force Reduction")]
        [SerializeField, Range(0f, 60f)]
        [Tooltip("Lean angle (degrees) at which movement force begins to reduce. " +
                 "Below this angle, full force is applied. Prevents forward thrust " +
                 "from compounding stumbles.")]
        private float _leanReductionStartAngle = 10f;

        [SerializeField, Range(10f, 90f)]
        [Tooltip("Lean angle (degrees) at which movement force reaches its minimum multiplier. " +
                 "Between start and full, force scales linearly.")]
        private float _leanReductionFullAngle = 35f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Minimum movement force multiplier at or beyond the full lean angle. " +
                 "0 = no force at extreme lean, 0.2 = 20% force remains for some forward intent.")]
        private float _leanReductionMinMultiplier = 0.1f;

        [Header("Lean-Proportional Braking")]
        [SerializeField, Range(0f, 60f)]
        [Tooltip("Lean angle (degrees) at which horizontal braking begins. " +
                 "Actively decelerates the character when stumbling, bleeding off the " +
                 "kinetic energy that feeds the topple.")]
        private float _leanBrakingStartAngle = 15f;

        [SerializeField, Range(10f, 90f)]
        [Tooltip("Lean angle (degrees) at which braking reaches full strength.")]
        private float _leanBrakingFullAngle = 40f;

        [SerializeField, Range(0f, 500f)]
        [Tooltip("Maximum braking coefficient at full lean. Applied as a drag force " +
                 "proportional to horizontal velocity: F = -velocity * coefficient. " +
                 "Higher = stronger deceleration during stumbles.")]
        private float _leanBrakingCoefficient = 200f;

        [SerializeField]
        private Camera _camera;

        private Rigidbody _rb;
        private BalanceController _balance;
        private CharacterState _characterState;
        private LegAnimator _legAnimator;
        private PlayerInputActions _inputActions;
        private Vector2 _currentMoveInput;
        private Vector3 _currentFacingDirection = Vector3.forward;
        private bool _hasFacingDirection;
        private bool _hasReceivedMovementInput;

        /// <summary>
        /// True when the jump button was pressed this frame (or injected via test seam).
        /// Consumed (cleared) in FixedUpdate after the jump attempt is processed to
        /// enforce the one-frame consume rule - the impulse never fires twice per press.
        /// </summary>
        private bool _jumpPressedThisFrame;

        /// <summary>Current phase of the multi-frame jump sequence (C8.5).</summary>
        private JumpPhase _jumpPhase;

        /// <summary>Remaining wind-up time in seconds.</summary>
        private float _jumpWindUpTimer;

        /// <summary>Remaining launch-extension time in seconds.</summary>
        private float _jumpLaunchTimer;

        /// <summary>Remaining grace time used to bridge launch intent into CharacterState airborne classification.</summary>
        private float _jumpAirborneStateGraceTimer;

        /// <summary>Remaining post-landing grace time that keeps IsRecentJumpAirborne true through landing recovery.</summary>
        private float _jumpPostLandingGraceTimer;

        /// <summary>True once IsGrounded has been observed after the jump launched.</summary>
        private bool _jumpLandingDetected;

        /// <summary>True from jump launch until the character is confirmed grounded after the jump completes.</summary>
        private bool _recentJumpAirborne;

        /// <summary>World-space horizontal travel direction captured at jump launch for bounded airborne carry preservation.</summary>
        private Vector3 _jumpAirborneTravelDirection;

        /// <summary>Pre-jump horizontal speed along <see cref="_jumpAirborneTravelDirection"/> captured at launch.</summary>
        private float _jumpAirborneLaunchHorizontalSpeed;

        private readonly List<JumpTelemetryEvent> _jumpTelemetryLog = new List<JumpTelemetryEvent>(JumpTelemetryCapacity);
        private int _jumpAttemptCounter;
        private int _activeJumpAttemptId;

        /// <summary>
        /// Latest jump intent sampled for the current physics step before TryApplyJump consumes it.
        /// This lets higher-level coordination systems observe the same jump intent without
        /// changing the existing jump execution path.
        /// </summary>
        private bool _jumpRequestedThisPhysicsStep;

        /// <summary>
        /// Latest sampled held state of the Sprint button from Update.
        /// FixedUpdate snapshots this into <see cref="_sprintHeldThisPhysicsStep"/> so
        /// future physics-owned sprint logic uses a stable value for the current tick.
        /// </summary>
        private bool _sprintHeld;

        /// <summary>
        /// Sprint held state latched for the current physics step.
        /// This is intentionally separate from Update sampling so button state changes are
        /// observed once per physics tick instead of polling the Input System in FixedUpdate.
        /// </summary>
        private bool _sprintHeldThisPhysicsStep;

        /// <summary>
        /// Smoothed sprint blend exposed to downstream locomotion readers.
        /// 0 = walk, 1 = full sprint, with the value ramping over <see cref="_sprintBlendDuration"/>.
        /// </summary>
        private float _sprintNormalized;

        /// <summary>
        /// Override flag set by <see cref="SetJumpInputForTest"/>. When true,
        /// FixedUpdate reads <see cref="_jumpPressedThisFrame"/> directly and does not
        /// poll the Input System for the jump button.
        /// </summary>
        private bool _overrideJumpInput;

        /// <summary>
        /// Override flag set by <see cref="SetSprintInputForTest"/>. When true,
        /// Update keeps <see cref="_sprintHeld"/> at the test-provided held state and does
        /// not poll the Input System for sprint.
        /// </summary>
        private bool _overrideSprintInput;

        private bool _overrideMoveInput;

        /// <summary>Latest sampled movement input from the Player action map.</summary>
        public Vector2 CurrentMoveInput => _currentMoveInput;

        /// <summary>
        /// Latest world-space facing direction requested by movement input.
        /// Exposed so root-level locomotion systems can reason about intended travel direction.
        /// </summary>
        public Vector3 CurrentFacingDirection => _hasFacingDirection ? _currentFacingDirection : transform.forward;

        /// <summary>
        /// Current world-space travel direction implied by the active move input.
        /// Exposed so downstream systems can reason about commanded travel independent of facing slew.
        /// </summary>
        public Vector3 CurrentMoveWorldDirection
        {
            get
            {
                return TryGetMoveWorldDirection(_currentMoveInput, out Vector3 worldDirection)
                    ? worldDirection
                    : Vector3.zero;
            }
        }

        /// <summary>
        /// Smoothed sprint blend for downstream locomotion systems.
        /// 0 = walk, 1 = full sprint.
        /// </summary>
        public float SprintNormalized => _sprintNormalized;

        /// <summary>
        /// Current phase of the multi-frame jump sequence.
        /// <see cref="JumpPhase.None"/> outside of a jump.
        /// </summary>
        public JumpPhase CurrentJumpPhase => _jumpPhase;

        internal bool ShouldTreatJumpLaunchAsAirborne =>
            _jumpAirborneStateGraceTimer > 0f &&
            _rb != null &&
            _rb.linearVelocity.y > _jumpAirborneStateVelocityThreshold;

        /// <summary>
        /// True from jump launch until the character has been confirmed grounded
        /// after all jump phases and the airborne grace timer have completed.
        /// Used by BalanceController to apply a higher airborne upright multiplier
        /// during intentional jumps.
        /// </summary>
        public bool IsRecentJumpAirborne => _recentJumpAirborne;

        internal IReadOnlyList<JumpTelemetryEvent> JumpTelemetryLog => _jumpTelemetryLog;

        internal bool CurrentSprintHeld => _sprintHeldThisPhysicsStep;

        internal DesiredInput CurrentDesiredInput =>
            new DesiredInput(
                _currentMoveInput,
                CurrentMoveWorldDirection,
                CurrentFacingDirection,
                _jumpRequestedThisPhysicsStep,
                _sprintNormalized);

        /// <summary>
        /// Test seam: directly inject move input, bypassing the Input System.
        /// FixedUpdate will not overwrite this value while the override is active.
        /// Do not call from production code.
        /// </summary>
        public void SetMoveInputForTest(Vector2 input)
        {
            _currentMoveInput = input;
            _overrideMoveInput = true;
        }

        /// <summary>
        /// Test seam: directly inject a jump-button state, bypassing the Input System.
        /// When <paramref name="pressed"/> is <c>true</c>, a jump attempt will be made
        /// on the next FixedUpdate and then consumed (one-frame rule applies exactly as
        /// in production - call again with <c>true</c> to fire a second jump).
        /// FixedUpdate will not poll the Input System for jump while this override is active.
        /// Do not call from production code.
        /// </summary>
        /// <param name="pressed">
        /// <c>true</c> to simulate the jump button pressed this frame;
        /// <c>false</c> to simulate the button released (clears the pending jump).
        /// </param>
        public void SetJumpInputForTest(bool pressed)
        {
            _jumpPressedThisFrame = pressed;
            _jumpRequestedThisPhysicsStep = pressed;
            _overrideJumpInput = true;
        }

        /// <summary>
        /// Test seam: directly inject a sprint-button held state, bypassing the Input System.
        /// The override persists until the test calls this method again, matching the held-state
        /// behaviour of <see cref="SetMoveInputForTest"/> rather than the one-frame consume used
        /// by <see cref="SetJumpInputForTest"/>.
        /// Update will not poll the Input System for sprint while this override is active.
        /// Do not call from production code.
        /// </summary>
        /// <param name="held">
        /// <c>true</c> to simulate sprint held;
        /// <c>false</c> to simulate sprint released.
        /// </param>
        public void SetSprintInputForTest(bool held)
        {
            _sprintHeld = held;
            _sprintHeldThisPhysicsStep = held;
            _overrideSprintInput = true;
        }

        private void Awake()
        {
            // STEP 1: Cache required components on the Hips root object.
            if (!TryGetComponent(out _rb))
            {
                Debug.LogError("[PlayerMovement] Missing Rigidbody.", this);
                return;
            }

            if (!TryGetComponent(out _balance))
            {
                Debug.LogError("[PlayerMovement] Missing BalanceController.", this);
                return;
            }

            // STEP 2: Cache CharacterState - needed for jump gate (Standing/Moving only).
            //         CharacterState may be added after PlayerMovement in component order, so
            //         we attempt to cache here but also retry lazily in FixedUpdate on first use.
            TryGetComponent(out _characterState);

            // STEP 2b: Cache LegAnimator for jump wind-up gait suppression (C8.5).
            TryGetComponent(out _legAnimator);

            // STEP 3: Resolve a camera reference (serialized value preferred, main camera fallback).
            if (_camera == null)
            {
                _camera = Camera.main;
            }

            // STEP 4: Create and enable PlayerInputActions for the local movement map.
            _inputActions = new PlayerInputActions();
            _inputActions.Enable();

            // STEP 4a: Prefab-backed test rigs can retain older serialized values for new tuning
            //          fields. Clamp Slice 3's carry-preservation knobs to the tuned minimums so
            //          sprint reach behaves consistently until the prefab is resaved.
            _jumpAirborneVelocityPreservationFactor = Mathf.Max(
                _jumpAirborneVelocityPreservationFactor,
                MinimumSprintReachVelocityPreservationFactor);
            _jumpAirborneVelocityPreservationAcceleration = Mathf.Max(
                _jumpAirborneVelocityPreservationAcceleration,
                MinimumSprintReachVelocityPreservationAcceleration);
            _jumpPostLandingGraceDuration = Mathf.Max(
                _jumpPostLandingGraceDuration,
                MinimumSprintReachPostLandingGraceDuration);
            // STEP 4b: Do not force new air-control fields up to non-zero minimums here.
            //          Slice 4 must be able to disable airborne correction entirely on old
            //          prefab/test-rig instances by leaving the serialized fraction at 0.

            Vector3 initialFacing = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (initialFacing.sqrMagnitude < 0.001f)
            {
                initialFacing = Vector3.forward;
            }

            _currentFacingDirection = initialFacing.normalized;
            _hasFacingDirection = true;
            _jumpTelemetryLog.Clear();
            _jumpAttemptCounter = 0;
            _activeJumpAttemptId = 0;
        }

        private void FixedUpdate()
        {
            // STEP 0: Read Move action (Vector2) into _currentMoveInput once per physics tick.
            if (!_overrideMoveInput)
            {
                if (_inputActions == null)
                {
                    _currentMoveInput = Vector2.zero;
                }
                else
                {
                    _currentMoveInput = _inputActions.Player.Move.ReadValue<Vector2>();
                }
            }

            // STEP 1: Snapshot button states sampled in Update for this physics tick.
            //         Jump stays edge-latched until TryApplyJump consumes it, while Sprint
            //         captures the current held state for future sprint-speed logic.
            _jumpRequestedThisPhysicsStep = _jumpPressedThisFrame;
            _sprintHeldThisPhysicsStep = _sprintHeld;

            // STEP 1b: Blend the sprint output using the latched physics-step state so
            //          downstream locomotion readers observe one stable ramp per physics tick.
            UpdateSprintNormalized();

            if (_jumpAirborneStateGraceTimer > 0f)
            {
                _jumpAirborneStateGraceTimer = Mathf.Max(0f, _jumpAirborneStateGraceTimer - Time.fixedDeltaTime);
            }

            // Track when the character first touches down after a jump launch.
            // Start the post-landing grace timer so IsRecentJumpAirborne persists
            // through the landing impact window.
            if (_recentJumpAirborne && _balance != null && _balance.IsGrounded)
            {
                if (!_jumpLandingDetected)
                {
                    _jumpLandingDetected = true;
                    _jumpPostLandingGraceTimer = _jumpPostLandingGraceDuration;
                }
                else if (_jumpPostLandingGraceTimer > 0f)
                {
                    _jumpPostLandingGraceTimer -= Time.fixedDeltaTime;
                }
            }

            // Clear the recent-jump-airborne flag once the character is confirmed
            // grounded, all jump phases have completed, and the post-landing
            // grace window has elapsed.
            if (_recentJumpAirborne &&
                _jumpPhase == JumpPhase.None &&
                _jumpAirborneStateGraceTimer <= 0f &&
                _jumpLandingDetected &&
                _jumpPostLandingGraceTimer <= 0f &&
                _balance != null && _balance.IsGrounded)
            {
                _recentJumpAirborne = false;
                _jumpLandingDetected = false;
            }

            // STEP 2: Early-out when required dependencies are missing.
            if (_rb == null || _balance == null)
            {
                return;
            }

            if (_camera == null)
            {
                _camera = Camera.main;
            }

            // STEP 3: Attempt jump before movement forces.
            //         Jump is gated on:
            //           (a) jump input pressed this frame,
            //           (b) CharacterState is Standing or Moving,
            //           (c) BalanceController.IsGrounded is true.
            //         The input flag is consumed immediately regardless of whether the
            //         jump succeeded, enforcing the one-frame consume rule.
            //         C8.5: TryApplyJump now enters a wind-up phase. TickJumpWindUp
            //         counts down the wind-up and fires the impulse when it expires.
            //         C8.5b: TickJumpLaunch drives leg extension after the impulse.
            TryApplyJump();
            TickJumpWindUp();
            TickJumpLaunch();

            // STEP 4: Preserve a bounded slice of earned sprint carry during the intentional
            //         airborne window before deciding whether full locomotion is suppressed.
            ApplyRecentJumpAirborneVelocityPreservation();

            // STEP 4a: Allow only a tiny midair correction path during recent intentional jumps.
            //          This bypasses the full locomotion suppression gate on purpose, but the
            //          applied force stays capped far below grounded movement authority.
            ApplyRecentJumpAirborneCorrectionForce(_currentMoveInput);

            // STEP 5: Movement forces. Skip when the character is in a confirmed fall/collapse path.
            if (!ShouldSuppressLocomotion())
            {
                ApplyMovementForces(_currentMoveInput);
            }

            // STEP 6: Lean-proportional braking. Applied regardless of locomotion suppression
            //         because the goal is to bleed off existing horizontal momentum that feeds
            //         the topple, not to add new movement.
            ApplyLeanBraking();
        }

        private void Update()
        {
            // STEP 1: Sample transient button input in Update so edge-triggered presses are
            //         not missed when multiple render frames occur before the next physics tick.
            if (_inputActions == null)
            {
                return;
            }

            // STEP 2: Latch jump press edges until FixedUpdate consumes them.
            //         OR-assignment preserves a press that happened on any render frame
            //         since the last physics step.
            if (!_overrideJumpInput)
            {
                _jumpPressedThisFrame |= _inputActions.Player.Jump.WasPressedThisFrame();
            }

            // STEP 3: Keep Sprint as a held-state sample so FixedUpdate can snapshot the
            //         latest button state without polling the Input System from physics code.
            if (!_overrideSprintInput)
            {
                _sprintHeld = _inputActions.Player.Sprint.IsPressed();
            }
        }

        private void OnDestroy()
        {
            // STEP 1: Dispose input actions to release Input System resources.
            if (_inputActions != null)
            {
                _inputActions.Dispose();
                _inputActions = null;
            }
        }

        /// <summary>
        /// Evaluates the jump gate and, if all conditions are met, begins the multi-frame
        /// jump wind-up sequence (C8.5). The jump input flag is consumed (cleared)
        /// unconditionally so the impulse cannot repeat while the button is held.
        ///
        /// Gate conditions (ALL must be true):
        ///   1. <see cref="_jumpPressedThisFrame"/> is set.
        ///   2. <see cref="CharacterState.CurrentState"/> is
        ///      <see cref="CharacterStateType.Standing"/> or
        ///      <see cref="CharacterStateType.Moving"/>.
        ///   3. <see cref="BalanceController.IsGrounded"/> is true.
        ///   4. No wind-up is already in progress.
        /// </summary>
        private void TryApplyJump()
        {
            // Always consume the jump flag first - this is the one-frame consume.
            // Doing it unconditionally ensures that even a rejected jump cannot fire
            // on a later frame from the same button press.
            bool wantsJump = _jumpPressedThisFrame;
            _jumpPressedThisFrame = false;

            // When using the test seam, reset the override so the next frame is
            // clean unless the test explicitly calls SetJumpInputForTest again.
            if (_overrideJumpInput)
            {
                _overrideJumpInput = false;
            }

            if (!wantsJump)
            {
                return;
            }

            int attemptId = ++_jumpAttemptCounter;

            // Gate 0: already winding up — consume the input but don't restart.
            if (_jumpPhase != JumpPhase.None)
            {
                EmitJumpTelemetry(attemptId, JumpTelemetryEventType.RequestRejected, "sequence_in_progress");
                return;
            }

            // Gate 1: CharacterState must be Standing or Moving.
            // Lazy-resolve in case CharacterState was added after PlayerMovement in component order.
            if (_characterState == null)
            {
                TryGetComponent(out _characterState);
            }

            if (_characterState == null)
            {
                EmitJumpTelemetry(attemptId, JumpTelemetryEventType.RequestRejected, "missing_character_state");
                return;
            }

            CharacterStateType state = _characterState.CurrentState;
            bool canJumpFromState = state == CharacterStateType.Standing ||
                                    state == CharacterStateType.Moving;
            if (!canJumpFromState)
            {
                EmitJumpTelemetry(
                    attemptId,
                    JumpTelemetryEventType.RequestRejected,
                    "state_not_jumpable:" + state);
                return;
            }

            // Gate 2: must be grounded.
            if (!_balance.IsGrounded)
            {
                EmitJumpTelemetry(attemptId, JumpTelemetryEventType.RequestRejected, "not_grounded");
                return;
            }

            EmitJumpTelemetry(attemptId, JumpTelemetryEventType.JumpAccepted, "accepted");
            _activeJumpAttemptId = attemptId;

            // All gates passed — enter wind-up phase (C8.5a).
            if (_jumpWindUpDuration > 0f)
            {
                _jumpPhase = JumpPhase.WindUp;
                _jumpWindUpTimer = _jumpWindUpDuration;
                _balance.SetJumpCrouchOffset(-_jumpCrouchHeightOffset);
                if (_legAnimator != null)
                {
                    _legAnimator.SetJumpWindUp(true, _jumpWindUpKneeBendBoost);
                }

                EmitJumpTelemetry(attemptId, JumpTelemetryEventType.WindUpEntered, "wind_up_started");
            }
            else
            {
                // Zero-duration wind-up: fire immediately (legacy behaviour / test override).
                _rb.AddForce(Vector3.up * _jumpForce, ForceMode.Impulse);
                BeginJumpAirborneStateGrace();
                EmitJumpTelemetry(attemptId, JumpTelemetryEventType.LaunchFired, "launch_without_wind_up");
                _activeJumpAttemptId = 0;
            }
        }

        /// <summary>
        /// Ticks the jump wind-up timer each FixedUpdate. When the timer expires the
        /// upward impulse fires and all wind-up overrides are cleared.
        /// </summary>
        private void TickJumpWindUp()
        {
            if (_jumpPhase != JumpPhase.WindUp)
            {
                return;
            }

            // Abort wind-up if the character fell. If the accepted jump briefly loses
            // grounding near the end of the crouch, commit the launch instead of
            // cancelling the sequence — the preload itself can unweight the feet.
            if (_balance.IsFallen)
            {
                EmitJumpTelemetry(_activeJumpAttemptId, JumpTelemetryEventType.WindUpAborted, "became_fallen");
                ClearJumpSequence();
                return;
            }

            if (!_balance.IsGrounded)
            {
                if (ShouldCommitJumpAfterGroundLoss())
                {
                    FireJumpLaunch("wind_up_completed_after_ground_loss");
                    return;
                }

                EmitJumpTelemetry(_activeJumpAttemptId, JumpTelemetryEventType.WindUpAborted, "lost_grounded");
                ClearJumpSequence();
                return;
            }

            _jumpWindUpTimer -= Time.fixedDeltaTime;

            if (_jumpWindUpTimer > 0f)
            {
                return;
            }

            FireJumpLaunch("wind_up_completed");
        }

        /// <summary>
        /// Ticks the jump launch timer each FixedUpdate. When the timer expires the
        /// leg-extension override is cleared. The launch phase naturally overlaps with
        /// early airborne — LegAnimator clears the flag on Airborne entry.
        /// </summary>
        private void TickJumpLaunch()
        {
            if (_jumpPhase != JumpPhase.Launch)
            {
                return;
            }

            _jumpLaunchTimer -= Time.fixedDeltaTime;

            if (_jumpLaunchTimer > 0f)
            {
                return;
            }

            ClearJumpLaunch();
        }

        private void ClearJumpLaunch()
        {
            _jumpPhase = JumpPhase.None;
            _jumpLaunchTimer = 0f;
            _activeJumpAttemptId = 0;
            if (_legAnimator != null)
            {
                _legAnimator.SetJumpLaunch(false);
            }
        }

        private bool ShouldCommitJumpAfterGroundLoss()
        {
            if (_jumpWindUpDuration <= 0f)
            {
                return false;
            }

            float normalizedProgress = 1f - Mathf.Clamp01(_jumpWindUpTimer / _jumpWindUpDuration);
            return normalizedProgress >= _jumpWindUpGroundLossCommitProgress;
        }

        private void FireJumpLaunch(string telemetryReason)
        {
            // STEP 1: Preserve the grounded, short-leg read by keeping the vertical launch
            //         force stable and adding any extra standing reach as a small input-shaped
            //         horizontal impulse instead of inflating apex height.
            Vector3 launchImpulse = Vector3.up * _jumpForce;
            Vector3 launchDirection = Vector3.zero;
            bool hasLaunchDirection = TryGetMoveWorldDirection(_currentMoveInput, out launchDirection);
            if (hasLaunchDirection)
            {
                float launchInputMagnitude = Mathf.Clamp01(_currentMoveInput.magnitude);
                // STEP 1a: Keep slice 2 focused on standing reach only. Sprint-specific carry tuning
                //          belongs to the next slice, so full sprint ramp should not inherit any
                //          extra horizontal launch shove from the standing-reach mechanism.
                float sprintLaunchBonusMultiplier = Mathf.Lerp(1f, 0f, _sprintNormalized);
                launchImpulse += launchDirection * (_jumpLaunchHorizontalImpulse * launchInputMagnitude * sprintLaunchBonusMultiplier);
            }

            Vector3 launchHorizontalVelocity = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z);
            CaptureJumpAirborneCarryBaseline(launchHorizontalVelocity, hasLaunchDirection ? launchDirection : Vector3.zero);

            _rb.AddForce(launchImpulse, ForceMode.Impulse);
            _recentJumpAirborne = true;
            BeginJumpAirborneStateGrace();
            EmitJumpTelemetry(_activeJumpAttemptId, JumpTelemetryEventType.LaunchFired, telemetryReason);

            _jumpWindUpTimer = 0f;
            _balance.ClearJumpCrouchOffset();
            if (_legAnimator != null)
            {
                _legAnimator.SetJumpWindUp(false, 0f);
            }

            if (_jumpLaunchDuration > 0f)
            {
                _jumpPhase = JumpPhase.Launch;
                _jumpLaunchTimer = _jumpLaunchDuration;
                if (_legAnimator != null)
                {
                    _legAnimator.SetJumpLaunch(true);
                }
            }
            else
            {
                _jumpPhase = JumpPhase.None;
                _activeJumpAttemptId = 0;
            }
        }

        /// <summary>
        /// Aborts the entire jump sequence (wind-up or launch) and clears all overrides.
        /// Used when the character falls or loses ground during the jump preparation.
        /// </summary>
        private void ClearJumpSequence()
        {
            _jumpPhase = JumpPhase.None;
            _jumpWindUpTimer = 0f;
            _jumpLaunchTimer = 0f;
            _jumpAirborneStateGraceTimer = 0f;
            _activeJumpAttemptId = 0;
            _balance.ClearJumpCrouchOffset();
            if (_legAnimator != null)
            {
                _legAnimator.SetJumpWindUp(false, 0f);
                _legAnimator.SetJumpLaunch(false);
            }
        }

        private void BeginJumpAirborneStateGrace()
        {
            _jumpAirborneStateGraceTimer = Mathf.Max(_jumpAirborneStateGraceDuration, Time.fixedDeltaTime);
        }

        private void CaptureJumpAirborneCarryBaseline(Vector3 preLaunchHorizontalVelocity, Vector3 inputLaunchDirection)
        {
            // STEP 1b: Preserve earned sprint carry from the run-up itself instead of sneaking extra
            //          forward launch force into sprint jumps. If input is missing, fall back to the
            //          actual horizontal travel direction so the airborne assist tracks real momentum.
            Vector3 travelDirection = inputLaunchDirection;
            if (travelDirection.sqrMagnitude < 0.0001f && preLaunchHorizontalVelocity.sqrMagnitude > 0.0001f)
            {
                travelDirection = preLaunchHorizontalVelocity.normalized;
            }

            if (travelDirection.sqrMagnitude < 0.0001f)
            {
                _jumpAirborneTravelDirection = Vector3.zero;
                _jumpAirborneLaunchHorizontalSpeed = 0f;
                return;
            }

            travelDirection.Normalize();
            _jumpAirborneTravelDirection = travelDirection;
            _jumpAirborneLaunchHorizontalSpeed = Mathf.Max(0f, Vector3.Dot(preLaunchHorizontalVelocity, travelDirection));
        }

        private void ApplyRecentJumpAirborneVelocityPreservation()
        {
            if (_rb == null || _balance == null)
            {
                return;
            }

            if (!_recentJumpAirborne || _balance.IsGrounded)
            {
                return;
            }

            if (_jumpAirborneLaunchHorizontalSpeed <= 0f || _jumpAirborneTravelDirection.sqrMagnitude < 0.0001f)
            {
                return;
            }

            Vector3 horizontalVelocity = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z);
            float currentSpeedAlongLaunch = Vector3.Dot(horizontalVelocity, _jumpAirborneTravelDirection);
            float preservedSpeedFloor = _jumpAirborneLaunchHorizontalSpeed * _jumpAirborneVelocityPreservationFactor;
            if (currentSpeedAlongLaunch >= preservedSpeedFloor)
            {
                return;
            }

            float missingSpeed = preservedSpeedFloor - currentSpeedAlongLaunch;
            float maxSpeedCorrectionThisStep = _jumpAirborneVelocityPreservationAcceleration * Time.fixedDeltaTime;
            float appliedSpeedCorrection = Mathf.Min(missingSpeed, maxSpeedCorrectionThisStep);

            // STEP 1c: Use VelocityChange so this acts like a bounded anti-damping path during
            //          the intentional jump window. It restores only lost forward carry and does
            //          not add lateral steering or extra vertical energy.
            _rb.AddForce(_jumpAirborneTravelDirection * appliedSpeedCorrection, ForceMode.VelocityChange);
        }

        private void ApplyRecentJumpAirborneCorrectionForce(Vector2 moveInput)
        {
            if (_rb == null || _balance == null)
            {
                return;
            }

            if (!_recentJumpAirborne || _balance.IsGrounded)
            {
                return;
            }

            if (moveInput.sqrMagnitude < 0.0001f)
            {
                return;
            }

            if (!TryGetMoveWorldDirection(moveInput, out Vector3 worldDirection))
            {
                return;
            }

            Vector3 correctionDirection = worldDirection;
            if (_jumpAirborneTravelDirection.sqrMagnitude > 0.0001f)
            {
                float alignment = Vector3.Dot(worldDirection, _jumpAirborneTravelDirection);
                Vector3 alignedComponent = _jumpAirborneTravelDirection * alignment;
                Vector3 lateralComponent = worldDirection - alignedComponent;

                if (alignment > 0f)
                {
                    // STEP 1d: Treat this as correction, not bonus propulsion. Preserve the
                    //          Slice 3 carry path for earned forward travel and spend the bounded
                    //          air-control budget on lateral trim plus limited reverse braking.
                    correctionDirection = lateralComponent;
                }
                else if (alignment < 0f)
                {
                    correctionDirection = lateralComponent + (alignedComponent * _jumpAirControlOppositeDirectionMultiplier);
                }
            }

            float correctionMagnitude = correctionDirection.magnitude;
            if (correctionMagnitude <= 0.0001f)
            {
                return;
            }

            float airControlForce = _moveForce * _jumpAirControlForceFraction * Mathf.Clamp01(correctionMagnitude);
            if (airControlForce <= 0f)
            {
                return;
            }

            Vector3 normalizedCorrectionDirection = correctionDirection / correctionMagnitude;

            // STEP 1e: Bypass the full locomotion suppression gate for this narrow path only.
            //          Force mode stays as continuous Force, capped to 15% of grounded authority,
            //          so the player can trim landing placement without generating arcade reversal.
            _rb.AddForce(normalizedCorrectionDirection * airControlForce, ForceMode.Force);

            if (normalizedCorrectionDirection.sqrMagnitude > 0.01f)
            {
                UpdateFacingDirection(normalizedCorrectionDirection, forceImmediateFacing: false);
                _hasReceivedMovementInput = true;
            }
        }

        private void EmitJumpTelemetry(int attemptId, JumpTelemetryEventType eventType, string reason)
        {
            if (attemptId <= 0)
            {
                return;
            }

            if (_jumpTelemetryLog.Count >= JumpTelemetryCapacity)
            {
                _jumpTelemetryLog.RemoveAt(0);
            }

            CharacterStateType characterState = _characterState != null
                ? _characterState.CurrentState
                : CharacterStateType.Standing;
            bool isGrounded = _balance != null && _balance.IsGrounded;
            bool isFallen = _balance != null && _balance.IsFallen;

            _jumpTelemetryLog.Add(new JumpTelemetryEvent(
                attemptId,
                Time.frameCount,
                Time.time,
                eventType,
                reason,
                characterState,
                isGrounded,
                isFallen,
                _jumpPhase));
        }

        private void ApplyMovementForces(Vector2 moveInput)
        {
            // STEP 1: Convert 2D move input into camera-relative XZ world movement direction.
            //         We derive movement direction from the camera's YAW only (not its full
            //         forward vector) so steep pitch angles don't affect WASD direction.
            //         Extracting yaw from the camera's euler angles gives a stable flat
            //         forward regardless of how far up or down the camera is pitched.
            // STEP 2: Skip force application when input is near zero or the active walk/sprint
            //         speed tier is already capped.
            // STEP 3: Apply force using ForceMode.Force, scaling both cap and force when sprint
            //         is held in the Moving state, then update facing direction.
            if (_rb == null || _balance == null || ShouldSuppressLocomotion())
            {
                return;
            }

            if (moveInput.sqrMagnitude < 0.0001f)
            {
                return;
            }

            if (!TryGetMoveWorldDirection(moveInput, out Vector3 worldDirection))
            {
                return;
            }

            Vector3 horizontalVelocity = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z);
            float sprintMovementMultiplier = GetSprintMovementMultiplier();
            float activeMaxSpeed = _maxSpeed * sprintMovementMultiplier;
            float activeMoveForce = _moveForce * sprintMovementMultiplier;

            // During the early post-landing window, ramp ground drive back up smoothly
            // so the jump recovery stays stable even after a slightly longer launch reach.
            if (_recentJumpAirborne && _balance != null && _jumpPostLandingGraceTimer > 0f)
            {
                // STEP 3a: Reintroduce ground drive across the full landing-grace window
                //          instead of snapping back to full push halfway through recovery.
                //          Slice 2 only needs launch reach; it should not re-accelerate the
                //          ragdoll into a faceplant on the first grounded frames.
                float landingProgress = 1f - (_jumpPostLandingGraceTimer / Mathf.Max(0.0001f, _jumpPostLandingGraceDuration));
                // STEP 3b: Ease the landing-drive return with a squared ramp so the extra sprint
                //          carry does not immediately shove the ragdoll past the recovery posture.
                activeMoveForce *= Mathf.Clamp01(landingProgress * landingProgress);
            }

            if (horizontalVelocity.magnitude < activeMaxSpeed)
            {
                float leanMultiplier = GetLeanForceMultiplier();
                _rb.AddForce(worldDirection * (activeMoveForce * leanMultiplier), ForceMode.Force);
            }

            if (worldDirection.sqrMagnitude > 0.01f)
            {
                bool forceImmediateFacing = !_hasReceivedMovementInput;
                UpdateFacingDirection(worldDirection, forceImmediateFacing);
                _hasReceivedMovementInput = true;
            }
        }

        private bool TryGetMoveWorldDirection(Vector2 moveInput, out Vector3 worldDirection)
        {
            worldDirection = Vector3.zero;

            if (_camera != null)
            {
                // Use camera yaw only so look pitch never changes the horizontal move direction.
                float cameraYaw = _camera.transform.eulerAngles.y;
                Vector3 cameraForward = Quaternion.Euler(0f, cameraYaw, 0f) * Vector3.forward;
                Vector3 cameraRight = Quaternion.Euler(0f, cameraYaw, 0f) * Vector3.right;
                worldDirection = cameraRight * moveInput.x + cameraForward * moveInput.y;
            }
            else
            {
                worldDirection = new Vector3(moveInput.x, 0f, moveInput.y);
            }

            if (worldDirection.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            worldDirection.Normalize();
            return true;
        }

        private void UpdateFacingDirection(Vector3 desiredWorldDirection, bool forceImmediateFacing)
        {
            Vector3 desiredFacing = new Vector3(desiredWorldDirection.x, 0f, desiredWorldDirection.z);
            if (desiredFacing.sqrMagnitude < 0.001f)
            {
                return;
            }

            desiredFacing.Normalize();

            if (forceImmediateFacing || !_hasFacingDirection || _maxFacingTurnRateDegPerSecond <= 0f)
            {
                _currentFacingDirection = desiredFacing;
                _hasFacingDirection = true;
            }
            else
            {
                float maxRadiansDelta = _maxFacingTurnRateDegPerSecond * Mathf.Deg2Rad * Time.fixedDeltaTime;
                _currentFacingDirection = Vector3.RotateTowards(
                    _currentFacingDirection,
                    desiredFacing,
                    maxRadiansDelta,
                    0f);

                if (_currentFacingDirection.sqrMagnitude < 0.001f)
                {
                    _currentFacingDirection = desiredFacing;
                }
                else
                {
                    _currentFacingDirection.Normalize();
                }
            }
        }

        private float GetSprintMovementMultiplier()
        {
            if (!IsSprintSpeedTierActive())
            {
                return 1f;
            }

            return _sprintSpeedMultiplier;
        }

        private bool IsSprintSpeedTierActive()
        {
            if (!_sprintHeldThisPhysicsStep)
            {
                return false;
            }

            if (_characterState == null)
            {
                TryGetComponent(out _characterState);
            }

            if (_characterState == null)
            {
                return false;
            }

            return _characterState.CurrentState == CharacterStateType.Moving;
        }

        private void UpdateSprintNormalized()
        {
            // STEP 1: Convert the current sprint activation state into a stable walk/sprint target.
            float targetSprintNormalized = GetSprintNormalizedTarget();

            // STEP 2: Snap immediately only if the blend duration was tuned to a degenerate value.
            if (_sprintBlendDuration <= 0.0001f)
            {
                _sprintNormalized = targetSprintNormalized;
                return;
            }

            // STEP 3: Move toward the target at a constant rate so the full 0→1 or 1→0
            //         transition takes roughly _sprintBlendDuration seconds.
            float maxDelta = Time.fixedDeltaTime / _sprintBlendDuration;
            _sprintNormalized = Mathf.MoveTowards(_sprintNormalized, targetSprintNormalized, maxDelta);
        }

        private float GetSprintNormalizedTarget()
        {
            if (_currentMoveInput.sqrMagnitude < 0.0001f)
            {
                return 0f;
            }

            return IsSprintSpeedTierActive() ? 1f : 0f;
        }

        private bool ShouldSuppressLocomotion()
        {
            if (_balance != null)
            {
                if (_balance.IsFallen)
                {
                    return true;
                }

                if (_recentJumpAirborne && !_balance.IsGrounded)
                {
                    return true;
                }
            }

            if (_characterState == null)
            {
                TryGetComponent(out _characterState);
            }

            if (_characterState == null)
            {
                return false;
            }

            return _characterState.CurrentState == CharacterStateType.Fallen ||
                   _characterState.CurrentState == CharacterStateType.GettingUp;
        }

        private float GetLeanForceMultiplier()
        {
            if (_balance == null)
            {
                return 1f;
            }

            float lean = _balance.UprightAngle;
            if (lean <= _leanReductionStartAngle)
            {
                return 1f;
            }

            float range = _leanReductionFullAngle - _leanReductionStartAngle;
            if (range <= 0f)
            {
                return _leanReductionMinMultiplier;
            }

            float t = Mathf.Clamp01((lean - _leanReductionStartAngle) / range);
            return Mathf.Lerp(1f, _leanReductionMinMultiplier, t);
        }

        private void ApplyLeanBraking()
        {
            if (_balance == null || _rb == null)
            {
                return;
            }

            float lean = _balance.UprightAngle;
            if (lean <= _leanBrakingStartAngle)
            {
                return;
            }

            float range = _leanBrakingFullAngle - _leanBrakingStartAngle;
            if (range <= 0f)
            {
                return;
            }

            float brakeT = Mathf.Clamp01((lean - _leanBrakingStartAngle) / range);
            Vector3 horizontalVel = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z);
            _rb.AddForce(-horizontalVel * (_leanBrakingCoefficient * brakeT), ForceMode.Force);
        }
    }
}
