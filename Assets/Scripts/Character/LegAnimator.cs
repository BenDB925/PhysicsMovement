using System.IO;
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

        [SerializeField, Range(0.1f, 5f)]
        [Tooltip("Scales actual horizontal speed (m/s) to gait cycles per second. " +
                 "At 2 m/s with scale 1.5 → 3 cycles/sec. " +
                 "Eliminates body-outruns-legs: cadence is always proportional to real speed.")]
        private float _stepFrequencyScale = 0.1f;

        [SerializeField, Range(0f, 10f)]
        [Tooltip("Minimum gait cadence in cycles per second, applied even when the character " +
                 "is nearly stationary (optional slow idle cycle). Default 0 = legs still at idle.")]
        private float _stepFrequency = 1f;

        [SerializeField, Range(0f, 75f)]
        [Tooltip("Constant knee-bend angle (degrees) applied to lower leg joints during gait. " +
                 "Larger values = more aggressive, deliberate stride. Default 65°.")]
        private float _kneeAngle = 60f;

        [SerializeField, Range(0f, 55f)]
        [Tooltip("Extra upward lift bias (degrees) added to the upper leg that is in the " +
                 "forward-swing phase (sin(phase) > 0). Biases the knee toward the chest " +
                 "for a powerful, high-stepping gait. Default 15°.")]
        private float _upperLegLiftBoost = 31.9f;

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

        internal float StepAngleDegrees => _stepAngle;

        internal float KneeAngleDegrees => _kneeAngle;

        /// <summary>
        /// True while the stuck-leg recovery pose is being actively applied.
        /// Exposed for test verification; read-only at runtime.
        /// </summary>
        public bool IsRecovering => _isRecovering;

        /// <summary>
        /// True when the stranded-foot forward bias is active this frame: both feet are
        /// behind the hips while the player has active movement input. The gait swing
        /// targets are biased forward by <see cref="_stepAngle"/> × <see cref="_smoothedInputMag"/>
        /// so the backward phase bottoms out at neutral (0°) instead of driving the
        /// already-stranded legs further behind.
        /// Exposed for test verification; read-only at runtime.
        /// </summary>
        public bool IsGaitBiasedForward => _isGaitBiasedForward;

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

            // STEP 5: Create the joint driver that owns swing target application.
            _jointDriver = new LegJointDriver(_upperLegL, _upperLegR, _lowerLegL, _lowerLegR, _swingAxis, _kneeAxis);

            // STEP 6: Seed the per-leg state machines so Chapter 3.2 starts from the same
            //         mirrored cadence shape as the legacy pass-through gait.
            ResetLegStateMachinesForMirroredCadence();
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
                float effectiveCyclesPerSec = Mathf.Max(_stepFrequency, horizontalSpeedGate * _stepFrequencyScale);
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
                    bothFeetFarBehind = leftFootForwardOffset < -0.2f && rightFootForwardOffset < -0.2f;
                }

                // STEP 2: Chapter 3.4 gives sharp turns and recovery explicit per-leg ownership
                //         instead of pushing one body-level reason through both legs.
                bool promoteRecoveryOverride = desiredInput.HasMoveIntent &&
                    (observation.IsLocomotionCollapsed || bothFeetFarBehind);
                bool forceCatchStep = desiredInput.HasMoveIntent &&
                    (observation.IsLocomotionCollapsed || bothFeetFarBehind);
                bool hasTurnAsymmetry = TryGetTurnLegRoles(
                    desiredInput,
                    observation,
                    out LocomotionLeg outsideLeg,
                    out LocomotionLeg insideLeg);
                LocomotionLeg recoveryLeg = SelectRecoveryLeg(gaitReferenceDirection, observation);
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

                float leftSwingDeg = LegExecutionProfileResolver.BuildSwingAngleFromPhase(
                    _leftLegStateMachine.CyclePhase,
                    _smoothedInputMag,
                    leftStateFrame.State,
                    _stepAngle,
                    _upperLegLiftBoost);
                float rightSwingDeg = LegExecutionProfileResolver.BuildSwingAngleFromPhase(
                    _rightLegStateMachine.CyclePhase,
                    _smoothedInputMag,
                    rightStateFrame.State,
                    _stepAngle,
                    _upperLegLiftBoost);

                if (bothFeetBehind)
                {
                    float strandedBias = _stepAngle * _smoothedInputMag;
                    leftSwingDeg += strandedBias;
                    rightSwingDeg += strandedBias;
                    _isGaitBiasedForward = true;
                }

                float kneeBendDeg = _kneeAngle * _smoothedInputMag;

                StepTarget leftStepTarget = _stepPlanner.ComputeSwingTarget(
                    LocomotionLeg.Left,
                    _leftLegStateMachine.CyclePhase,
                    leftStateFrame.State,
                    leftTransitionReason,
                    desiredInput,
                    observation,
                    _hipsRigidbody.position,
                    gaitReferenceDirection,
                    _stepFrequency);
                StepTarget rightStepTarget = _stepPlanner.ComputeSwingTarget(
                    LocomotionLeg.Right,
                    _rightLegStateMachine.CyclePhase,
                    rightStateFrame.State,
                    rightTransitionReason,
                    desiredInput,
                    observation,
                    _hipsRigidbody.position,
                    gaitReferenceDirection,
                    _stepFrequency);

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
                _confidenceEvaluator.ApplyFallback(
                    ref explicitLeftCommand,
                    ref explicitRightCommand,
                    gaitReferenceDirection,
                    bothFeetBehind,
                    _stepAngle,
                    _kneeAngle,
                    _upperLegLiftBoost);

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

            LegExecutionProfileResolver.Resolve(_leftLegCommand, _useStateDrivenExecution, _stepAngle, _kneeAngle, out float leftSwingDeg, out float leftKneeBendDeg);
            LegExecutionProfileResolver.Resolve(_rightLegCommand, _useStateDrivenExecution, _stepAngle, _kneeAngle, out float rightSwingDeg, out float rightKneeBendDeg);

            _jointDriver.ApplySwingTargets(leftSwingDeg, rightSwingDeg, leftKneeBendDeg, rightKneeBendDeg, _useWorldSpaceSwing, _commandDesiredInput, _commandObservation);
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
                    recoveryScore += Mathf.Clamp01(-footForwardOffset / 0.4f) * 0.4f;
                }
            }

            return recoveryScore;
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
                _jointDriver.SetSpringMultiplier(_airborneSpringMultiplier);
            }
            else if (previousState == CharacterStateType.Airborne)
            {
                // Exiting airborne (any landing — Standing, Moving, Fallen, GettingUp):
                // restore full spring stiffness.
                _isAirborne = false;
                _jointDriver.SetSpringMultiplier(1f);
            }
        }
    }
}
