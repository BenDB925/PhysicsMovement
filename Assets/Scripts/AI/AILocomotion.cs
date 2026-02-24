using UnityEngine;
using PhysicsDrivenMovement.Character;

namespace PhysicsDrivenMovement.AI
{
    /// <summary>
    /// Force-based locomotion for AI ragdoll characters. Implements <see cref="IMovementInput"/>
    /// so that <see cref="CharacterState"/> can drive the state machine identically to
    /// a player-controlled character. Uses the same movement model as
    /// <see cref="PlayerMovement"/>: applies force to the Hips Rigidbody, caps horizontal
    /// speed, and updates <see cref="BalanceController.SetFacingDirection"/>.
    /// Attach to the Hips (root) GameObject of an AI ragdoll.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(BalanceController))]
    public class AILocomotion : MonoBehaviour, IMovementInput
    {
        [SerializeField, Range(0f, 2000f)]
        [Tooltip("Movement force applied to Hips Rigidbody toward the target. " +
                 "Lower than player (150) for a gentle stroll that the balance controller can handle.")]
        private float _moveForce = 80f;

        [SerializeField, Range(0f, 20f)]
        [Tooltip("Maximum horizontal speed. Lower than player default — visitors stroll.")]
        private float _maxSpeed = 2f;

        [SerializeField, Range(0.1f, 5f)]
        [Tooltip("Distance at which the AI is considered to have arrived at the target.")]
        private float _arrivalDistance = 1.0f;

        private Rigidbody _rb;
        private BalanceController _balance;
        private Vector2 _currentMoveInput;
        private Vector3 _targetPosition;
        private bool _hasTarget;
        private Vector3 _facingOnlyDirection;
        private bool _facingOnly;

        /// <summary>Latest movement input for CharacterState compatibility.</summary>
        public Vector2 CurrentMoveInput => _currentMoveInput;

        /// <summary>True when the AI is within arrival distance of its target.</summary>
        public bool HasArrived => !_hasTarget || DistanceToTarget() <= _arrivalDistance;

        /// <summary>Current movement target position (only valid when HasTarget is true).</summary>
        public Vector3 TargetPosition => _targetPosition;

        /// <summary>True when a movement target is set.</summary>
        public bool HasTarget => _hasTarget;

        /// <summary>
        /// Sets a movement destination. The AI will walk toward this position.
        /// </summary>
        public void SetTarget(Vector3 worldPos)
        {
            _targetPosition = worldPos;
            _hasTarget = true;
            _facingOnly = false;
        }

        /// <summary>
        /// Clears the current movement target. The AI will stop moving.
        /// </summary>
        public void ClearTarget()
        {
            _hasTarget = false;
            _currentMoveInput = Vector2.zero;
            _facingOnly = false;
        }

        /// <summary>
        /// Face a direction without moving. Used during art observation.
        /// </summary>
        public void SetFacingOnly(Vector3 direction)
        {
            _facingOnly = true;
            _hasTarget = false;
            _currentMoveInput = Vector2.zero;

            Vector3 flatDir = new Vector3(direction.x, 0f, direction.z);
            if (flatDir.sqrMagnitude > 0.001f)
            {
                _facingOnlyDirection = flatDir.normalized;
            }
        }

        // ─── Test Seams ──────────────────────────────────────────────────────

        private bool _overrideMoveInput;

        /// <summary>
        /// Test seam: directly inject move input, bypassing the AI navigation.
        /// </summary>
        public void SetMoveInputForTest(Vector2 input)
        {
            _currentMoveInput = input;
            _overrideMoveInput = true;
        }

        // ─── Unity Lifecycle ─────────────────────────────────────────────────

        private void Awake()
        {
            TryGetComponent(out _rb);
            TryGetComponent(out _balance);

            if (_rb == null)
            {
                Debug.LogError("[AILocomotion] Missing Rigidbody.", this);
            }

            if (_balance == null)
            {
                Debug.LogError("[AILocomotion] Missing BalanceController.", this);
            }
        }

        private void FixedUpdate()
        {
            if (_rb == null || _balance == null)
            {
                return;
            }

            // Facing-only mode: just update facing direction, no movement.
            if (_facingOnly)
            {
                _currentMoveInput = Vector2.zero;
                if (_balance != null && _facingOnlyDirection.sqrMagnitude > 0.001f)
                {
                    _balance.SetFacingDirection(_facingOnlyDirection);
                }
                return;
            }

            if (_overrideMoveInput)
            {
                ApplyMovementForces(_currentMoveInput);
                return;
            }

            // No target or arrived — stop moving.
            if (!_hasTarget || DistanceToTarget() <= _arrivalDistance)
            {
                _currentMoveInput = Vector2.zero;
                return;
            }

            // Compute direction to target on XZ plane.
            Vector3 toTarget = _targetPosition - _rb.position;
            toTarget.y = 0f;

            if (toTarget.sqrMagnitude < 0.0001f)
            {
                _currentMoveInput = Vector2.zero;
                return;
            }

            Vector3 direction = toTarget.normalized;
            _currentMoveInput = new Vector2(direction.x, direction.z);

            ApplyMovementForces(_currentMoveInput);
        }

        private void ApplyMovementForces(Vector2 moveInput)
        {
            if (_rb == null || _balance == null || _balance.IsFallen)
            {
                return;
            }

            if (moveInput.sqrMagnitude < 0.0001f)
            {
                return;
            }

            Vector3 worldDirection = new Vector3(moveInput.x, 0f, moveInput.y);
            if (worldDirection.sqrMagnitude < 0.0001f)
            {
                return;
            }
            worldDirection.Normalize();

            // Speed cap — same pattern as PlayerMovement.
            Vector3 horizontalVelocity = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z);
            if (horizontalVelocity.magnitude < _maxSpeed)
            {
                _rb.AddForce(worldDirection * _moveForce, ForceMode.Force);
            }

            // Update facing direction.
            if (worldDirection.sqrMagnitude > 0.01f)
            {
                _balance.SetFacingDirection(worldDirection);
            }
        }

        private float DistanceToTarget()
        {
            Vector3 toTarget = _targetPosition - _rb.position;
            toTarget.y = 0f;
            return toTarget.magnitude;
        }
    }
}
