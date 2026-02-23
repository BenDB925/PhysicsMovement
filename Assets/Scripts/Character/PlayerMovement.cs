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

        [SerializeField]
        private Camera _camera;

        private Rigidbody _rb;
        private BalanceController _balance;
        private CharacterState _characterState;
        private PlayerInputActions _inputActions;
        private Vector2 _currentMoveInput;

        // ─── Jump state ────────────────────────────────────────────────────

        /// <summary>
        /// True when the jump button was pressed this frame (or injected via test seam).
        /// Consumed (cleared) in FixedUpdate after the jump attempt is processed to
        /// enforce the one-frame consume rule — the impulse never fires twice per press.
        /// </summary>
        private bool _jumpPressedThisFrame;

        /// <summary>
        /// Override flag set by <see cref="SetJumpInputForTest"/>. When true,
        /// FixedUpdate reads <see cref="_jumpPressedThisFrame"/> directly and does not
        /// poll the Input System for the jump button.
        /// </summary>
        private bool _overrideJumpInput;

        // ─── Move input override ───────────────────────────────────────────

        private bool _overrideMoveInput;

        // ─── Public Properties ─────────────────────────────────────────────

        /// <summary>Latest sampled movement input from the Player action map.</summary>
        public Vector2 CurrentMoveInput => _currentMoveInput;

        // ─── Test Seams ────────────────────────────────────────────────────

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
        /// in production — call again with <c>true</c> to fire a second jump).
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

        // ─── Unity Lifecycle ────────────────────────────────────────────────

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

            // STEP 2: Cache CharacterState — needed for jump gate (Standing/Moving only).
            //         CharacterState may be added after PlayerMovement in component order, so
            //         we attempt to cache here but also retry lazily in FixedUpdate on first use.
            TryGetComponent(out _characterState);

            // STEP 3: Resolve a camera reference (serialized value preferred, main camera fallback).
            if (_camera == null)
            {
                _camera = Camera.main;
            }

            // STEP 4: Create and enable PlayerInputActions for the local movement map.
            _inputActions = new PlayerInputActions();
            _inputActions.Enable();
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

            // STEP 4: Movement forces. Skip when character is fallen.
            if (!_balance.IsFallen)
            {
                ApplyMovementForces(_currentMoveInput);
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

        // ─── Private Helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Evaluates the jump gate and, if all conditions are met, applies a single upward
        /// impulse to the Hips Rigidbody.  The jump input flag is consumed (cleared)
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
            // Always consume the jump flag first — this is the one-frame consume.
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

            // All gates passed — apply impulse.
            _rb.AddForce(Vector3.up * _jumpForce, ForceMode.Impulse);
        }

        private void ApplyMovementForces(Vector2 moveInput)
        {
            // STEP 1: Convert 2D move input into camera-relative XZ world movement direction.
            // STEP 2: Skip force application when input is near zero or speed is capped.
            // STEP 3: Apply force using ForceMode.Force and update facing direction.
            if (_rb == null || _balance == null || _balance.IsFallen)
            {
                return;
            }

            if (moveInput.sqrMagnitude < 0.0001f)
            {
                return;
            }

            Vector3 worldDirection;
            if (_camera != null)
            {
                Vector3 cameraForward = Vector3.ProjectOnPlane(_camera.transform.forward, Vector3.up);
                Vector3 cameraRight = Vector3.ProjectOnPlane(_camera.transform.right, Vector3.up);

                if (cameraForward.sqrMagnitude < 0.0001f)
                {
                    cameraForward = Vector3.forward;
                }

                if (cameraRight.sqrMagnitude < 0.0001f)
                {
                    cameraRight = Vector3.right;
                }

                cameraForward.Normalize();
                cameraRight.Normalize();

                worldDirection = cameraRight * moveInput.x + cameraForward * moveInput.y;
            }
            else
            {
                worldDirection = new Vector3(moveInput.x, 0f, moveInput.y);
            }

            if (worldDirection.sqrMagnitude < 0.0001f)
            {
                return;
            }

            worldDirection.Normalize();

            Vector3 horizontalVelocity = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z);
            if (horizontalVelocity.magnitude < _maxSpeed)
            {
                _rb.AddForce(worldDirection * _moveForce, ForceMode.Force);
            }

            if (worldDirection.sqrMagnitude > 0.01f)
            {
                _balance.SetFacingDirection(worldDirection);
            }
        }
    }
}