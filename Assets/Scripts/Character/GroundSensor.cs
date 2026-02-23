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
    [DefaultExecutionOrder(-55)]
    public class GroundSensor : MonoBehaviour
    {
        // ─── Serialised Fields ──────────────────────────────────────────────

        [SerializeField, Range(0.02f, 0.2f)]
        [Tooltip("Radius of the downward SphereCast. Should be slightly smaller than the foot width.")]
        private float _castRadius = 0.08f;

        [SerializeField, Range(0.05f, 0.5f)]
        [Tooltip("Maximum distance the cast travels below the foot origin to check for ground.")]
        private float _castDistance = 0.25f;

        [SerializeField]
        [Tooltip("Layer mask for surfaces that count as ground. Assign the Environment layer.")]
        private LayerMask _groundLayers;

        // ─── Private Fields ──────────────────────────────────────────────────

        private bool _isGrounded;
        private Collider _footCollider;

        // ─── Public Properties ────────────────────────────────────────────────

        /// <summary>
        /// True when the foot is close enough to a ground surface (determined by a downward
        /// SphereCast). Updated every FixedUpdate.
        /// </summary>
        public bool IsGrounded => _isGrounded;

        // ─── Unity Lifecycle ──────────────────────────────────────────────────

        private void Awake()
        {
            if (!TryGetComponent(out _footCollider))
            {
                Debug.LogWarning($"[GroundSensor] '{name}' has no Collider. Falling back to transform-based cast origin.", this);
            }
        }

        private void FixedUpdate()
        {
            // Use the foot collider's world-space bounds centre to anchor the cast.
            // Starting from the centre (not the sole) prevents missed detections when
            // the foot penetrates the floor — Physics.SphereCast ignores colliders
            // that overlap the starting sphere.
            Vector3 origin = GetCastOrigin();

            _isGrounded = Physics.SphereCast(
                origin: origin,
                radius:    _castRadius,
                direction: Vector3.down,
                hitInfo:   out _,
                maxDistance: _castDistance + _castRadius,
                layerMask:   _groundLayers,
                queryTriggerInteraction: QueryTriggerInteraction.Ignore);
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
        }

        private Vector3 GetCastOrigin()
        {
            if (_footCollider != null)
            {
                Bounds bounds = _footCollider.bounds;
                // Use bounds.center.y instead of bounds.min.y so the cast origin stays
                // above the ground surface even when the foot penetrates the floor
                // slightly. Physics.SphereCast ignores colliders that the starting
                // sphere overlaps, so an origin too close to or below the surface
                // causes missed detections.
                return new Vector3(bounds.center.x, bounds.center.y + _castRadius, bounds.center.z);
            }

            return transform.position + Vector3.up * _castRadius;
        }
    }
}
