using System;
using PhysicsDrivenMovement.Input;
using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Converts player input into locomotion forces for the active ragdoll hips body.
    /// This component belongs to the Character locomotion system and is responsible for
    /// camera-relative movement, speed limiting, jump impulse, and forwarding facing
    /// direction intent to <see cref="BalanceController"/>.
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
        [SerializeField, Range(0f, 2000f)]
        private float _moveForce = 300f;

        [SerializeField, Range(0f, 20f)]
        private float _maxSpeed = 5f;

        [SerializeField, Range(0f, 500f)]
        [Tooltip("Impulse magnitude applied to the Hips Rigidbody on a valid jump. " +
                 "Jump is only allowed from Standing or Moving state while grounded.")]
        private float _jumpForce = 100f;

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
        private LocomotionCollapseDetector _collapseDetector;
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

        /// <summary>
        /// Override flag set by <see cref="SetJumpInputForTest"/>. When true,
        /// FixedUpdate reads <see cref="_jumpPressedThisFrame"/> directly and does not
        /// poll the Input System for the jump button.
        /// </summary>
        private bool _overrideJumpInput;

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
            _overrideJumpInput = true;
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
            TryGetComponent(out _collapseDetector);

            // STEP 3: Resolve a camera reference (serialized value preferred, main camera fallback).
            if (_camera == null)
            {
                _camera = Camera.main;
            }

            if (_collapseDetector == null)
            {
                TryGetComponent(out _collapseDetector);
            }

            // STEP 4: Create and enable PlayerInputActions for the local movement map.
            _inputActions = new PlayerInputActions();
            _inputActions.Enable();

            Vector3 initialFacing = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (initialFacing.sqrMagnitude < 0.001f)
            {
                initialFacing = Vector3.forward;
            }

            _currentFacingDirection = initialFacing.normalized;
            _hasFacingDirection = true;
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

            // STEP 1: Read Jump action (button, WasPressedThisFrame) unless overridden.
            //         Use WasPressedThisFrame so the impulse fires on the leading edge
            //         of the button press and cannot repeat while held.
            if (!_overrideJumpInput)
            {
                _jumpPressedThisFrame = _inputActions != null &&
                                        _inputActions.Player.Jump.WasPressedThisFrame();
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
            TryApplyJump();

            // STEP 4: Movement forces. Skip when the character is in a confirmed fall/collapse path.
            if (!ShouldSuppressLocomotion())
            {
                ApplyMovementForces(_currentMoveInput);
            }

            // STEP 5: Lean-proportional braking. Applied regardless of locomotion suppression
            //         because the goal is to bleed off existing horizontal momentum that feeds
            //         the topple, not to add new movement.
            ApplyLeanBraking();
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
        /// Evaluates the jump gate and, if all conditions are met, applies a single upward
        /// impulse to the Hips Rigidbody. The jump input flag is consumed (cleared)
        /// unconditionally at the end of this method, enforcing the one-frame consume rule
        /// so the impulse cannot repeat while the button is held.
        ///
        /// Gate conditions (ALL must be true):
        ///   1. <see cref="_jumpPressedThisFrame"/> is set.
        ///   2. <see cref="CharacterState.CurrentState"/> is
        ///      <see cref="CharacterStateType.Standing"/> or
        ///      <see cref="CharacterStateType.Moving"/>.
        ///   3. <see cref="BalanceController.IsGrounded"/> is true.
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

            // Gate 1: CharacterState must be Standing or Moving.
            // Lazy-resolve in case CharacterState was added after PlayerMovement in component order.
            if (_characterState == null)
            {
                TryGetComponent(out _characterState);
            }

            if (_characterState == null)
            {
                return;
            }

            CharacterStateType state = _characterState.CurrentState;
            bool canJumpFromState = state == CharacterStateType.Standing ||
                                    state == CharacterStateType.Moving;
            if (!canJumpFromState)
            {
                return;
            }

            // Gate 2: must be grounded.
            if (!_balance.IsGrounded)
            {
                return;
            }

            // All gates passed - apply impulse.
            _rb.AddForce(Vector3.up * _jumpForce, ForceMode.Impulse);
        }

        private void ApplyMovementForces(Vector2 moveInput)
        {
            // STEP 1: Convert 2D move input into camera-relative XZ world movement direction.
            //         We derive movement direction from the camera's YAW only (not its full
            //         forward vector) so steep pitch angles don't affect WASD direction.
            //         Extracting yaw from the camera's euler angles gives a stable flat
            //         forward regardless of how far up or down the camera is pitched.
            // STEP 2: Skip force application when input is near zero or speed is capped.
            // STEP 3: Apply force using ForceMode.Force and update facing direction.
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
            if (horizontalVelocity.magnitude < _maxSpeed)
            {
                float leanMultiplier = GetLeanForceMultiplier();
                _rb.AddForce(worldDirection * (_moveForce * leanMultiplier), ForceMode.Force);
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

            _balance.SetFacingDirection(_currentFacingDirection);
        }

        private bool ShouldSuppressLocomotion()
        {
            if (_balance != null && _balance.IsFallen)
            {
                return true;
            }

            if (_characterState == null)
            {
                TryGetComponent(out _characterState);
            }

            if (_collapseDetector == null)
            {
                TryGetComponent(out _collapseDetector);
            }

            if (_collapseDetector != null && _collapseDetector.IsCollapseConfirmed)
            {
                return true;
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
