using PhysicsDrivenMovement.Input;
using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Centralised coordinator for the grab mechanic. Lives on the Hips root alongside
    /// <see cref="BalanceController"/> and <see cref="PlayerMovement"/>. Finds
    /// <see cref="HandGrabZone"/> components on Hand_L and Hand_R in Awake, then reads
    /// grab input each FixedUpdate to create/destroy FixedJoints via the zones.
    ///
    /// Grabbing is orthogonal to <see cref="CharacterStateType"/> — you can grab while
    /// Standing, Moving, or Airborne. Other systems query <see cref="IsGrabbingLeft"/>
    /// and <see cref="IsGrabbingRight"/> to react (e.g. <see cref="ArmAnimator"/> skips
    /// swing on grabbed arm).
    ///
    /// Also handles arm stiffening: when a hand is grabbing, the arm joints on that side
    /// have their SLERP drive springs/dampers multiplied to hold the grab firmly.
    ///
    /// Throwing: on grab release, if the character's horizontal speed exceeds
    /// <see cref="_throwMinSpeed"/>, an impulse is applied to the released object
    /// proportional to the character's velocity.
    ///
    /// Attach to the Hips (root) GameObject.
    /// Lifecycle: Awake (find zones + arm joints), Start (capture baseline drives),
    /// FixedUpdate (process grab/release + arm drives).
    /// Collaborators: <see cref="HandGrabZone"/>, <see cref="ArmAnimator"/>,
    /// <see cref="RagdollSetup"/>.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public class GrabController : MonoBehaviour
    {
        // ── Serialized Fields ────────────────────────────────────────────────

        [Header("Grab")]
        [SerializeField, Range(100f, 10000f)]
        [Tooltip("Break force threshold for the grab FixedJoint. " +
                 "Higher = harder to pull apart. Default 2000 N.")]
        private float _grabBreakForce = 2000f;

        [SerializeField, Range(100f, 10000f)]
        [Tooltip("Break torque threshold for the grab FixedJoint. " +
                 "Higher = harder to twist apart. Default 2000 Nm.")]
        private float _grabBreakTorque = 2000f;

        [Header("Arm Stiffening")]
        [SerializeField, Range(1f, 10f)]
        [Tooltip("Multiplier applied to arm joint SLERP drive springs while grabbing. " +
                 "Higher = stiffer hold. Default 3.")]
        private float _grabArmSpringMultiplier = 3f;

        [SerializeField, Range(1f, 10f)]
        [Tooltip("Multiplier applied to arm joint SLERP drive dampers while grabbing. " +
                 "Higher = less oscillation during grab. Default 2.")]
        private float _grabArmDamperMultiplier = 2f;

        [Header("Throwing")]
        [SerializeField, Range(0f, 50f)]
        [Tooltip("Impulse multiplier applied to the released target when throwing. " +
                 "Final impulse = velocity.normalized * speed * multiplier. Default 10.")]
        private float _throwForceMultiplier = 10f;

        [SerializeField, Range(0f, 10f)]
        [Tooltip("Minimum horizontal speed (m/s) required for a throw on grab release. " +
                 "Below this speed, the object is simply dropped. Default 1.")]
        private float _throwMinSpeed = 1f;

        // ── Private Fields ───────────────────────────────────────────────────

        private HandGrabZone _zoneL;
        private HandGrabZone _zoneR;
        private Rigidbody _hipsRb;

        // Arm joints for stiffening (left side: UpperArm_L, LowerArm_L, Hand_L)
        private ConfigurableJoint _upperArmL;
        private ConfigurableJoint _lowerArmL;
        private ConfigurableJoint _handJointL;

        // Arm joints for stiffening (right side: UpperArm_R, LowerArm_R, Hand_R)
        private ConfigurableJoint _upperArmR;
        private ConfigurableJoint _lowerArmR;
        private ConfigurableJoint _handJointR;

        // Baseline drives captured in Start after RagdollSetup has applied values.
        private JointDrive _baseUpperArmLDrive;
        private JointDrive _baseLowerArmLDrive;
        private JointDrive _baseHandLDrive;
        private JointDrive _baseUpperArmRDrive;
        private JointDrive _baseLowerArmRDrive;
        private JointDrive _baseHandRDrive;

        private bool _baselinesCaptured;

        // Input
        private PlayerInputActions _inputActions;
        private bool _grabInputHeld;
        private bool _overrideGrabInput;

        // Track previous grab state for throw detection.
        private bool _wasGrabbingLeft;
        private bool _wasGrabbingRight;

        // ── Public Properties ────────────────────────────────────────────────

        /// <summary>True when the left hand has an active grab FixedJoint.</summary>
        public bool IsGrabbingLeft => _zoneL != null && _zoneL.IsGrabbing;

        /// <summary>True when the right hand has an active grab FixedJoint.</summary>
        public bool IsGrabbingRight => _zoneR != null && _zoneR.IsGrabbing;

        /// <summary>The Rigidbody currently grabbed by the left hand, or null.</summary>
        public Rigidbody GrabbedTargetLeft => _zoneL != null ? _zoneL.GrabbedTarget : null;

        /// <summary>The Rigidbody currently grabbed by the right hand, or null.</summary>
        public Rigidbody GrabbedTargetRight => _zoneR != null ? _zoneR.GrabbedTarget : null;

        /// <summary>Left hand grab zone reference, exposed for tests.</summary>
        public HandGrabZone ZoneL => _zoneL;

        /// <summary>Right hand grab zone reference, exposed for tests.</summary>
        public HandGrabZone ZoneR => _zoneR;

        // ── Test Seams ───────────────────────────────────────────────────────

        /// <summary>
        /// Test seam: directly inject grab input state, bypassing the Input System.
        /// </summary>
        public void SetGrabInputForTest(bool held)
        {
            _grabInputHeld = held;
            _overrideGrabInput = true;
        }

        // ── Unity Lifecycle ──────────────────────────────────────────────────

        private void Awake()
        {
            // STEP 1: Cache Hips Rigidbody.
            TryGetComponent(out _hipsRb);

            // STEP 2: Find HandGrabZone components on Hand_L and Hand_R.
            HandGrabZone[] zones = GetComponentsInChildren<HandGrabZone>(includeInactive: true);
            foreach (HandGrabZone zone in zones)
            {
                switch (zone.gameObject.name)
                {
                    case "Hand_L":
                        _zoneL = zone;
                        break;
                    case "Hand_R":
                        _zoneR = zone;
                        break;
                }
            }

            if (_zoneL == null)
                Debug.LogWarning("[GrabController] HandGrabZone not found on 'Hand_L'.", this);
            if (_zoneR == null)
                Debug.LogWarning("[GrabController] HandGrabZone not found on 'Hand_R'.", this);

            // STEP 3: Find arm ConfigurableJoints by name.
            ConfigurableJoint[] allJoints = GetComponentsInChildren<ConfigurableJoint>(includeInactive: true);
            foreach (ConfigurableJoint joint in allJoints)
            {
                switch (joint.gameObject.name)
                {
                    case "UpperArm_L": _upperArmL = joint; break;
                    case "LowerArm_L": _lowerArmL = joint; break;
                    case "Hand_L":     _handJointL = joint; break;
                    case "UpperArm_R": _upperArmR = joint; break;
                    case "LowerArm_R": _lowerArmR = joint; break;
                    case "Hand_R":     _handJointR = joint; break;
                }
            }

            // STEP 4: Create and enable PlayerInputActions for grab input.
            _inputActions = new PlayerInputActions();
            _inputActions.Enable();
        }

        private void Start()
        {
            // Capture baseline drives after RagdollSetup.Awake has applied authoritative values.
            CaptureBaselineDrives();
        }

        private void FixedUpdate()
        {
            // STEP 1: Read grab input (unless overridden by test seam).
            if (!_overrideGrabInput)
            {
                _grabInputHeld = _inputActions != null &&
                                 _inputActions.Player.Grab.IsPressed();
            }

            // STEP 2: Track pre-update grab state for throw detection.
            _wasGrabbingLeft = IsGrabbingLeft;
            _wasGrabbingRight = IsGrabbingRight;

            // STEP 3: Process grab/release.
            if (_grabInputHeld)
            {
                TryGrab();
            }
            else
            {
                TryRelease();
            }

            // STEP 4: Apply arm stiffening based on current grab state.
            ApplyArmStiffening();
        }

        private void OnDestroy()
        {
            if (_inputActions != null)
            {
                _inputActions.Dispose();
                _inputActions = null;
            }
        }

        /// <summary>
        /// Called by Unity when any FixedJoint on this hierarchy breaks.
        /// We check if either hand zone's joint was the one that broke.
        /// </summary>
        private void OnJointBreak(float breakForce)
        {
            // Unity destroys the joint component after this callback.
            // We need to notify the zones so they clear their internal reference.
            // Since we can't determine which joint broke from the callback alone,
            // we schedule a cleanup check.
            // The zones' FixedUpdate will detect the null connectedBody next frame.
            if (_zoneL != null && _zoneL.IsGrabbing)
            {
                // Check next frame — the joint is about to be destroyed.
                _zoneL.NotifyJointBroken();
            }
            if (_zoneR != null && _zoneR.IsGrabbing)
            {
                _zoneR.NotifyJointBroken();
            }
        }

        // ── Private Methods ──────────────────────────────────────────────────

        private void TryGrab()
        {
            // Attempt grab on left hand if not already grabbing.
            if (_zoneL != null && !_zoneL.IsGrabbing)
            {
                Rigidbody target = _zoneL.NearestTarget;
                if (target != null)
                {
                    _zoneL.CreateGrabJoint(target, _grabBreakForce, _grabBreakTorque);
                }
            }

            // Attempt grab on right hand if not already grabbing.
            if (_zoneR != null && !_zoneR.IsGrabbing)
            {
                Rigidbody target = _zoneR.NearestTarget;
                if (target != null)
                {
                    _zoneR.CreateGrabJoint(target, _grabBreakForce, _grabBreakTorque);
                }
            }
        }

        private void TryRelease()
        {
            // Release left hand.
            if (_zoneL != null && _wasGrabbingLeft)
            {
                Rigidbody releasedTarget = _zoneL.GrabbedTarget;
                _zoneL.DestroyGrabJoint();
                TryApplyThrowImpulse(releasedTarget);
            }

            // Release right hand.
            if (_zoneR != null && _wasGrabbingRight)
            {
                Rigidbody releasedTarget = _zoneR.GrabbedTarget;
                _zoneR.DestroyGrabJoint();
                TryApplyThrowImpulse(releasedTarget);
            }
        }

        private void TryApplyThrowImpulse(Rigidbody target)
        {
            if (target == null || _hipsRb == null)
                return;

            Vector3 hVel = new Vector3(_hipsRb.linearVelocity.x, 0f, _hipsRb.linearVelocity.z);
            float speed = hVel.magnitude;

            if (speed < _throwMinSpeed)
                return;

            Vector3 throwImpulse = hVel.normalized * speed * _throwForceMultiplier;
            target.AddForce(throwImpulse, ForceMode.Impulse);
        }

        private void CaptureBaselineDrives()
        {
            if (_upperArmL != null) _baseUpperArmLDrive = _upperArmL.slerpDrive;
            if (_lowerArmL != null) _baseLowerArmLDrive = _lowerArmL.slerpDrive;
            if (_handJointL != null) _baseHandLDrive = _handJointL.slerpDrive;
            if (_upperArmR != null) _baseUpperArmRDrive = _upperArmR.slerpDrive;
            if (_lowerArmR != null) _baseLowerArmRDrive = _lowerArmR.slerpDrive;
            if (_handJointR != null) _baseHandRDrive = _handJointR.slerpDrive;
            _baselinesCaptured = true;
        }

        private void ApplyArmStiffening()
        {
            if (!_baselinesCaptured)
                return;

            // Left arm: stiffen when grabbing, restore when not.
            bool leftGrabbing = IsGrabbingLeft;
            ApplyDriveMultiplier(_upperArmL, _baseUpperArmLDrive, leftGrabbing);
            ApplyDriveMultiplier(_lowerArmL, _baseLowerArmLDrive, leftGrabbing);
            ApplyDriveMultiplier(_handJointL, _baseHandLDrive, leftGrabbing);

            // Right arm: stiffen when grabbing, restore when not.
            bool rightGrabbing = IsGrabbingRight;
            ApplyDriveMultiplier(_upperArmR, _baseUpperArmRDrive, rightGrabbing);
            ApplyDriveMultiplier(_lowerArmR, _baseLowerArmRDrive, rightGrabbing);
            ApplyDriveMultiplier(_handJointR, _baseHandRDrive, rightGrabbing);
        }

        private void ApplyDriveMultiplier(ConfigurableJoint joint, JointDrive baseDrive, bool stiffen)
        {
            if (joint == null)
                return;

            JointDrive drive = baseDrive;
            if (stiffen)
            {
                drive.positionSpring = baseDrive.positionSpring * _grabArmSpringMultiplier;
                drive.positionDamper = baseDrive.positionDamper * _grabArmDamperMultiplier;
            }

            joint.slerpDrive = drive;
        }
    }
}
