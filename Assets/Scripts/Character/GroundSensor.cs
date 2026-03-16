using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Detects whether a foot is in contact with the ground via a downward SphereCast.
    /// Attach to <c>Foot_L</c> and <c>Foot_R</c> GameObjects on the PlayerRagdoll prefab.
    /// The cast origin is the foot's world position; casting direction is always world-down.
    /// Using world-down (not -transform.up) is intentional: a tilted foot should still
    /// detect the ground directly below it.
    /// Lifecycle: FixedUpdate — result is valid from the first physics step onward.
    /// Collaborators: <see cref="BalanceController"/> reads <see cref="IsGrounded"/>.
    /// </summary>
    public class GroundSensor : MonoBehaviour
    {
        private const float DefaultForwardProbeDistance = 0.18f;
        private const float DefaultForwardProbeHeight = 0.025f;
        private const float DefaultForwardProbeRadius = 0.035f;
        private const float DefaultMaxStepHeight = 0.30f;
        private const float DirectionEpsilon = 0.0001f;
        private const float MinimumStepHeight = 0.03f;
        private const float UpwardSurfaceNormalThreshold = 0.55f;

        // ─── Serialised Fields ──────────────────────────────────────────────

        [SerializeField, Range(0.02f, 0.2f)]
        [Tooltip("Radius of the downward SphereCast. Should be slightly smaller than the foot width.")]
        private float _castRadius = 0.04f;

        [SerializeField, Range(0.05f, 0.5f)]
        [Tooltip("Maximum distance the cast travels below the foot origin to check for ground.")]
        private float _castDistance = 0.12f;

        [SerializeField, Range(0f, 0.2f)]
        [Tooltip("How long a grounded foot may temporarily miss the floor before the sensor reports ungrounded. " +
             "Suppresses single-frame contact flicker during fast turns and steps.")]
        private float _groundedExitDelay = 0.03f;

        [SerializeField]
        [Tooltip("Layer mask for surfaces that count as ground. Assign the Environment layer.")]
        private LayerMask _groundLayers;

        [SerializeField, Range(0.05f, 0.6f)]
        [Tooltip("How far ahead of the foot to probe for a step face or other forward obstruction.")]
        private float _forwardProbeDistance = 0.18f;

        [SerializeField, Range(0.01f, 0.12f)]
        [Tooltip("Radius of the forward obstruction SphereCast. Keep slightly smaller than the foot sole.")]
        private float _forwardProbeRadius = 0.035f;

        [SerializeField, Range(0f, 0.2f)]
        [Tooltip("Height above the current sole/support height used for the forward obstruction probe.")]
        private float _forwardProbeHeight = 0.025f;

        [SerializeField, Range(0.1f, 1f)]
        [Tooltip("Maximum rise considered a step-up candidate when probing forward.")]
        private float _maxStepHeight = 0.30f;

        // ─── Private Fields ──────────────────────────────────────────────────

        private bool _isGrounded;
        private bool _hasForwardObstruction;
        private Collider _footCollider;
        private float _estimatedStepHeight;
        private float _forwardObstructionConfidence;
        private Vector3 _forwardObstructionTopSurfacePoint;
        private Vector3 _groundPoint;
        private Vector3 _groundNormal;
        private float _ungroundedTimer;

        // ─── Public Properties ────────────────────────────────────────────────

        /// <summary>
        /// True when the foot is close enough to a ground surface (determined by a downward
        /// SphereCast). Updated every FixedUpdate.
        /// </summary>
        public bool IsGrounded => _isGrounded;

        /// <summary>
        /// Latest world-space ground contact point reported by the sensor while grounded.
        /// When the sensor is temporarily coasting through the grounded exit delay, this preserves
        /// the last confirmed support point so higher-level aggregation keeps a stable contact anchor.
        /// </summary>
        public Vector3 GroundPoint => _groundPoint;

        /// <summary>
        /// True when the sensor detects a step-up style obstruction ahead of the foot even if the
        /// downward grounded sample is currently false.
        /// </summary>
        public bool HasForwardObstruction => _hasForwardObstruction;

        /// <summary>
        /// Estimated top-surface rise above the current support height when a forward obstruction is present.
        /// </summary>
        public float EstimatedStepHeight => _estimatedStepHeight;

        /// <summary>
        /// Confidence that the detected forward obstruction represents a real step-up surface.
        /// </summary>
        public float ForwardObstructionConfidence => _forwardObstructionConfidence;

        /// <summary>
        /// World-space point sampled on the reachable top surface above the detected step face.
        /// </summary>
        public Vector3 ForwardObstructionTopSurfacePoint => _forwardObstructionTopSurfacePoint;

        /// <summary>
        /// Surface normal of the last confirmed ground contact. Defaults to Vector3.up when not grounded.
        /// </summary>
        public Vector3 GroundNormal => _groundNormal;

        /// <summary>
        /// Dot product of the ground contact normal with Vector3.up (0–1 range for upward-facing surfaces).
        /// Returns 1.0 on perfectly flat ground and degrades toward 0 on steeper slopes.
        /// </summary>
        public float GroundNormalUpAlignment => Mathf.Max(0f, Vector3.Dot(_groundNormal, Vector3.up));

        // ─── Unity Lifecycle ──────────────────────────────────────────────────

        private void Awake()
        {
            _groundNormal = Vector3.up;
            SanitizeProbeSettings();

            if (!TryGetComponent(out _footCollider))
            {
                Debug.LogWarning($"[GroundSensor] '{name}' has no Collider. Falling back to transform-based cast origin.", this);
            }
        }

        private void FixedUpdate()
        {
            // Use the foot collider's world-space bounds to anchor the cast at the sole.
            // This keeps the sensor stable even when the foot transform pivot is not
            // exactly at the bottom of the collider.
            Vector3 origin = GetCastOrigin();

            bool detectedGround = Physics.SphereCast(
                origin: origin,
                radius:    _castRadius,
                direction: Vector3.down,
                hitInfo:   out RaycastHit hitInfo,
                maxDistance: _castDistance + _castRadius,
                layerMask:   _groundLayers,
                queryTriggerInteraction: QueryTriggerInteraction.Ignore);

            float supportHeight = detectedGround
                ? hitInfo.point.y
                : GetSupportReferenceHeight(origin);

            if (detectedGround)
            {
                _isGrounded = true;
                _groundPoint = hitInfo.point;
                _groundNormal = hitInfo.normal;
                _ungroundedTimer = 0f;
            }
            else if (_isGrounded)
            {
                _ungroundedTimer += Time.fixedDeltaTime;
                if (_ungroundedTimer >= _groundedExitDelay)
                {
                    _isGrounded = false;
                    _groundNormal = Vector3.up;
                    _ungroundedTimer = 0f;
                }
            }

            UpdateForwardObstruction(origin, supportHeight);
        }

        // ─── Editor Visualisation ─────────────────────────────────────────────

        private void OnDrawGizmosSelected()
        {
            // Draw the cast origin sphere and endpoint sphere to show the effective
            // ground detection range.
            Vector3 origin = GetCastOrigin();
            Vector3 castEnd = origin + Vector3.down * (_castDistance + _castRadius);

            Gizmos.color = _isGrounded ? Color.green : Color.red;
            Gizmos.DrawWireSphere(origin, _castRadius);
            Gizmos.DrawWireSphere(castEnd, _castRadius);

            // Also draw a line from origin to endpoint for clarity.
            Gizmos.color = new Color(Gizmos.color.r, Gizmos.color.g, Gizmos.color.b, 0.5f);
            Gizmos.DrawLine(origin, castEnd);

            Vector3 forward = GetProbeForward();
            float supportHeight = GetSupportReferenceHeight(origin);
            Vector3 forwardOrigin = GetForwardProbeOrigin(origin, supportHeight);
            Vector3 forwardEnd = forwardOrigin + forward * _forwardProbeDistance;

            Gizmos.color = _hasForwardObstruction ? new Color(1f, 0.75f, 0.2f) : new Color(1f, 1f, 0f, 0.6f);
            Gizmos.DrawWireSphere(forwardOrigin, _forwardProbeRadius);
            Gizmos.DrawWireSphere(forwardEnd, _forwardProbeRadius);
            Gizmos.DrawLine(forwardOrigin, forwardEnd);
        }

        private Vector3 GetCastOrigin()
        {
            if (_footCollider != null)
            {
                Bounds bounds = _footCollider.bounds;
                float soleY = bounds.min.y;
                return new Vector3(bounds.center.x, soleY + _castRadius + 0.002f, bounds.center.z);
            }

            return transform.position + Vector3.up * _castRadius;
        }

        private void SanitizeProbeSettings()
        {
            _castRadius = Mathf.Max(0.02f, _castRadius);
            _castDistance = Mathf.Max(0.05f, _castDistance);
            _groundedExitDelay = Mathf.Max(0f, _groundedExitDelay);
            _forwardProbeDistance = _forwardProbeDistance > 0f
                ? Mathf.Max(0.05f, _forwardProbeDistance)
                : DefaultForwardProbeDistance;
            _forwardProbeRadius = _forwardProbeRadius > 0f
                ? Mathf.Max(0.01f, _forwardProbeRadius)
                : DefaultForwardProbeRadius;
            _forwardProbeHeight = _forwardProbeHeight >= 0f
                ? _forwardProbeHeight
                : DefaultForwardProbeHeight;
            _maxStepHeight = _maxStepHeight > 0f
                ? Mathf.Max(MinimumStepHeight, _maxStepHeight)
                : DefaultMaxStepHeight;
        }

        private void UpdateForwardObstruction(Vector3 castOrigin, float supportHeight)
        {
            // STEP 1: Probe forward from the current sole/support height to look for a near step face.
            Vector3 forward = GetProbeForward();
            Vector3 probeOrigin = GetForwardProbeOrigin(castOrigin, supportHeight);

            bool detectedFace = Physics.SphereCast(
                origin: probeOrigin,
                radius: _forwardProbeRadius,
                direction: forward,
                hitInfo: out RaycastHit faceHit,
                maxDistance: _forwardProbeDistance,
                layerMask: _groundLayers,
                queryTriggerInteraction: QueryTriggerInteraction.Ignore);

            if (!detectedFace)
            {
                ClearForwardObstruction();
                return;
            }

            // STEP 2: Sample down from above the hit so we can confirm a reachable top surface.
            Vector3 topProbeOrigin = faceHit.point
                + forward * (_forwardProbeRadius + 0.02f)
                + Vector3.up * (_maxStepHeight + _forwardProbeRadius);
            float topProbeDistance = _maxStepHeight + _forwardProbeRadius + _forwardProbeHeight + 0.1f;

            bool detectedTopSurface = Physics.Raycast(
                origin: topProbeOrigin,
                direction: Vector3.down,
                hitInfo: out RaycastHit topHit,
                maxDistance: topProbeDistance,
                layerMask: _groundLayers,
                queryTriggerInteraction: QueryTriggerInteraction.Ignore);

            if (!detectedTopSurface)
            {
                ClearForwardObstruction();
                return;
            }

            // STEP 3: Reject flat-ground hits and non-step surfaces, then cache the obstruction sample.
            float stepHeight = topHit.point.y - supportHeight;
            float surfaceUpDot = Vector3.Dot(topHit.normal, Vector3.up);
            if (stepHeight < MinimumStepHeight ||
                stepHeight > _maxStepHeight ||
                surfaceUpDot < UpwardSurfaceNormalThreshold)
            {
                ClearForwardObstruction();
                return;
            }

            float distanceFactor = 1f - Mathf.Clamp01(faceHit.distance / Mathf.Max(_forwardProbeDistance, DirectionEpsilon));
            float surfaceFactor = Mathf.InverseLerp(UpwardSurfaceNormalThreshold, 1f, surfaceUpDot);

            _hasForwardObstruction = true;
            _estimatedStepHeight = stepHeight;
            _forwardObstructionConfidence = Mathf.Clamp01(Mathf.Lerp(0.4f, 1f, distanceFactor) * Mathf.Lerp(0.7f, 1f, surfaceFactor));
            _forwardObstructionTopSurfacePoint = topHit.point;
        }

        private void ClearForwardObstruction()
        {
            _hasForwardObstruction = false;
            _estimatedStepHeight = 0f;
            _forwardObstructionConfidence = 0f;
            _forwardObstructionTopSurfacePoint = Vector3.zero;
        }

        private float GetSupportReferenceHeight(Vector3 castOrigin)
        {
            if (_isGrounded)
            {
                return _groundPoint.y;
            }

            return castOrigin.y - _castRadius;
        }

        private Vector3 GetForwardProbeOrigin(Vector3 castOrigin, float supportHeight)
        {
            return new Vector3(
                castOrigin.x,
                supportHeight + _forwardProbeHeight + _forwardProbeRadius,
                castOrigin.z);
        }

        private Vector3 GetProbeForward()
        {
            Vector3 rootForward = transform.root != null ? transform.root.forward : transform.forward;
            Vector3 planarRootForward = Vector3.ProjectOnPlane(rootForward, Vector3.up);
            if (planarRootForward.sqrMagnitude > DirectionEpsilon)
            {
                return planarRootForward.normalized;
            }

            Vector3 planarLocalForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (planarLocalForward.sqrMagnitude > DirectionEpsilon)
            {
                return planarLocalForward.normalized;
            }

            return Vector3.forward;
        }
    }
}
