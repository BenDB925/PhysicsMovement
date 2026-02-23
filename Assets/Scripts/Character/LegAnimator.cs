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
    public class LegAnimator : MonoBehaviour
    {
        // ── Serialized Gait Fields ──────────────────────────────────────────

        [SerializeField, Range(0f, 60f)]
        [Tooltip("Peak forward/backward swing angle (degrees) for upper leg joints during gait. " +
                 "Controls the visible stride amplitude. Typical range: 15–35°.")]
        private float _stepAngle = 50.3f;

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

        [SerializeField, Range(0f, 180f)]
        [Tooltip("Suppress leg swing when hips are more than this many degrees from the input direction. " +
                 "Prevents leg tangle on sharp turns (e.g. 180° reversal). " +
                 "Keep high (≥90°) for smooth straight-line walking — lower values cause gait " +
                 "stalls when leg forces nudge the hips slightly off-axis mid-stride. Default 90°.")]
        private float _yawAlignThresholdDeg = 90f;

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

        /// <summary>Cooldown frames after recovery before stuck detector can fire again.</summary>
        private int _recoveryCooldownCounter;

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

        /// <summary>
        /// True while the stuck-leg recovery pose is being actively applied.
        /// Exposed for test verification; read-only at runtime.
        /// </summary>
        public bool IsRecovering => _isRecovering;

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
            // STEP 0: Reset _worldSwingAxis so it never retains a stale value from a
            //         previous frame. ApplyWorldSpaceSwing will set this to a fresh value
            //         when the world-space path executes; it remains Vector3.zero for
            //         all other paths (idle, fallen, local-space fallback).
            //         DESIGN: Without this reset, frames where the early-exit (fallen/idle)
            //         path runs would leave _worldSwingAxis at the last active-gait value,
            //         causing the debug log to show a frozen non-zero axis while gaitFwd
            //         shows zero — the "stale axis" bug seen in the runtime debug log.
            _worldSwingAxis = Vector3.zero;

            // STEP 1: Gate on missing dependencies — skip gracefully.
            if (_playerMovement == null || _characterState == null)
            {
                return;
            }

            // STEP 2: When the character is in a non-ambulatory state, reset all four
            //         leg joints to Quaternion.identity immediately and exit early.
            //         This covers both Fallen (limp) and GettingUp (recovery) states.
            //         Phase and smoothed input scale are also reset to zero so the next
            //         standing gait starts cleanly.
            CharacterStateType state = _characterState.CurrentState;
            if (state == CharacterStateType.Fallen || state == CharacterStateType.GettingUp)
            {
                _phase = 0f;
                _smoothedInputMag = 0f;
                SetAllLegTargetsToIdentity();
                return;
            }

            // STEP 3: Gate gait on move input OR actual horizontal velocity.
            //         We use input as a binary gate but also check real velocity so that
            //         legs continue to animate while the body coasts after key release.
            //         Without the velocity check, inputMagnitude drops to zero the frame
            //         the key is released, SetAllLegTargetsToIdentity() fires, and the
            //         legs snap to rest even though the body is still sliding forward.
            float inputMagnitude = _playerMovement.CurrentMoveInput.magnitude;

            float horizontalSpeedGate = 0f;
            if (_hipsRigidbody != null)
            {
                Vector3 hVelGate = _hipsRigidbody.linearVelocity;
                horizontalSpeedGate = new Vector3(hVelGate.x, 0f, hVelGate.z).magnitude;
            }

            bool isMoving = inputMagnitude > 0.01f || horizontalSpeedGate > 0.02f;

            // STEP 3c (Phase 3F2): Suppress gait phase advancement while airborne.
            //          Legs shouldn't keep cycling mid-air — force isMoving = false
            //          when CharacterState is Airborne. The spring scaling itself is
            //          handled reactively via OnCharacterStateChanged; this suppresses
            //          the gait cycle so legs don't flap mid-jump.
            if (_isAirborne)
            {
                isMoving = false;
            }

            // STEP 3d (Phase 3T — GAP-2 angular velocity gate):
            //          Suppress gait when the hips are spinning rapidly in yaw.
            //          High angular velocity means leg joint targets fight the rotational
            //          momentum and can cross over, tangling for 1–2 seconds post-spin.
            //          We use hysteresis to avoid premature re-engagement:
            //            • Suppress immediately when |angVel.y| > threshold.
            //            • Re-enable only after |angVel.y| < threshold * 0.5 for 5 consecutive frames.
            if (_hipsRigidbody != null)
            {
                float absAngVelY = Mathf.Abs(_hipsRigidbody.angularVelocity.y);
                if (absAngVelY > _angularVelocityGaitThreshold)
                {
                    isMoving = false;
                    _spinSuppressFrames = 0; // reset hysteresis counter while still spinning fast
                }
                else if (absAngVelY < _angularVelocityGaitThreshold * 0.5f)
                {
                    // Angular velocity is below the low hysteresis band — increment counter.
                    // Only re-enable gait once the counter reaches 5 consecutive frames.
                    if (_spinSuppressFrames < 5)
                    {
                        _spinSuppressFrames++;
                        isMoving = false; // still suppressed until hysteresis satisfied
                    }
                    // When _spinSuppressFrames >= 5, isMoving is determined by input/velocity above.
                }
                else
                {
                    // In the intermediate band (between threshold*0.5 and threshold): keep suppressed.
                    _spinSuppressFrames = 0;
                    isMoving = false;
                }
            }

            // STEP 3b-yaw: Yaw alignment gate removed (was comparing raw stick input against
            //              hips forward, which is incorrect with a camera-relative movement
            //              system — raw stick input is not a world direction). BC's yaw torque
            //              (with ±170° clamp) handles turning without this gate. The original
            //              concern about leg tangle on 180° turns is handled by BC committing
            //              to a rotation direction before reaching full stride.

            // STEP 3b: Phase reset on movement restart or sharp direction change.
            //          If we were stopped (smoothedInputMag near 0) and now have input,
            //          snap phase to 0 so legs restart from neutral — avoids launching
            //          into a large arc mid-stride and clipping the ground.
            //          Also reset on sharp direction change (dot < 0.5 = >60° turn).
            Vector2 currentInputDir = inputMagnitude > 0.01f
                ? _playerMovement.CurrentMoveInput.normalized
                : Vector2.zero;

            bool restarting = !_wasMoving && isMoving && _smoothedInputMag < 0.05f;
            bool sharpTurn  = _prevInputDir.sqrMagnitude > 0.01f
                && currentInputDir.sqrMagnitude > 0.01f
                && Vector2.Dot(_prevInputDir, currentInputDir) < 0.5f;

            if (restarting || sharpTurn)
            {
                _phase = 0f;
                _smoothedInputMag = 0f;
                SetAllLegTargetsToIdentity();  // snap joints to neutral immediately on phase reset
            }

            _prevInputDir = currentInputDir;
            _wasMoving    = isMoving;

            // ── STEP 3E: Stuck-Leg Recovery (Option D) ────────────────────────────────
            //   Detection: character is stuck when ALL of the following are true for
            //   _stuckFrameThreshold consecutive frames:
            //     - SmoothedInputMag > 0.5 (actively trying to move)
            //     - horizontalSpeedGate < _stuckSpeedThreshold (not actually moving)
            //     - CharacterState is Standing or Moving (not Fallen/GettingUp/Airborne)
            //   Recovery: drive both UpperLeg joints to forward-split pose (AngleAxis(-30f, _swingAxis))
            //   with spring multiplier _recoverySpringMultiplier for _recoveryFrames frames,
            //   then restore spring and resume normal gait.

            bool stateAllowsRecovery = state == CharacterStateType.Standing ||
                                       state == CharacterStateType.Moving;

            if (!_isRecovering)
            {
                // Measure how much velocity is actually in the intended direction of travel.
                // Raw horizontal speed fails at corners — hips slide sideways so speed > threshold
                // even though the character isn't making forward progress.
                float forwardProgress = 0f;
                if (_hipsRigidbody != null && currentInputDir.sqrMagnitude > 0.01f)
                {
                    // Convert 2D input direction to world-space forward vector (XZ plane).
                    Vector3 worldInputDir = new Vector3(currentInputDir.x, 0f, currentInputDir.y).normalized;
                    Vector3 hipsVel       = _hipsRigidbody.linearVelocity;
                    Vector3 hipsVelFlat   = new Vector3(hipsVel.x, 0f, hipsVel.z);
                    forwardProgress       = Vector3.Dot(hipsVelFlat, worldInputDir);
                }

                // Stuck = trying to move but making no forward progress in the input direction.
                bool stuckCondition = _smoothedInputMag > 0.5f
                    && forwardProgress < _stuckSpeedThreshold
                    && stateAllowsRecovery
                    && _recoveryCooldownCounter <= 0;  // don't re-fire immediately after recovery

                if (stuckCondition)
                {
                    _stuckFrameCounter++;
                }
                else
                {
                    _stuckFrameCounter = 0;
                }

                // Tick down the post-recovery cooldown each frame regardless.
                if (_recoveryCooldownCounter > 0) _recoveryCooldownCounter--;

                // Trigger recovery when stuck long enough.
                if (_stuckFrameCounter >= _stuckFrameThreshold && stateAllowsRecovery)
                {
                    _isRecovering = true;
                    _recoveryFrameCounter = _recoveryFrames;
                    _stuckFrameCounter = 0;
                    SetLegSpringMultiplier(_recoverySpringMultiplier);
                }
            }

            if (_isRecovering)
            {
                // Apply forward-split recovery pose to both UpperLeg joints.
                Quaternion recoveryPose = Quaternion.AngleAxis(-30f, _swingAxis);
                if (_upperLegL != null) { _upperLegL.targetRotation = recoveryPose; }
                if (_upperLegR != null) { _upperLegR.targetRotation = recoveryPose; }

                _recoveryFrameCounter--;
                if (_recoveryFrameCounter <= 0)
                {
                    // Recovery complete: restore spring, start cooldown, resume normal gait.
                    _isRecovering = false;
                    _stuckFrameCounter = 0;
                    _recoveryCooldownCounter = _recoveryFrames * 2; // cooldown = 2× recovery duration
                    SetLegSpringMultiplier(1f);
                }

                // Skip normal gait this frame — recovery pose is already applied.
                return;
            }

            // ────────────────────────────────────────────────────────────────────────────────
            if (isMoving)
            {
                // STEP 4a: MOVING — advance phase accumulator based on actual speed.
                //          effectiveCycles = max(_stepFrequency, horizontalSpeed × _stepFrequencyScale)
                //          This ensures:
                //            • At idle (zero velocity, non-zero input) we get _stepFrequency cycles/sec
                //              (default 0 = stationary legs until the body actually moves).
                //            • At 2 m/s with scale 1.5 → 3 cycles/sec.
                //            • Legs never outrun or lag the body.
                //          Re-use horizontalSpeedGate computed above (same value, already cached).
                float effectiveCyclesPerSec = Mathf.Max(_stepFrequency, horizontalSpeedGate * _stepFrequencyScale);
                _phase += effectiveCyclesPerSec * 2f * Mathf.PI * Time.fixedDeltaTime;

                // Wrap phase to [0, 2π) to prevent float overflow over time.
                if (_phase >= 2f * Mathf.PI)
                {
                    _phase -= 2f * Mathf.PI;
                }

                // STEP 4b: Ramp up the smoothed input magnitude toward the actual value.
                //          This is the anti-pop mechanism: instead of the gait amplitude
                //          snapping to full value on frame 1 of resumed movement, it ramps
                //          up smoothly. We use inputMagnitude (not speed) as the target here
                //          so the ramp works even at zero actual velocity (starting to push).
                float t = Mathf.Clamp01(_idleBlendSpeed * Time.fixedDeltaTime);
                // Use whichever is higher — input magnitude or normalised velocity — as the
                // amplitude target. This ensures legs animate at full amplitude when coasting
                // with no key held, matching the visual expectation that moving = legs move.
                float velocityMag01 = Mathf.Clamp01(horizontalSpeedGate / 2f); // normalise ~0–1 at 2 m/s max
                float amplitudeTarget = Mathf.Max(inputMagnitude, velocityMag01);
                _smoothedInputMag = Mathf.Lerp(_smoothedInputMag, amplitudeTarget, t);

                // STEP 5: Compute sinusoidal upper-leg swing angles with optional lift boost.
                //         Left leg uses phase directly; right leg is offset by π (half-cycle)
                //         so they always swing in opposite directions — the alternating gait.
                //         We scale amplitude by _smoothedInputMag to get the anti-pop ramp.
                //
                //         _upperLegLiftBoost adds extra upward bias to the forward-swinging leg
                //         (whichever has sin > 0). This biases the knee toward the chest rather
                //         than simply swinging the leg forward flat, for a high, deliberate stride.
                float sinL = Mathf.Sin(_phase);
                float sinR = Mathf.Sin(_phase + Mathf.PI);

                float liftBoostL = sinL > 0f ? sinL * _upperLegLiftBoost * _smoothedInputMag : 0f;
                float liftBoostR = sinR > 0f ? sinR * _upperLegLiftBoost * _smoothedInputMag : 0f;

                float leftSwingDeg  = sinL * _stepAngle * _smoothedInputMag + liftBoostL;
                float rightSwingDeg = sinR * _stepAngle * _smoothedInputMag + liftBoostR;

                // STEP 6: Compute lower-leg knee-bend angle.
                //         The knee holds a constant positive bend during gait so the character
                //         looks dynamically flexed rather than stiff-legged.
                float kneeBendDeg = _kneeAngle * _smoothedInputMag;

                // STEP 7: Apply computed rotations to joint targetRotations.
                if (_useWorldSpaceSwing)
                {
                    ApplyWorldSpaceSwing(leftSwingDeg, rightSwingDeg, kneeBendDeg);
                }
                else
                {
                    ApplyLocalSpaceSwing(leftSwingDeg, rightSwingDeg, kneeBendDeg);
                }
            }
            else
            {
                // STEP 4b: IDLE — set joints to identity, decay phase, and snap smoothed scale.
                //
                //          JOINT RESET: Set all four joint targetRotations directly to
                //          Quaternion.identity every idle frame.  The joint's internal spring
                //          drive then smoothly returns the physical limb to its rest angle.
                //
                //          PHASE DECAY: Move the phase toward 0 by a fixed amount per frame.
                //          At speed 5 and dt 0.01 the phase shrinks by ~0.16 rad/frame,
                //          clearing a full cycle (2π ≈ 6.28 rad) in ~40 frames / 0.4 s.
                //          This ensures that when input next resumes, the phase is near 0 and
                //          the first computed rotation is small, reducing the visual pop even
                //          further on top of the smoothed-input ramp.
                //
                //          SMOOTH SCALE DECAY: Lerp _smoothedInputMag toward 0 rather than
                //          snapping. This prevents mid-stride leg snap when key is released —
                //          legs finish their current arc naturally before returning to rest.
                float decayStep = _idleBlendSpeed * Mathf.PI * Time.fixedDeltaTime;
                _phase = Mathf.Max(0f, _phase - decayStep);
                float decayT = Mathf.Clamp01(_idleBlendSpeed * Time.fixedDeltaTime);
                _smoothedInputMag = Mathf.Lerp(_smoothedInputMag, 0f, decayT);

                // Only reset joints to identity once smoothed magnitude is negligible.
                if (_smoothedInputMag < 0.01f)
                {
                    _smoothedInputMag = 0f;
                    SetAllLegTargetsToIdentity();
                }
                else
                {
                    // Continue driving joints at decaying amplitude so legs land smoothly.
                    float sinL = Mathf.Sin(_phase);
                    float sinR = Mathf.Sin(_phase + Mathf.PI);
                    float liftBoostL = sinL > 0f ? sinL * _upperLegLiftBoost * _smoothedInputMag : 0f;
                    float liftBoostR = sinR > 0f ? sinR * _upperLegLiftBoost * _smoothedInputMag : 0f;
                    float leftSwingDeg  = sinL * _stepAngle * _smoothedInputMag + liftBoostL;
                    float rightSwingDeg = sinR * _stepAngle * _smoothedInputMag + liftBoostR;
                    float kneeBendDeg   = _kneeAngle * _smoothedInputMag;
                    ApplyLocalSpaceSwing(leftSwingDeg, rightSwingDeg, kneeBendDeg);
                }
            }

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

        // ── Private Methods ──────────────────────────────────────────────────

        /// <summary>
        /// Applies world-space swing targets to the four leg ConfigurableJoints.
        /// The swing axis is computed from the character's actual movement direction (world
        /// horizontal velocity or move-input fallback), ensuring legs step forward in world
        /// space even when the torso is pitched forward.
        /// </summary>
        /// <param name="leftSwingDeg">Signed swing angle (degrees) for the left upper leg.</param>
        /// <param name="rightSwingDeg">Signed swing angle (degrees) for the right upper leg.</param>
        /// <param name="kneeBendDeg">Knee bend angle (degrees, positive = forward flex).</param>
        private void ApplyWorldSpaceSwing(float leftSwingDeg, float rightSwingDeg, float kneeBendDeg)
        {
            // STEP A: Determine the world-space gait-forward direction.
            //         Primary: use the Hips Rigidbody's horizontal velocity (most accurate
            //         proxy for actual movement direction regardless of torso pitch).
            //         Fallback: project CurrentMoveInput (XZ) when velocity is near zero
            //         (e.g. just starting to move).
            Vector3 gaitForward = GetWorldGaitForward();

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
                ApplyLocalSpaceSwing(leftSwingDeg, rightSwingDeg, kneeBendDeg);
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
            ApplyWorldSpaceJointTarget(_lowerLegL, -kneeBendDeg, worldSwingAxis);
            if (_lowerLegL != null) { _lowerLegLTargetEuler = _lowerLegL.targetRotation.eulerAngles; }
            ApplyWorldSpaceJointTarget(_lowerLegR, -kneeBendDeg, worldSwingAxis);
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
        private Vector3 GetWorldGaitForward()
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

            Vector3 horizontalVel = Vector3.zero;
            if (_hipsRigidbody != null)
            {
                horizontalVel = new Vector3(
                    _hipsRigidbody.linearVelocity.x,
                    0f,
                    _hipsRigidbody.linearVelocity.z);
            }

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
            if (_playerMovement != null)
            {
                Vector2 moveInput = _playerMovement.CurrentMoveInput;
                if (moveInput.magnitude > 0.01f)
                {
                    Vector3 inputDir = new Vector3(moveInput.x, 0f, moveInput.y);
                    return inputDir.normalized;
                }
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
        /// <param name="kneeBendDeg">Knee bend angle (degrees).</param>
        private void ApplyLocalSpaceSwing(float leftSwingDeg, float rightSwingDeg, float kneeBendDeg)
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
                _lowerLegL.targetRotation = Quaternion.AngleAxis(-kneeBendDeg, _kneeAxis);
                _lowerLegLTargetEuler = _lowerLegL.targetRotation.eulerAngles;
            }
            else
            {
                Debug.LogError("[LegAnimator] ApplyLocalSpaceSwing: _lowerLegL is null — targetRotation NOT applied to LowerLeg_L.");
            }

            if (_lowerLegR != null)
            {
                _lowerLegR.targetRotation = Quaternion.AngleAxis(-kneeBendDeg, _kneeAxis);
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
            Vector3 gf = GetWorldGaitForward();

            Vector3 ulActual = _upperLegL != null ? _upperLegL.transform.localEulerAngles : Vector3.zero;
            Vector3 llActual = _lowerLegL != null ? _lowerLegL.transform.localEulerAngles : Vector3.zero;

            string line =
                $"FRAME:{Time.frameCount}" +
                $" vel:{velMag:F2}" +
                $" gaitFwd:{gf.x:F2},{gf.y:F2},{gf.z:F2}" +
                $" swingAxis:{(_useWorldSpaceSwing ? _worldSwingAxis : _swingAxis).x:F2},{(_useWorldSpaceSwing ? _worldSwingAxis : _swingAxis).y:F2},{(_useWorldSpaceSwing ? _worldSwingAxis : _swingAxis).z:F2}" +
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
