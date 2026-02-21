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
        // ─── Serialised Fields ──────────────────────────────────────────────

        [SerializeField, Range(0.02f, 0.2f)]
        [Tooltip("Radius of the downward SphereCast. Should be slightly smaller than the foot width.")]
        private float _castRadius = 0.06f;

        [SerializeField, Range(0.05f, 0.5f)]
        [Tooltip("Maximum distance the cast travels below the foot origin to check for ground.")]
        private float _castDistance = 0.12f;

        [SerializeField]
        [Tooltip("Layer mask for surfaces that count as ground. Assign the Environment layer.")]
        private LayerMask _groundLayers;

        // ─── Private Fields ──────────────────────────────────────────────────

        private bool _isGrounded;

        // ─── Public Properties ────────────────────────────────────────────────

        /// <summary>
        /// True when the foot is close enough to a ground surface (determined by a downward
        /// SphereCast). Updated every FixedUpdate.
        /// </summary>
        public bool IsGrounded => _isGrounded;

        // ─── Unity Lifecycle ──────────────────────────────────────────────────

        private void FixedUpdate()
        {
            // Cast downward from this foot's world position.
            // world-down is used deliberately — an angled foot should still detect
            // the ground that is directly beneath it in world space.
            _isGrounded = Physics.SphereCast(
                origin:    transform.position,
                radius:    _castRadius,
                direction: Vector3.down,
                hitInfo:   out _,
                maxDistance: _castDistance,
                layerMask:   _groundLayers,
                queryTriggerInteraction: QueryTriggerInteraction.Ignore);
        }

        // ─── Editor Visualisation ─────────────────────────────────────────────

        private void OnDrawGizmosSelected()
        {
            // Draw the cast endpoint sphere to show the effective ground detection range.
            Gizmos.color = _isGrounded ? Color.green : Color.red;
            Vector3 castEnd = transform.position + Vector3.down * _castDistance;
            Gizmos.DrawWireSphere(castEnd, _castRadius);

            // Also draw a line from origin to endpoint for clarity.
            Gizmos.color = new Color(Gizmos.color.r, Gizmos.color.g, Gizmos.color.b, 0.5f);
            Gizmos.DrawLine(transform.position, castEnd);
        }
    }
}
