using PhysicsDrivenMovement.Input;
using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Controls the punch mechanic: reads punch input, stiffens the right arm, drives
    /// the UpperArm toward an extended-forward target rotation, and applies an impulse
    /// to the Hand Rigidbody for impact force.
    ///
    /// The punch is duration-based (default 0.3s) with a cooldown timer. It is gated on
    /// the character being in Standing or Moving state and the punch arm not currently
    /// grabbing (via <see cref="GrabController"/>).
    ///
    /// Attach to the Hips (root) GameObject alongside other character controllers.
    /// Lifecycle: Awake (cache joints + dependencies), Start (capture baseline drives),
    /// FixedUpdate (process punch state machine).
    /// Collaborators: <see cref="GrabController"/>, <see cref="BalanceController"/>,
    /// <see cref="CharacterState"/>.
    /// </summary>
    public class PunchController : MonoBehaviour
    {
        // ── Serialized Fields ────────────────────────────────────────────────

        [SerializeField, Range(0f, 1000f)]
        [Tooltip("Impulse magnitude applied to the punching hand's Rigidbody at punch start. " +
                 "Creates the forward punch motion. Default 80 N·s.")]
        private float _punchImpulse = 80f;

        [SerializeField, Range(0.05f, 1f)]
        [Tooltip("Duration of the punch in seconds. Arm is stiffened for this period. Default 0.3s.")]
        private float _punchDuration = 0.3f;

        [SerializeField, Range(0f, 2f)]
        [Tooltip("Cooldown time in seconds after a punch before another can be started. Default 0.5s.")]
        private float _punchCooldown = 0.5f;

        [SerializeField, Range(1f, 10f)]
        [Tooltip("Multiplier applied to right arm joint springs during a punch. Default 4.")]
        private float _punchArmSpringMultiplier = 4f;

        [SerializeField, Range(1f, 10f)]
        [Tooltip("Multiplier applied to right arm joint dampers during a punch. Default 2.")]
        private float _punchArmDamperMultiplier = 2f;

        [SerializeField, Range(0f, 90f)]
        [Tooltip("Target angle (degrees) for the UpperArm_R joint during punch — drives arm forward. Default 60.")]
        private float _punchTargetAngle = 60f;

        // ── Private Fields ───────────────────────────────────────────────────

        private BalanceController _balance;
        private CharacterState _characterState;
        private GrabController _grabController;

        private ConfigurableJoint _upperArmR;
        private ConfigurableJoint _lowerArmR;
        private ConfigurableJoint _handJointR;
        private Rigidbody _handRbR;

        private JointDrive _baseUpperArmRDrive;
        private JointDrive _baseLowerArmRDrive;
        private JointDrive _baseHandRDrive;
        private bool _baselinesCaptured;

        private bool _isPunching;
        private float _punchTimer;
        private float _cooldownTimer;

        // Input
        private PlayerInputActions _inputActions;
        private bool _punchInputPressed;
        private bool _overridePunchInput;

        // ── Public Properties ────────────────────────────────────────────────

        /// <summary>True while the punch is active (arm stiffened, impulse applied).</summary>
        public bool IsPunching => _isPunching;

        // ── Test Seams ───────────────────────────────────────────────────────

        /// <summary>
        /// Test seam: directly inject punch input, bypassing the Input System.
        /// </summary>
        public void SetPunchInputForTest(bool pressed)
        {
            _punchInputPressed = pressed;
            _overridePunchInput = true;
        }

        // ── Unity Lifecycle ──────────────────────────────────────────────────

        private void Awake()
        {
            TryGetComponent(out _balance);
            TryGetComponent(out _characterState);
            TryGetComponent(out _grabController);

            // Find right arm joints by name.
            ConfigurableJoint[] allJoints = GetComponentsInChildren<ConfigurableJoint>(includeInactive: true);
            foreach (ConfigurableJoint joint in allJoints)
            {
                switch (joint.gameObject.name)
                {
                    case "UpperArm_R": _upperArmR = joint; break;
                    case "LowerArm_R": _lowerArmR = joint; break;
                    case "Hand_R":     _handJointR = joint; break;
                }
            }

            // Cache Hand_R Rigidbody for impulse.
            if (_handJointR != null)
            {
                _handRbR = _handJointR.GetComponent<Rigidbody>();
            }

            // Create and enable PlayerInputActions for punch input.
            _inputActions = new PlayerInputActions();
            _inputActions.Enable();
        }

        private void Start()
        {
            CaptureBaselineDrives();
        }

        private void FixedUpdate()
        {
            // STEP 1: Read punch input (unless overridden).
            if (!_overridePunchInput)
            {
                _punchInputPressed = _inputActions != null &&
                                     _inputActions.Player.Punch.WasPressedThisFrame();
            }

            // STEP 2: Tick cooldown.
            if (_cooldownTimer > 0f)
            {
                _cooldownTimer -= Time.fixedDeltaTime;
            }

            // STEP 3: If currently punching, tick the punch timer.
            if (_isPunching)
            {
                _punchTimer -= Time.fixedDeltaTime;
                if (_punchTimer <= 0f)
                {
                    EndPunch();
                }
                return;
            }

            // STEP 4: Try to start a new punch.
            if (_punchInputPressed && CanPunch())
            {
                StartPunch();
            }

            // Consume the input.
            if (_overridePunchInput)
            {
                _punchInputPressed = false;
                _overridePunchInput = false;
            }
        }

        private void OnDestroy()
        {
            if (_inputActions != null)
            {
                _inputActions.Dispose();
                _inputActions = null;
            }
        }

        // ── Private Methods ──────────────────────────────────────────────────

        private bool CanPunch()
        {
            // Gate: cooldown must have expired.
            if (_cooldownTimer > 0f)
                return false;

            // Gate: must not be fallen.
            if (_balance != null && _balance.IsFallen)
                return false;

            // Gate: character must be Standing or Moving.
            if (_characterState != null)
            {
                CharacterStateType state = _characterState.CurrentState;
                if (state != CharacterStateType.Standing && state != CharacterStateType.Moving)
                    return false;
            }

            // Gate: right arm must not be grabbing.
            if (_grabController != null && _grabController.IsGrabbingRight)
                return false;

            return true;
        }

        private void StartPunch()
        {
            _isPunching = true;
            _punchTimer = _punchDuration;

            // Stiffen right arm joints.
            StiffenRightArm(true);

            // Drive UpperArm_R toward the extended-forward target angle.
            if (_upperArmR != null)
            {
                _upperArmR.targetRotation = Quaternion.AngleAxis(_punchTargetAngle, Vector3.forward);
            }

            // Apply forward impulse to the hand.
            if (_handRbR != null)
            {
                Vector3 punchDir = transform.forward;
                _handRbR.AddForce(punchDir * _punchImpulse, ForceMode.Impulse);
            }
        }

        private void EndPunch()
        {
            _isPunching = false;
            _cooldownTimer = _punchCooldown;

            // Restore right arm drives to baseline.
            StiffenRightArm(false);

            // Reset target rotation to identity.
            if (_upperArmR != null)
            {
                _upperArmR.targetRotation = Quaternion.identity;
            }
        }

        private void CaptureBaselineDrives()
        {
            if (_upperArmR != null) _baseUpperArmRDrive = _upperArmR.slerpDrive;
            if (_lowerArmR != null) _baseLowerArmRDrive = _lowerArmR.slerpDrive;
            if (_handJointR != null) _baseHandRDrive = _handJointR.slerpDrive;
            _baselinesCaptured = true;
        }

        private void StiffenRightArm(bool stiffen)
        {
            if (!_baselinesCaptured)
                return;

            ApplyDrive(_upperArmR, _baseUpperArmRDrive, stiffen);
            ApplyDrive(_lowerArmR, _baseLowerArmRDrive, stiffen);
            ApplyDrive(_handJointR, _baseHandRDrive, stiffen);
        }

        private void ApplyDrive(ConfigurableJoint joint, JointDrive baseDrive, bool stiffen)
        {
            if (joint == null)
                return;

            JointDrive drive = baseDrive;
            if (stiffen)
            {
                drive.positionSpring = baseDrive.positionSpring * _punchArmSpringMultiplier;
                drive.positionDamper = baseDrive.positionDamper * _punchArmDamperMultiplier;
            }

            joint.slerpDrive = drive;
        }
    }
}
