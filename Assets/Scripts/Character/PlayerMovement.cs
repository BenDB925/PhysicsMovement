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

        [SerializeField]
        private Camera _camera;

        private Rigidbody _rb;
        private BalanceController _balance;
        private PlayerInputActions _inputActions;
        private Vector2 _currentMoveInput;

        /// <summary>Latest sampled movement input from the Player action map.</summary>
        public Vector2 CurrentMoveInput => _currentMoveInput;

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

            // STEP 3: Create and enable PlayerInputActions for the local movement map.
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

            // STEP 1: Early-out when dependencies are missing or character is fallen.
            if (_rb == null || _balance == null || _balance.IsFallen)
            {
                return;
            }

            if (_camera == null)
            {
                _camera = Camera.main;
            }

            // STEP 2: Convert move input to camera-relative world direction on XZ plane.
            // STEP 3: Apply AddForce only when below configured horizontal speed cap.
            // STEP 4: Forward non-trivial movement direction to BalanceController facing target.
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