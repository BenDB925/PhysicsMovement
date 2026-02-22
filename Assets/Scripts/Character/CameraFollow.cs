using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Simple third-person camera that follows the ragdoll hips with SmoothDamp.
    /// Attach to the Main Camera or let <see cref="RagdollSetup"/> wire it at runtime.
    /// Lifecycle: Awake (resolve target), LateUpdate (follow + look-at).
    /// Collaborators: <see cref="PlayerMovement"/> (target resolution).
    /// </summary>
    public class CameraFollow : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("The transform to follow. If null, auto-finds the first PlayerMovement in the scene.")]
        private Transform _target;

        [SerializeField]
        [Tooltip("Offset from the target in world space.")]
        private Vector3 _offset = new Vector3(0f, 3f, -6f);

        [SerializeField, Range(0f, 1f)]
        [Tooltip("SmoothDamp time for position following. Lower = snappier.")]
        private float _smoothTime = 0.15f;

        [SerializeField, Range(0f, 3f)]
        [Tooltip("Height above the target pivot that the camera looks at.")]
        private float _lookAtHeightOffset = 0.5f;

        private Vector3 _velocity;

        private void Awake()
        {
            if (_target != null)
            {
                return;
            }

            PlayerMovement pm = FindFirstObjectByType<PlayerMovement>();
            if (pm != null)
            {
                _target = pm.transform;
            }
            else
            {
                Debug.LogWarning("[CameraFollow] No PlayerMovement found in scene. " +
                                 "Assign a target manually or ensure the ragdoll is in the scene.", this);
            }
        }

        private void LateUpdate()
        {
            if (_target == null)
            {
                return;
            }

            Vector3 desiredPosition = _target.position + _offset;
            transform.position = Vector3.SmoothDamp(
                transform.position, desiredPosition, ref _velocity, _smoothTime);

            Vector3 lookAtPoint = _target.position + Vector3.up * _lookAtHeightOffset;
            transform.LookAt(lookAtPoint);
        }
    }
}
