using System.IO;
using PhysicsDrivenMovement.Core;
using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Drives procedural gait animation on the ragdoll's four leg ConfigurableJoints
    /// by advancing a phase accumulator from actual Rigidbody horizontal speed and
    /// applying sinusoidal target rotations each FixedUpdate.
    ///
    /// Subsystem decomposition (each is a separate file for focused editing):
    ///   • <see cref="LegExecutionProfileResolver"/> — state-driven execution profiles.
    ///   • <see cref="GaitConfidenceEvaluator"/> — confidence evaluation and fallback blend.
    ///   • <see cref="LegJointDriver"/> — joint target application (world/local space).
    ///
    /// Lifecycle: Awake (cache joints + siblings), FixedUpdate (advance phase, apply rotations).
    /// Collaborators: <see cref="PlayerMovement"/>, <see cref="CharacterState"/>, <see cref="Rigidbody"/>.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class LegAnimator : MonoBehaviour
    {
        // ── Serialized Gait Fields ──────────────────────────────────────────

        [SerializeField, Range(0f, 60f)]
        [Tooltip("Peak forward/backward swing angle (degrees) for upper leg joints during gait. " +
                 "Controls the visible stride amplitude. Typical range: 15–35°.")]
        private float _stepAngle = 60f;

        [SerializeField, Range(60f, 90f)]
        [Tooltip("Peak forward/backward swing angle (degrees) at full sprint. " +
             "Blended from _stepAngle by PlayerMovement.SprintNormalized so sprint widens " +
             "the visible stride without changing step-target planning. Default 75°.")]
        private float _sprintStepAngle = 75f;

        [SerializeField, Tooltip("Seed for organic variation RNG. Change to alter the character's movement personality.")]
        private int _noiseSeed = 42;

        [SerializeField, Tooltip("When true, all organic gait variation is bypassed. Used by tests that need deterministic foot placement.")]
        private bool _disableOrganicVariation = false;

        [SerializeField, Tooltip("Stride length multiplier for the left leg. 1.0 = symmetric. Values slightly above 1.0 give a subtle natural asymmetry (e.g. 1.04).")]
        private float _leftStrideAsymmetry = 1.0f;

        [SerializeField, Tooltip("Stride length multiplier for the right leg. 1.0 = symmetric.")]
        private float _rightStrideAsymmetry = 1.0f;

        [SerializeField, Range(0.1f, 5f)]
        [Tooltip("Scales actual horizontal speed (m/s) to gait cycles per second. " +
                 "At 2 m/s with scale 1.5 → 3 cycles/sec. " +
                 "Eliminates body-outruns-legs: cadence is always proportional to real speed.")]
        private float _stepFrequencyScale = 0.1f;

        [SerializeField, Range(0f, 10f)]
        [Tooltip("Minimum gait cadence in cycles per second, applied even when the character " +
                 "is nearly stationary (optional slow idle cycle). Default 0 = legs still at idle.")]
        private float _stepFrequency = 1f;

        [SerializeField, Range(1f, 2f)]
        [Tooltip("Extra cadence multiplier at full sprint. Blended from 1x by " +
             "PlayerMovement.SprintNormalized so sprint steps faster without changing " +
             "the authored walk cadence. Default 1.2x.")]
        private float _sprintCadenceBoost = 1.2f;

        [SerializeField, Range(0f, 75f)]
        [Tooltip("Constant knee-bend angle (degrees) applied to lower leg joints during gait. " +
                 "Larger values = more aggressive, deliberate stride. Default 65°.")]
        private float _kneeAngle = 60f;

           [SerializeField, Range(60f, 90f)]
           [Tooltip("Constant knee-bend angle (degrees) at full sprint. Blended from " +
               "_kneeAngle by PlayerMovement.SprintNormalized so sprint adds a stronger " +
               "rear-kick without changing the walk gait. Default 70°.")]
           private float _sprintKneeAngle = 70f;

        [SerializeField, Range(0f, 55f)]
        [Tooltip("Extra upward lift bias (degrees) added to the upper leg that is in the " +
                 "forward-swing phase (sin(phase) > 0). Biases the knee toward the chest " +
                 "for a powerful, high-stepping gait. Default 15°.")]
        private float _upperLegLiftBoost = 31.9f;

        [SerializeField, Range(30f, 55f)]
        [Tooltip("Extra upward lift bias (degrees) at full sprint. Blended from " +
             "_upperLegLiftBoost by PlayerMovement.SprintNormalized so sprint gains " +
             "a higher-knee forward swing without changing the walk gait. Default 42°.")]
        private float _sprintUpperLegLiftBoost = 42f;

        [SerializeField, Range(0.05f, 0.5f)]
        [Tooltip("Requested clearance height (metres) that maps to the full step-up execution boost. " +
             "Smaller planner requests scale proportionally below this reference height.")]
        private float _stepUpClearanceReferenceHeight = 0.10f;

        [SerializeField, Range(0f, 45f)]
        [Tooltip("Extra upper-leg swing angle (degrees) available to step-up-tagged swings when the " +
             "planner requests terrain clearance.")]
           private float _stepUpClearanceSwingBoost = 16f;

        [SerializeField, Range(0f, 45f)]
        [Tooltip("Extra knee-bend angle (degrees) available to step-up-tagged swings when the " +
             "planner requests terrain clearance.")]
        private float _stepUpClearanceKneeBoost = 42f;

        [SerializeField, Range(0f, 20f)]
        [Tooltip("Controls three related smooth-transition behaviours:\n" +
                 "1. Phase decay rate: how quickly the gait phase accumulator decays toward zero\n" +
                 "   during idle (radians removed per fixedDeltaTime × π).\n" +
                 "2. Gait entry ramp: how quickly the smoothed input scale lerps from 0→1 when\n" +
                 "   movement resumes, preventing a snap to full gait amplitude.\n" +
                 "Higher = faster transition; 0 = no smoothing. Typical: 3–8.")]
        private float _idleBlendSpeed = 5f;

        // DESIGN: _swingAxis and _kneeAxis are specified in ConfigurableJoint targetRotation
        //         space, which maps joint.axis (the primary hinge) to the Z component of the
        //         Euler/quaternion rotation — NOT the X component as intuition might suggest.
        //         With joint.axis=Vector3.right and secondaryAxis=Vector3.forward (as set by
        //         RagdollBuilder for all leg joints), the targetRotation frame is:
        //           Z → joint.axis (Vector3.right) → sagittal forward/backward swing ✓
        //           X → secondaryAxis (Vector3.forward) → lateral abduction (unwanted for gait)
        //           Y → right×forward = Vector3.down → leg twist (unwanted for gait)
        //         Therefore the correct axis for leg swing and knee bend is Vector3.forward (Z).
        //         These fields are exposed for Inspector tuning so they can be adjusted without
        //         code changes if the ragdoll's joint axes are reconfigured in future.
        //         These are only used when _useWorldSpaceSwing = false (legacy mode).

        [SerializeField]
        [Tooltip("Rotation axis for upper-leg forward/backward swing in ConfigurableJoint targetRotation " +
                 "space. With RagdollBuilder defaults (joint.axis=right, secondaryAxis=forward), " +
                 "the correct value is (0, 0, 1) — Z maps to the primary hinge axis. " +
                 "Only used when _useWorldSpaceSwing is false. " +
                 "Adjust if the joint axis configuration changes.")]
        private Vector3 _swingAxis = new Vector3(0f, 0f, 1f);

        [SerializeField]
        [Tooltip("Rotation axis for lower-leg knee bend in ConfigurableJoint targetRotation space. " +
                 "With RagdollBuilder defaults (joint.axis=right, secondaryAxis=forward), " +
                 "the correct value is (0, 0, 1) — Z maps to the primary hinge axis. " +
                 "Only used when _useWorldSpaceSwing is false. " +
                 "Adjust if the joint axis configuration changes.")]
        private Vector3 _kneeAxis = new Vector3(0f, 0f, 1f);

        [SerializeField]
        [Tooltip("When true (default), leg swing targets are computed relative to the world-space " +
                 "movement direction so legs always step forward regardless of torso pitch angle. " +
                 "When false, the legacy local-frame swing is used (may cause feet to drag when " +
                 "the torso is pitched forward). Toggle for side-by-side comparison.")]
        private bool _useWorldSpaceSwing = false;

        [SerializeField]
        [Tooltip("When true, writes one debug line every 10 FixedUpdate frames to " +
                 "Logs/debug_gait.txt and also outputs to Debug.Log. Disabled by default.")]
        private bool _debugLog = false;

        [SerializeField]
        [Tooltip("Logs per-leg state transitions emitted by the Chapter 3 pass-through gait model.")]
        private bool _debugStateTransitions = false;

        [SerializeField]
        [Tooltip("When enabled, explicit Chapter 3 leg states shape per-leg upper-leg and knee targets before the legacy sinusoidal executor applies them. Disable to fall back to the raw pass-through command angles during migration.")]
        private bool _useStateDrivenExecution = true;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Minimum observation confidence required to keep the explicit Chapter 3 per-leg controller fully active. Below this the animator starts blending back toward a stable mirrored fallback gait.")]
        private float _minimumStateMachineConfidence = 0.18f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Confidence required before the low-confidence fallback gait is released. Keep this above the entry threshold to avoid frame-to-frame chatter.")]
        private float _minimumStateMachineConfidenceExit = 0.35f;

        [SerializeField, Range(0f, 20f)]
        [Tooltip("How quickly the graceful fallback blend rises toward the stable mirrored gait once state-machine confidence stays low.")]
        private float _fallbackGaitBlendRiseSpeed = 2f;

        [SerializeField, Range(0f, 20f)]
        [Tooltip("How quickly the graceful fallback blend falls back toward the explicit per-leg controller after confidence recovers.")]
        private float _fallbackGaitBlendFallSpeed = 4f;

        // ── Airborne Spring Scaling (Phase 3F2) ────────────────────────────

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Fraction of the baseline joint spring applied while the character is Airborne. " +
                 "0.15 = legs go loose/dangly mid-air; 1.0 = no change. Restored to 1.0 on landing.")]
        private float _airborneSpringMultiplier = 0.15f;

        // ── Angular Velocity Gait Gate (Phase 3T — GAP-2 fix) ──────────────

        [SerializeField, Range(0f, 20f)]
        [Tooltip("Yaw angular velocity threshold (rad/s) above which gait is suppressed to " +
                 "prevent leg tangle during rapid spinning. Default 4 rad/s. " +
                 "Gait re-enables with hysteresis at threshold × 0.5 held for 5 consecutive frames.")]
        private float _angularVelocityGaitThreshold = 8f;

        // ── Stranded-Foot Forward Bias ────────────────────────────────────
        //   Detects the pathological state where BOTH feet are simultaneously behind
        //   the hips while the player has active movement intent. During this state the
        //   normal sinusoidal gait's backward phase pushes already-stranded feet further
        //   back, creating a self-reinforcing stuck loop. The fix biases swing targets
        //   forward by exactly the current gait amplitude (stepAngle × smoothedInputMag)
        //   so the backward phase bottoms out at 0° (neutral) instead of -stepAngle.
        //   This uses actual foot positions (body-aware) and existing gait parameters
        //   (no magic numbers). Recovery looks like a brief stumble: within 1–2 gait
        //   cycles at least one foot replants in front, the condition clears, and normal
        //   alternating gait resumes.

        // ── Stuck-Leg Recovery (Option D) ────────────────────────────────

        [SerializeField]
        [Tooltip("Number of consecutive FixedUpdate frames where stuck conditions must be met " +
                 "before recovery pose is triggered. Default 25.")]
        private int _stuckFrameThreshold = 25;

        [SerializeField]
        [Tooltip("Horizontal speed (m/s) below which the character is considered stuck when " +
                 "input is also non-zero. Default 0.15 m/s.")]
        private float _stuckSpeedThreshold = 0.15f;

        [SerializeField]
        [Tooltip("Number of FixedUpdate frames to hold the recovery pose before resuming " +
                 "normal gait. Default 30.")]
        private int _recoveryFrames = 30;

        [SerializeField]
        [Tooltip("Spring multiplier applied to both upper-leg joints during recovery to drive " +
                 "the legs forcefully into the forward-split pose. Default 2.5.")]
        private float _recoverySpringMultiplier = 2.5f;

        private const int RecoveryCooldownFrames = 120;

        // ── Private Fields ──────────────────────────────────────────────────

        /// <summary>Left upper leg ConfigurableJoint, found by name in Awake.</summary>
        private ConfigurableJoint _upperLegL;

        /// <summary>Right upper leg ConfigurableJoint, found by name in Awake.</summary>
        private ConfigurableJoint _upperLegR;

        /// <summary>Left lower leg ConfigurableJoint, found by name in Awake.</summary>
        private ConfigurableJoint _lowerLegL;

        /// <summary>Right lower leg ConfigurableJoint, found by name in Awake.</summary>
        private ConfigurableJoint _lowerLegR;

        /// <summary>Sibling PlayerMovement component used to read current move input magnitude.</summary>
        private PlayerMovement _playerMovement;

        /// <summary>Sibling CharacterState component used to gate gait on posture state.</summary>
        private CharacterState _characterState;

        /// <summary>Hips Rigidbody used to read world-space velocity for movement direction.</summary>
        private Rigidbody _hipsRigidbody;

        /// <summary>
        /// Current gait phase in radians, in the range [0, 2π).
        /// Advances proportionally to move input magnitude each FixedUpdate while moving.
        /// Decays linearly toward 0 during idle at a rate proportional to
        /// <see cref="_idleBlendSpeed"/>, so the next gait cycle resumes from a near-zero
        /// phase with a naturally small starting rotation amplitude.
        /// </summary>
        private float _phase;

        // ── Smoothed target angles for frame-to-frame interpolation ─────────
        private float _smoothedLeftSwingDeg;
        private float _smoothedRightSwingDeg;
        private float _smoothedLeftKneeDeg;
        private float _smoothedRightKneeDeg;

        /// <summary>
        /// Smoothed version of the actual move input magnitude, in the range [0, 1].
        /// When input resumes after idle this value ramps up from 0 at a rate controlled
        /// by <see cref="_idleBlendSpeed"/>, preventing the gait amplitude from snapping
        /// to its full value on the very first frame of movement.
        /// When input stops this value is immediately reset to 0, ensuring legs snap
        /// cleanly to identity without any residual sway.
        /// </summary>
        private float _smoothedInputMag;

        /// <summary>Counter incremented each FixedUpdate; used to gate debug logging every 10 frames.</summary>
        private int _debugFrameCounter;

        /// <summary>
        /// Input direction from the previous FixedUpdate frame, used to detect direction changes
        /// that warrant a phase reset so legs restart from neutral rather than mid-arc.
        /// </summary>
        private Vector2 _prevInputDir;

        /// <summary>Whether the character was moving last frame — used to detect movement restarts.</summary>
        private bool _wasMoving;

        /// <summary>True while CharacterState is Airborne; used to suppress gait phase advancement.</summary>
        private bool _isAirborne;

        // ── Jump Wind-Up (C8.5a) ───────────────────────────────────────────

        /// <summary>True while PlayerMovement is in the jump wind-up crouch phase.</summary>
        private bool _isJumpWindUp;

        /// <summary>Extra knee-bend degrees requested during jump wind-up.</summary>
        private float _jumpWindUpKneeBendBoost;

        // ── Jump Launch (C8.5b) ───────────────────────────────────────────

        /// <summary>True while PlayerMovement is in the jump launch leg-extension phase.</summary>
        private bool _isJumpLaunch;

        // ── Landing Absorption (C8.5d) ────────────────────────────────────

        /// <summary>Extra knee-bend degrees applied to both legs during landing absorption.</summary>
        [SerializeField, Range(0f, 45f)]
        [Tooltip("Extra knee-bend degrees applied to both legs during the landing " +
                 "absorption squat. Decays over the blend-out duration.")]
        private float _landingAbsorbKneeBendBoost = 15f;

        /// <summary>Duration in seconds of the full landing squat hold before blend-out starts.</summary>
        [SerializeField, Range(0.05f, 0.5f)]
        [Tooltip("Duration of the full knee-bend hold phase after landing.")]
        private float _landingAbsorbDuration = 0.15f;

        /// <summary>Duration in seconds for the landing knee-bend to blend back to normal.</summary>
        [SerializeField, Range(0.05f, 0.5f)]
        [Tooltip("Duration for the landing knee-bend boost to decay back to zero.")]
        private float _landingAbsorbBlendOutDuration = 0.2f;

        /// <summary>Remaining landing absorption time (hold + blend-out) in seconds.</summary>
        private float _landingAbsorbTimer;

        /// <summary>Cached total duration (hold + blend-out) for phase computation.</summary>
        private float _landingAbsorbTotalDuration;

        // ── Angular Velocity Gait Gate — Hysteresis (Phase 3T — GAP-2 fix) ─

        /// <summary>
        /// Counts consecutive FixedUpdate frames where
        /// |hipsAngularVelocity.y| &lt; _angularVelocityGaitThreshold × 0.5.
        /// Gait is re-enabled only after this counter reaches 5, preventing premature
        /// re-engagement during the brief dip between oscillation peaks mid-spin.
        /// </summary>
        private int _spinSuppressFrames;

        // ── Stuck-Leg Recovery — Runtime State ───────────────────────────

        /// <summary>Counts consecutive frames where stuck conditions are all true.</summary>
        private int _stuckFrameCounter;

        /// <summary>True while the recovery pose is being actively applied.</summary>
        private bool _isRecovering;

        /// <summary>Counts FixedUpdate frames remaining in the current recovery window.</summary>
        private int _recoveryFrameCounter;

        // ── Stranded-Foot Forward Bias ──────────────────────────────────

        /// <summary>Left foot Transform, found by name ("Foot_L") in Awake. May be null if the hierarchy lacks it.</summary>
        private Transform _footL;

        /// <summary>Right foot Transform, found by name ("Foot_R") in Awake. May be null if the hierarchy lacks it.</summary>
        private Transform _footR;

        /// <summary>Original physics layer of the foot GameObjects, cached in Awake for step-up collision bypass.</summary>
        private int _originalFootLayer;

        /// <summary>True while the left foot is temporarily on the LowerLegParts layer to bypass step face collision.</summary>
        private bool _footLOnBypassLayer;

        /// <summary>True while the right foot is temporarily on the LowerLegParts layer to bypass step face collision.</summary>
        private bool _footROnBypassLayer;

        /// <summary>
        /// True when the stranded-foot forward bias is active this frame: both feet are
        /// behind the hips and the player has active movement input. Exposed for test
        /// verification; read-only at runtime.
        /// </summary>
        private bool _isGaitBiasedForward;

        private int _recoveryCooldownFrameCounter;

        private DesiredInput _commandDesiredInput;
        private LocomotionObservation _commandObservation;
        private LegCommandOutput _leftLegCommand;
        private LegCommandOutput _rightLegCommand;
        private bool _hasCommandFrame;
        private bool _suppressIncomingCommandFrame;
        private LegStateMachine _leftLegStateMachine;
        private LegStateMachine _rightLegStateMachine;
        private readonly StepPlanner _stepPlanner = new StepPlanner();
        private readonly GaitConfidenceEvaluator _confidenceEvaluator = new GaitConfidenceEvaluator();
        private LegJointDriver _jointDriver;
        // Two separate RNG instances so left/right draws never interleave and
        // the sequence is stable regardless of which leg triggers each frame.
        private System.Random _organicRng;
        private System.Random _organicRngRight;
        // Separate lateral streams so lateral draws never shift the step-angle draw sequence.
        private System.Random _organicRngLateralLeft;
        private System.Random _organicRngLateralRight;
        private float _leftStepAngleNoise;
        private float _rightStepAngleNoise;
        private float _leftLateralNoise;
        private float _rightLateralNoise;

        // ── Public Properties ────────────────────────────────────────────────

        /// <summary>
        /// Current gait phase in radians [0, 2π). Advanced each FixedUpdate while moving.
        /// Exposed so collaborators (e.g. <see cref="ArmAnimator"/>) can synchronise their
        /// own animation cycle to the same base phase.
        /// </summary>
        public float Phase => _phase;

        /// <summary>
        /// Smoothed input magnitude in the range [0, 1]. Ramps up when gait starts and
        /// decays to 0 when idle. Exposed so collaborators (e.g. <see cref="ArmAnimator"/>)
        /// can blend their effects in/out in sync with the leg gait amplitude.
        /// </summary>
        public float SmoothedInputMag => _smoothedInputMag;

        internal float StepAngleDegrees => GetEffectiveStepAngle();
        internal float LeftStepAngleDegrees => GetEffectiveStepAngle(GetCurrentSprintNormalized(), isLeftLeg: true);
        internal float RightStepAngleDegrees => GetEffectiveStepAngle(GetCurrentSprintNormalized(), isLeftLeg: false);

        internal float UpperLegLiftBoostDegrees => GetEffectiveUpperLegLiftBoost();

        internal float KneeAngleDegrees => GetEffectiveKneeAngle();
        internal Vector3 LeftFootWorldPosition => _footL != null ? _footL.position : Vector3.zero;
        internal Vector3 RightFootWorldPosition => _footR != null ? _footR.position : Vector3.zero;

        /// <summary>
        /// True while the stuck-leg recovery pose is being actively applied.
        /// Exposed for test verification; read-only at runtime.
        /// </summary>
        public bool IsRecovering => _isRecovering;

        /// <summary>
        /// True when the stranded-foot forward bias is active this frame: both feet are
        /// behind the hips while the player has active movement input. The gait swing
        /// targets are biased forward by the current effective step angle × <see cref="_smoothedInputMag"/>
        /// so the backward phase bottoms out at neutral (0°) instead of driving the
        /// already-stranded legs further behind.
        /// Exposed for test verification; read-only at runtime.
        /// </summary>
        public bool IsGaitBiasedForward => _isGaitBiasedForward;

        /// <summary>
        /// Called by <see cref="PlayerMovement"/> to enter or exit the jump wind-up
        /// braced-leg pose. While active, gait phase advancement is suppressed and
        /// both legs hold a high-knee-bend stance.
        /// </summary>
        public void SetJumpWindUp(bool active, float kneeBendBoostDeg)
        {
            _isJumpWindUp = active;
            _jumpWindUpKneeBendBoost = active ? kneeBendBoostDeg : 0f;
        }

        /// <summary>
        /// Called by <see cref="PlayerMovement"/> to enter or exit the jump launch
        /// leg-extension phase (C8.5b). While active, both legs drive toward full
        /// extension (0° knee bend) so the character visibly springs upward.
        /// </summary>
        public void SetJumpLaunch(bool active)
        {
            _isJumpLaunch = active;
        }

        /// <summary>Absolute path to the debug gait log file.</summary>
        private static readonly string DebugLogPath =
            @"H:\Work\PhysicsDrivenMovementDemo\Logs\debug_gait.txt";

        // ── Unity Lifecycle ─────────────────────────────────────────────────

        private void Awake()
        {
            // STEP 1: Cache sibling components on the Hips GameObject.
            if (!TryGetComponent(out _playerMovement))
            {
                Debug.LogError("[LegAnimator] Missing PlayerMovement on this GameObject.", this);
            }

            if (!TryGetComponent(out _characterState))
            {
                Debug.LogError("[LegAnimator] Missing CharacterState on this GameObject.", this);
            }

            if (!TryGetComponent(out _hipsRigidbody))
            {
                Debug.LogError("[LegAnimator] Missing Rigidbody on this GameObject.", this);
            }

            // STEP 2: Locate the four leg ConfigurableJoints by searching children by name.
            //         The hierarchy is: Hips → UpperLeg_L → LowerLeg_L
            //                           Hips → UpperLeg_R → LowerLeg_R
            //         We search all children (inclusive of nested) to be hierarchy-agnostic.
            ConfigurableJoint[] allJoints = GetComponentsInChildren<ConfigurableJoint>(includeInactive: true);
            foreach (ConfigurableJoint joint in allJoints)
            {
                string segmentName = joint.gameObject.name;
                switch (segmentName)
                {
                    case "UpperLeg_L":
                        _upperLegL = joint;
                        break;
                    case "UpperLeg_R":
                        _upperLegR = joint;
                        break;
                    case "LowerLeg_L":
                        _lowerLegL = joint;
                        break;
                    case "LowerLeg_R":
                        _lowerLegR = joint;
                        break;
                }
            }

            // STEP 3: Warn about missing joints so misconfigured prefabs are obvious.
            if (_upperLegL == null)
            {
                Debug.LogWarning("[LegAnimator] 'UpperLeg_L' ConfigurableJoint not found in children.", this);
            }

            if (_upperLegR == null)
            {
                Debug.LogWarning("[LegAnimator] 'UpperLeg_R' ConfigurableJoint not found in children.", this);
            }

            if (_lowerLegL == null)
            {
                Debug.LogWarning("[LegAnimator] 'LowerLeg_L' ConfigurableJoint not found in children.", this);
            }

            if (_lowerLegR == null)
            {
                Debug.LogWarning("[LegAnimator] 'LowerLeg_R' ConfigurableJoint not found in children.", this);
            }

            // STEP 4: Cache foot Transforms by name ("Foot_L", "Foot_R") for stranded-foot
            //         bias detection. These are children of the lower leg segments. If the
            //         hierarchy lacks them (e.g. minimal test rigs) the bias feature degrades
            //         gracefully — IsFootBehindHips returns false when the transform is null.
            CacheFootTransforms();
            if (_footL != null)
                _originalFootLayer = _footL.gameObject.layer;
            else if (_footR != null)
                _originalFootLayer = _footR.gameObject.layer;

            // STEP 5: Create the joint driver that owns swing target application.
            _jointDriver = new LegJointDriver(_upperLegL, _upperLegR, _lowerLegL, _lowerLegR, _swingAxis, _kneeAxis);

            // STEP 6: Seed the per-leg state machines so Chapter 3.2 starts from the same
            //         mirrored cadence shape as the legacy pass-through gait.
            ResetLegStateMachinesForMirroredCadence();
            _organicRng = new System.Random(_noiseSeed);
            // Right leg uses a different seed offset so the two streams are independent.
            _organicRngRight = new System.Random(_noiseSeed + 1000);
            // Lateral streams use separate offsets so lateral draws never shift step-angle sequences.
            _organicRngLateralLeft = new System.Random(_noiseSeed + 2000);
            _organicRngLateralRight = new System.Random(_noiseSeed + 3000);
        }

        private void Start()
        {
            // STEP: If debug logging is enabled, clear the log file at startup so each play
            //       session starts with a fresh file rather than appending to stale data.
            if (_debugLog)
            {
                string dir = Path.GetDirectoryName(DebugLogPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(DebugLogPath, string.Empty);
            }

            // STEP (Phase 3F2 fix): Capture baseline drives in Start, not Awake.
            // RagdollSetup.Awake applies the authoritative spring/damper values — but
            // Unity does not guarantee Awake execution order between scripts on the same
            // GameObject. Start is guaranteed to run after ALL Awakes, so by the time
            // we get here RagdollSetup has already written the correct spring values.
            _jointDriver.CaptureBaselineDrives();
        }

        private void OnEnable()
        {
            // STEP (Phase 3F2): Subscribe to CharacterState.OnStateChanged so we can react to
            //                   Airborne entry/exit and scale leg springs accordingly.
            //                   Guard against null (characterState is cached in Awake; if that
            //                   failed, the warning was already logged there).
            if (_characterState != null)
            {
                _characterState.OnStateChanged += OnCharacterStateChanged;
            }
        }

        private void OnDisable()
        {
            // STEP (Phase 3F2): Unsubscribe to avoid dangling delegates after this component
            //                   is disabled or the GameObject is destroyed.
            if (_characterState != null)
            {
                _characterState.OnStateChanged -= OnCharacterStateChanged;
            }
        }

        private void FixedUpdate()
        {
            _jointDriver.ResetFrameState();
            _isGaitBiasedForward = false;

            if (_playerMovement == null || _characterState == null)
            {
                return;
            }

            CharacterStateType state = _characterState.CurrentState;
            if (state == CharacterStateType.Fallen || state == CharacterStateType.GettingUp)
            {
                _suppressIncomingCommandFrame = true;
                _phase = 0f;
                _smoothedInputMag = 0f;
                _confidenceEvaluator.Reset();
                ResetLegStateMachinesToIdle();
                _jointDriver.SetAllTargetsToIdentity();
                return;
            }

            _suppressIncomingCommandFrame = false;

            // C8.5d: Tick landing absorption timer.
            if (_landingAbsorbTimer > 0f)
            {
                _landingAbsorbTimer -= Time.fixedDeltaTime;
            }

            if (!_hasCommandFrame)
            {
                _phase = 0f;
                _smoothedInputMag = 0f;
                _confidenceEvaluator.Reset();
                ResetLegStateMachinesToIdle();
                _jointDriver.SetAllTargetsToIdentity();
                return;
            }

            ApplyCommandFrame();

            // STEP 8: Debug logging — write one line every 10 FixedUpdate frames when enabled.
            if (_debugLog)
            {
                _debugFrameCounter++;
                if (_debugFrameCounter >= 10)
                {
                    _debugFrameCounter = 0;
                    WriteDebugLogLine();
                }
            }
        }

        private float GetCurrentSprintNormalized()
        {
            // STEP 1: Prefer the live command-frame input while commands are active so the
            //         exposed stride seam reports the same sprint blend the executor uses.
            if (_hasCommandFrame)
            {
                return Mathf.Clamp01(_commandDesiredInput.SprintNormalized);
            }

            // STEP 2: Fall back to PlayerMovement when the director has not published a
            //         command frame yet so inspector/debug readers still see the sprint blend.
            return _playerMovement != null ? Mathf.Clamp01(_playerMovement.SprintNormalized) : 0f;
        }

        /// <summary>Resets the organic RNG to a known seed. Call from PlayMode tests for deterministic results.</summary>
        public void SetOrganicVariationSeedForTest(int seed)
        {
            _organicRng = new System.Random(seed);
            _organicRngRight = new System.Random(seed + 1000);
            _organicRngLateralLeft = new System.Random(seed + 2000);
            _organicRngLateralRight = new System.Random(seed + 3000);
            _leftStepAngleNoise = 0f;
            _rightStepAngleNoise = 0f;
            _leftLateralNoise = 0f;
            _rightLateralNoise = 0f;
        }

        private float GetEffectiveStepAngle()
        {
            return GetEffectiveStepAngle(GetCurrentSprintNormalized());
        }

        private float GetEffectiveStepAngle(float sprintNormalized)
        {
            return GetEffectiveStepAngle(sprintNormalized, isLeftLeg: true);
        }

        private float GetEffectiveStepAngle(float sprintNormalized, bool isLeftLeg)
        {
            float effectiveStepAngle = Mathf.Lerp(_stepAngle, _sprintStepAngle, Mathf.Clamp01(sprintNormalized));

            // Apply natural stride asymmetry first, then add per-stride noise around that baseline.
            // Asymmetry is a pure multiplier (e.g. 1.04 = 4% longer left stride).
            // Suppressed during any jump phase (wind-up through landing grace) and when
            // organic variation is disabled, so sprint-jump stability tests are unaffected.
            float asymmetry = 1.0f;
            if (!_disableOrganicVariation &&
                !_isJumpWindUp && !_isJumpLaunch &&
                (_playerMovement == null || !_playerMovement.IsRecentJumpAirborne))
            {
                asymmetry = isLeftLeg ? _leftStrideAsymmetry : _rightStrideAsymmetry;
            }
            effectiveStepAngle *= asymmetry;

            // Option A: add a per-leg parameter here so every caller reuses the same bounded noise path.
            if (ShouldApplyOrganicStepAngleVariation())
            {
                effectiveStepAngle += isLeftLeg ? _leftStepAngleNoise : _rightStepAngleNoise;
            }

            return Mathf.Clamp(effectiveStepAngle, 30f, 90f);
        }

        private bool ShouldApplyOrganicStepAngleVariation()
        {
            // Only the disable flag gates this. Removing the IsGrounded/IsFallen guards:
            // - The noise is always clamped to [30,90] so it cannot cause falls on its own.
            // - IsGrounded gating caused the frozen noise value (set on last grounded frame)
            //   to persist into jump wind-up, where a large offset pushed step angle near
            //   the 90 deg ceiling and contributed to sprint faceplants.
            return !_disableOrganicVariation;
        }

        private float GetEffectiveUpperLegLiftBoost()
        {
            return GetEffectiveUpperLegLiftBoost(GetCurrentSprintNormalized());
        }

        private float GetEffectiveUpperLegLiftBoost(float sprintNormalized)
        {
            return Mathf.Lerp(_upperLegLiftBoost, _sprintUpperLegLiftBoost, Mathf.Clamp01(sprintNormalized));
        }

        private float GetEffectiveKneeAngle()
        {
            return GetEffectiveKneeAngle(GetCurrentSprintNormalized());
        }

        private float GetEffectiveKneeAngle(float sprintNormalized)
        {
            return Mathf.Lerp(_kneeAngle, _sprintKneeAngle, Mathf.Clamp01(sprintNormalized));
        }

        private float GetEffectiveCadenceCyclesPerSecond(float horizontalSpeed, float sprintNormalized)
        {
            float clampedSprintNormalized = Mathf.Clamp01(sprintNormalized);
            float baseCyclesPerSecond = Mathf.Max(_stepFrequency, Mathf.Max(0f, horizontalSpeed) * _stepFrequencyScale);
            float cadenceBoost = Mathf.Lerp(1f, _sprintCadenceBoost, clampedSprintNormalized);
            return baseCyclesPerSecond * cadenceBoost;
        }

        internal void BuildPassThroughCommands(
            DesiredInput desiredInput,
            LocomotionObservation observation,
            out LegCommandOutput leftCommand,
            out LegCommandOutput rightCommand)
        {
            if (_suppressIncomingCommandFrame)
            {
                _confidenceEvaluator.Reset();
                leftCommand = LegCommandOutput.Disabled(LocomotionLeg.Left);
                rightCommand = LegCommandOutput.Disabled(LocomotionLeg.Right);
                return;
            }

            CharacterStateType state = observation.CharacterState;
            if (state == CharacterStateType.Fallen ||
                state == CharacterStateType.GettingUp)
            {
                _phase = 0f;
                _smoothedInputMag = 0f;
                _prevInputDir = Vector2.zero;
                _wasMoving = false;
                _stuckFrameCounter = 0;
                _confidenceEvaluator.Reset();
                ResetLegStateMachinesToIdle();
                leftCommand = LegCommandOutput.Disabled(LocomotionLeg.Left);
                rightCommand = LegCommandOutput.Disabled(LocomotionLeg.Right);
                return;
            }

            float inputMagnitude = desiredInput.MoveMagnitude;
            float horizontalSpeedGate = observation.PlanarSpeed;
            bool isMoving = inputMagnitude > 0.01f || horizontalSpeedGate > 0.15f;

            // C8.5a: During jump wind-up, suppress normal gait and hold a braced stance
            // with boosted knee bend so the character visibly loads the spring.
            if (_isJumpWindUp)
            {
                _confidenceEvaluator.Reset();
                EnsureLegStateMachines();
                LegStateFrame bracedLeft = BuildStateFrame(_leftLegStateMachine);
                LegStateFrame bracedRight = BuildStateFrame(_rightLegStateMachine);

                leftCommand = new LegCommandOutput(
                    LocomotionLeg.Left,
                    LegCommandMode.HoldPose,
                    bracedLeft,
                    _leftLegStateMachine.CyclePhase,
                    0f,
                    _jumpWindUpKneeBendBoost,
                    1f,
                    StepTarget.Invalid);
                rightCommand = new LegCommandOutput(
                    LocomotionLeg.Right,
                    LegCommandMode.HoldPose,
                    bracedRight,
                    _rightLegStateMachine.CyclePhase,
                    0f,
                    _jumpWindUpKneeBendBoost,
                    1f,
                    StepTarget.Invalid);
                return;
            }

            // C8.5b: During jump launch, drive both legs toward full extension (0° knee
            // bend) so the character visibly springs upward from the crouch.
            if (_isJumpLaunch)
            {
                _confidenceEvaluator.Reset();
                EnsureLegStateMachines();
                LegStateFrame launchLeft = BuildStateFrame(_leftLegStateMachine);
                LegStateFrame launchRight = BuildStateFrame(_rightLegStateMachine);

                leftCommand = new LegCommandOutput(
                    LocomotionLeg.Left,
                    LegCommandMode.HoldPose,
                    launchLeft,
                    _leftLegStateMachine.CyclePhase,
                    0f,
                    0f,
                    1f,
                    StepTarget.Invalid);
                rightCommand = new LegCommandOutput(
                    LocomotionLeg.Right,
                    LegCommandMode.HoldPose,
                    launchRight,
                    _rightLegStateMachine.CyclePhase,
                    0f,
                    0f,
                    1f,
                    StepTarget.Invalid);
                return;
            }

            if (_isAirborne || state == CharacterStateType.Airborne)
            {
                isMoving = false;
            }

            float absAngVelY = Mathf.Abs(observation.AngularVelocity.y);
            if (absAngVelY > _angularVelocityGaitThreshold)
            {
                isMoving = false;
                _spinSuppressFrames = 0;
            }
            else if (absAngVelY < _angularVelocityGaitThreshold * 0.5f)
            {
                if (_spinSuppressFrames < 5)
                {
                    _spinSuppressFrames++;
                    isMoving = false;
                }
            }
            else
            {
                _spinSuppressFrames = 0;
                isMoving = false;
            }

            Vector2 currentInputDir = inputMagnitude > 0.01f
                ? desiredInput.MoveInput.normalized
                : Vector2.zero;

            bool restarting = !_wasMoving && isMoving && _smoothedInputMag < 0.05f;
            bool sharpTurn = _prevInputDir.sqrMagnitude > 0.01f &&
                             currentInputDir.sqrMagnitude > 0.01f &&
                             Vector2.Dot(_prevInputDir, currentInputDir) < 0.5f;
            if (restarting || sharpTurn)
            {
                _phase = 0f;
                _smoothedInputMag = 0f;
                ResetLegStateMachinesForMirroredCadence();
            }

            _prevInputDir = currentInputDir;
            _wasMoving = isMoving;

            if (_recoveryCooldownFrameCounter > 0)
            {
                _recoveryCooldownFrameCounter--;
            }

            bool stateAllowsRecovery = state == CharacterStateType.Standing ||
                                       state == CharacterStateType.Moving;

            if (!_isRecovering)
            {
                bool stuckCondition = _smoothedInputMag > 0.5f &&
                                      horizontalSpeedGate < _stuckSpeedThreshold &&
                                      _recoveryCooldownFrameCounter <= 0 &&
                                      stateAllowsRecovery;

                if (stuckCondition)
                {
                    _stuckFrameCounter++;
                }
                else
                {
                    _stuckFrameCounter = 0;
                }

                if (_stuckFrameCounter >= _stuckFrameThreshold && stateAllowsRecovery)
                {
                    _isRecovering = true;
                    _recoveryFrameCounter = _recoveryFrames;
                    _stuckFrameCounter = 0;
                    _jointDriver.SetSpringMultiplier(_recoverySpringMultiplier);
                }
            }

            Vector3 gaitReferenceDirection = desiredInput.MoveWorldDirection.sqrMagnitude > 0.0001f
                ? desiredInput.MoveWorldDirection
                : observation.BodyForward;

            EnsureLegStateMachines();

            if (_isRecovering)
            {
                _confidenceEvaluator.Reset();
                _leftLegStateMachine.ForceState(
                    LegStateType.RecoveryStep,
                    LegStateTransitionReason.StumbleRecovery,
                    0f);
                _rightLegStateMachine.ForceState(
                    LegStateType.RecoveryStep,
                    LegStateTransitionReason.StumbleRecovery,
                    Mathf.PI);
                _phase = _leftLegStateMachine.CyclePhase;

                leftCommand = new LegCommandOutput(
                    LocomotionLeg.Left,
                    LegCommandMode.HoldPose,
                    BuildStateFrame(_leftLegStateMachine),
                    _leftLegStateMachine.CyclePhase,
                    -30f,
                    0f,
                    1f,
                    StepTarget.Invalid);
                rightCommand = new LegCommandOutput(
                    LocomotionLeg.Right,
                    LegCommandMode.HoldPose,
                    BuildStateFrame(_rightLegStateMachine),
                    _rightLegStateMachine.CyclePhase,
                    -30f,
                    0f,
                    1f,
                    StepTarget.Invalid);

                _recoveryFrameCounter--;
                if (_recoveryFrameCounter <= 0)
                {
                    _isRecovering = false;
                    _stuckFrameCounter = 0;
                    _recoveryCooldownFrameCounter = RecoveryCooldownFrames;
                    _jointDriver.SetSpringMultiplier(1f);
                }

                return;
            }

            // STEP 1: Start from a cadence reason that describes the overall move context,
            //         then let C3.4 override it per leg for sharp-turn support or recovery.
            LegStateTransitionReason cadenceReason = DetermineCadenceTransitionReason(
                desiredInput,
                observation);
            SynchronizeLegStateMachinesFromLegacyPhaseIfNeeded(cadenceReason);

            if (isMoving)
            {
                float effectiveUpperLegLiftBoost = GetEffectiveUpperLegLiftBoost(desiredInput.SprintNormalized);
                float effectiveKneeAngle = GetEffectiveKneeAngle(desiredInput.SprintNormalized);
                float effectiveCyclesPerSec = GetEffectiveCadenceCyclesPerSecond(
                    horizontalSpeedGate,
                    desiredInput.SprintNormalized);
                float phaseAdvance = effectiveCyclesPerSec * 2f * Mathf.PI * Time.fixedDeltaTime;

                float t = Mathf.Clamp01(_idleBlendSpeed * Time.fixedDeltaTime);
                float velocityMag01 = Mathf.Clamp01(horizontalSpeedGate / 2f);
                float amplitudeTarget = Mathf.Max(inputMagnitude, velocityMag01);
                _smoothedInputMag = Mathf.Lerp(_smoothedInputMag, amplitudeTarget, t);

                bool bothFeetBehind = false;
                bool bothFeetFarBehind = false;
                if (desiredInput.HasMoveIntent && _footL != null && _footR != null)
                {
                    float leftFootForwardOffset = GetFootForwardOffsetFromHips(_footL, gaitReferenceDirection);
                    float rightFootForwardOffset = GetFootForwardOffsetFromHips(_footR, gaitReferenceDirection);
                    bothFeetBehind = leftFootForwardOffset < 0f && rightFootForwardOffset < 0f;
                    bothFeetFarBehind = leftFootForwardOffset < -0.07f && rightFootForwardOffset < -0.07f;
                }

                // STEP 2: Chapter 3.4 gives sharp turns and recovery explicit per-leg ownership
                //         instead of pushing one body-level reason through both legs.
                LocomotionLeg weakSupportLeg = LocomotionLeg.Left;
                bool promoteWeakSupportRecovery = desiredInput.HasMoveIntent &&
                    TryGetWeakSupportRecoveryLeg(observation, out weakSupportLeg);
                bool promoteRecoveryOverride = desiredInput.HasMoveIntent &&
                    (observation.IsLocomotionCollapsed || bothFeetFarBehind || promoteWeakSupportRecovery);
                bool forceCatchStep = desiredInput.HasMoveIntent &&
                    (observation.IsLocomotionCollapsed || bothFeetFarBehind);
                bool hasTurnAsymmetry = TryGetTurnLegRoles(
                    desiredInput,
                    observation,
                    out LocomotionLeg outsideLeg,
                    out LocomotionLeg insideLeg);
                LocomotionLeg recoveryLeg = promoteWeakSupportRecovery
                    ? weakSupportLeg
                    : SelectRecoveryLeg(gaitReferenceDirection, observation);

                LegStateTransitionReason leftTransitionReason = DetermineLegTransitionReason(
                    LocomotionLeg.Left,
                    cadenceReason,
                    hasTurnAsymmetry,
                    outsideLeg,
                    insideLeg,
                    promoteRecoveryOverride,
                    recoveryLeg);
                LegStateTransitionReason rightTransitionReason = DetermineLegTransitionReason(
                    LocomotionLeg.Right,
                    cadenceReason,
                    hasTurnAsymmetry,
                    outsideLeg,
                    insideLeg,
                    promoteRecoveryOverride,
                    recoveryLeg);

                LegStateType previousLeftState = _leftLegStateMachine.CurrentState;
                LegStateType previousRightState = _rightLegStateMachine.CurrentState;
                LegStateFrame leftStateFrame = _leftLegStateMachine.AdvanceMoving(
                    observation.LeftFoot,
                    previousRightState,
                    leftTransitionReason,
                    phaseAdvance,
                    forceCatchStep && recoveryLeg == LocomotionLeg.Left);
                LegStateFrame rightStateFrame = _rightLegStateMachine.AdvanceMoving(
                    observation.RightFoot,
                    previousLeftState,
                    rightTransitionReason,
                    phaseAdvance,
                    forceCatchStep && recoveryLeg == LocomotionLeg.Right);

                StepTarget leftStepTarget = _stepPlanner.ComputeSwingTarget(
                    LocomotionLeg.Left,
                    _leftLegStateMachine.CyclePhase,
                    leftStateFrame.State,
                    leftTransitionReason,
                    desiredInput,
                    observation,
                    _hipsRigidbody.position,
                    gaitReferenceDirection,
                    effectiveCyclesPerSec);
                if (leftStepTarget.IsValid && !_disableOrganicVariation && _organicRng != null)
                {
                    float sprintNorm = _playerMovement != null ? _playerMovement.SprintNormalized : 0f;
                    float noiseMag = Mathf.Lerp(8f, 4f, Mathf.Clamp01(sprintNorm));
                    _leftStepAngleNoise = (float)(_organicRng.NextDouble() * 2.0 - 1.0) * noiseMag;

                    // Lateral draw uses its own stream so it never shifts the step-angle sequence.
                    float latNoiseMag = Mathf.Lerp(0.15f, 0.07f, Mathf.Clamp01(sprintNorm));
                    _leftLateralNoise = (float)(_organicRngLateralLeft.NextDouble() * 2.0 - 1.0) * latNoiseMag;
                }

                // Lateral position shift only on grounded strides -- shifting foot placement
                // mid-air or on a landing step destabilises the character (63+ deg tilt observed
                // on second consecutive jump landing). Step angle noise is fine airborne because
                // it only affects swing shape; landing position shifts are not.
                if (leftStepTarget.IsValid && !_disableOrganicVariation && observation.IsGrounded)
                {
                    Vector3 leftForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
                    if (leftForward.sqrMagnitude < 0.0001f)
                    {
                        leftForward = Vector3.forward;
                    }

                    Vector3 leftLateral = Vector3.Cross(Vector3.up, leftForward.normalized);
                    Vector3 leftLandingPosition = leftStepTarget.LandingPosition + leftLateral * _leftLateralNoise;
                    leftStepTarget = new StepTarget(
                        leftLandingPosition,
                        leftStepTarget.DesiredTiming,
                        leftStepTarget.WidthBias,
                        leftStepTarget.BrakingBias,
                        leftStepTarget.Confidence,
                        leftStepTarget.RequestedClearanceHeight);
                }

                StepTarget rightStepTarget = _stepPlanner.ComputeSwingTarget(
                    LocomotionLeg.Right,
                    _rightLegStateMachine.CyclePhase,
                    rightStateFrame.State,
                    rightTransitionReason,
                    desiredInput,
                    observation,
                    _hipsRigidbody.position,
                    gaitReferenceDirection,
                    effectiveCyclesPerSec);
                if (rightStepTarget.IsValid && !_disableOrganicVariation && _organicRngRight != null)
                {
                    float sprintNorm = _playerMovement != null ? _playerMovement.SprintNormalized : 0f;
                    float noiseMag = Mathf.Lerp(8f, 4f, Mathf.Clamp01(sprintNorm));
                    _rightStepAngleNoise = (float)(_organicRngRight.NextDouble() * 2.0 - 1.0) * noiseMag;

                    // Lateral draw uses its own stream so it never shifts the step-angle sequence.
                    float latNoiseMag = Mathf.Lerp(0.15f, 0.07f, Mathf.Clamp01(sprintNorm));
                    _rightLateralNoise = (float)(_organicRngLateralRight.NextDouble() * 2.0 - 1.0) * latNoiseMag;
                }

                if (rightStepTarget.IsValid && !_disableOrganicVariation && observation.IsGrounded)
                {
                    Vector3 rightForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
                    if (rightForward.sqrMagnitude < 0.0001f)
                    {
                        rightForward = Vector3.forward;
                    }

                    Vector3 rightLateral = Vector3.Cross(Vector3.up, rightForward.normalized);
                    Vector3 rightLandingPosition = rightStepTarget.LandingPosition + rightLateral * _rightLateralNoise;
                    rightStepTarget = new StepTarget(
                        rightLandingPosition,
                        rightStepTarget.DesiredTiming,
                        rightStepTarget.WidthBias,
                        rightStepTarget.BrakingBias,
                        rightStepTarget.Confidence,
                        rightStepTarget.RequestedClearanceHeight);
                }

                float leftEffectiveStepAngle = GetEffectiveStepAngle(desiredInput.SprintNormalized, isLeftLeg: true);
                float rightEffectiveStepAngle = GetEffectiveStepAngle(desiredInput.SprintNormalized, isLeftLeg: false);
                float leftSwingDeg = LegExecutionProfileResolver.BuildSwingAngleFromPhase(
                    _leftLegStateMachine.CyclePhase,
                    _smoothedInputMag,
                    leftStateFrame.State,
                    leftEffectiveStepAngle,
                    effectiveUpperLegLiftBoost);
                float rightSwingDeg = LegExecutionProfileResolver.BuildSwingAngleFromPhase(
                    _rightLegStateMachine.CyclePhase,
                    _smoothedInputMag,
                    rightStateFrame.State,
                    rightEffectiveStepAngle,
                    effectiveUpperLegLiftBoost);

                if (bothFeetBehind)
                {
                    float leftStrandedBias = leftEffectiveStepAngle * _smoothedInputMag;
                    float rightStrandedBias = rightEffectiveStepAngle * _smoothedInputMag;
                    leftSwingDeg += leftStrandedBias;
                    rightSwingDeg += rightStrandedBias;
                    _isGaitBiasedForward = true;
                }

                float kneeBendDeg = effectiveKneeAngle * _smoothedInputMag;

                LegCommandOutput explicitLeftCommand = new LegCommandOutput(
                    LocomotionLeg.Left,
                    LegCommandMode.Cycle,
                    leftStateFrame,
                    _leftLegStateMachine.CyclePhase,
                    leftSwingDeg,
                    kneeBendDeg,
                    _smoothedInputMag,
                    leftStepTarget);
                LegCommandOutput explicitRightCommand = new LegCommandOutput(
                    LocomotionLeg.Right,
                    LegCommandMode.Cycle,
                    rightStateFrame,
                    _rightLegStateMachine.CyclePhase,
                    rightSwingDeg,
                    kneeBendDeg,
                    _smoothedInputMag,
                    rightStepTarget);

                // STEP 3: Estimate whether the explicit per-leg controller still has enough
                //         observation confidence to keep divergent gait roles active, then
                //         blend the emitted commands toward a stable mirrored fallback gait
                //         when that confidence stays low for multiple frames.
                float stateMachineConfidence = _confidenceEvaluator.ComputeConfidence(
                    observation,
                    hasTurnAsymmetry,
                    forceCatchStep,
                    explicitLeftCommand,
                    explicitRightCommand,
                    _minimumStateMachineConfidenceExit);
                _confidenceEvaluator.UpdateFallbackBlend(
                    stateMachineConfidence,
                    _minimumStateMachineConfidence,
                    _minimumStateMachineConfidenceExit,
                    _fallbackGaitBlendRiseSpeed,
                    _fallbackGaitBlendFallSpeed);
                float fallbackEffectiveStepAngle = GetEffectiveStepAngle(desiredInput.SprintNormalized);
                _confidenceEvaluator.ApplyFallback(
                    ref explicitLeftCommand,
                    ref explicitRightCommand,
                    gaitReferenceDirection,
                    bothFeetBehind,
                    fallbackEffectiveStepAngle,
                    effectiveKneeAngle,
                    effectiveUpperLegLiftBoost);

                leftCommand = explicitLeftCommand;
                rightCommand = explicitRightCommand;
                _phase = leftCommand.CyclePhase;
                return;
            }

            float decayStep = _idleBlendSpeed * Mathf.PI * Time.fixedDeltaTime;
            float decayT = Mathf.Clamp01(_idleBlendSpeed * Time.fixedDeltaTime);
            _smoothedInputMag = Mathf.Lerp(_smoothedInputMag, 0f, decayT);
            _confidenceEvaluator.Reset();

            LegStateFrame idleLeftStateFrame = _leftLegStateMachine.AdvanceIdle(decayStep);
            LegStateFrame idleRightStateFrame = _rightLegStateMachine.AdvanceIdle(decayStep);
            _phase = _leftLegStateMachine.CyclePhase;

            if (_smoothedInputMag < 0.01f)
            {
                _smoothedInputMag = 0f;
                ResetLegStateMachinesToIdle();
                idleLeftStateFrame = BuildStateFrame(_leftLegStateMachine);
                idleRightStateFrame = BuildStateFrame(_rightLegStateMachine);
                _phase = _leftLegStateMachine.CyclePhase;

                leftCommand = new LegCommandOutput(
                    LocomotionLeg.Left,
                    LegCommandMode.HoldPose,
                    idleLeftStateFrame,
                    _leftLegStateMachine.CyclePhase,
                    0f,
                    0f,
                    0f,
                    StepTarget.Invalid);
                rightCommand = new LegCommandOutput(
                    LocomotionLeg.Right,
                    LegCommandMode.HoldPose,
                    idleRightStateFrame,
                    _rightLegStateMachine.CyclePhase,
                    0f,
                    0f,
                    0f,
                    StepTarget.Invalid);
                return;
            }

            // STEP 3: Once movement intent is gone, keep decaying the hidden phase/state
            //         machine so re-entry stays smooth, but publish neutral hold-pose targets
            //         so the joints relax back toward identity immediately instead of carrying
            //         a support-side knee bend deeper into idle.
            leftCommand = new LegCommandOutput(
                LocomotionLeg.Left,
                LegCommandMode.HoldPose,
                idleLeftStateFrame,
                _leftLegStateMachine.CyclePhase,
                0f,
                0f,
                0f,
                StepTarget.Invalid);
            rightCommand = new LegCommandOutput(
                LocomotionLeg.Right,
                LegCommandMode.HoldPose,
                idleRightStateFrame,
                _rightLegStateMachine.CyclePhase,
                0f,
                0f,
                0f,
                StepTarget.Invalid);
        }

        internal void SetCommandFrame(
            DesiredInput desiredInput,
            LocomotionObservation observation,
            LegCommandOutput leftCommand,
            LegCommandOutput rightCommand)
        {
            if (_suppressIncomingCommandFrame)
            {
                return;
            }

            LogLegStateTransitionIfNeeded(_leftLegCommand, leftCommand);
            LogLegStateTransitionIfNeeded(_rightLegCommand, rightCommand);

            _commandDesiredInput = desiredInput;
            _commandObservation = observation;
            _leftLegCommand = leftCommand;
            _rightLegCommand = rightCommand;
            EnsureLegStateMachines();
            _leftLegStateMachine.ForceState(leftCommand.State, leftCommand.TransitionReason, leftCommand.CyclePhase);
            _rightLegStateMachine.ForceState(rightCommand.State, rightCommand.TransitionReason, rightCommand.CyclePhase);
            _phase = leftCommand.Mode == LegCommandMode.Disabled ? 0f : leftCommand.CyclePhase;
            _hasCommandFrame = true;
            ApplyCommandFrame();
        }

        internal void ClearCommandFrame()
        {
            _commandDesiredInput = default;
            _commandObservation = default;
            _leftLegCommand = LegCommandOutput.Disabled(LocomotionLeg.Left);
            _rightLegCommand = LegCommandOutput.Disabled(LocomotionLeg.Right);
            _hasCommandFrame = false;
            _suppressIncomingCommandFrame = false;
            _phase = 0f;
            _smoothedInputMag = 0f;
            _smoothedLeftSwingDeg = 0f;
            _smoothedRightSwingDeg = 0f;
            _smoothedLeftKneeDeg = 0f;
            _smoothedRightKneeDeg = 0f;
            _prevInputDir = Vector2.zero;
            _wasMoving = false;
            _stuckFrameCounter = 0;
            _isRecovering = false;
            _recoveryFrameCounter = 0;
            _recoveryCooldownFrameCounter = 0;
            _isGaitBiasedForward = false;
            _leftStepAngleNoise = 0f;
            _rightStepAngleNoise = 0f;
            _leftLateralNoise = 0f;
            _rightLateralNoise = 0f;
            _confidenceEvaluator.Reset();
            ResetLegStateMachinesToIdle();
            _jointDriver.SetSpringMultiplier(1f);
            _jointDriver.SetAllTargetsToIdentity();
        }

        private void ApplyCommandFrame()
        {
            if (_leftLegCommand.Mode == LegCommandMode.Disabled && _rightLegCommand.Mode == LegCommandMode.Disabled)
            {
                _jointDriver.SetAllTargetsToIdentity();
                return;
            }

            float leftEffectiveStepAngle = GetEffectiveStepAngle(_commandDesiredInput.SprintNormalized, isLeftLeg: true);
            float rightEffectiveStepAngle = GetEffectiveStepAngle(_commandDesiredInput.SprintNormalized, isLeftLeg: false);
            float effectiveKneeAngle = GetEffectiveKneeAngle(_commandDesiredInput.SprintNormalized);

            LegExecutionProfileResolver.Resolve(
                _leftLegCommand,
                _useStateDrivenExecution,
                leftEffectiveStepAngle,
                effectiveKneeAngle,
                _stepUpClearanceReferenceHeight,
                _stepUpClearanceSwingBoost,
                _stepUpClearanceKneeBoost,
                out float leftSwingDeg,
                out float leftKneeBendDeg);
            LegExecutionProfileResolver.Resolve(
                _rightLegCommand,
                _useStateDrivenExecution,
                rightEffectiveStepAngle,
                effectiveKneeAngle,
                _stepUpClearanceReferenceHeight,
                _stepUpClearanceSwingBoost,
                _stepUpClearanceKneeBoost,
                out float rightSwingDeg,
                out float rightKneeBendDeg);

            Vector3 gaitForward = LegJointDriver.GetWorldGaitForward(_commandDesiredInput, _commandObservation);
            leftSwingDeg = ApplyStepTargetReachFloor(_leftLegCommand, _footL, gaitForward, leftEffectiveStepAngle, leftSwingDeg);
            rightSwingDeg = ApplyStepTargetReachFloor(_rightLegCommand, _footR, gaitForward, rightEffectiveStepAngle, rightSwingDeg);
            ApplyStepTargetLandingHeightFloor(_leftLegCommand, _footL, leftEffectiveStepAngle, effectiveKneeAngle, ref leftSwingDeg, ref leftKneeBendDeg);
            ApplyStepTargetLandingHeightFloor(_rightLegCommand, _footR, rightEffectiveStepAngle, effectiveKneeAngle, ref rightSwingDeg, ref rightKneeBendDeg);
            ApplyStepUpSupportLegExtension(
                _leftLegCommand,
                _footR,
                _rightLegCommand.State,
                effectiveKneeAngle,
                ref rightKneeBendDeg);
            ApplyStepUpSupportLegExtension(
                _rightLegCommand,
                _footL,
                _leftLegCommand.State,
                effectiveKneeAngle,
                ref leftKneeBendDeg);

            // C8.5d: Landing absorption adds transient knee-bend boost to both legs.
            if (_landingAbsorbTimer > 0f)
            {
                float remaining = Mathf.Max(0f, _landingAbsorbTimer);
                float absorbBlend;
                if (remaining > _landingAbsorbBlendOutDuration)
                {
                    absorbBlend = 1f;
                }
                else
                {
                    absorbBlend = remaining / Mathf.Max(0.001f, _landingAbsorbBlendOutDuration);
                }
                float kneeBendBoost = _landingAbsorbKneeBendBoost * absorbBlend;
                leftKneeBendDeg += kneeBendBoost;
                rightKneeBendDeg += kneeBendBoost;
            }

            _jointDriver.ApplySwingTargets(leftSwingDeg, rightSwingDeg, leftKneeBendDeg, rightKneeBendDeg, _useWorldSpaceSwing, _commandDesiredInput, _commandObservation);

            // STEP 8: Temporarily bypass foot-Environment collision during clearance-tagged swing
            //         so the foot box collider does not jam against the step face. The foot is
            //         moved to the LowerLegParts layer (same as the shins) and restored once
            //         the foot is above the planned landing height or the swing phase ends.
            ManageStepUpFootCollision(_leftLegCommand, _footL, ref _footLOnBypassLayer);
            ManageStepUpFootCollision(_rightLegCommand, _footR, ref _footROnBypassLayer);
        }

        private void EnsureLegStateMachines()
        {
            if (_leftLegStateMachine == null)
            {
                _leftLegStateMachine = new LegStateMachine(LocomotionLeg.Left, startsInSwing: true);
            }

            if (_rightLegStateMachine == null)
            {
                _rightLegStateMachine = new LegStateMachine(LocomotionLeg.Right, startsInSwing: false);
            }
        }

        private void ResetLegStateMachinesForMirroredCadence()
        {
            EnsureLegStateMachines();
            _leftLegStateMachine.ResetForMirroredCadence();
            _rightLegStateMachine.ResetForMirroredCadence();
        }

        private void ResetLegStateMachinesToIdle()
        {
            EnsureLegStateMachines();
            _leftLegStateMachine.ResetToIdle();
            _rightLegStateMachine.ResetToIdle();
        }

        private void SynchronizeLegStateMachinesFromLegacyPhaseIfNeeded(LegStateTransitionReason transitionReason)
        {
            if (Mathf.Abs(Mathf.DeltaAngle(_leftLegStateMachine.CyclePhase * Mathf.Rad2Deg, _phase * Mathf.Rad2Deg)) <= 0.01f)
            {
                return;
            }

            _leftLegStateMachine.SyncFromLegacyPhase(_phase, transitionReason);
            _rightLegStateMachine.SyncFromLegacyPhase(
                Mathf.Repeat(_phase + Mathf.PI, Mathf.PI * 2f),
                transitionReason);
        }

        private LocomotionLeg SelectRecoveryLeg(
            Vector3 gaitReferenceDirection,
            LocomotionObservation observation)
        {
            // STEP 1: Prefer the foot that is least reliable as support right now so the
            //         recovery override goes to the leg that actually needs to reclaim ground.
            float leftRecoveryScore = BuildRecoveryLegScore(_footL, observation.LeftFoot, gaitReferenceDirection);
            float rightRecoveryScore = BuildRecoveryLegScore(_footR, observation.RightFoot, gaitReferenceDirection);

            if (Mathf.Abs(leftRecoveryScore - rightRecoveryScore) > 0.05f)
            {
                return leftRecoveryScore > rightRecoveryScore ? LocomotionLeg.Left : LocomotionLeg.Right;
            }

            // STEP 2: If both feet look equally compromised, fall back to the current phase
            //         so the selected recovery leg is still deterministic frame to frame.
            return SelectLegByPhaseBias();
        }

        private bool TryGetWeakSupportRecoveryLeg(
            LocomotionObservation observation,
            out LocomotionLeg recoveryLeg)
        {
            recoveryLeg = LocomotionLeg.Left;

            bool leftUnexpectedWeakSupport = IsUnexpectedWeakSupport(
                _leftLegStateMachine,
                observation.LeftFoot);
            bool rightUnexpectedWeakSupport = IsUnexpectedWeakSupport(
                _rightLegStateMachine,
                observation.RightFoot);

            if (leftUnexpectedWeakSupport == rightUnexpectedWeakSupport)
            {
                return false;
            }

            if (leftUnexpectedWeakSupport)
            {
                recoveryLeg = LocomotionLeg.Left;
                return true;
            }

            if (rightUnexpectedWeakSupport)
            {
                recoveryLeg = LocomotionLeg.Right;
                return true;
            }

            return false;
        }

        private float BuildRecoveryLegScore(
            Transform footTransform,
            FootContactObservation footObservation,
            Vector3 gaitReferenceDirection)
        {
            float recoveryScore = 0f;

            if (!footObservation.IsGrounded)
            {
                recoveryScore += 0.35f;
            }

            if (!footObservation.IsPlanted)
            {
                recoveryScore += 0.25f;
            }

            recoveryScore += footObservation.SlipEstimate * 0.2f;

            if (footTransform != null && gaitReferenceDirection.sqrMagnitude > 0.0001f)
            {
                float footForwardOffset = GetFootForwardOffsetFromHips(footTransform, gaitReferenceDirection);
                if (footForwardOffset < 0f)
                {
                    recoveryScore += Mathf.Clamp01(-footForwardOffset / 0.15f) * 0.4f;
                }
            }

            return recoveryScore;
        }

        private static bool IsUnexpectedWeakSupport(
            LegStateMachine stateMachine,
            FootContactObservation footObservation)
        {
            switch (stateMachine.CurrentState)
            {
                case LegStateType.CatchStep:
                case LegStateType.RecoveryStep:
                    return false;

                case LegStateType.Swing:
                    return !footObservation.IsGrounded &&
                        stateMachine.CyclePhase >= Mathf.PI - 0.08f;

                case LegStateType.Plant:
                    return !footObservation.IsGrounded;
            }

            if (!footObservation.IsGrounded)
            {
                return true;
            }

            return !footObservation.IsPlanted &&
                (footObservation.PlantedConfidence <= 0.2f ||
                 footObservation.ContactConfidence <= 0.25f);
        }

        private LocomotionLeg SelectLegByPhaseBias()
        {
            float leftSwingBias = Mathf.Sin(_leftLegStateMachine.CyclePhase);
            float rightSwingBias = Mathf.Sin(_rightLegStateMachine.CyclePhase);
            return leftSwingBias >= rightSwingBias ? LocomotionLeg.Left : LocomotionLeg.Right;
        }

        private static LegStateFrame BuildStateFrame(LegStateMachine stateMachine)
        {
            return new LegStateFrame(stateMachine.Leg, stateMachine.CurrentState, stateMachine.TransitionReason);
        }

        private static LegStateTransitionReason DetermineCadenceTransitionReason(
            DesiredInput desiredInput,
            LocomotionObservation observation)
        {
            if (observation.TurnSeverity >= 0.45f && desiredInput.HasMoveIntent)
            {
                return LegStateTransitionReason.TurnSupport;
            }

            if (!desiredInput.HasMoveIntent && observation.PlanarSpeed > 0.15f)
            {
                return LegStateTransitionReason.Braking;
            }

            float normalizedPlanarSpeed = Mathf.Clamp01(observation.PlanarSpeed / 2f);
            if (desiredInput.HasMoveIntent && desiredInput.MoveMagnitude > normalizedPlanarSpeed + 0.15f)
            {
                return LegStateTransitionReason.SpeedUp;
            }

            if (desiredInput.HasMoveIntent || observation.PlanarSpeed > 0.05f)
            {
                return LegStateTransitionReason.DefaultCadence;
            }

            return LegStateTransitionReason.None;
        }

        private static LegStateTransitionReason DetermineLegTransitionReason(
            LocomotionLeg leg,
            LegStateTransitionReason cadenceReason,
            bool hasTurnAsymmetry,
            LocomotionLeg outsideLeg,
            LocomotionLeg insideLeg,
            bool promoteRecoveryLeg,
            LocomotionLeg recoveryLeg)
        {
            // STEP 1: Recovery ownership outranks cadence so only the selected leg claims
            //         the stumble-recovery override during C3.4 catch-step promotion.
            if (promoteRecoveryLeg && leg == recoveryLeg)
            {
                return LegStateTransitionReason.StumbleRecovery;
            }

            // STEP 2: Sharp turns split the outside support leg from the inside cadence leg.
            if (hasTurnAsymmetry)
            {
                if (leg == outsideLeg)
                {
                    return LegStateTransitionReason.TurnSupport;
                }

                if (leg == insideLeg)
                {
                    return LegStateTransitionReason.SpeedUp;
                }
            }

            return cadenceReason;
        }

        private static bool TryGetTurnLegRoles(
            DesiredInput desiredInput,
            LocomotionObservation observation,
            out LocomotionLeg outsideLeg,
            out LocomotionLeg insideLeg)
        {
            outsideLeg = LocomotionLeg.Left;
            insideLeg = LocomotionLeg.Right;

            if (!desiredInput.HasMoveIntent || observation.TurnSeverity < 0.45f)
            {
                return false;
            }

            Vector3 requestedDirection = desiredInput.MoveWorldDirection.sqrMagnitude > 0.0001f
                ? desiredInput.MoveWorldDirection
                : desiredInput.FacingDirection;
            if (requestedDirection.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            float signedTurn = Vector3.Cross(observation.BodyForward, requestedDirection).y;
            if (Mathf.Abs(signedTurn) <= 0.0001f)
            {
                return false;
            }

            bool turningRight = signedTurn > 0f;
            outsideLeg = turningRight ? LocomotionLeg.Left : LocomotionLeg.Right;
            insideLeg = turningRight ? LocomotionLeg.Right : LocomotionLeg.Left;
            return true;
        }

        private void LogLegStateTransitionIfNeeded(LegCommandOutput previousCommand, LegCommandOutput nextCommand)
        {
            if (!_debugStateTransitions)
            {
                return;
            }

            if (previousCommand.Leg == nextCommand.Leg &&
                previousCommand.State == nextCommand.State &&
                previousCommand.TransitionReason == nextCommand.TransitionReason)
            {
                return;
            }

            Debug.Log(
                $"[LegAnimator] {nextCommand.Leg} leg state {previousCommand.State} -> {nextCommand.State} ({nextCommand.TransitionReason})",
                this);
        }

        // ── Private Methods ──────────────────────────────────────────────────

        /// <summary>
        /// Caches <see cref="_footL"/> and <see cref="_footR"/> by searching all descendant
        /// Transforms for objects named "Foot_L" and "Foot_R". Called from Awake.
        /// Mirrors the same lookup pattern used by <see cref="LocomotionCollapseDetector"/>.
        /// If the hierarchy lacks foot objects (e.g. minimal test rigs), the fields stay null
        /// and <see cref="IsFootBehindHips"/> degrades gracefully (returns false).
        /// </summary>
        private void ManageStepUpFootCollision(
            LegCommandOutput command,
            Transform foot,
            ref bool isOnBypassLayer)
        {
            if (foot == null)
                return;

            bool isSwingLike = command.State == LegStateType.Swing ||
                               command.State == LegStateType.CatchStep;
            bool needsBypass = isSwingLike &&
                               command.StepTarget.HasClearanceRequest &&
                               command.StepTarget.IsValid &&
                               foot.position.y < command.StepTarget.LandingPosition.y;

            if (needsBypass && !isOnBypassLayer)
            {
                foot.gameObject.layer = GameSettings.LayerLowerLegParts;
                isOnBypassLayer = true;
            }
            else if (!needsBypass && isOnBypassLayer)
            {
                foot.gameObject.layer = _originalFootLayer;
                isOnBypassLayer = false;
            }
        }

        private void CacheFootTransforms()
        {
            Transform[] children = GetComponentsInChildren<Transform>(includeInactive: true);
            for (int i = 0; i < children.Length; i++)
            {
                Transform child = children[i];
                if (_footL == null && child.name == "Foot_L")
                {
                    _footL = child;
                }
                else if (_footR == null && child.name == "Foot_R")
                {
                    _footR = child;
                }
            }
        }

        /// <summary>
        /// Returns true when the given foot Transform is behind the hips in the horizontal
        /// plane, measured against the commanded travel heading when available and the hips'
        /// physical forward direction otherwise. "Behind" means the horizontal dot product
        /// of (foot − hips) with that reference direction is negative.
        /// Returns false if the transform is null or the reference direction is degenerate.
        /// </summary>
        private bool IsFootBehindHips(Transform footTransform, Vector3 referenceDirection)
        {
            return GetFootForwardOffsetFromHips(footTransform, referenceDirection) < 0f;
        }

        private float ApplyStepTargetReachFloor(
            LegCommandOutput command,
            Transform footTransform,
            Vector3 gaitReferenceDirection,
            float stepAngleDegrees,
            float swingAngleDegrees)
        {
            if (!command.StepTarget.IsValid ||
                !command.StepTarget.HasClearanceRequest ||
                footTransform == null ||
                command.BlendWeight <= 0f)
            {
                return swingAngleDegrees;
            }

            float currentFootForwardOffset = GetFootForwardOffsetFromHips(footTransform, gaitReferenceDirection);
            float targetForwardOffset = GetPointForwardOffsetFromHips(command.StepTarget.LandingPosition, gaitReferenceDirection);
            float reachDeficit = targetForwardOffset - currentFootForwardOffset;
            if (reachDeficit <= 0f)
            {
                return swingAngleDegrees;
            }

            float swingProgress = Mathf.InverseLerp(0f, Mathf.PI, Mathf.Min(command.CyclePhase, Mathf.PI));
            float reachBlend = Mathf.SmoothStep(0.2f, 1f, swingProgress);
            float normalizedReach = Mathf.Clamp01(reachDeficit / 0.22f) * reachBlend;
            float reachFloor = Mathf.Lerp(stepAngleDegrees * 0.62f, stepAngleDegrees * 1.08f, normalizedReach) * command.BlendWeight;
            return Mathf.Max(swingAngleDegrees, reachFloor);
        }

        private void ApplyStepTargetLandingHeightFloor(
            LegCommandOutput command,
            Transform footTransform,
            float stepAngleDegrees,
            float baseKneeAngleDegrees,
            ref float swingAngleDegrees,
            ref float kneeAngleDegrees)
        {
            if (!command.StepTarget.IsValid ||
                !command.StepTarget.HasClearanceRequest ||
                footTransform == null ||
                command.BlendWeight <= 0f ||
                _stepUpClearanceReferenceHeight <= 0f)
            {
                return;
            }

            float landingHeightDelta = command.StepTarget.LandingPosition.y - footTransform.position.y;
            if (landingHeightDelta <= 0f)
            {
                return;
            }

            float swingProgress = Mathf.InverseLerp(0f, Mathf.PI, Mathf.Min(command.CyclePhase, Mathf.PI));
            float lateSwingBlend = Mathf.SmoothStep(0.45f, 1f, swingProgress);
            if (lateSwingBlend <= 0f)
            {
                return;
            }

            float normalizedLandingHeight = Mathf.Clamp01(landingHeightDelta / _stepUpClearanceReferenceHeight);
            float landingHeightBlend = Mathf.SmoothStep(0f, 1f, normalizedLandingHeight) * lateSwingBlend;
            if (landingHeightBlend <= 0f)
            {
                return;
            }

            float landingSwingContribution = Mathf.Lerp(0f, stepAngleDegrees * 0.22f, landingHeightBlend) * command.BlendWeight;
            float landingKneeContribution = Mathf.Lerp(0f, baseKneeAngleDegrees * 0.48f, landingHeightBlend) * command.BlendWeight;

            swingAngleDegrees += landingSwingContribution;
            kneeAngleDegrees += landingKneeContribution;
        }

        private void ApplyStepUpSupportLegExtension(
            LegCommandOutput swingCommand,
            Transform supportFootTransform,
            LegStateType supportLegState,
            float baseKneeAngleDegrees,
            ref float supportKneeBendDegrees)
        {
            if (!swingCommand.StepTarget.IsValid ||
                !swingCommand.StepTarget.HasClearanceRequest ||
                supportFootTransform == null ||
                supportLegState != LegStateType.Stance ||
                swingCommand.BlendWeight <= 0f ||
                _stepUpClearanceReferenceHeight <= 0f)
            {
                return;
            }

            float landingRiseAboveSupport = swingCommand.StepTarget.LandingPosition.y - supportFootTransform.position.y;
            if (landingRiseAboveSupport <= 0f)
            {
                return;
            }

            float swingProgress = Mathf.InverseLerp(0f, Mathf.PI, Mathf.Min(swingCommand.CyclePhase, Mathf.PI));
            float supportExtensionBlend = Mathf.SmoothStep(0.2f, 0.85f, swingProgress) * swingCommand.BlendWeight;
            if (supportExtensionBlend <= 0f)
            {
                return;
            }

            float normalizedRise = Mathf.Clamp01(landingRiseAboveSupport / _stepUpClearanceReferenceHeight);
            float extensionStrength = Mathf.SmoothStep(0f, 1f, normalizedRise) * supportExtensionBlend;
            float supportKneeCeiling = Mathf.Lerp(baseKneeAngleDegrees * 0.18f, baseKneeAngleDegrees * 0.04f, extensionStrength);
            supportKneeBendDegrees = Mathf.Min(supportKneeBendDegrees, supportKneeCeiling);
        }

        private float GetFootForwardOffsetFromHips(Transform footTransform, Vector3 referenceDirection)
        {
            if (footTransform == null) return 0f;

            Vector3 hipToFoot = footTransform.position - transform.position;

            if (referenceDirection.sqrMagnitude >= 0.0001f)
            {
                referenceDirection = new Vector3(referenceDirection.x, 0f, referenceDirection.z);
            }
            else
            {
                referenceDirection = new Vector3(transform.forward.x, 0f, transform.forward.z);
            }

            if (referenceDirection.sqrMagnitude < 0.0001f) return 0f;

            referenceDirection.Normalize();
            float forwardDot = Vector3.Dot(
                new Vector3(hipToFoot.x, 0f, hipToFoot.z),
                referenceDirection);

            return forwardDot;
        }

        private float GetPointForwardOffsetFromHips(Vector3 point, Vector3 referenceDirection)
        {
            Vector3 hipToPoint = point - transform.position;

            if (referenceDirection.sqrMagnitude >= 0.0001f)
            {
                referenceDirection = new Vector3(referenceDirection.x, 0f, referenceDirection.z);
            }
            else
            {
                referenceDirection = new Vector3(transform.forward.x, 0f, transform.forward.z);
            }

            if (referenceDirection.sqrMagnitude < 0.0001f)
            {
                return 0f;
            }

            referenceDirection.Normalize();
            return Vector3.Dot(new Vector3(hipToPoint.x, 0f, hipToPoint.z), referenceDirection);
        }

        /// <summary>
        /// Formats and writes one gait debug line to <see cref="DebugLogPath"/> and to
        /// <see cref="Debug.Log"/>. Called every 10 FixedUpdate frames when
        /// <see cref="_debugLog"/> is true. Captures velocity, gait forward direction,
        /// world swing axis, and target/actual Euler angles for both left leg joints.
        /// </summary>
        private void WriteDebugLogLine()
        {
            float velMag = _hipsRigidbody != null ? _hipsRigidbody.linearVelocity.magnitude : 0f;
            float horizontalSpeed = 0f;
            float yawAngularVelocity = 0f;
            if (_hipsRigidbody != null)
            {
                Vector3 horizontalVelocity = _hipsRigidbody.linearVelocity;
                horizontalSpeed = new Vector3(horizontalVelocity.x, 0f, horizontalVelocity.z).magnitude;
                yawAngularVelocity = _hipsRigidbody.angularVelocity.y;
            }

            float inputMagnitude = _commandDesiredInput.MoveMagnitude;
            CharacterStateType currentState = _commandObservation.CharacterState;
            Vector3 gf = LegJointDriver.GetWorldGaitForward(_commandDesiredInput, _commandObservation);

            Vector3 ulActual = _upperLegL != null ? _upperLegL.transform.localEulerAngles : Vector3.zero;
            Vector3 llActual = _lowerLegL != null ? _lowerLegL.transform.localEulerAngles : Vector3.zero;

            string line =
                $"FRAME:{Time.frameCount}" +
                $" state:{currentState}" +
                $" input:{inputMagnitude:F2}" +
                $" vel:{velMag:F2}" +
                $" hSpeed:{horizontalSpeed:F2}" +
                $" yawVel:{yawAngularVelocity:F2}" +
                $" gaitFwd:{gf.x:F2},{gf.y:F2},{gf.z:F2}" +
                $" swingAxis:{(_useWorldSpaceSwing ? _jointDriver.WorldSwingAxis : _swingAxis).x:F2},{(_useWorldSpaceSwing ? _jointDriver.WorldSwingAxis : _swingAxis).y:F2},{(_useWorldSpaceSwing ? _jointDriver.WorldSwingAxis : _swingAxis).z:F2}" +
                $" recovering:{_isRecovering}" +
                $" stuckCtr:{_stuckFrameCounter}" +
                $" cooldown:{_recoveryCooldownFrameCounter}" +
                $" biasedFwd:{_isGaitBiasedForward}" +
                $" UL_targetEuler:{_jointDriver.UpperLegLTargetEuler.x:F0},{_jointDriver.UpperLegLTargetEuler.y:F0},{_jointDriver.UpperLegLTargetEuler.z:F0}" +
                $" UL_actualEuler:{ulActual.x:F0},{ulActual.y:F0},{ulActual.z:F0}" +
                $" LL_targetEuler:{_jointDriver.LowerLegLTargetEuler.x:F0},{_jointDriver.LowerLegLTargetEuler.y:F0},{_jointDriver.LowerLegLTargetEuler.z:F0}" +
                $" LL_actualEuler:{llActual.x:F0},{llActual.y:F0},{llActual.z:F0}" +
                $" worldSpacePath:{_useWorldSpaceSwing}";

            Debug.Log(line);

            try
            {
                File.AppendAllText(DebugLogPath, line + "\n");
            }
            catch (IOException ex)
            {
                Debug.LogWarning($"[LegAnimator] Failed to write debug log: {ex.Message}");
            }
        }

        /// <summary>
        /// Reacts to <see cref="CharacterState.OnStateChanged"/> events.
        /// Entering <see cref="CharacterStateType.Airborne"/>: reduces all leg joint springs
        /// by <see cref="_airborneSpringMultiplier"/> (legs go loose/dangly).
        /// Exiting <see cref="CharacterStateType.Airborne"/> (any landing): restores all
        /// leg joint springs to baseline values.
        /// Also sets/clears <see cref="_isAirborne"/> to suppress gait cycling mid-air.
        /// </summary>
        /// <param name="previousState">The state the character was in before the transition.</param>
        /// <param name="newState">The state the character has just entered.</param>
        private void OnCharacterStateChanged(CharacterStateType previousState, CharacterStateType newState)
        {
            if (newState == CharacterStateType.Airborne)
            {
                // Entering airborne: loosen springs so legs dangle naturally.
                _isAirborne = true;
                _isJumpWindUp = false;
                _jumpWindUpKneeBendBoost = 0f;
                _isJumpLaunch = false;
                _jointDriver.SetSpringMultiplier(_airborneSpringMultiplier);
            }
            else if (previousState == CharacterStateType.Airborne)
            {
                // Exiting airborne (any landing — Standing, Moving, Fallen, GettingUp):
                // restore full spring stiffness.
                _isAirborne = false;
                _jointDriver.SetSpringMultiplier(1f);

                // C8.5d: Start landing absorption knee-bend boost on clean landings.
                if (newState == CharacterStateType.Standing || newState == CharacterStateType.Moving)
                {
                    // Keep the active landing window alive through brief bounce chatter
                    // instead of rearming the knee-bend budget on every same-landing contact.
                    if (_landingAbsorbTimer <= 0f)
                    {
                        _landingAbsorbTotalDuration = _landingAbsorbDuration + _landingAbsorbBlendOutDuration;
                        _landingAbsorbTimer = _landingAbsorbTotalDuration;
                    }
                }
            }

            if (newState == CharacterStateType.Fallen)
            {
                _isJumpWindUp = false;
                _jumpWindUpKneeBendBoost = 0f;
                _isJumpLaunch = false;
                _landingAbsorbTimer = 0f;
            }
        }
    }
}

