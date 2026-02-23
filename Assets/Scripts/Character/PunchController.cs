using PhysicsDrivenMovement.Input;
using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Controls the punch mechanic for both hands: fires on button RELEASE (so a
    /// quick click-release = punch, while holding = grab via <see cref="GrabController"/>).
    /// Stiffens the corresponding arm, drives the UpperArm toward an extended-forward
    /// target rotation, and applies an impulse to the Hand Rigidbody for impact force.
    ///
    /// Each hand punches independently with its own duration timer and cooldown.
    /// Punch is gated on the character being in Standing or Moving state, the
    /// punch arm not currently grabbing, and the release not being a grab-release.
    ///
    /// Attach to the Hips (root) GameObject alongside other character controllers.
    /// Lifecycle: Awake (cache joints + dependencies), Start (capture baseline drives),
    /// FixedUpdate (process punch state machine per hand).
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
        [Tooltip("Multiplier applied to arm joint springs during a punch. Default 4.")]
        private float _punchArmSpringMultiplier = 4f;

        [SerializeField, Range(1f, 10f)]
        [Tooltip("Multiplier applied to arm joint dampers during a punch. Default 2.")]
        private float _punchArmDamperMultiplier = 2f;

        [SerializeField, Range(0f, 90f)]
        [Tooltip("Target angle (degrees) for the UpperArm joint during punch — drives arm forward. Default 60.")]
        private float _punchTargetAngle = 60f;

        // ── Per-Hand Arm Data ───────────────────────────────────────────────

        private struct ArmData
        {
            public ConfigurableJoint UpperArm;
            public ConfigurableJoint LowerArm;
            public ConfigurableJoint HandJoint;
            public Rigidbody HandRb;

            public JointDrive BaseUpperArmDrive;
            public JointDrive BaseLowerArmDrive;
            public JointDrive BaseHandDrive;

            public bool IsPunching;
            public float PunchTimer;
            public float CooldownTimer;
        }

        // ── Private Fields ──────────────────────────────────────────────────

        private BalanceController _balance;
        private CharacterState _characterState;
        private GrabController _grabController;

        private ArmData _left;
        private ArmData _right;
        private bool _baselinesCaptured;

        // Input
        private PlayerInputActions _inputActions;
        private bool _leftPunchPressed;
        private bool _rightPunchPressed;
        private bool _overridePunchInput;

        // ── Public Properties ───────────────────────────────────────────────

        /// <summary>True while the left arm is actively punching.</summary>
        public bool IsPunchingLeft => _left.IsPunching;

        /// <summary>True while the right arm is actively punching.</summary>
        public bool IsPunchingRight => _right.IsPunching;

        /// <summary>True while either arm is actively punching.</summary>
        public bool IsPunching => _left.IsPunching || _right.IsPunching;

        // ── Test Seams ──────────────────────────────────────────────────────

        /// <summary>
        /// Test seam: directly inject punch input for both hands, bypassing the Input System.
        /// </summary>
        public void SetPunchInputForTest(bool pressed)
        {
            _leftPunchPressed = pressed;
            _rightPunchPressed = pressed;
            _overridePunchInput = true;
        }

        /// <summary>
        /// Test seam: directly inject per-hand punch input, bypassing the Input System.
        /// </summary>
        public void SetPunchInputForTest(bool leftPressed, bool rightPressed)
        {
            _leftPunchPressed = leftPressed;
            _rightPunchPressed = rightPressed;
            _overridePunchInput = true;
        }

        // ── Unity Lifecycle ─────────────────────────────────────────────────

        private void Awake()
        {
            TryGetComponent(out _balance);
            TryGetComponent(out _characterState);
            TryGetComponent(out _grabController);

            // Find arm joints by name.
            ConfigurableJoint[] allJoints = GetComponentsInChildren<ConfigurableJoint>(includeInactive: true);
            foreach (ConfigurableJoint joint in allJoints)
            {
                switch (joint.gameObject.name)
                {
                    case "UpperArm_L": _left.UpperArm = joint; break;
                    case "LowerArm_L": _left.LowerArm = joint; break;
                    case "Hand_L":     _left.HandJoint = joint; break;
                    case "UpperArm_R": _right.UpperArm = joint; break;
                    case "LowerArm_R": _right.LowerArm = joint; break;
                    case "Hand_R":     _right.HandJoint = joint; break;
                }
            }

            // Cache Hand Rigidbodies for impulse.
            if (_left.HandJoint != null)
                _left.HandRb = _left.HandJoint.GetComponent<Rigidbody>();
            if (_right.HandJoint != null)
                _right.HandRb = _right.HandJoint.GetComponent<Rigidbody>();

            // Create and enable PlayerInputActions.
            _inputActions = new PlayerInputActions();
            _inputActions.Enable();
        }

        private void Start()
        {
            CaptureBaselineDrives();
        }

        private void FixedUpdate()
        {
            // STEP 1: Read per-hand punch input (unless overridden).
            // Punch fires on button RELEASE. If the hand was grabbing, the release
            // is a grab-release (handled by GrabController), not a punch.
            if (!_overridePunchInput)
            {
                bool leftReleased = _inputActions != null &&
                                    _inputActions.Player.LeftHand.WasReleasedThisFrame();
                bool rightReleased = _inputActions != null &&
                                     _inputActions.Player.RightHand.WasReleasedThisFrame();

                // Only punch if the hand was NOT grabbing (grab release ≠ punch).
                _leftPunchPressed = leftReleased &&
                                    !(_grabController != null && _grabController.WasGrabbingLeft);
                _rightPunchPressed = rightReleased &&
                                     !(_grabController != null && _grabController.WasGrabbingRight);
            }

            // STEP 2: Tick cooldowns.
            if (_left.CooldownTimer > 0f)
                _left.CooldownTimer -= Time.fixedDeltaTime;
            if (_right.CooldownTimer > 0f)
                _right.CooldownTimer -= Time.fixedDeltaTime;

            // STEP 3: Process each hand's punch state.
            ProcessHand(ref _left, _leftPunchPressed, isLeftHand: true);
            ProcessHand(ref _right, _rightPunchPressed, isLeftHand: false);

            // Consume the input.
            if (_overridePunchInput)
            {
                _leftPunchPressed = false;
                _rightPunchPressed = false;
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

        // ── Private Methods ─────────────────────────────────────────────────

        private void ProcessHand(ref ArmData arm, bool inputPressed, bool isLeftHand)
        {
            // If currently punching, tick the punch timer.
            if (arm.IsPunching)
            {
                arm.PunchTimer -= Time.fixedDeltaTime;
                if (arm.PunchTimer <= 0f)
                {
                    EndPunch(ref arm);
                }
                return;
            }

            // Try to start a new punch.
            if (inputPressed && CanPunch(ref arm, isLeftHand))
            {
                StartPunch(ref arm);
            }
        }

        private bool CanPunch(ref ArmData arm, bool isLeftHand)
        {
            if (arm.CooldownTimer > 0f)
                return false;

            if (_balance != null && _balance.IsFallen)
                return false;

            if (_characterState != null)
            {
                CharacterStateType state = _characterState.CurrentState;
                if (state != CharacterStateType.Standing && state != CharacterStateType.Moving)
                    return false;
            }

            // Gate: this arm must not be grabbing.
            if (_grabController != null)
            {
                if (isLeftHand && _grabController.IsGrabbingLeft)
                    return false;
                if (!isLeftHand && _grabController.IsGrabbingRight)
                    return false;
            }

            return true;
        }

        private void StartPunch(ref ArmData arm)
        {
            arm.IsPunching = true;
            arm.PunchTimer = _punchDuration;

            StiffenArm(ref arm, true);

            if (arm.UpperArm != null)
            {
                arm.UpperArm.targetRotation = Quaternion.AngleAxis(_punchTargetAngle, Vector3.forward);
            }

            if (arm.HandRb != null)
            {
                Vector3 punchDir = transform.forward;
                arm.HandRb.AddForce(punchDir * _punchImpulse, ForceMode.Impulse);
            }
        }

        private void EndPunch(ref ArmData arm)
        {
            arm.IsPunching = false;
            arm.CooldownTimer = _punchCooldown;

            StiffenArm(ref arm, false);

            if (arm.UpperArm != null)
            {
                arm.UpperArm.targetRotation = Quaternion.identity;
            }
        }

        private void CaptureBaselineDrives()
        {
            CaptureArmBaseline(ref _left);
            CaptureArmBaseline(ref _right);
            _baselinesCaptured = true;
        }

        private static void CaptureArmBaseline(ref ArmData arm)
        {
            if (arm.UpperArm != null) arm.BaseUpperArmDrive = arm.UpperArm.slerpDrive;
            if (arm.LowerArm != null) arm.BaseLowerArmDrive = arm.LowerArm.slerpDrive;
            if (arm.HandJoint != null) arm.BaseHandDrive = arm.HandJoint.slerpDrive;
        }

        private void StiffenArm(ref ArmData arm, bool stiffen)
        {
            if (!_baselinesCaptured)
                return;

            ApplyDrive(arm.UpperArm, arm.BaseUpperArmDrive, stiffen);
            ApplyDrive(arm.LowerArm, arm.BaseLowerArmDrive, stiffen);
            ApplyDrive(arm.HandJoint, arm.BaseHandDrive, stiffen);
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
