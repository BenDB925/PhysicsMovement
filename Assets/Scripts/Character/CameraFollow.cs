using PhysicsDrivenMovement.Input;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Orbital third-person camera that orbits around the ragdoll hips using mouse/stick
    /// look input. The camera yaw determines the "forward" direction for movement — WASD
    /// pushes the ragdoll in the direction the camera is facing.
    /// Includes pitch clamping, distance collision avoidance via SphereCast, and cursor lock.
    /// Lifecycle: Awake (resolve target, create input), Update (sample look), LateUpdate (orbit).
    /// Collaborators: <see cref="PlayerMovement"/> (reads this camera for movement direction),
    /// <see cref="PlayerInputActions"/>.
    /// </summary>
    public class CameraFollow : MonoBehaviour
    {
        // ─── Serialised Fields ──────────────────────────────────────────────

        [SerializeField]
        [Tooltip("The transform to follow. If null, auto-finds the first PlayerMovement in the scene.")]
        private Transform _target;

        [Header("Orbit")]
        [SerializeField, Range(1f, 20f)]
        [Tooltip("Default distance from the target pivot.")]
        private float _distance = 6f;

        [SerializeField, Range(0f, 5f)]
        [Tooltip("Height offset above the target pivot that the camera orbits around.")]
        private float _pivotHeightOffset = 1.2f;

        [SerializeField, Range(-89f, 0f)]
        [Tooltip("Minimum pitch angle (looking up). Negative = above horizon.")]
        private float _minPitch = -20f;

        [SerializeField, Range(0f, 89f)]
        [Tooltip("Maximum pitch angle (looking down). Positive = below horizon.")]
        private float _maxPitch = 60f;

        [Header("Sensitivity")]
        [SerializeField, Range(0.01f, 1f)]
        [Tooltip("Mouse look sensitivity. Applied to mouse delta each frame.")]
        private float _mouseSensitivity = 0.15f;

        [SerializeField, Range(0.5f, 10f)]
        [Tooltip("Gamepad right stick look sensitivity in degrees per second.")]
        private float _stickSensitivity = 3f;

        [Header("Smoothing")]
        [SerializeField, Range(0f, 0.3f)]
        [Tooltip("SmoothDamp time for position following. Lower = snappier.")]
        private float _positionSmoothTime = 0.08f;

        [Header("Collision")]
        [SerializeField, Range(0.1f, 1f)]
        [Tooltip("SphereCast radius for camera collision avoidance.")]
        private float _collisionRadius = 0.25f;

        [SerializeField]
        [Tooltip("Layers the camera collides with to avoid clipping through geometry.")]
        private LayerMask _collisionLayers = ~0;

        // ─── Private Fields ─────────────────────────────────────────────────

        private PlayerInputActions _inputActions;
        private float _yaw;
        private float _pitch = 15f;
        private Vector3 _smoothVelocity;
        private bool _ownsInputActions;

        // ─── Unity Lifecycle ────────────────────────────────────────────────

        private void Awake()
        {
            // STEP 1: Resolve target.
            if (_target == null)
            {
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

            // STEP 2: Create input actions for look.
            _inputActions = new PlayerInputActions();
            _inputActions.Enable();
            _ownsInputActions = true;

            // STEP 3: Lock cursor for mouse look.
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            // STEP 4: Initialise yaw from current camera facing.
            Vector3 flat = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (flat.sqrMagnitude > 0.001f)
            {
                _yaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
            }
        }

        private void Update()
        {
            if (_inputActions == null)
            {
                return;
            }

            // STEP 1: Read look input.
            InputAction lookAction = _inputActions.Player.Look;
            Vector2 lookDelta = lookAction.ReadValue<Vector2>();

            // STEP 2: Apply to yaw/pitch.
            // DESIGN: Mouse delta is in pixels (large values), gamepad stick is -1..1
            // (small values scaled by deltaTime). We pick the sensitivity based on which
            // device is actively driving the action.
            float sensitivity;
            InputControl activeControl = lookAction.activeControl;
            if (activeControl != null && activeControl.device is Gamepad)
            {
                // Stick values are -1..1; scale by degrees/sec × deltaTime.
                lookDelta *= _stickSensitivity * Time.deltaTime * 100f;
                sensitivity = 1f;
            }
            else
            {
                sensitivity = _mouseSensitivity;
            }

            _yaw   += lookDelta.x * sensitivity;
            _pitch -= lookDelta.y * sensitivity;
            _pitch  = Mathf.Clamp(_pitch, _minPitch, _maxPitch);

            // STEP 3: Toggle cursor lock with Escape.
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                if (Cursor.lockState == CursorLockMode.Locked)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
                else
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
            }

            // STEP 4: Re-lock cursor on click when unlocked.
            if (Cursor.lockState != CursorLockMode.Locked &&
                Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        private void LateUpdate()
        {
            if (_target == null)
            {
                return;
            }

            // STEP 1: Compute the pivot point (target + height offset).
            Vector3 pivot = _target.position + Vector3.up * _pivotHeightOffset;

            // STEP 2: Compute orbit rotation from yaw and pitch.
            Quaternion orbitRotation = Quaternion.Euler(_pitch, _yaw, 0f);

            // STEP 3: Desired camera position = pivot + rotated offset at distance.
            Vector3 desiredOffset = orbitRotation * new Vector3(0f, 0f, -_distance);
            Vector3 desiredPosition = pivot + desiredOffset;

            // STEP 4: Collision avoidance — pull camera forward if geometry is in the way.
            float actualDistance = _distance;
            Vector3 direction = (desiredPosition - pivot).normalized;

            if (Physics.SphereCast(
                    pivot, _collisionRadius, direction, out RaycastHit hit,
                    _distance, _collisionLayers, QueryTriggerInteraction.Ignore))
            {
                actualDistance = Mathf.Max(hit.distance - _collisionRadius, 0.3f);
            }

            Vector3 finalPosition = pivot + direction * actualDistance;

            // STEP 5: Smooth follow.
            transform.position = Vector3.SmoothDamp(
                transform.position, finalPosition, ref _smoothVelocity, _positionSmoothTime);

            // STEP 6: Look at the pivot point.
            transform.LookAt(pivot);
        }

        private void OnDestroy()
        {
            if (_ownsInputActions && _inputActions != null)
            {
                _inputActions.Dispose();
                _inputActions = null;
            }

            // Restore cursor on destroy.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
