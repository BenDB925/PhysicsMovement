using System;
using PhysicsDrivenMovement.Input;
using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Converts player input into locomotion forces for the active ragdoll hips body.
    /// This component belongs to the Character locomotion system and is responsible for
    /// camera-relative movement, speed limiting, and forwarding facing direction intent
    /// to <see cref="BalanceController"/>.
    /// Lifecycle: caches dependencies in Awake, samples input in Update, and applies
    /// movement forces in FixedUpdate.
    /// Collaborators: <see cref="BalanceController"/>, <see cref="Rigidbody"/>,
    /// <see cref="PlayerInputActions"/>.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class PlayerMovement : MonoBehaviour
    {
        [SerializeField, Range(0f, 2000f)]
        private float _moveForce = 300f;

        [SerializeField, Range(0f, 20f)]
        private float _maxSpeed = 5f;

        [SerializeField, Range(0f, 100f)]
        [Tooltip("Upward impulse (N·s) applied when the player presses jump while grounded.")]
        private float _jumpImpulse = 20f;

        [SerializeField, Range(0f, 2f)]
        [Tooltip("Minimum time between consecutive jumps to prevent spamming.")]
        private float _jumpCooldown = 0.3f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Fraction of movement force applied at the feet (rest goes to Hips). " +
                 "Higher values push from the ground up, preventing forward lean and knee buckling.")]
        private float _footForceFraction = 0.85f;

        [Header("Sprint")]
        [SerializeField, Range(1f, 3f)]
        [Tooltip("Multiplied with _maxSpeed while sprinting to raise the horizontal speed cap.")]
        private float _sprintSpeedMultiplier = 1.8f;

        [SerializeField, Range(1f, 3f)]
        [Tooltip("Multiplied with _moveForce while sprinting so the character actually accelerates to the higher cap.")]
        private float _sprintForceMultiplier = 1.5f;

        [SerializeField]
        private Camera _camera;

        private Rigidbody _rb;
        private BalanceController _balance;
        private PlayerInputActions _inputActions;
        private Rigidbody _footL;
        private Rigidbody _footR;
        private Vector2 _currentMoveInput;
        private bool _jumpRequested;
        private float _lastJumpTime = -10f;
        private bool _sprintHeld;

        /// <summary>Latest sampled movement input from the Player action map.</summary>
        public Vector2 CurrentMoveInput => _currentMoveInput;

        /// <summary>True while the sprint button is held and the character is moving.</summary>
        public bool IsSprinting => _sprintHeld && _currentMoveInput.sqrMagnitude > 0.0001f;

        /// <summary>
        /// Directly inject move input, bypassing the Input System.
        /// FixedUpdate will not overwrite this value while the override is active.
        /// Used by AI controllers and tests.
        /// </summary>
        public void SetMoveInputOverride(Vector2 input)
        {
            _currentMoveInput = input;
            _overrideMoveInput = true;
        }

        private bool _overrideMoveInput;

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

            // STEP 2: Resolve a camera reference (serialized value preferred, main camera fallback).
            if (_camera == null)
            {
                _camera = Camera.main;
            }

            // STEP 3: Cache foot Rigidbodies for ground-up force distribution.
            Rigidbody[] bodies = GetComponentsInChildren<Rigidbody>(includeInactive: true);
            for (int i = 0; i < bodies.Length; i++)
            {
                string bodyName = bodies[i].gameObject.name;
                if (_footL == null && bodyName == "Foot_L") _footL = bodies[i];
                else if (_footR == null && bodyName == "Foot_R") _footR = bodies[i];
            }

            // STEP 4: Create and enable PlayerInputActions for the local movement map.
            _inputActions = new PlayerInputActions();
            _inputActions.Enable();
        }

        private void Update()
        {
            // Sample input in Update to avoid missing button presses between FixedUpdate ticks.
            if (!_overrideMoveInput && _inputActions != null)
            {
                _currentMoveInput = _inputActions.Player.Move.ReadValue<Vector2>();
            }

            // Cache sprint hold state every frame.
            if (_inputActions != null)
            {
                _sprintHeld = _inputActions.Player.Sprint.IsPressed();
            }

            // Latch jump press so FixedUpdate can consume it even if Update runs multiple times.
            if (_inputActions != null && _inputActions.Player.Jump.WasPressedThisFrame())
            {
                _jumpRequested = true;
            }
        }

        private void FixedUpdate()
        {
            // STEP 0: Fallback for missing input.
            if (!_overrideMoveInput && _inputActions == null)
            {
                _currentMoveInput = Vector2.zero;
            }

            // STEP 1: Early-out when dependencies are missing or character is fallen.
            if (_rb == null || _balance == null || _balance.IsFallen)
            {
                _jumpRequested = false;
                return;
            }

            if (_camera == null)
            {
                _camera = Camera.main;
            }

            // STEP 2: Apply jump impulse if requested, grounded, and cooldown elapsed.
            if (_jumpRequested && _balance.IsGrounded && Time.time >= _lastJumpTime + _jumpCooldown)
            {
                _rb.AddForce(Vector3.up * _jumpImpulse, ForceMode.Impulse);
                _lastJumpTime = Time.time;
            }
            _jumpRequested = false;

            // STEP 3: Convert move input to camera-relative world direction on XZ plane.
            // STEP 4: Apply AddForce only when below configured horizontal speed cap.
            // STEP 5: Forward non-trivial movement direction to BalanceController facing target.
            ApplyMovementForces(_currentMoveInput);
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

            float effectiveMaxSpeed = IsSprinting ? _maxSpeed * _sprintSpeedMultiplier : _maxSpeed;
            float effectiveMoveForce = IsSprinting ? _moveForce * _sprintForceMultiplier : _moveForce;

            Vector3 horizontalVelocity = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z);
            if (horizontalVelocity.magnitude < effectiveMaxSpeed)
            {
                Vector3 totalForce = worldDirection * effectiveMoveForce;
                float footShare = _footForceFraction;
                float hipsShare = 1f - footShare;

                // Apply the hips portion.
                _rb.AddForce(totalForce * hipsShare, ForceMode.Force);

                // Split the foot portion between both feet, pushing from the ground up.
                if (_footL != null && _footR != null)
                {
                    Vector3 perFootForce = totalForce * (footShare * 0.5f);
                    _footL.AddForce(perFootForce, ForceMode.Force);
                    _footR.AddForce(perFootForce, ForceMode.Force);
                }
                else
                {
                    // Fallback: no feet found, apply everything at hips.
                    _rb.AddForce(totalForce * footShare, ForceMode.Force);
                }
            }

            if (worldDirection.sqrMagnitude > 0.01f)
            {
                _balance.SetFacingDirection(worldDirection);
            }
        }
    }
}