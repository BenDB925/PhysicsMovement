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
        private bool _grabLeftHeld;
        private bool _grabRightHeld;
        private bool _overrideGrabInput;

        // Track previous grab state for throw detection.
        private bool _wasGrabbingLeft;
        private bool _wasGrabbingRight;

        // Debug: hand renderers for grab color feedback.
        private Renderer _handRendererL;
        private Renderer _handRendererR;
        private Color _baseHandColor;
        private static readonly Color GrabColor = Color.red;

        // ── Public Properties ────────────────────────────────────────────────

        /// <summary>True when the left hand has an active grab FixedJoint.</summary>
        public bool IsGrabbingLeft => _zoneL != null && _zoneL.IsGrabbing;

        /// <summary>True when the right hand has an active grab FixedJoint.</summary>
        public bool IsGrabbingRight => _zoneR != null && _zoneR.IsGrabbing;

        /// <summary>True if the left hand was grabbing at the start of the current FixedUpdate (before release).</summary>
        public bool WasGrabbingLeft => _wasGrabbingLeft;

        /// <summary>True if the right hand was grabbing at the start of the current FixedUpdate (before release).</summary>
        public bool WasGrabbingRight => _wasGrabbingRight;

        /// <summary>True when the left hand is grabbing static geometry (wall/floor).</summary>
        public bool IsWallGrabbingLeft => _zoneL != null && _zoneL.IsGrabbing && _zoneL.IsWorldGrab;

        /// <summary>True when the right hand is grabbing static geometry (wall/floor).</summary>
        public bool IsWallGrabbingRight => _zoneR != null && _zoneR.IsGrabbing && _zoneR.IsWorldGrab;

        /// <summary>True when either hand is grabbing static geometry (wall/floor).</summary>
        public bool IsWallGrabbing => IsWallGrabbingLeft || IsWallGrabbingRight;

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
        /// Test seam: directly inject grab input state for both hands, bypassing the Input System.
        /// </summary>
        public void SetGrabInputForTest(bool held)
        {
            _grabLeftHeld = held;
            _grabRightHeld = held;
            _overrideGrabInput = true;
        }

        /// <summary>
        /// Test seam: directly inject per-hand grab input state, bypassing the Input System.
        /// </summary>
        public void SetGrabInputForTest(bool leftHeld, bool rightHeld)
        {
            _grabLeftHeld = leftHeld;
            _grabRightHeld = rightHeld;
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

            // STEP 4: Cache hand renderers for debug grab coloring.
            _handRendererL = FindChildRenderer(_zoneL);
            _handRendererR = FindChildRenderer(_zoneR);
            if (_handRendererL != null)
                _baseHandColor = _handRendererL.material.color;

            // STEP 5: Create and enable PlayerInputActions for grab input.
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
            // STEP 1: Read per-hand grab input (unless overridden by test seam).
            // Grab uses IsPressed (hold). Punch fires on release (in PunchController).
            if (!_overrideGrabInput)
            {
                _grabLeftHeld = _inputActions != null &&
                                _inputActions.Player.LeftHand.IsPressed();
                _grabRightHeld = _inputActions != null &&
                                 _inputActions.Player.RightHand.IsPressed();
            }

            // STEP 2: Track pre-update grab state for throw detection.
            _wasGrabbingLeft = IsGrabbingLeft;
            _wasGrabbingRight = IsGrabbingRight;

            // STEP 3: Process grab/release per hand.
            TryGrabOrRelease(_zoneL, _grabLeftHeld, _wasGrabbingLeft);
            TryGrabOrRelease(_zoneR, _grabRightHeld, _wasGrabbingRight);

            // STEP 4: Apply arm stiffening based on current grab state.
            ApplyArmStiffening();

            // STEP 5: Debug — color hands red while grabbing.
            UpdateGrabDebugColors();
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

        private void TryGrabOrRelease(HandGrabZone zone, bool inputHeld, bool wasGrabbing)
        {
            if (zone == null)
                return;

            if (inputHeld)
            {
                // Attempt grab if not already grabbing.
                if (!zone.IsGrabbing)
                {
                    // Prefer dynamic Rigidbody targets; fall back to static geometry (walls).
                    Rigidbody target = zone.NearestTarget;
                    if (target != null)
                    {
                        zone.CreateGrabJoint(target, _grabBreakForce, _grabBreakTorque);
                    }
                    else
                    {
                        Collider staticTarget = zone.NearestStaticCollider;
                        if (staticTarget != null)
                        {
                            Vector3 anchor = staticTarget.ClosestPoint(zone.transform.position);
                            zone.CreateWorldGrabJoint(anchor, _grabBreakForce, _grabBreakTorque);
                        }
                    }
                }
            }
            else if (wasGrabbing)
            {
                // Release and optionally throw (no throw for world grabs).
                Rigidbody releasedTarget = zone.GrabbedTarget;
                zone.DestroyGrabJoint();
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

        private static Renderer FindChildRenderer(HandGrabZone zone)
        {
            if (zone == null)
                return null;

            // The hand's visual mesh is on a child "Visual" GameObject.
            Transform visual = zone.transform.Find("Visual");
            if (visual != null)
                return visual.GetComponent<Renderer>();

            // Fallback: renderer directly on the hand.
            return zone.GetComponent<Renderer>();
        }

        private void UpdateGrabDebugColors()
        {
            if (_handRendererL != null)
                _handRendererL.material.color = IsGrabbingLeft ? GrabColor : _baseHandColor;
            if (_handRendererR != null)
                _handRendererR.material.color = IsGrabbingRight ? GrabColor : _baseHandColor;
        }
    }
}
