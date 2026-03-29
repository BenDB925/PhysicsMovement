using UnityEngine;

namespace PhysicsDrivenMovement.Environment
{
    /// <summary>
    /// Drives a twirling hammer hazard using a HingeJoint so collisions transfer
    /// real physics momentum to whatever they hit.
    ///
    /// Setup:
    ///  1. This component goes on the PIVOT GameObject (the empty at the rotation centre).
    ///  2. The PIVOT needs a Rigidbody (IsKinematic = true — anchors the joint in world space).
    ///  3. The HAMMER child needs its own Rigidbody (IsKinematic = false, mass = 60+).
    ///  4. A HingeJoint on the HAMMER child, Connected Body = PIVOT rigidbody, axis = rotation axis.
    ///  5. HingeJoint Motor: enabled, Target Velocity = _targetRPM * 6, Force = _motorForce.
    ///  6. HingeJoint Use Motor = true, Use Limits = false.
    ///
    /// The motor spins the hammer up to target RPM. Because the hammer has real mass,
    /// collisions with the player ragdoll transfer genuine momentum — no scripted forces needed.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class TwirlingHammerHazard : MonoBehaviour
    {
        [SerializeField, Range(10f, 300f)]
        [Tooltip("Target rotation speed in RPM. 60 RPM = one full rotation per second.")]
        private float _targetRpm = 60f;

        [SerializeField, Range(100f, 5000f)]
        [Tooltip("Motor force applied by the HingeJoint to maintain target RPM. "
               + "Higher = snaps back to speed faster after a collision.")]
        private float _motorForce = 1000f;

        [SerializeField]
        [Tooltip("Rotation axis in local space. Vector3.up for horizontal spin, Vector3.right for vertical.")]
        private Vector3 _rotationAxis = Vector3.up;

        private HingeJoint _joint;
        private Rigidbody _hammerRb;

        private void Awake()
        {
            // This component sits on the PIVOT (kinematic anchor).
            // Find the hammer child — the first child with a non-kinematic Rigidbody.
            foreach (Transform child in transform)
            {
                Rigidbody rb = child.GetComponent<Rigidbody>();
                if (rb != null && !rb.isKinematic)
                {
                    _hammerRb = rb;
                    break;
                }
            }

            if (_hammerRb == null)
            {
                Debug.LogWarning("[TwirlingHammerHazard] No non-kinematic Rigidbody found on a child. "
                               + "Add a Rigidbody (IsKinematic=false, mass>=60) to the hammer child GameObject.", this);
                return;
            }

            // Get or add the HingeJoint on the hammer child.
            _joint = _hammerRb.GetComponent<HingeJoint>();
            if (_joint == null)
                _joint = _hammerRb.gameObject.AddComponent<HingeJoint>();

            // Connect to this pivot's Rigidbody (kinematic anchor).
            _joint.connectedBody = GetComponent<Rigidbody>();

            // Set rotation axis.
            _joint.axis = _rotationAxis;
            _joint.anchor = Vector3.zero;

            // Configure motor to spin at target RPM.
            ApplyMotorSettings();
        }

        private void OnValidate()
        {
            if (_joint != null)
                ApplyMotorSettings();
        }

        private void ApplyMotorSettings()
        {
            if (_joint == null) return;

            _joint.useMotor = true;
            _joint.useLimits = false;

            JointMotor motor = _joint.motor;
            // HingeJoint targetVelocity is in degrees/second. RPM * 6 = deg/s.
            motor.targetVelocity = _targetRpm * 6f;
            motor.force = _motorForce;
            motor.freeSpin = false;
            _joint.motor = motor;
        }
    }
}
