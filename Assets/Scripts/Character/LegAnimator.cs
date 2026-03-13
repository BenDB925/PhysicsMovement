using System.IO;
using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Drives procedural gait animation on the ragdoll's four leg ConfigurableJoints
    /// by advancing a phase accumulator from actual Rigidbody horizontal speed and
    /// applying sinusoidal target rotations each FixedUpdate.
    /// Left and right upper legs are offset by π (half-cycle), producing an alternating
    /// stepping pattern. Lower legs receive a constant knee-bend target during gait plus
    /// an optional upward lift boost on the forward-swinging leg.
    ///
    /// Axis convention (Bug fix — Phase 3E1 axis correction):
    /// Unity's ConfigurableJoint.targetRotation maps the joint's primary axis (joint.axis)
    /// to the Z component of the target Euler rotation, NOT the X component as might be
    /// intuited. With RagdollBuilder defaults (joint.axis = Vector3.right, secondaryAxis =
    /// Vector3.forward), the correct swing axis in targetRotation space is Vector3.forward
    /// (Z), not Vector3.right (X). Using X caused lateral abduction (side-to-side wiggle)
    /// instead of sagittal swing (forward/backward lift). The axes are now serialized as
    /// <see cref="_swingAxis"/> and <see cref="_kneeAxis"/> for Inspector tuning.
    ///
    /// World-space swing (Phase 3E3 — forward-tilt fix):
    /// When <see cref="_useWorldSpaceSwing"/> is true (default), leg swing targets are
    /// computed relative to the world-space movement direction rather than the Hips local
    /// frame. This fixes the "dragging feet" symptom that occurs when the torso is pitched
    /// forward: in that state the Hips +Z local axis points downward in world space, so a
    /// pure local-frame "forward swing" actually drives the legs into the ground instead of
    /// stepping ahead of the body.
    ///
    /// World-space swing implementation:
    ///   1. A world-space gait-forward direction is derived from the Hips Rigidbody's
    ///      horizontal velocity. If velocity is negligible (e.g. just-started), the move
    ///      input direction (projected onto the XZ plane) is used as a fallback.
    ///   2. The swing axis in world space = Cross(gaitForward, worldUp), which is the
    ///      horizontal axis perpendicular to the movement direction. Rotating around this
    ///      axis swings a leg forward or backward in the sagittal plane of movement,
    ///      regardless of the torso's pitch angle.
    ///   3. The desired world-space rotation for each upper leg is computed as
    ///      Quaternion.AngleAxis(swingDeg, worldSwingAxis) applied to the joint body's
    ///      current world rotation. This is then converted to the ConnectedBody (parent)
    ///      frame before being assigned to ConfigurableJoint.targetRotation, so the SLERP
    ///      drive interprets it correctly.
    ///   4. The lower-leg knee bend uses a fixed angle around the same world-space swing
    ///      axis (positive = knee bends forward / upward relative to world up), ensuring
    ///      knees never fold into the ground even when the torso is pitched forward.
    ///
    /// When <see cref="_useWorldSpaceSwing"/> is false, the original local-frame behaviour
    /// is used (Quaternion.AngleAxis around <see cref="_swingAxis"/>/<see cref="_kneeAxis"/>
    /// in targetRotation space). This is provided for side-by-side comparison only.
    ///
    /// Idle settle behaviour (Phase 3E2):
    /// — When move input drops to zero, all four joint targetRotations are set directly
    ///   to <see cref="Quaternion.identity"/> each FixedUpdate, so legs immediately
    ///   relax to their rest pose.
    /// — The gait phase accumulator decays toward zero during idle at a rate controlled
    ///   by <see cref="_idleBlendSpeed"/>.  When input resumes the next step starts from
    ///   a near-neutral phase, keeping the first-frame rotation amplitude small.
    /// — A smoothed input scale (<see cref="_smoothedInputMag"/>) ramps from 0 to 1 as
    ///   input resumes, preventing an abrupt snap to full gait amplitude immediately
    ///   after a period of idle.  The ramp rate is also controlled by
    ///   <see cref="_idleBlendSpeed"/>.  When stopping, the smoothed scale is immediately
    ///   zeroed so legs snap cleanly to identity without residual sway.
    ///
    /// The combination of phase decay + smoothed re-entry eliminates abrupt visual pops
    /// on quick move/stop/move toggles while preserving the correct L/R alternating gait
    /// phase relationship at all times.
    ///
    /// Velocity-driven gait speed (Phase 3E4 — gait-velocity-knees):
    /// The phase accumulator now advances based on actual Rigidbody horizontal speed
    /// (metres per second) rather than raw input magnitude. This eliminates the
    /// body-outruns-legs and tap-dancing-at-idle problems: legs always cycle at a
    /// cadence proportional to how fast the character is actually moving.
    ///   effectiveCyclesPerSec = max(_stepFrequency, horizontalSpeed × _stepFrequencyScale)
    /// <see cref="_stepFrequencyScale"/> maps m/s to cycles/s (default 1.5: at 2 m/s → 3 Hz).
    /// <see cref="_stepFrequency"/> is the minimum cadence (default 0: legs are still at idle).
    ///
    /// Aggressive knee lift (Phase 3E4 — gait-velocity-knees):
    /// <see cref="_kneeAngle"/> default raised from 20° to 55° for powerful, deliberate strides.
    /// <see cref="_upperLegLiftBoost"/> (default 15°, range 0–45°) adds an extra upward component
    /// to the upper leg that is in the forward-swing phase (sin > 0), biasing the knee toward
    /// the chest rather than just swinging the leg forward flat.
    ///
    /// When the character is in the <see cref="CharacterStateType.Fallen"/> or
    /// <see cref="CharacterStateType.GettingUp"/> state, all leg joints are returned
    /// immediately to <see cref="Quaternion.identity"/> and the phase / scale are reset
    /// to zero.
    /// Attach to the Hips (root) GameObject alongside <see cref="BalanceController"/>,
    /// <see cref="PlayerMovement"/>, and <see cref="CharacterState"/>.
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

        // ── Airborne Spring Scaling (Phase 3F2) ────────────────────────────

        /// <summary>Baseline SLERP drive stored from each leg joint at Start, restored on landing.</summary>
        private JointDrive _baselineUpperLegLDrive;
        private JointDrive _baselineUpperLegRDrive;
        private JointDrive _baselineLowerLegLDrive;
        private JointDrive _baselineLowerLegRDrive;

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
        private float _fallbackGaitBlend;
        private bool _isFallbackGaitLatched;
        private readonly StepPlanner _stepPlanner = new StepPlanner();

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

        /// <summary>
        /// Last world-space swing axis computed by <see cref="ApplyWorldSpaceSwing"/> this frame.
        /// Refreshed unconditionally at the top of every <see cref="FixedUpdate"/> call so it is
        /// never stale. Zero when world-space swing was not applied this frame (e.g. idle,
        /// gait-forward degenerate, or local-space fallback active).
        /// DESIGN: Previously this field was set only inside ApplyWorldSpaceSwing, which meant
        /// it could retain a non-zero value from a prior frame if the code path did not reach
        /// that method (e.g. early-exit for fallen state). Resetting it at the top of FixedUpdate
        /// guarantees that the value in WriteDebugLogLine always reflects the current frame.
        /// </summary>
        private Vector3 _worldSwingAxis;

        /// <summary>Last target Euler angles (degrees) applied to UpperLeg_L's ConfigurableJoint targetRotation.</summary>
        private Vector3 _upperLegLTargetEuler;

        /// <summary>Last target Euler angles (degrees) applied to LowerLeg_L's ConfigurableJoint targetRotation.</summary>
        private Vector3 _lowerLegLTargetEuler;

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

            // STEP 5: Seed the per-leg state machines so Chapter 3.2 starts from the same
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
            CaptureBaselineDrives();
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
            _worldSwingAxis = Vector3.zero;
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
                ResetFallbackGaitBlend();
                ResetLegStateMachinesToIdle();
                SetAllLegTargetsToIdentity();
                return;
            }

            _suppressIncomingCommandFrame = false;

            if (!_hasCommandFrame)
            {
                _phase = 0f;
                _smoothedInputMag = 0f;
                ResetFallbackGaitBlend();
                ResetLegStateMachinesToIdle();
                SetAllLegTargetsToIdentity();
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
                ResetFallbackGaitBlend();
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
                ResetFallbackGaitBlend();
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
                    SetLegSpringMultiplier(_recoverySpringMultiplier);
                }
            }

            Vector3 gaitReferenceDirection = desiredInput.MoveWorldDirection.sqrMagnitude > 0.0001f
                ? desiredInput.MoveWorldDirection
                : observation.BodyForward;

            EnsureLegStateMachines();

            if (_isRecovering)
            {
                ResetFallbackGaitBlend();
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
                    SetLegSpringMultiplier(1f);
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

                float leftSwingDeg = BuildSwingAngleFromPhase(
                    _leftLegStateMachine.CyclePhase,
                    _smoothedInputMag,
                    leftStateFrame.State);
                float rightSwingDeg = BuildSwingAngleFromPhase(
                    _rightLegStateMachine.CyclePhase,
                    _smoothedInputMag,
                    rightStateFrame.State);

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
                float stateMachineConfidence = ComputeStateMachineConfidence(
                    observation,
                    hasTurnAsymmetry,
                    forceCatchStep,
                    explicitLeftCommand,
                    explicitRightCommand);
                UpdateFallbackGaitBlend(stateMachineConfidence);
                ApplyLowConfidenceFallback(
                    gaitReferenceDirection,
                    bothFeetBehind,
                    ref explicitLeftCommand,
                    ref explicitRightCommand);

                leftCommand = explicitLeftCommand;
                rightCommand = explicitRightCommand;
                _phase = leftCommand.CyclePhase;
                return;
            }

            float decayStep = _idleBlendSpeed * Mathf.PI * Time.fixedDeltaTime;
            float decayT = Mathf.Clamp01(_idleBlendSpeed * Time.fixedDeltaTime);
            _smoothedInputMag = Mathf.Lerp(_smoothedInputMag, 0f, decayT);
            ResetFallbackGaitBlend();

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
            ResetFallbackGaitBlend();
            ResetLegStateMachinesToIdle();
            SetLegSpringMultiplier(1f);
            SetAllLegTargetsToIdentity();
        }

        private void ApplyCommandFrame()
        {
            if (_leftLegCommand.Mode == LegCommandMode.Disabled && _rightLegCommand.Mode == LegCommandMode.Disabled)
            {
                SetAllLegTargetsToIdentity();
                return;
            }

            ResolveLegExecutionTargets(_leftLegCommand, out float leftSwingDeg, out float leftKneeBendDeg);
            ResolveLegExecutionTargets(_rightLegCommand, out float rightSwingDeg, out float rightKneeBendDeg);

            if (_useWorldSpaceSwing)
            {
                ApplyWorldSpaceSwing(leftSwingDeg, rightSwingDeg, leftKneeBendDeg, rightKneeBendDeg);
                return;
            }

            ApplyLocalSpaceSwing(leftSwingDeg, rightSwingDeg, leftKneeBendDeg, rightKneeBendDeg);
        }

        private void ResolveLegExecutionTargets(
            LegCommandOutput command,
            out float swingAngleDegrees,
            out float kneeAngleDegrees)
        {
            if (command.Mode == LegCommandMode.Disabled)
            {
                swingAngleDegrees = 0f;
                kneeAngleDegrees = 0f;
                return;
            }

            // STEP 1: Start from the raw pass-through payload so the legacy sinusoidal
            //         executor remains the fallback for any state that is not yet bridged.
            swingAngleDegrees = command.SwingAngleDegrees;
            kneeAngleDegrees = command.KneeAngleDegrees;

            if (!_useStateDrivenExecution)
            {
                return;
            }

            // STEP 2: Shape per-leg targets from the explicit Chapter 3 state so support,
            //         touchdown, and catch-step windows can diverge even when the raw command
            //         payload still mirrors the old sinusoidal executor contract.
            switch (command.State)
            {
                case LegStateType.Swing:
                    ApplySwingExecutionProfile(command, ref swingAngleDegrees, ref kneeAngleDegrees);
                    break;

                case LegStateType.Stance:
                    ApplyStanceExecutionProfile(command, ref swingAngleDegrees, ref kneeAngleDegrees);
                    break;

                case LegStateType.Plant:
                    ApplyPlantExecutionProfile(command, ref swingAngleDegrees, ref kneeAngleDegrees);
                    break;

                case LegStateType.RecoveryStep:
                    ApplyRecoveryStepExecutionProfile(command, ref swingAngleDegrees, ref kneeAngleDegrees);
                    break;

                case LegStateType.CatchStep:
                    ApplyCatchStepExecutionProfile(command, ref swingAngleDegrees, ref kneeAngleDegrees);
                    break;
            }
        }

        private void ApplySwingExecutionProfile(
            LegCommandOutput command,
            ref float swingAngleDegrees,
            ref float kneeAngleDegrees)
        {
            // STEP 1: Explicit swing windows should keep a reliable forward arc so each leg
            //         visibly takes a turn leading even when physics noise eats part of the raw gait command.
            float swingProgress = Mathf.InverseLerp(0f, Mathf.PI, Mathf.Min(command.CyclePhase, Mathf.PI));
            float swingForwardTarget = Mathf.Lerp(
                _stepAngle * 0.58f,
                _stepAngle * 0.68f,
                Mathf.SmoothStep(0f, 1f, swingProgress)) * command.BlendWeight;

            swingAngleDegrees = Mathf.Max(swingAngleDegrees, swingForwardTarget);
            kneeAngleDegrees = Mathf.Max(kneeAngleDegrees, _kneeAngle * 0.35f * command.BlendWeight);
        }

        private void ApplyStanceExecutionProfile(
            LegCommandOutput command,
            ref float swingAngleDegrees,
            ref float kneeAngleDegrees)
        {
            // STEP 1: Support-side stance should stay comparatively extended so the opposite
            //         swing leg owns most of the visible lift and knee tuck.
            float stanceProgress = Mathf.InverseLerp(Mathf.PI, Mathf.PI * 2f, command.CyclePhase);
            float supportKneeTarget = Mathf.Lerp(_kneeAngle * 0.2f, _kneeAngle * 0.08f, stanceProgress) * command.BlendWeight;

            kneeAngleDegrees = Mathf.Min(kneeAngleDegrees, supportKneeTarget);

            if (command.TransitionReason == LegStateTransitionReason.None)
            {
                swingAngleDegrees = Mathf.Lerp(swingAngleDegrees, 0f, 0.85f);
                return;
            }

            if (swingAngleDegrees > 0f)
            {
                swingAngleDegrees *= 1f - stanceProgress * 0.5f;
            }
        }

        private void ApplyPlantExecutionProfile(
            LegCommandOutput command,
            ref float swingAngleDegrees,
            ref float kneeAngleDegrees)
        {
            // STEP 1: The plant window should keep the legacy upper-leg forward reach intact,
            //         but extend the knee back toward the neutral support pose as the foot settles.
            float plantProgress = Mathf.InverseLerp(Mathf.PI * 0.85f, Mathf.PI, command.CyclePhase);
            float easedPlantProgress = Mathf.SmoothStep(0f, 1f, plantProgress);
            float touchdownKneeTarget = Mathf.Lerp(
                kneeAngleDegrees,
                _kneeAngle * 0.1f * command.BlendWeight,
                easedPlantProgress);

            kneeAngleDegrees = Mathf.Min(kneeAngleDegrees, touchdownKneeTarget);
        }

        private void ApplyRecoveryStepExecutionProfile(
            LegCommandOutput command,
            ref float swingAngleDegrees,
            ref float kneeAngleDegrees)
        {
            // STEP 1: Recovery steps should move through an explicit brace-to-reach window
            //         instead of replaying the old static stuck pose. Early recovery braces
            //         the leg back under the body; late recovery reaches further forward with
            //         more knee tuck so the leg can reclaim support.
            float recoveryStepProgress = Mathf.InverseLerp(0f, Mathf.PI, Mathf.Min(command.CyclePhase, Mathf.PI));
            float easedRecoveryProgress = Mathf.SmoothStep(0f, 1f, recoveryStepProgress);
            float recoverySwingTarget = Mathf.Lerp(
                -_stepAngle * 0.28f,
                _stepAngle * 0.72f,
                easedRecoveryProgress) * command.BlendWeight;
            float recoveryKneeTarget = Mathf.Lerp(
                _kneeAngle * 0.12f,
                _kneeAngle * 0.7f,
                easedRecoveryProgress) * command.BlendWeight;

            swingAngleDegrees = command.Mode == LegCommandMode.HoldPose
                ? recoverySwingTarget
                : Mathf.Lerp(swingAngleDegrees, recoverySwingTarget, 0.85f);
            kneeAngleDegrees = Mathf.Max(kneeAngleDegrees, recoveryKneeTarget);
        }

        private void ApplyCatchStepExecutionProfile(
            LegCommandOutput command,
            ref float swingAngleDegrees,
            ref float kneeAngleDegrees)
        {
            // STEP 1: Catch steps need a more assertive forward placement than normal swing
            //         so the recovery leg reaches under the body instead of tracing the old arc.
            float catchStepProgress = Mathf.InverseLerp(0f, Mathf.PI, Mathf.Min(command.CyclePhase, Mathf.PI));
            float catchStepForwardTarget = Mathf.Lerp(
                _stepAngle * 0.64f,
                _stepAngle * 0.78f,
                Mathf.SmoothStep(0f, 1f, catchStepProgress)) * command.BlendWeight;
            float catchStepKneeTarget = _kneeAngle * 0.65f * command.BlendWeight;

            swingAngleDegrees = Mathf.Max(swingAngleDegrees, catchStepForwardTarget);
            kneeAngleDegrees = Mathf.Max(kneeAngleDegrees, catchStepKneeTarget);
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

        private void ResetFallbackGaitBlend()
        {
            _fallbackGaitBlend = 0f;
            _isFallbackGaitLatched = false;
        }

        private float ComputeStateMachineConfidence(
            LocomotionObservation observation,
            bool hasTurnAsymmetry,
            bool forceCatchStep,
            LegCommandOutput leftCommand,
            LegCommandOutput rightCommand)
        {
            // STEP 1: Start from the weakest observation signal that the explicit gait
            //         controller currently depends on so brief sensor drops reduce trust.
            float supportConfidence = Mathf.Min(
                observation.SupportQuality,
                observation.ContactConfidence);
            float stabilityConfidence = Mathf.Min(
                supportConfidence,
                Mathf.Min(observation.PlantedFootConfidence, 1f - observation.SlipEstimate));
            bool preserveTurnSupportOwnership = hasTurnAsymmetry &&
                !forceCatchStep &&
                !observation.IsLocomotionCollapsed;
            bool developingTurn = observation.TurnSeverity > 0.15f &&
                !forceCatchStep &&
                !observation.IsLocomotionCollapsed;

            // STEP 2: Penalize observation states that historically imply unstable or
            //         contradictory support, because those are the frames where mirrored
            //         fallback is safer than preserving asymmetric gait roles.
            //         Use the softer COM penalty when a turn is developing but not yet
            //         past the TryGetTurnLegRoles threshold, so early turn frames don't
            //         crater confidence before preserveTurnSupportOwnership activates.
            if (observation.IsComOutsideSupport)
            {
                stabilityConfidence *= (preserveTurnSupportOwnership || developingTurn) ? 0.85f : 0.6f;
            }

            if (observation.IsInSnapRecovery)
            {
                stabilityConfidence *= 0.75f;
            }

            if (observation.IsLocomotionCollapsed)
            {
                stabilityConfidence *= 0.45f;
            }

            // STEP 3: Punish unexplained phase drift away from the mirrored gait when
            //         turn/recovery ownership is absent. Preserve the explicit gait via
            //         the planted-foot floor that is applied as the final step below.
            if (!preserveTurnSupportOwnership && !forceCatchStep)
            {
                float mirroredRightPhase = Mathf.Repeat(leftCommand.CyclePhase + Mathf.PI, Mathf.PI * 2f);
                float mirrorDeviation = Mathf.Abs(
                    Mathf.DeltaAngle(rightCommand.CyclePhase * Mathf.Rad2Deg, mirroredRightPhase * Mathf.Rad2Deg)) * Mathf.Deg2Rad;
                float footAsymmetry = Mathf.Max(
                    Mathf.Abs(observation.LeftFoot.ContactConfidence - observation.RightFoot.ContactConfidence),
                    Mathf.Abs(observation.LeftFoot.PlantedConfidence - observation.RightFoot.PlantedConfidence));
                float unexpectedAsymmetry = Mathf.InverseLerp(0.05f, 0.3f, mirrorDeviation) * footAsymmetry;
                // Attenuate the unexplained-asymmetry penalty as TurnSeverity ramps
                // toward the 0.45 recognition threshold so early turn frames don't
                // latch fallback before preserveTurnSupportOwnership activates.
                float turnAttenuation = 1f - Mathf.InverseLerp(0.15f, 0.45f, observation.TurnSeverity);
                stabilityConfidence *= Mathf.Lerp(1f, 0.25f, unexpectedAsymmetry * turnAttenuation);
            }

            if (hasTurnAsymmetry)
            {
                stabilityConfidence *= preserveTurnSupportOwnership
                    ? Mathf.Lerp(1f, 0.95f, observation.TurnSeverity)
                    : Mathf.Lerp(1f, 0.85f, observation.TurnSeverity);
            }

            if (forceCatchStep)
            {
                stabilityConfidence *= 0.75f;
            }

            // STEP 4: Floor — applied AFTER all penalties so it is the definitive minimum.
            //         During a recognized sharp turn the floor guarantees at least the
            //         exit threshold so a fallback latch that accumulated during normal
            //         walking is immediately released once the turn is recognized.
            //         The planted-foot component still applies when available, but a hard
            //         turn-recognition floor ensures the state machine stays confident
            //         enough to execute turn-specific leg roles even when GroundSensor
            //         planted confidence is transiently zero.
            if (preserveTurnSupportOwnership)
            {
                float plantedSupportConfidence = Mathf.Min(
                    Mathf.Max(observation.LeftFoot.PlantedConfidence, observation.RightFoot.PlantedConfidence),
                    1f - observation.SlipEstimate);
                float floorValue = plantedSupportConfidence * 0.7f;

                // During a recognized sharp turn, guarantee at least the exit threshold.
                if (hasTurnAsymmetry)
                {
                    floorValue = Mathf.Max(floorValue, _minimumStateMachineConfidenceExit);
                }

                stabilityConfidence = Mathf.Max(stabilityConfidence, floorValue);
            }

            return Mathf.Clamp01(stabilityConfidence);
        }

        private void UpdateFallbackGaitBlend(float stateMachineConfidence)
        {
            // STEP 1: Latch the fallback gate with hysteresis so a noisy confidence sample
            //         does not cause the gait to flicker between explicit and mirrored modes.
            if (_isFallbackGaitLatched)
            {
                if (stateMachineConfidence >= _minimumStateMachineConfidenceExit)
                {
                    _isFallbackGaitLatched = false;
                }
            }
            else if (stateMachineConfidence <= _minimumStateMachineConfidence)
            {
                _isFallbackGaitLatched = true;
            }

            // STEP 2: Blend numerically into or out of the fallback gait instead of hard
            //         snapping phases and swing targets in a single frame.
            float targetBlend = _isFallbackGaitLatched ? 1f : 0f;
            float blendSpeed = _isFallbackGaitLatched
                ? _fallbackGaitBlendRiseSpeed
                : _fallbackGaitBlendFallSpeed;
            _fallbackGaitBlend = Mathf.MoveTowards(
                _fallbackGaitBlend,
                targetBlend,
                Mathf.Max(0f, blendSpeed) * Time.fixedDeltaTime);
        }

        private void ApplyLowConfidenceFallback(
            Vector3 gaitReferenceDirection,
            bool applyStrandedBias,
            ref LegCommandOutput leftCommand,
            ref LegCommandOutput rightCommand)
        {
            if (_fallbackGaitBlend <= 0.0001f)
            {
                return;
            }

            // STEP 1: Rebuild a stable mirrored gait anchored on the current exposed left-leg
            //         phase, preserving forward continuity while steering the opposite leg
            //         back toward the legacy half-cycle relationship.
            LegCommandOutput fallbackLeftCommand = BuildFallbackCycleCommand(
                LocomotionLeg.Left,
                leftCommand.CyclePhase,
                leftCommand.BlendWeight,
                gaitReferenceDirection,
                applyStrandedBias);
            LegCommandOutput fallbackRightCommand = BuildFallbackCycleCommand(
                LocomotionLeg.Right,
                Mathf.Repeat(leftCommand.CyclePhase + Mathf.PI, Mathf.PI * 2f),
                rightCommand.BlendWeight,
                gaitReferenceDirection,
                applyStrandedBias);

            // STEP 2: Blend the emitted command payloads toward that mirrored fallback so
            //         the safety path converges over several frames instead of hard snapping.
            leftCommand = BlendTowardFallbackCommand(leftCommand, fallbackLeftCommand);
            rightCommand = BlendTowardFallbackCommand(rightCommand, fallbackRightCommand);
        }

        private LegCommandOutput BuildFallbackCycleCommand(
            LocomotionLeg leg,
            float cyclePhase,
            float blendWeight,
            Vector3 gaitReferenceDirection,
            bool applyStrandedBias)
        {
            LegStateFrame stateFrame = new LegStateFrame(
                leg,
                InferFallbackStateFromPhase(cyclePhase),
                LegStateTransitionReason.LowConfidenceFallback);
            float swingAngleDegrees = BuildSwingAngleFromPhase(cyclePhase, blendWeight, stateFrame.State);
            if (applyStrandedBias)
            {
                swingAngleDegrees += _stepAngle * blendWeight;
            }

            return new LegCommandOutput(
                leg,
                LegCommandMode.Cycle,
                stateFrame,
                cyclePhase,
                swingAngleDegrees,
                _kneeAngle * blendWeight,
                blendWeight,
                StepTarget.Invalid);
        }

        private LegCommandOutput BlendTowardFallbackCommand(
            LegCommandOutput explicitCommand,
            LegCommandOutput fallbackCommand)
        {
            float blendedCyclePhase = LerpWrappedPhase(
                explicitCommand.CyclePhase,
                fallbackCommand.CyclePhase,
                _fallbackGaitBlend);

            // STEP 2: Promote the logged state/reason into the fallback path once the
            //         numeric blend is clearly underway, rather than waiting until the
            //         gait is almost fully mirrored and risking a collapse before the
            //         safety mode becomes authoritative.
            LegStateFrame blendedStateFrame = _fallbackGaitBlend >= 0.35f
                ? fallbackCommand.StateFrame
                : explicitCommand.StateFrame;

            return new LegCommandOutput(
                explicitCommand.Leg,
                explicitCommand.Mode,
                blendedStateFrame,
                blendedCyclePhase,
                Mathf.Lerp(explicitCommand.SwingAngleDegrees, fallbackCommand.SwingAngleDegrees, _fallbackGaitBlend),
                Mathf.Lerp(explicitCommand.KneeAngleDegrees, fallbackCommand.KneeAngleDegrees, _fallbackGaitBlend),
                Mathf.Lerp(explicitCommand.BlendWeight, fallbackCommand.BlendWeight, _fallbackGaitBlend),
                explicitCommand.StepTarget);
        }

        private static float LerpWrappedPhase(float fromPhase, float toPhase, float blend)
        {
            float deltaDegrees = Mathf.DeltaAngle(fromPhase * Mathf.Rad2Deg, toPhase * Mathf.Rad2Deg);
            return Mathf.Repeat(fromPhase + deltaDegrees * Mathf.Deg2Rad * Mathf.Clamp01(blend), Mathf.PI * 2f);
        }

        private static LegStateType InferFallbackStateFromPhase(float cyclePhase)
        {
            if (cyclePhase < Mathf.PI * 0.85f)
            {
                return LegStateType.Swing;
            }

            if (cyclePhase < Mathf.PI)
            {
                return LegStateType.Plant;
            }

            return LegStateType.Stance;
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

        private float BuildSwingAngleFromPhase(
            float cyclePhase,
            float amplitudeScale,
            LegStateType state)
        {
            float swingSin = Mathf.Sin(cyclePhase);
            float liftBoost = swingSin > 0f ? swingSin * _upperLegLiftBoost * amplitudeScale : 0f;
            float swingAngle = swingSin * _stepAngle * amplitudeScale + liftBoost;

            if ((state == LegStateType.Swing || state == LegStateType.CatchStep) && amplitudeScale > 0f)
            {
                swingAngle += _upperLegLiftBoost * 0.6f * amplitudeScale;

                float minimumForwardArc = _stepAngle * 0.55f * amplitudeScale;
                swingAngle = Mathf.Max(swingAngle, minimumForwardArc);
            }

            return swingAngle;
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
        /// Applies world-space swing targets to the four leg ConfigurableJoints.
        /// The swing axis is computed from the character's actual movement direction (world
        /// horizontal velocity or move-input fallback), ensuring legs step forward in world
        /// space even when the torso is pitched forward.
        /// </summary>
        /// <param name="leftSwingDeg">Signed swing angle (degrees) for the left upper leg.</param>
        /// <param name="rightSwingDeg">Signed swing angle (degrees) for the right upper leg.</param>
        /// <param name="leftKneeBendDeg">Knee bend angle (degrees, positive = forward flex) for the left leg.</param>
        /// <param name="rightKneeBendDeg">Knee bend angle (degrees, positive = forward flex) for the right leg.</param>
        private void ApplyWorldSpaceSwing(
            float leftSwingDeg,
            float rightSwingDeg,
            float leftKneeBendDeg,
            float rightKneeBendDeg)
        {
            // STEP A: Determine the world-space gait-forward direction.
            //         Primary: use the Hips Rigidbody's horizontal velocity (most accurate
            //         proxy for actual movement direction regardless of torso pitch).
            //         Fallback: project CurrentMoveInput (XZ) when velocity is near zero
            //         (e.g. just starting to move).
            Vector3 gaitForward = GetWorldGaitForward(_commandDesiredInput, _commandObservation);

            // STEP B: Build the world-space swing axis.
            //         Cross(up, forward) gives the correct right-hand axis so that a positive
            //         swing angle lifts the leg *forward* in the direction of travel.
            //         If gaitForward is zero (velocity below threshold, no input), reuse the
            //         last known good axis so legs keep striding rather than snapping to the
            //         broken local-space fallback path (which causes "cat pulsing").
            Vector3 worldSwingAxis;
            if (gaitForward.sqrMagnitude > 0.0001f)
            {
                worldSwingAxis = Vector3.Cross(Vector3.up, gaitForward).normalized;
            }
            else if (_worldSwingAxis.sqrMagnitude > 0.0001f)
            {
                // Reuse last known good axis — legs continue striding in the last direction.
                worldSwingAxis = _worldSwingAxis;
            }
            else
            {
                // No axis at all (very first frames) — local-space fallback.
                ApplyLocalSpaceSwing(leftSwingDeg, rightSwingDeg, leftKneeBendDeg, rightKneeBendDeg);
                return;
            }

            // Store the computed world swing axis so it can be included in debug log output.
            _worldSwingAxis = worldSwingAxis;

            // STEP C: Apply upper-leg targets in world space, converted to joint-local frame.
            ApplyWorldSpaceJointTarget(_upperLegL, leftSwingDeg,  worldSwingAxis);
            if (_upperLegL != null) { _upperLegLTargetEuler = _upperLegL.targetRotation.eulerAngles; }
            ApplyWorldSpaceJointTarget(_upperLegR, rightSwingDeg, worldSwingAxis);

            // STEP D: Apply lower-leg knee-bend targets.
            //         Positive kneeBendDeg bends the knee forward (in the direction of
            //         movement) — same worldSwingAxis, but opposite sign convention
            //         so the lower leg folds in the physiologically correct direction.
            ApplyWorldSpaceJointTarget(_lowerLegL, -leftKneeBendDeg, worldSwingAxis);
            if (_lowerLegL != null) { _lowerLegLTargetEuler = _lowerLegL.targetRotation.eulerAngles; }
            ApplyWorldSpaceJointTarget(_lowerLegR, -rightKneeBendDeg, worldSwingAxis);
        }

        /// <summary>
        /// Computes and assigns a world-space swing target rotation to a single
        /// ConfigurableJoint. The target is expressed as: "rotate the joint body's
        /// current orientation by <paramref name="swingDeg"/> degrees around
        /// <paramref name="worldAxis"/> in world space", then converted to the connected
        /// body's local frame for <c>ConfigurableJoint.targetRotation</c>.
        /// </summary>
        /// <param name="joint">The ConfigurableJoint to drive. Logs a LogError and returns if null.</param>
        /// <param name="swingDeg">
        /// Signed angle in degrees. Positive = swing in the direction of the axis
        /// by the right-hand rule (forward/upward for knee, forward swing for upper leg).
        /// </param>
        /// <param name="worldAxis">The world-space rotation axis (should be pre-normalised).</param>
        private static void ApplyWorldSpaceJointTarget(ConfigurableJoint joint, float swingDeg, Vector3 worldAxis)
        {
            if (joint == null)
            {
                Debug.LogError("[LegAnimator] ApplyWorldSpaceJointTarget: joint reference is null — targetRotation was NOT applied. " +
                               "Check that Awake() found the correct ConfigurableJoint by name (UpperLeg_L / UpperLeg_R / LowerLeg_L / LowerLeg_R).");
                return;
            }

            // DESIGN: ConfigurableJoint.targetRotation is specified in the space of the
            // connected body (parent body). It represents the desired local rotation of the
            // joint body relative to its connected body, measured in the connected body's
            // frame at the time the joint was created (i.e., the initial reference frame).
            //
            // To drive toward a world-space orientation:
            //   1. Build the desired world-space rotation delta: a rotation around worldAxis
            //      by swingDeg, applied on top of the joint body's current world rotation.
            //      We use Quaternion.identity as the "rest" reference, so swingDeg=0 → identity
            //      target → joint returns to rest pose (exactly matching the legacy behaviour).
            //
            //   2. Convert to connected-body-local frame:
            //      localTarget = Inverse(connectedBody.rotation) × worldSpaceTargetRot
            //
            //   This is equivalent to: "in the parent's frame, the child should be at this
            //   rotation" — which is exactly what ConfigurableJoint.targetRotation expects.
            //
            //   Note: we intentionally do NOT multiply by the joint body's current world rotation.
            //   Instead we treat swingDeg as an absolute offset from the rest pose (identity),
            //   expressed in a world-aligned frame. This keeps the L/R alternating phase
            //   mathematics identical to the legacy path (sin(phase) × stepAngle) while
            //   ensuring the axis of rotation is always world-horizontal.

            Quaternion worldSwingRotation = Quaternion.AngleAxis(swingDeg, worldAxis);

            // Get the connected body's current world rotation to express the target in its frame.
            Quaternion connectedBodyRot = joint.connectedBody != null
                ? joint.connectedBody.rotation
                : Quaternion.identity;

            // Convert world-space rotation to connected-body local space.
            Quaternion localTarget = Quaternion.Inverse(connectedBodyRot) * worldSwingRotation;

            joint.targetRotation = localTarget;
        }

        /// <summary>
        /// Returns the world-space horizontal forward direction for gait animation.
        /// Uses the Hips Rigidbody's horizontal velocity as the primary source (accurate
        /// even when the torso is pitched). Falls back to CurrentMoveInput projected onto
        /// the XZ plane when velocity magnitude is below the threshold (e.g. start of motion).
        /// Returns <see cref="Vector3.zero"/> if neither source has a usable direction.
        /// </summary>
        private Vector3 GetWorldGaitForward(DesiredInput desiredInput, LocomotionObservation observation)
        {
            // DESIGN: Velocity is the best proxy for actual movement direction because it is
            // already in world space and naturally accounts for camera yaw, slopes, and any
            // forces applied by PlayerMovement.
            //
            // THRESHOLD CHANGE (bug fix): Lowered from 0.1 m/s to 0.05 m/s.
            // At 0.1 m/s the velocity path failed to trigger for most of the acceleration
            // ramp (frames 4-222 in the debug log, vel 0.08-3.83 m/s showed gaitFwd=0),
            // because physics velocity at the very start of motion is below 0.1 m/s and
            // the input fallback was not being reached aggressively enough. 0.05 m/s catches
            // motion 2× sooner and is still large enough to avoid noisy near-zero direction.
            //
            // INPUT FALLBACK CHANGE (bug fix): The input fallback is now checked BEFORE
            // the velocity threshold returns early. If move input magnitude > 0.01 and
            // velocity < threshold, the input direction is used immediately rather than
            // returning Vector3.zero. This eliminates the window at the start of motion
            // where velocity is non-negligible (> 0.05 m/s) but the input path was
            // returning zero because no fallback was attempted.
            const float VelocityThreshold = 0.05f;

            Vector3 horizontalVel = observation.PlanarVelocity;

            if (horizontalVel.magnitude >= VelocityThreshold)
            {
                return horizontalVel.normalized;
            }

            // Aggressive input fallback: if move input is non-negligible, use it immediately
            // rather than waiting for velocity to build up. This ensures gaitFwd is non-zero
            // on the very first frame of input and prevents the 'gaitFwd stays 0,0,0' bug
            // seen in frames 4–222 of the debug log.
            // CurrentMoveInput is a raw 2D input; without camera transform it maps X→world-X,
            // Y→world-Z. This is an approximation sufficient for tests and the zero-velocity
            // start-of-movement window. The velocity path takes over once the body is moving.
            if (desiredInput.HasMoveIntent)
            {
                return desiredInput.MoveWorldDirection;
            }

            return Vector3.zero;
        }

        /// <summary>
        /// Applies leg swing in the joint's local targetRotation frame (original behaviour).
        /// Used when <see cref="_useWorldSpaceSwing"/> is false, or as a fallback when a
        /// valid world-space gait direction cannot be computed.
        /// </summary>
        /// <param name="leftSwingDeg">Signed swing angle (degrees) for the left upper leg.</param>
        /// <param name="rightSwingDeg">Signed swing angle (degrees) for the right upper leg.</param>
        /// <param name="leftKneeBendDeg">Knee bend angle (degrees) for the left leg.</param>
        /// <param name="rightKneeBendDeg">Knee bend angle (degrees) for the right leg.</param>
        private void ApplyLocalSpaceSwing(
            float leftSwingDeg,
            float rightSwingDeg,
            float leftKneeBendDeg,
            float rightKneeBendDeg)
        {
            // DESIGN: Quaternion.AngleAxis with _swingAxis (default Vector3.forward / Z)
            //         is used because ConfigurableJoint.targetRotation maps the primary joint
            //         hinge (joint.axis = Vector3.right) to the Z component of the rotation
            //         in targetRotation space. Using X-axis (Quaternion.Euler(angle, 0, 0))
            //         produced lateral abduction (side-to-side wiggle) instead of the intended
            //         sagittal forward/backward swing that lifts the leg off the ground.
            if (_upperLegL != null)
            {
                _upperLegL.targetRotation = Quaternion.AngleAxis(leftSwingDeg, _swingAxis);
                _upperLegLTargetEuler = _upperLegL.targetRotation.eulerAngles;
            }
            else
            {
                Debug.LogError("[LegAnimator] ApplyLocalSpaceSwing: _upperLegL is null — targetRotation NOT applied to UpperLeg_L.");
            }

            if (_upperLegR != null)
            {
                _upperLegR.targetRotation = Quaternion.AngleAxis(rightSwingDeg, _swingAxis);
            }
            else
            {
                Debug.LogError("[LegAnimator] ApplyLocalSpaceSwing: _upperLegR is null — targetRotation NOT applied to UpperLeg_R.");
            }

            if (_lowerLegL != null)
            {
                _lowerLegL.targetRotation = Quaternion.AngleAxis(-leftKneeBendDeg, _kneeAxis);
                _lowerLegLTargetEuler = _lowerLegL.targetRotation.eulerAngles;
            }
            else
            {
                Debug.LogError("[LegAnimator] ApplyLocalSpaceSwing: _lowerLegL is null — targetRotation NOT applied to LowerLeg_L.");
            }

            if (_lowerLegR != null)
            {
                _lowerLegR.targetRotation = Quaternion.AngleAxis(-rightKneeBendDeg, _kneeAxis);
            }
            else
            {
                Debug.LogError("[LegAnimator] ApplyLocalSpaceSwing: _lowerLegR is null — targetRotation NOT applied to LowerLeg_R.");
            }
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
            Vector3 gf = GetWorldGaitForward(_commandDesiredInput, _commandObservation);

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
                $" swingAxis:{(_useWorldSpaceSwing ? _worldSwingAxis : _swingAxis).x:F2},{(_useWorldSpaceSwing ? _worldSwingAxis : _swingAxis).y:F2},{(_useWorldSpaceSwing ? _worldSwingAxis : _swingAxis).z:F2}" +
                $" recovering:{_isRecovering}" +
                $" stuckCtr:{_stuckFrameCounter}" +
                $" cooldown:{_recoveryCooldownFrameCounter}" +
                $" biasedFwd:{_isGaitBiasedForward}" +
                $" UL_targetEuler:{_upperLegLTargetEuler.x:F0},{_upperLegLTargetEuler.y:F0},{_upperLegLTargetEuler.z:F0}" +
                $" UL_actualEuler:{ulActual.x:F0},{ulActual.y:F0},{ulActual.z:F0}" +
                $" LL_targetEuler:{_lowerLegLTargetEuler.x:F0},{_lowerLegLTargetEuler.y:F0},{_lowerLegLTargetEuler.z:F0}" +
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
        /// Sets all four leg joint <c>targetRotation</c> values immediately to
        /// <see cref="Quaternion.identity"/>, removing any active gait pose and letting
        /// the joint's natural drive return the limb to its resting orientation.
        /// Called from both the idle branch and the Fallen/GettingUp early-exit path.
        /// </summary>
        private void SetAllLegTargetsToIdentity()
        {
            if (_upperLegL != null) { _upperLegL.targetRotation = Quaternion.identity; }
            if (_upperLegR != null) { _upperLegR.targetRotation = Quaternion.identity; }
            if (_lowerLegL != null) { _lowerLegL.targetRotation = Quaternion.identity; }
            if (_lowerLegR != null) { _lowerLegR.targetRotation = Quaternion.identity; }
        }

        // ── Airborne Spring Scaling (Phase 3F2) ────────────────────────────

        /// <summary>
        /// Captures the current <c>slerpDrive</c> from each leg ConfigurableJoint as the
        /// baseline. Called from <see cref="Start"/> after <see cref="RagdollSetup.Awake"/>
        /// has applied the authoritative spring/damper values, so the captured values
        /// accurately reflect the intended runtime stiffness.
        /// </summary>
        private void CaptureBaselineDrives()
        {
            if (_upperLegL != null) { _baselineUpperLegLDrive = _upperLegL.slerpDrive; }
            if (_upperLegR != null) { _baselineUpperLegRDrive = _upperLegR.slerpDrive; }
            if (_lowerLegL != null) { _baselineLowerLegLDrive = _lowerLegL.slerpDrive; }
            if (_lowerLegR != null) { _baselineLowerLegRDrive = _lowerLegR.slerpDrive; }
        }

        /// <summary>
        /// Applies a spring multiplier to all four leg joint SLERP drives.
        /// When <paramref name="multiplier"/> is less than 1, springs are reduced and legs
        /// go loose (airborne dangly). When multiplier is 1, baseline springs are restored.
        /// The damper is kept at the baseline value in all cases.
        /// </summary>
        /// <param name="multiplier">
        /// Fraction of baseline positionSpring to apply. 0 = fully limp; 1 = full stiffness.
        /// </param>
        private void SetLegSpringMultiplier(float multiplier)
        {
            // DESIGN: We rebuild the JointDrive struct each time because it is a value type —
            // modifying a copied field would not affect the joint. We multiply spring only,
            // keeping the damper at baseline so the joint still settles without oscillation.
            // GUARD: If baselines are zero (CaptureBaselineDrives not yet called — i.e. this
            // fires in the OnEnable→Start window), bail out silently. The correct springs are
            // already on the joints from RagdollSetup; don't overwrite them with zeros.
            if (_baselineUpperLegLDrive.positionSpring <= 0f)
            {
                return;
            }

            if (_upperLegL != null)
            {
                JointDrive drive = _baselineUpperLegLDrive;
                drive.positionSpring = _baselineUpperLegLDrive.positionSpring * multiplier;
                _upperLegL.slerpDrive = drive;
            }

            if (_upperLegR != null)
            {
                JointDrive drive = _baselineUpperLegRDrive;
                drive.positionSpring = _baselineUpperLegRDrive.positionSpring * multiplier;
                _upperLegR.slerpDrive = drive;
            }

            if (_lowerLegL != null)
            {
                JointDrive drive = _baselineLowerLegLDrive;
                drive.positionSpring = _baselineLowerLegLDrive.positionSpring * multiplier;
                _lowerLegL.slerpDrive = drive;
            }

            if (_lowerLegR != null)
            {
                JointDrive drive = _baselineLowerLegRDrive;
                drive.positionSpring = _baselineLowerLegRDrive.positionSpring * multiplier;
                _lowerLegR.slerpDrive = drive;
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
                SetLegSpringMultiplier(_airborneSpringMultiplier);
            }
            else if (previousState == CharacterStateType.Airborne)
            {
                // Exiting airborne (any landing — Standing, Moving, Fallen, GettingUp):
                // restore full spring stiffness.
                _isAirborne = false;
                SetLegSpringMultiplier(1f);
            }
        }
    }
}
