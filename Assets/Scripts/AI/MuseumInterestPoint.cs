using UnityEngine;

namespace PhysicsDrivenMovement.AI
{
    /// <summary>
    /// Marks an art piece (pedestal exhibit or wall painting) as an interest point
    /// that AI visitors can navigate to and observe. Computes a view position
    /// (where the AI should stand) and view direction (which way to face) based on
    /// the art piece's transform and a configurable view distance.
    /// Attach to the root GameObject of each exhibit or painting.
    /// </summary>
    public class MuseumInterestPoint : MonoBehaviour
    {
        [SerializeField, Range(0.5f, 5f)]
        [Tooltip("Distance from the art piece where the AI should stand to observe it.")]
        private float _viewDistance = 1.5f;

        [SerializeField]
        [Tooltip("Local-space direction from the art piece toward the viewer. " +
                 "Auto-computed in Awake if left at zero. For paintings, typically the surface normal.")]
        private Vector3 _viewDirectionLocal = Vector3.zero;

        /// <summary>World position where an AI should stand to observe this art piece.</summary>
        public Vector3 ViewPosition { get; private set; }

        /// <summary>World direction the AI should face while observing (toward the art).</summary>
        public Vector3 ViewDirection { get; private set; }

        private void Awake()
        {
            ComputeViewTransform();
        }

        private void ComputeViewTransform()
        {
            // If no explicit view direction set, default to the art piece's forward axis.
            Vector3 localDir = _viewDirectionLocal.sqrMagnitude > 0.001f
                ? _viewDirectionLocal.normalized
                : Vector3.forward;

            // World-space outward direction from the art piece.
            Vector3 worldOutward = transform.TransformDirection(localDir);
            worldOutward.y = 0f;
            if (worldOutward.sqrMagnitude < 0.001f)
            {
                worldOutward = Vector3.forward;
            }
            worldOutward.Normalize();

            // View position: stand _viewDistance away from the art piece in the outward direction.
            Vector3 artPosition = transform.position;
            artPosition.y = 0f; // stand on ground level
            ViewPosition = artPosition + worldOutward * _viewDistance;

            // View direction: face toward the art piece (opposite of outward).
            ViewDirection = -worldOutward;
        }

        /// <summary>
        /// Initialises the interest point data. Called by the editor builder at scene-build time.
        /// </summary>
        public void Initialise(float viewDistance, Vector3 viewDirectionLocal)
        {
            _viewDistance = viewDistance;
            _viewDirectionLocal = viewDirectionLocal;
            ComputeViewTransform();
        }
    }
}
