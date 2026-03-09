using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Detects the sustained locomotion-collapse regime where the character still has strong
    /// move intent, but the feet/support center trail behind the hips and projected forward
    /// progress collapses without the posture-only fallen threshold being crossed.
    /// Lives on the character root so both the finite-state machine and torque/movement
    /// systems can consume the same bounded fall signal.
    /// Lifecycle: caches dependencies in Awake and evaluates collapse evidence in FixedUpdate.
    /// Collaborators: <see cref="BalanceController"/>, <see cref="PlayerMovement"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(BalanceController))]
    public class LocomotionCollapseDetector : MonoBehaviour
    {
        [SerializeField, Range(0.05f, 1f)]
        private float _confirmDuration = 0.3f;

        [SerializeField, Range(0f, 1f)]
        private float _moveIntentThreshold = 0.7f;

        [SerializeField, Range(-2f, 2f)]
        private float _projectedProgressThreshold = 0.2f;

        [SerializeField, Range(0f, 1f)]
        private float _supportBehindThreshold = 0.25f;

        [SerializeField, Range(0f, 90f)]
        private float _uprightCompromiseAngleThreshold = 30f;

        [SerializeField, Range(0.5f, 1f)]
        private float _groundedMajorityThreshold = 0.7f;

        [SerializeField, Range(0f, 0.25f)]
        private float _groundedGraceDuration = 0.05f;

        [SerializeField, Range(0f, 2f), Tooltip("Minimum horizontal speed (m/s) at which the velocity-direction gate activates.")]
        private float _turnVelocityGateSpeed = 0.15f;

        [SerializeField, Range(0f, 120f), Tooltip("When horizontal velocity exceeds the gate speed AND its angle to the requested direction exceeds this, the character is mid-turn. Collapse evidence is reset.")]
        private float _turnVelocityAngleThreshold = 60f;

        [SerializeField, Range(0f, 3f), Tooltip("Seconds after collapse evidence clears before re-confirmation is allowed. " +
                 "Breaks the Fallen→Moving→Fallen stutter loop where gait-resets prevent natural recovery.")]
        private float _postCollapseCooldown = 1f;

        [SerializeField]
        private bool _debugTransitions = false;

        private Rigidbody _hipsBody;
        private BalanceController _balanceController;
        private PlayerMovement _playerMovement;
        private Transform _leftFootTransform;
        private Transform _rightFootTransform;

        private float _evidenceWindowTime;
        private float _groundedWindowTime;
        private float _groundedGraceTimeRemaining;
        private float _cooldownRemaining;

        /// <summary>
        /// True once collapse evidence has remained active for the configured bounded window.
        /// Consumers should treat this the same way they treat a confirmed fall trigger.
        /// </summary>
        public bool IsCollapseConfirmed { get; private set; }

        private void Awake()
        {
            // STEP 1: Cache required root dependencies.
            TryGetComponent(out _hipsBody);
            TryGetComponent(out _balanceController);
            TryGetComponent(out _playerMovement);

            CacheFootTransforms();
        }

        private void FixedUpdate()
        {
            // STEP 1: Resolve runtime dependencies lazily for test rigs and staged setup.
            if (!TryResolveDependencies())
            {
                ResetEvidence();
                return;
            }

            // STEP 1a: Tick down post-collapse cooldown. While active, suppress all evidence
            //          accumulation so the gait has time to produce recovery steps.
            if (_cooldownRemaining > 0f)
            {
                _cooldownRemaining -= Time.fixedDeltaTime;
                if (_cooldownRemaining > 0f)
                {
                    // Don't call ResetEvidence here — evidence is already clear and
                    // calling it would overwrite _cooldownRemaining to 0 via the
                    // IsCollapseConfirmed=false path. Just keep timers zeroed.
                    IsCollapseConfirmed = false;
                    _evidenceWindowTime = 0f;
                    _groundedWindowTime = 0f;
                    return;
                }
            }

            Vector3 horizontalVelocity = new Vector3(_hipsBody.linearVelocity.x, 0f, _hipsBody.linearVelocity.z);
            float moveMagnitude = _playerMovement.CurrentMoveInput.magnitude;

            // STEP 2: Preserve a short grounded grace window so one-frame airborne blips do not reset the detector.
            if (_balanceController.IsGrounded)
            {
                _groundedGraceTimeRemaining = _groundedGraceDuration;
            }
            else
            {
                _groundedGraceTimeRemaining = Mathf.Max(0f, _groundedGraceTimeRemaining - Time.fixedDeltaTime);
            }

            if (!TryGetRequestedDirection(out Vector3 requestedDirection))
            {
                ResetEvidence();
                return;
            }

            if (!TryGetSupportBehindDistance(requestedDirection, out float supportBehindDistance))
            {
                ResetEvidence();
                return;
            }

            // STEP 3: Evaluate the sustained collapse evidence using intent, progress, support, and posture.
            float projectedProgress = Vector3.Dot(horizontalVelocity, requestedDirection);
            float uprightAngle = Vector3.Angle(transform.up, Vector3.up);

            // STEP 3a: Suppress collapse detection when the character is mid-turn.
            // During turns, velocity lags behind the new input direction — the character
            // has good speed but it points in the old travel direction.  This looks like
            // a collapse to the progress/support checks.  Velocity direction is a more
            // reliable turn indicator than hips heading because velocity realigns slower.
            if (horizontalVelocity.sqrMagnitude > _turnVelocityGateSpeed * _turnVelocityGateSpeed)
            {
                float velocityInputAngle = Vector3.Angle(horizontalVelocity, requestedDirection);
                if (velocityInputAngle > _turnVelocityAngleThreshold)
                {
                    ResetEvidence();
                    return;
                }
            }

            bool strongIntent = moveMagnitude >= _moveIntentThreshold;
            bool lowProjectedProgress = projectedProgress <= _projectedProgressThreshold;
            bool supportBehindHips = supportBehindDistance >= _supportBehindThreshold;
            bool compromisedUpright = uprightAngle >= _uprightCompromiseAngleThreshold;

            bool hasCollapseEvidence = strongIntent &&
                                       lowProjectedProgress &&
                                       supportBehindHips &&
                                       compromisedUpright &&
                                       !_balanceController.IsFallen;

            if (!hasCollapseEvidence)
            {
                ResetEvidence();
                return;
            }

            // STEP 4: Confirm collapse only after the evidence window is sustained and grounded for most of that window.
            _evidenceWindowTime += Time.fixedDeltaTime;
            if (_balanceController.IsGrounded || _groundedGraceTimeRemaining > 0f)
            {
                _groundedWindowTime += Time.fixedDeltaTime;
            }

            bool groundedForMostOfWindow = _groundedWindowTime >= (_evidenceWindowTime * _groundedMajorityThreshold);
            bool nextConfirmed = _evidenceWindowTime >= _confirmDuration && groundedForMostOfWindow;

            if (nextConfirmed && !IsCollapseConfirmed && _debugTransitions)
            {
                Debug.Log(
                    $"[LocomotionCollapseDetector] '{name}': collapse confirmed " +
                    $"(intent={moveMagnitude:F2}, progress={projectedProgress:F2} m/s, " +
                    $"supportBehind={supportBehindDistance:F2} m, upright={uprightAngle:F1} deg).",
                    this);
            }

            IsCollapseConfirmed = nextConfirmed;
        }

        private bool TryResolveDependencies()
        {
            if (_hipsBody == null)
            {
                TryGetComponent(out _hipsBody);
            }

            if (_balanceController == null)
            {
                TryGetComponent(out _balanceController);
            }

            if (_playerMovement == null)
            {
                TryGetComponent(out _playerMovement);
            }

            if (_leftFootTransform == null || _rightFootTransform == null)
            {
                CacheFootTransforms();
            }

            return _hipsBody != null &&
                   _balanceController != null &&
                   _playerMovement != null &&
                   _leftFootTransform != null &&
                   _rightFootTransform != null;
        }

        private void CacheFootTransforms()
        {
            Transform[] children = GetComponentsInChildren<Transform>(includeInactive: true);
            for (int i = 0; i < children.Length; i++)
            {
                Transform child = children[i];
                if (_leftFootTransform == null && child.name == "Foot_L")
                {
                    _leftFootTransform = child;
                }
                else if (_rightFootTransform == null && child.name == "Foot_R")
                {
                    _rightFootTransform = child;
                }
            }
        }

        private bool TryGetRequestedDirection(out Vector3 requestedDirection)
        {
            requestedDirection = Vector3.ProjectOnPlane(_playerMovement.CurrentMoveWorldDirection, Vector3.up);
            if (requestedDirection.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            requestedDirection.Normalize();
            return true;
        }

        private bool TryGetSupportBehindDistance(Vector3 requestedDirection, out float supportBehindDistance)
        {
            supportBehindDistance = 0f;
            if (_leftFootTransform == null || _rightFootTransform == null)
            {
                return false;
            }

            Vector3 supportCenter = (_leftFootTransform.position + _rightFootTransform.position) * 0.5f;
            Vector3 supportOffset = supportCenter - _hipsBody.position;
            float signedSupportOffset = Vector3.Dot(supportOffset, requestedDirection);
            supportBehindDistance = Mathf.Max(0f, -signedSupportOffset);
            return true;
        }

        private void ResetEvidence()
        {
            if (IsCollapseConfirmed)
            {
                // Start cooldown when collapse was confirmed and is now clearing.
                // This prevents re-confirmation for N seconds, breaking the stutter loop.
                _cooldownRemaining = _postCollapseCooldown;

                if (_debugTransitions)
                {
                    Debug.Log(
                        $"[LocomotionCollapseDetector] '{name}': collapse evidence cleared, " +
                        $"cooldown {_postCollapseCooldown:F2}s.",
                        this);
                }
            }

            IsCollapseConfirmed = false;
            _evidenceWindowTime = 0f;
            _groundedWindowTime = 0f;
            _groundedGraceTimeRemaining = 0f;
        }
    }
}
