using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Applies a Proportional-Derivative (PD) torque to the ragdoll Hips Rigidbody each
    /// FixedUpdate to keep the character upright and facing the desired direction.
    /// Reads ground state from two <see cref="GroundSensor"/> components found in the
    /// ragdoll hierarchy (expected on sibling or child foot GameObjects). The
    /// fallen state is tracked via angle thresholds and exposed through
    /// <see cref="IsFallen"/> as a gameplay signal, while torque can continue
    /// to support recovery behavior.
    /// Attach to the Hips (root) GameObject of the PlayerRagdoll prefab.
    /// Lifecycle: Awake → FixedUpdate.
    /// Collaborators: <see cref="RagdollSetup"/>, <see cref="GroundSensor"/>.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class BalanceController : MonoBehaviour
    {
        // TUNING LOG:
        // - Character stands still: ✓
        // - 200 N push → recovers within 3 s: ✗ (pending in-editor verification)
        // - 800 N push → IsFallen = true: ✗ (pending in-editor verification)
        // - Lift off ground → IsGrounded = false, balance torque reduces: ✗ (pending in-editor verification)

        // ─── Serialised Fields ──────────────────────────────────────────────
        [Header("Upright PD Gains")]        [SerializeField, Range(0f, 5000f)]
        [Tooltip("Proportional gain for the upright (pitch + roll) correction torque. " +
                 "Higher = snappier recovery, lower = softer wobble. " +
                 "Only affects pitch and roll axes; yaw is controlled by _kPYaw separately. " +
                 "Default 2000: high enough to resist forward lean from move force (raised from 800).")]
        private float _kP = 2000f;

        [SerializeField, Range(0f, 500f)]
        [Tooltip("Derivative gain for the upright (pitch + roll) damping term. " +
                 "Increase if the character oscillates, decrease if it is too sluggish. " +
                 "Only affects pitch and roll axes. " +
                 "Default 200: maintains 10:1 P/D ratio with _kP = 2000 (raised from 80).")]
        private float _kD = 200f;

        [Header("Yaw Control")]
        [SerializeField, Range(0f, 2000f)]
        [Tooltip("Proportional gain for the yaw correction torque (rotation around world Y). " +
                 "Controls how quickly the character turns to face the desired direction. " +
                 "Airborne multiplier does NOT apply to yaw torque.")]
        private float _kPYaw = 80f;

        [SerializeField, Range(0f, 500f)]
        [Tooltip("Derivative gain for yaw damping. Prevents the character from spinning " +
                 "past the target facing direction. " +
                 "Airborne multiplier does NOT apply to yaw torque.")]
        private float _kDYaw = 60f;

        [SerializeField, Range(0f, 10f)]
        [Tooltip("Minimum yaw error in degrees before any yaw torque is applied. " +
                 "Suppresses micro-oscillation when the character is already nearly facing " +
                 "the target direction (Phase 3D2). Typical value: 1–3 degrees.")]
        private float _yawDeadZoneDeg = 2f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Multiplier applied to upright (pitch + roll) PD torque while airborne. " +
                 "Lower values reduce in-air correction and preserve floppy feel. " +
                 "This multiplier does NOT affect yaw torque.")]
        private float _airborneMultiplier = 0.2f;

        [Header("Snap Recovery")]
        [SerializeField, Range(0.3f, 1f)]
        [Tooltip("Minimum kD multiplier at the instant of a sharp direction change. " +
                 "Temporarily reduces over-damping so the character corrects post-snap " +
                 "lean faster (closer to critical damping). Decays linearly to 1.0.")]
        private float _snapRecoveryKdScale = 0.6f;

        [SerializeField, Range(0.2f, 1f)]
        [Tooltip("Minimum COM stabilization damping multiplier at the instant of a " +
                 "sharp direction change. Reduces velocity drag so the character " +
                 "re-accelerates faster in the new direction. Decays linearly to 1.0.")]
        private float _snapRecoveryComDampScale = 0.4f;

        [SerializeField, Range(0.3f, 1f)]
        [Tooltip("Minimum COM stabilization spring multiplier during snap recovery. " +
                 "Reduces the positional correction force so the character can develop " +
                 "the forward lean needed for efficient locomotion sooner.")]
        private float _snapRecoveryComSpringScale = 1.0f;

        [SerializeField, Range(0f, 200f)]
        [Tooltip("Supplementary forward force (Newtons) applied in the movement direction " +
                 "during snap recovery. Compensates for the braking effect of the upright " +
                 "correction torque during re-acceleration. Decays linearly to zero.")]
        private float _snapRecoveryForceBoost = 0f;

        [SerializeField, Range(10, 150)]
        [Tooltip("FixedUpdate frames over which COM damping reductions remain " +
                 "active after a sharp direction change. The reduction stays " +
                 "constant for most of the window and fades out over the last 10 frames.")]
        private int _snapRecoveryDurationFrames = 130;

        [SerializeField, Range(10, 150)]
        [Tooltip("FixedUpdate frames over which kD reduction remains active after " +
                 "a sharp direction change. Shorter than the main duration so the " +
                 "character regains full stability before the next turn.")]
        private int _snapRecoveryKdDurationFrames = 100;

        [Header("Fallen Thresholds")]
        [SerializeField, Range(0f, 90f)]
        [Tooltip("World-up deviation in degrees at which the character enters the fallen state. " +
             "Use a higher value than exit threshold to avoid chatter near the boundary.")]
        private float _fallenEnterAngleThreshold = 65f;

        [SerializeField, Range(0f, 90f)]
        [Tooltip("World-up deviation in degrees at which the character exits the fallen state. " +
             "Use a lower value than enter threshold to provide hysteresis.")]
        private float _fallenExitAngleThreshold = 55f;

         [Header("Surrender Threshold")]
         [SerializeField, Range(0f, 90f)]
         [Tooltip("World-up deviation in degrees that must persist for two FixedUpdate frames " +
               "before surrender fires and balance stops fighting the fall.")]
         private float _surrenderAngleThreshold = 80f;

         [SerializeField, Range(0f, 90f)]
         [Tooltip("Lower angle threshold used with tilt-direction angular velocity to detect " +
               "momentum-driven blow-through before the extreme-angle gate is reached.")]
         private float _surrenderAnglePlusMomentumThreshold = 65f;

         [SerializeField, Range(0f, 20f)]
         [Tooltip("Minimum tilt-direction angular velocity (rad/s) that combines with the " +
               "angle-plus-momentum threshold to trigger surrender.")]
         private float _surrenderAngularVelocityThreshold = 3f;

          [SerializeField, Range(0f, 1f)]
          [Tooltip("Duration in seconds used by ClearSurrender() to restore the local support scales.")]
          private float _clearSurrenderRampDuration = 0.35f;

        [Header("Startup Stand Assist")]
        [SerializeField]
        [Tooltip("Applies extra upward support while grounded during the first seconds after landing " +
                 "to help recover from seated startup poses.")]
        private bool _enableStartupStandAssist = false;

        [SerializeField, Range(0f, 5f)]
        [Tooltip("How long after first ground contact the startup stand assist is allowed to run.")]
        private float _startupStandAssistDuration = 4f;

        [SerializeField]
        [Tooltip("Keeps a reduced stand assist active after startup while grounded and below target height.")]
        private bool _enablePersistentSeatedRecovery = false;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Reduced assist strength used after the startup phase for seated recovery.")]
        private float _persistentSeatedRecoveryAssistScale = 0.35f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Minimum persistent assist scale while seated/fallen so lift remains physically meaningful.")]
        private float _persistentSeatedRecoveryMinAssistScale = 0.55f;

        [SerializeField, Range(0f, 2f)]
        [Tooltip("Target minimum hips world height for startup stand assist. " +
                 "Assist applies only when current height is below this value.")]
        private float _startupAssistTargetHeight = 0.35f;

        [SerializeField, Range(0f, 2000f)]
        [Tooltip("Maximum upward force (Newtons) applied by startup stand assist.")]
        private float _startupStandAssistForce = 1200f;

        [SerializeField, Range(0.05f, 1f)]
        [Tooltip("Height error range used to scale stand assist force from 0 to max.")]
        private float _startupAssistHeightRange = 0.15f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("How strongly assist force follows current hips-up direction (0 = world up, 1 = hips up).")]
        private float _startupAssistUseBodyUp = 0.4f;

        [SerializeField, Range(0f, 5f)]
        [Tooltip("When upward velocity reaches this value, stand assist is fully damped out.")]
        private float _startupAssistMaxRiseSpeed = 2f;

        [SerializeField, Range(1f, 4f)]
        [Tooltip("Temporary spring multiplier applied to leg joints during startup stand assist.")]
        private float _startupLegSpringMultiplier = 2.25f;

        [SerializeField, Range(1f, 4f)]
        [Tooltip("Temporary damper multiplier applied to leg joints during startup stand assist.")]
        private float _startupLegDamperMultiplier = 1.75f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Fraction of startup assist force routed through leg rigidbodies (rest is applied at hips).")]
        private float _startupAssistLegForceFraction = 0.8f;

        [Header("LegAnimator Cooperation")]
        [SerializeField]
        [Tooltip("When true and a LegAnimator component exists on this GameObject, " +
                 "BalanceController will NOT apply direct forces or modify SLERP drives on the four " +
                 "leg joints (UpperLeg_L, UpperLeg_R, LowerLeg_L, LowerLeg_R). " +
                 "LegAnimator owns those joints exclusively; BalanceController owns Hips/Spine/torso. " +
                 "This eliminates the fighting-systems bug where BC forces override LA targetRotation. " +
                 "Disable only for debugging purposes.")]
        private bool _deferLegJointsToAnimator = true;
        [Header("Debug")]        [SerializeField]
        [Tooltip("Log state transitions (Standing ↔ Fallen) to the console. " +
                 "Disable in production builds.")]
        private bool _debugStateTransitions = false;

        [SerializeField]
        [Tooltip("Logs throttled runtime recovery telemetry including hips height and assist values.")]
        private bool _debugRecoveryTelemetry = false;

        [SerializeField, Range(0.1f, 2f)]
        [Tooltip("Seconds between recovery telemetry logs while running.")]
        private float _debugRecoveryTelemetryInterval = 0.25f;

        [SerializeField, Range(0f, 2f)]
        [Tooltip("Hips height below this threshold is considered seated for telemetry.")]
        private float _debugSeatedHeightThreshold = 0.25f;

        // ─── COM Stabilization ──────────────────────────────────────────────

        [Header("COM-over-Feet Stabilization")]
        [SerializeField]
        [Tooltip("When grounded, applies a horizontal force to keep the hips centered above the feet. " +
                 "This is essential for preventing topple when the PD torque alone cannot maintain balance.")]
        private bool _enableComStabilization = true;

        [SerializeField, Range(0f, 1000f)]
        [Tooltip("Horizontal spring strength pulling hips toward the feet midpoint. " +
                 "Higher = snappier COM correction, lower = more wobbly.")]
        private float _comStabilizationStrength = 400f;

        [SerializeField, Range(0f, 200f)]
        [Tooltip("Horizontal velocity damping for COM stabilization. Reduces oscillation.")]
        private float _comStabilizationDamping = 60f;

        [SerializeField, Range(0f, 0.5f)]
        [Tooltip("Maximum horizontal COM target shift (meters) when DesiredLeanDegrees is at its peak. " +
                 "The shift nudges the stabilization target toward the facing direction during turns.")]
        private float _maxComLeanOffset = 0.09f;

        // ─── Pelvis Expression ────────────────────────────────────────────────

        [Header("Pelvis Expression")]
        [SerializeField, Range(0f, 10f)]
        [Tooltip("Maximum forward/backward pelvis tilt in degrees driven by horizontal " +
                 "acceleration. Positive acceleration tilts the pelvis forward; deceleration " +
                 "tilts it backward. Blended by LegAnimator.SmoothedInputMag so it is zero at idle.")]
        private float _pelvisTiltMaxDeg = 3f;

        [SerializeField, Range(1f, 30f)]
        [Tooltip("Smoothing speed for the pelvis tilt signal. Higher = more responsive, " +
                 "lower = more gradual and cinematic.")]
        private float _pelvisTiltSmoothing = 8f;

        [SerializeField, Range(0f, 0.05f)]
        [Tooltip("Maximum lateral COM target shift (meters) toward the stance foot during " +
                 "single-support phases. Creates a visible hip sway toward the planted leg. " +
                 "Blended by LegAnimator.SmoothedInputMag so it is zero at idle.")]
        private float _pelvisSwayMaxOffset = 0.015f;

        [SerializeField, Range(1f, 30f)]
        [Tooltip("Smoothing speed for the lateral pelvis sway signal. Higher = more responsive, " +
                 "lower = more gradual transitions between stance legs.")]
        private float _pelvisSwaySmoothing = 8f;

        // ─── Accel/Decel Expression ──────────────────────────────────────────

        [Header("Accel/Decel Expression")]
        [SerializeField, Range(0f, 15f)]
        [Tooltip("Forward lean impulse in degrees applied when transitioning from " +
                 "Standing to Moving. Decays linearly to zero over the configured decay duration.")]
        private float _accelStartLeanDeg = 5f;

        [SerializeField, Range(0.05f, 1f)]
        [Tooltip("Duration in seconds for the start-walk forward lean to decay back to zero.")]
        private float _accelStartLeanDecay = 0.3f;

        [SerializeField, Range(0f, 10f)]
        [Tooltip("Backward lean impulse in degrees applied when transitioning from " +
                 "Moving to Standing. Decays linearly to zero over the configured decay duration.")]
        private float _decelStopLeanDeg = 3f;

        [SerializeField, Range(0.05f, 1f)]
        [Tooltip("Duration in seconds for the stop-walk backward lean to decay back to zero.")]
        private float _decelStopLeanDecay = 0.2f;

        // ─── Height Maintenance ─────────────────────────────────────────────

        [Header("Height Maintenance")]
        [SerializeField]
        [Tooltip("When grounded, applies upward force to keep hips at standing height. " +
                 "Prevents settling into a seated pose.")]
        private bool _enableHeightMaintenance = true;

        [SerializeField, Range(0.1f, 2f)]
        [Tooltip("Target world-space Y position for the hips when standing. " +
                 "Height force only applies when hips are below this value.")]
        private float _standingHipsHeight = 0.35f;

        [SerializeField, Range(0f, 3000f)]
        [Tooltip("Spring strength for height maintenance. Higher = faster lift from seated.")]
        private float _heightMaintenanceStrength = 1500f;

        [SerializeField, Range(0f, 300f)]
        [Tooltip("Vertical velocity damping for height maintenance. Prevents bouncing.")]
        private float _heightMaintenanceDamping = 120f;

        // ─── Private Fields ──────────────────────────────────────────────────

        private Rigidbody _rb;
        private CharacterState _characterState;
        private RagdollSetup _ragdollSetup;

        /// <summary>Left-foot GroundSensor, located in Awake via component search.</summary>
        private GroundSensor _footL;

        /// <summary>Right-foot GroundSensor, located in Awake via component search.</summary>
        private GroundSensor _footR;

        /// <summary>Left-foot Rigidbody, cached from the GroundSensor's GameObject for step-up lift assist.</summary>
        private Rigidbody _footRbL;

        /// <summary>Right-foot Rigidbody, cached from the GroundSensor's GameObject for step-up lift assist.</summary>
        private Rigidbody _footRbR;

        /// <summary>
        /// Desired world-space rotation (yaw + upright). Updated via
        /// <see cref="SetFacingDirection"/>. Defaults to forward-facing upright.
        /// </summary>
        private Quaternion _targetFacingRotation = Quaternion.identity;

        private BodySupportCommand _currentBodySupportCommand;

        /// <summary>Cached fallen state from the previous FixedUpdate for transition logging.</summary>
        private bool _wasFallen;

        /// <summary>
        /// Tracks whether at least one foot has touched ground since spawn.
        /// Used to avoid startup lockout while the ragdoll is still in initial drop.
        /// </summary>
        private bool _hasBeenGrounded;

        /// <summary>
        /// Time spent grounded since first ground contact. Used to gate startup stand assist.
        /// </summary>
        private float _groundedTimeSinceFirstContact;

        private StartupStandAssist _assist;
        private float _nextRecoveryTelemetryTime;

        /// <summary>Cached LegAnimator for reading SmoothedInputMag in pelvis expression.</summary>
        private LegAnimator _legAnimator;

        /// <summary>Smoothed forward speed used as a baseline to detect acceleration/deceleration.</summary>
        private float _smoothedForwardSpeed;

        /// <summary>Smoothed pelvis tilt in degrees (positive = forward lean).</summary>
        private float _smoothedPelvisTiltDeg;

        /// <summary>Smoothed lateral pelvis sway offset vector (world-space horizontal).</summary>
        private Vector3 _smoothedPelvisSwayOffset;

        /// <summary>Transient lean impulse magnitude in degrees (positive = forward). Set by state-change events.</summary>
        private float _transientLeanDeg;

        /// <summary>Remaining decay time for the current transient lean impulse.</summary>
        private float _transientLeanTimer;

        /// <summary>Total decay duration for the current transient lean impulse.</summary>
        private float _transientLeanDecay;

        /// <summary>True once we have subscribed to <see cref="CharacterState.OnStateChanged"/>.</summary>
        private bool _subscribedToCharacterState;

        /// <summary>
        /// True when a <see cref="LegAnimator"/> is found on the same GameObject and
        /// <see cref="_deferLegJointsToAnimator"/> is true. Cached in Awake to avoid
        /// per-frame GetComponent calls.
        /// </summary>
        private bool _hasLegAnimator;

        private int _surrenderExtremeAngleFrameCount;
        private bool _suppressPelvisExpression;
        private float _surrenderCooldownTimer;
    private SupportScaleRamp _uprightStrengthRamp;
    private SupportScaleRamp _heightMaintenanceRamp;
    private SupportScaleRamp _stabilizationRamp;

        // ─── Public Properties ────────────────────────────────────────────────

        /// <summary>
        /// True when at least one foot's <see cref="GroundSensor"/> reports ground contact.
        /// Updated every FixedUpdate.
        /// </summary>
        public bool IsGrounded { get; private set; }

        /// <summary>
        /// True when the Hips deviation from world-up exceeds
        /// <c>_fallenEnterAngleThreshold</c> and remains true until it drops below
        /// <c>_fallenExitAngleThreshold</c>.
        /// Updated every FixedUpdate.
        /// </summary>
        public bool IsFallen { get; private set; }

        /// <summary>
        /// Current deviation in degrees between the Hips up-axis and world up.
        /// Updated every FixedUpdate. 0 = perfectly upright, 90 = horizontal.
        /// </summary>
        public float UprightAngle { get; private set; }

        /// <summary>
        /// True once surrender has disabled the character's balance support systems.
        /// </summary>
        public bool IsSurrendered { get; private set; }

        /// <summary>
        /// Cached surrender severity in the 0–1 range for downstream knockdown systems.
        /// </summary>
        public float SurrenderSeverity { get; private set; }

        /// <summary>
        /// Monotonically increasing counter incremented on every
        /// <see cref="TriggerSurrender"/> call, even when already surrendered.
        /// Allows observers to detect re-knockdown while <see cref="IsSurrendered"/>
        /// is still true (e.g. an impact during GettingUp).
        /// </summary>
        public int SurrenderTriggerCount { get; private set; }

        /// <summary>
        /// Local multiplier layered on top of <see cref="BodySupportCommand.UprightStrengthScale"/>.
        /// </summary>
        public float UprightStrengthScale { get; private set; } = 1f;

        /// <summary>
        /// Local multiplier layered on top of <see cref="BodySupportCommand.HeightMaintenanceScale"/>.
        /// </summary>
        public float HeightMaintenanceScale { get; private set; } = 1f;

        /// <summary>
        /// Local multiplier layered on top of <see cref="BodySupportCommand.StabilizationStrengthScale"/>.
        /// </summary>
        public float StabilizationScale { get; private set; } = 1f;

        /// <summary>
        /// True while the snap recovery window is active after a sharp direction change.
        /// Used by <see cref="PlayerMovement"/> to keep movement forces alive during
        /// brief falls caused by aggressive turns.
        /// </summary>
        public bool IsInSnapRecovery => _currentBodySupportCommand.RecoveryBlend > 0.0001f;

        internal int SnapRecoveryDurationFrames => _snapRecoveryDurationFrames;

        internal int SnapRecoveryKdDurationFrames => _snapRecoveryKdDurationFrames;

        /// <summary>
        /// Target world-space Y position for the hips when the character is standing upright.
        /// Read by <see cref="LocomotionDirector"/> to compute height-deficit-based
        /// <see cref="BodySupportCommand.HeightMaintenanceScale"/> values.
        /// </summary>
        public float StandingHipsHeight => _standingHipsHeight;

        /// <summary>
        /// Test seam: directly override IsGrounded/IsFallen without needing GroundSensor components.
        /// FixedUpdate will not overwrite these values while the override is active.
        /// Do not call this from production code.
        /// </summary>
        public void SetGroundStateForTest(bool isGrounded, bool isFallen)
        {
            IsGrounded = isGrounded;
            IsFallen = isFallen;
            _overrideGroundState = true;
        }

        private bool _overrideGroundState;

        // ─── Public Methods ───────────────────────────────────────────────────

        /// <summary>
        /// Legacy test seam: sets the desired world-space facing direction via a
        /// <see cref="BodySupportCommand.PassThrough"/> wrapper. Production code now
        /// uses <see cref="SetBodySupportCommand"/> via <see cref="LocomotionDirector"/>
        /// instead.
        /// A zero or near-zero vector is silently ignored — the previous facing is kept.
        /// </summary>
        /// <param name="dir">Desired facing direction in world space (Y component is ignored).</param>
        [System.Obsolete("Use SetBodySupportCommand via LocomotionDirector instead. Kept for test compatibility.")]
        public void SetFacingDirection(Vector3 dir)
        {
            _currentBodySupportCommand = BodySupportCommand.PassThrough(dir);
            UpdateTargetFacingRotation(dir);
        }

        internal void SetBodySupportCommand(BodySupportCommand command)
        {
            _currentBodySupportCommand = command;
            UpdateTargetFacingRotation(command.FacingDirection);
        }

        internal void ClearBodySupportCommand()
        {
            Vector3 fallbackFacing = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (fallbackFacing.sqrMagnitude < 0.001f)
            {
                fallbackFacing = Vector3.forward;
            }

            _currentBodySupportCommand = BodySupportCommand.PassThrough(fallbackFacing.normalized);
            UpdateTargetFacingRotation(_currentBodySupportCommand.FacingDirection);
        }

        /// <summary>
        /// Disables the local balance support scales so the character stops resisting an unrecoverable fall.
        /// </summary>
        /// <param name="severity">Knockdown severity in the 0–1 range.</param>
        public void TriggerSurrender(float severity)
        {
            float clampedSeverity = Mathf.Clamp01(severity);
            SurrenderSeverity = Mathf.Max(SurrenderSeverity, clampedSeverity);
            SurrenderTriggerCount++;

            if (IsSurrendered)
            {
                return;
            }

            CancelAllRamps();
            IsSurrendered = true;
            UprightStrengthScale = 0f;
            HeightMaintenanceScale = 0f;
            StabilizationScale = 0f;
            _suppressPelvisExpression = true;
            _surrenderExtremeAngleFrameCount = 0;

            _smoothedPelvisTiltDeg = 0f;
            _smoothedPelvisSwayOffset = Vector3.zero;
            _transientLeanDeg = 0f;
            _transientLeanTimer = 0f;
            _transientLeanDecay = 0f;

            if (_ragdollSetup != null)
            {
                _ragdollSetup.SetSpringProfile(0.25f, 0.25f, 0.25f, 0.15f);
            }
        }

        /// <summary>
        /// Clears the surrendered flag and restores the local balance support scales with a deterministic ramp.
        /// </summary>
        public void ClearSurrender()
        {
            IsSurrendered = false;
            SurrenderSeverity = 0f;
            _suppressPelvisExpression = false;
            _surrenderExtremeAngleFrameCount = 0;

            // Prevent immediate re-trigger while the character transitions through
            // extreme angles during the stand-up sequence (GettingUp → Airborne → etc.).
            _surrenderCooldownTimer = 0.5f;

            RampUprightStrength(1f, _clearSurrenderRampDuration);
            RampHeightMaintenance(1f, _clearSurrenderRampDuration);
            RampStabilization(1f, _clearSurrenderRampDuration);

            if (_ragdollSetup != null)
            {
                _ragdollSetup.ResetSpringProfile(0f);
            }
        }

        /// <summary>
        /// Smoothly changes the local upright-strength multiplier over a fixed-duration ramp.
        /// </summary>
        /// <param name="targetScale">Target multiplier applied on top of the support command.</param>
        /// <param name="duration">Ramp duration in seconds. Zero snaps immediately.</param>
        public void RampUprightStrength(float targetScale, float duration)
        {
            _uprightStrengthRamp = CreateScaleRamp(UprightStrengthScale, targetScale, duration, out float nextValue);
            UprightStrengthScale = nextValue;
        }

        /// <summary>
        /// Smoothly changes the local height-maintenance multiplier over a fixed-duration ramp.
        /// </summary>
        /// <param name="targetScale">Target multiplier applied on top of the support command.</param>
        /// <param name="duration">Ramp duration in seconds. Zero snaps immediately.</param>
        public void RampHeightMaintenance(float targetScale, float duration)
        {
            _heightMaintenanceRamp = CreateScaleRamp(
                HeightMaintenanceScale,
                targetScale,
                duration,
                out float nextValue);
            HeightMaintenanceScale = nextValue;
        }

        /// <summary>
        /// Smoothly changes the local COM-stabilization multiplier over a fixed-duration ramp.
        /// </summary>
        /// <param name="targetScale">Target multiplier applied on top of the support command.</param>
        /// <param name="duration">Ramp duration in seconds. Zero snaps immediately.</param>
        public void RampStabilization(float targetScale, float duration)
        {
            _stabilizationRamp = CreateScaleRamp(StabilizationScale, targetScale, duration, out float nextValue);
            StabilizationScale = nextValue;
        }

        /// <summary>
        /// Stops all active local support-scale ramps and snaps each scale to its current ramp target.
        /// </summary>
        public void CancelAllRamps()
        {
            UprightStrengthScale = CancelScaleRamp(UprightStrengthScale, ref _uprightStrengthRamp);
            HeightMaintenanceScale = CancelScaleRamp(HeightMaintenanceScale, ref _heightMaintenanceRamp);
            StabilizationScale = CancelScaleRamp(StabilizationScale, ref _stabilizationRamp);
        }

        private void UpdateTargetFacingRotation(Vector3 dir)
        {
            Vector3 flatDir = new Vector3(dir.x, 0f, dir.z);
            if (flatDir.sqrMagnitude < 0.001f)
            {
                return;
            }

            _targetFacingRotation = Quaternion.LookRotation(flatDir.normalized, Vector3.up);
        }

        // ─── Unity Lifecycle ──────────────────────────────────────────────────

        private void Awake()
        {
            // STEP 1: Cache the Rigidbody on this GameObject (guaranteed by RequireComponent).
            _rb = GetComponent<Rigidbody>();
            TryGetComponent(out _ragdollSetup);

            Vector3 currentForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (currentForward.sqrMagnitude < 0.001f)
            {
                currentForward = Vector3.forward;
            }
            _targetFacingRotation = Quaternion.LookRotation(currentForward.normalized, Vector3.up);

            // STEP 2: Find the two GroundSensor components anywhere in the child hierarchy.
            //         RagdollBuilder attaches them to Foot_L and Foot_R.
            GroundSensor[] sensors = GetComponentsInChildren<GroundSensor>(includeInactive: true);
            if (sensors.Length > 0)
            {
                for (int i = 0; i < sensors.Length; i++)
                {
                    GroundSensor sensor = sensors[i];
                    string sensorName = sensor.gameObject.name;
                    if (_footL == null && sensorName == "Foot_L")
                    {
                        _footL = sensor;
                    }
                    else if (_footR == null && sensorName == "Foot_R")
                    {
                        _footR = sensor;
                    }
                }

                if (_footL == null)
                {
                    _footL = sensors[0];
                }

                if (_footR == null)
                {
                    _footR = sensors.Length > 1 ? sensors[1] : sensors[0];
                }
            }

            if (_footL != null && _footR != null && ReferenceEquals(_footL, _footR))
            {
                // Tolerate a single sensor (e.g., in unit tests or partial prefabs).
                Debug.LogWarning($"[BalanceController] '{name}': only one GroundSensor found. " +
                                 "Both feet will share the same sensor.", this);
            }

            if (_footL == null && _footR == null)
            {
                Debug.LogWarning($"[BalanceController] '{name}': no GroundSensor found in " +
                                 "children. IsGrounded will always be false.", this);
            }
            else if (_footL == null || _footR == null)
            {
                Debug.LogWarning($"[BalanceController] '{name}': only one named foot sensor " +
                                 "was resolved. Ground checks may be less reliable.", this);
            }

            if (_footL != null)
                _footRbL = _footL.GetComponent<Rigidbody>();
            if (_footR != null)
                _footRbR = _footR.GetComponent<Rigidbody>();

            _assist = new StartupStandAssist();
            _assist.Initialize(transform);

            // Cache whether a LegAnimator sibling is present. When true (and
            // _deferLegJointsToAnimator is enabled), we skip all direct forces/drive
            // modifications on the four leg joints so LegAnimator owns them exclusively.
            _hasLegAnimator = _deferLegJointsToAnimator && TryGetComponent(out _legAnimator);

            TryGetComponent(out _characterState);
            SubscribeToCharacterState();
            ClearBodySupportCommand();
        }

        private void OnDestroy()
        {
            if (_subscribedToCharacterState && _characterState != null)
                _characterState.OnStateChanged -= OnCharacterStateChanged;
        }

        private void SubscribeToCharacterState()
        {
            if (_subscribedToCharacterState || _characterState == null) return;
            _characterState.OnStateChanged += OnCharacterStateChanged;
            _subscribedToCharacterState = true;
        }

        private void OnCharacterStateChanged(CharacterStateType previous, CharacterStateType next)
        {
            if (previous == CharacterStateType.Standing && next == CharacterStateType.Moving)
            {
                _transientLeanDeg = _accelStartLeanDeg;
                _transientLeanTimer = _accelStartLeanDecay;
                _transientLeanDecay = _accelStartLeanDecay;
            }
            else if (previous == CharacterStateType.Moving && next == CharacterStateType.Standing)
            {
                _transientLeanDeg = -_decelStopLeanDeg;
                _transientLeanTimer = _decelStopLeanDecay;
                _transientLeanDecay = _decelStopLeanDecay;
            }
        }

        private void OnValidate()
        {
            if (_fallenExitAngleThreshold > _fallenEnterAngleThreshold)
            {
                _fallenExitAngleThreshold = _fallenEnterAngleThreshold;
            }

            _surrenderAngleThreshold = Mathf.Clamp(_surrenderAngleThreshold, 0f, 90f);
            _surrenderAnglePlusMomentumThreshold = Mathf.Clamp(
                _surrenderAnglePlusMomentumThreshold,
                0f,
                _surrenderAngleThreshold);
            _surrenderAngularVelocityThreshold = Mathf.Max(0f, _surrenderAngularVelocityThreshold);
            _clearSurrenderRampDuration = Mathf.Max(0f, _clearSurrenderRampDuration);

            _startupStandAssistDuration = Mathf.Max(0f, _startupStandAssistDuration);
            _startupAssistHeightRange = Mathf.Max(0.05f, _startupAssistHeightRange);
            _startupAssistMaxRiseSpeed = Mathf.Max(0.05f, _startupAssistMaxRiseSpeed);
            _startupAssistLegForceFraction = Mathf.Clamp01(_startupAssistLegForceFraction);
            _persistentSeatedRecoveryAssistScale = Mathf.Clamp01(_persistentSeatedRecoveryAssistScale);
            _persistentSeatedRecoveryMinAssistScale = Mathf.Clamp01(_persistentSeatedRecoveryMinAssistScale);
            _debugRecoveryTelemetryInterval = Mathf.Max(0.1f, _debugRecoveryTelemetryInterval);
            _debugSeatedHeightThreshold = Mathf.Max(0f, _debugSeatedHeightThreshold);
            // Phase 3D2: dead zone must be non-negative; 0 disables the guard (all errors fire).
            _yawDeadZoneDeg = Mathf.Max(0f, _yawDeadZoneDeg);
        }

        private void FixedUpdate()
        {
            // DESIGN: Override precedence chain (C5.4)
            //  1. LocomotionDirector publishes a BodySupportCommand each FixedUpdate (exec order 250).
            //  2. BalanceController consumes the command at exec order 0 (one-frame delay).
            //  3. Command fields modulate PD gains, height maintenance, commanded lean,
            //     COM lean, and yaw intent.
            //  4. Local angle-based IsFallen and CharacterState provide safety gates:
            //     - IsFallen: fast angle-based hysteresis (responds within one frame).
            //     - CharacterState: authoritative FSM with transition timing and get-up windows.
            //     Both gates suppress yaw torque independently so neither alone is a single
            //     point of failure.
            //  5. Snap recovery blends (RecoveryBlend, RecoveryKdBlend) and airborne multiplier
            //     further modulate the torques computed from the command.
            //  SetFacingDirection() is the legacy test seam that predates the command path;
            //  production code now uses SetBodySupportCommand() exclusively.

            if (_characterState == null)
            {
                TryGetComponent(out _characterState);
                SubscribeToCharacterState();
            }

            UpdateScaleRamps();

            if (IsSurrendered &&
                _characterState != null &&
                _characterState.CurrentState == CharacterStateType.Fallen)
            {
                UprightStrengthScale = 0f;
                HeightMaintenanceScale = 0f;
                StabilizationScale = 0f;
                _suppressPelvisExpression = true;
            }

            // STEP 1: Update ground state from foot sensors (unless overridden by test seam).
            if (!_overrideGroundState)
            {
                bool leftGrounded  = _footL != null && _footL.IsGrounded;
                bool rightGrounded = _footR != null && _footR.IsGrounded;
                IsGrounded = leftGrounded || rightGrounded;
            }

            // Hips-height proxy: treat character as "effectively grounded" when hips
            // are close to or below standing height.  This provides a safety net when
            // the GroundSensor SphereCast fails (e.g. scene ground on wrong layer).
            bool effectivelyGrounded = IsGrounded ||
                                       _rb.position.y < _standingHipsHeight + 0.1f;

            if (IsGrounded || effectivelyGrounded)
            {
                if (!_hasBeenGrounded)
                {
                    _hasBeenGrounded = true;
                    _groundedTimeSinceFirstContact = 0f;
                }
                else
                {
                    _groundedTimeSinceFirstContact += Time.fixedDeltaTime;
                }
            }

            // STEP 2: Measure how far the Hips' up-axis deviates from world-up.
            //         Vector3.Angle always returns 0–180°, so this is safe for all poses.
            Quaternion currentRot = _rb.rotation;
            Vector3 currentUp = currentRot * Vector3.up;
            float uprightAngle = Vector3.Angle(currentUp, Vector3.up);
            UprightAngle = uprightAngle;

            // STEP 3: Update fallen state with hysteresis (unless overridden by test seam).
            bool nowFallen;
            if (_overrideGroundState)
            {
                nowFallen = IsFallen; // preserve test-injected value
            }
            else
            {
                nowFallen = IsFallen;
                if (!IsFallen)
                {
                    nowFallen = uprightAngle > _fallenEnterAngleThreshold;
                }
                else
                {
                    nowFallen = uprightAngle > _fallenExitAngleThreshold;
                }
            }

            if (_debugStateTransitions && nowFallen != _wasFallen)
            {
                string transition = nowFallen ? "Standing → Fallen" : "Fallen → Standing";
                Debug.Log($"[BalanceController] '{name}': {transition} " +
                          $"(angle = {uprightAngle:F1}°, enter = {_fallenEnterAngleThreshold}°, " +
                          $"exit = {_fallenExitAngleThreshold}°)");
            }
            IsFallen  = nowFallen;
            _wasFallen = nowFallen;

            // STEP 3.5: Apply startup stand assist while grounded and low.
            // HeightMaintenanceScale from the director command gates and modulates the
            // assist force so the locomotion plan controls height recovery authority.
            bool startupAssistActive = false;
            float startupAssistScale = 0f;
            bool persistentRecoveryActive = false;
            float commandHeightScale = _currentBodySupportCommand.HeightMaintenanceScale * HeightMaintenanceScale;

            if (_enableStartupStandAssist &&
                commandHeightScale > 0f &&
                IsGrounded &&
                _hasBeenGrounded)
            {
                float heightError = _startupAssistTargetHeight - _rb.position.y;
                if (heightError > 0f)
                {
                    float heightScale = Mathf.Clamp01(heightError / _startupAssistHeightRange);
                    bool seatedOrFallen = IsFallen || _rb.position.y < (_startupAssistTargetHeight - _startupAssistHeightRange * 0.35f);
                    float minimumPersistentScale = 0f;
                    if (_enablePersistentSeatedRecovery)
                    {
                        float weightBasedScale = 0f;
                        if (_startupStandAssistForce > 0f && _assist.TotalBodyMass > 0f)
                        {
                            weightBasedScale = (_assist.TotalBodyMass * Physics.gravity.magnitude) / _startupStandAssistForce;
                        }

                        minimumPersistentScale = seatedOrFallen
                            ? Mathf.Max(_persistentSeatedRecoveryMinAssistScale, weightBasedScale * 1.15f)
                            : _persistentSeatedRecoveryAssistScale;
                        minimumPersistentScale = Mathf.Clamp01(minimumPersistentScale);
                    }

                    float timeScale;
                    if (_groundedTimeSinceFirstContact <= _startupStandAssistDuration)
                    {
                        float startup01 = (_startupStandAssistDuration <= 0f)
                            ? 1f
                            : Mathf.Clamp01(_groundedTimeSinceFirstContact / _startupStandAssistDuration);
                        float postStartupScale = _enablePersistentSeatedRecovery
                            ? minimumPersistentScale
                            : 0f;
                        timeScale = Mathf.Lerp(1f, postStartupScale, startup01);
                    }
                    else
                    {
                        timeScale = _enablePersistentSeatedRecovery
                            ? minimumPersistentScale
                            : 0f;
                    }

                    persistentRecoveryActive = _enablePersistentSeatedRecovery &&
                                               _groundedTimeSinceFirstContact > _startupStandAssistDuration &&
                                               minimumPersistentScale > 0f;

                    float riseSpeedScale = 1f - Mathf.Clamp01(_rb.linearVelocity.y / _startupAssistMaxRiseSpeed);
                    float assistScale = heightScale * timeScale * riseSpeedScale;

                    if (assistScale > 0f)
                    {
                        startupAssistActive = true;
                        startupAssistScale = assistScale;

                        Vector3 assistDirection = Vector3.Slerp(Vector3.up, _rb.transform.up, _startupAssistUseBodyUp).normalized;
                        float assistForce = _startupStandAssistForce * assistScale * commandHeightScale;
                        _assist.ApplyForces(_rb, assistDirection, assistForce, _startupAssistLegForceFraction, _hasLegAnimator);
                    }
                }
            }

            _assist.ApplyLegDrive(
                startupAssistActive ? startupAssistScale : 0f,
                _startupLegSpringMultiplier,
                _startupLegDamperMultiplier,
                _hasLegAnimator);

            float hipsHeight = _rb.position.y;
            float heightErrorForTelemetry = Mathf.Max(0f, _startupAssistTargetHeight - hipsHeight);
            LogRecoveryTelemetry(
                hipsHeight,
                heightErrorForTelemetry,
                startupAssistScale,
                startupAssistActive,
                persistentRecoveryActive,
                uprightAngle);

            // STEP 3.6: COM-over-feet horizontal stabilization.
            // DESIGN: The COM target is always the feet midpoint. The director controls
            // stabilization gain via StabilizationStrengthScale and can request a small
            // horizontal shift via DesiredLeanDegrees to let the character lean into turns.
            // If future step-planning work needs an explicit target position, add a
            // ComTargetOffset field to BodySupportCommand rather than computing it here.
            if (_enableComStabilization && effectivelyGrounded && _footL != null && _footR != null)
            {
                Vector3 feetCenter = (_footL.transform.position + _footR.transform.position) * 0.5f;

                // STEP 3.6b: Shift the stabilization target toward the facing direction
                // when the director requests lean (e.g. during turns).
                float leanDegrees = _currentBodySupportCommand.DesiredLeanDegrees;
                if (leanDegrees > 0f && _maxComLeanOffset > 0f)
                {
                    float leanFraction = Mathf.Clamp01(leanDegrees / 15f);
                    Vector3 facingXZ = Vector3.ProjectOnPlane(
                        _currentBodySupportCommand.FacingDirection, Vector3.up);
                    if (facingXZ.sqrMagnitude > 0.0001f)
                    {
                        feetCenter += facingXZ.normalized * (leanFraction * _maxComLeanOffset);
                    }
                }

                // STEP 3.6c: Lateral pelvis sway during single-support.
                // Shifts the COM target toward the stance foot when only one foot is
                // grounded, creating a visible hip sway toward the planted leg.
                // Scaled by SmoothedInputMag so it disappears at idle.
                {
                    Vector3 swayTarget = Vector3.zero;
                    if (_pelvisSwayMaxOffset > 0f && _legAnimator != null && !IsFallen && !_suppressPelvisExpression)
                    {
                        bool leftDown = _footL.IsGrounded;
                        bool rightDown = _footR.IsGrounded;
                        if (leftDown != rightDown)
                        {
                            Vector3 stancePos = leftDown
                                ? _footL.transform.position
                                : _footR.transform.position;
                            Vector3 toStance = stancePos - feetCenter;
                            toStance.y = 0f;
                            if (toStance.sqrMagnitude > 0.0001f)
                            {
                                swayTarget = toStance.normalized *
                                    (_pelvisSwayMaxOffset * _legAnimator.SmoothedInputMag);
                            }
                        }
                    }
                    _smoothedPelvisSwayOffset = Vector3.Lerp(
                        _smoothedPelvisSwayOffset, swayTarget,
                        Time.fixedDeltaTime * _pelvisSwaySmoothing);
                    feetCenter += _smoothedPelvisSwayOffset;
                }

                Vector3 hipsPos = _rb.position;
                Vector3 horizontalOffset = new Vector3(
                    hipsPos.x - feetCenter.x,
                    0f,
                    hipsPos.z - feetCenter.z);
                Vector3 horizontalVel = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z);

                // STEP 3.6a: Reduce COM spring and damping during snap recovery.
                // Spring reduction allows the character to develop the forward lean
                // that is the natural running equilibrium. Damping reduction allows
                // faster re-acceleration in the new direction.
                float recoveryBlend = _currentBodySupportCommand.RecoveryBlend;
                float stabilizationScale = _currentBodySupportCommand.StabilizationStrengthScale * StabilizationScale;
                float comSpringMul = Mathf.Lerp(1f, _snapRecoveryComSpringScale, recoveryBlend) * stabilizationScale;
                float comDampMul = Mathf.Lerp(1f, _snapRecoveryComDampScale, recoveryBlend) * stabilizationScale;

                Vector3 comForce = -horizontalOffset * (_comStabilizationStrength * comSpringMul)
                                   - horizontalVel * (_comStabilizationDamping * comDampMul);
                _rb.AddForce(comForce, ForceMode.Force);
            }

            // STEP 3.7: Height maintenance.
            // When near ground, pushes hips up toward standing height to prevent the
            // character from settling into a seated basin-of-attraction.
            // HeightMaintenanceScale from the director command modulates the force
            // so the locomotion plan can boost or suppress height recovery.
            // Ground-relative offset: when a grounded foot reports a contact point
            // above world-origin (step-up, raised platform), the target rises by the
            // highest grounded contact height so the character physically climbs.
            // Step-up anticipation: when either foot detects a forward obstruction,
            // the estimated step height is added preemptively so the body rises
            // before the swing foot reaches the face, giving the foot arc clearance.
            if (_enableHeightMaintenance && effectivelyGrounded)
            {
                float groundContactHeight = 0f;
                if (_footL != null && _footL.IsGrounded)
                    groundContactHeight = Mathf.Max(groundContactHeight, _footL.GroundPoint.y);
                if (_footR != null && _footR.IsGrounded)
                    groundContactHeight = Mathf.Max(groundContactHeight, _footR.GroundPoint.y);

                float anticipatedStepHeight = 0f;
                if (_footL != null && _footL.HasForwardObstruction)
                    anticipatedStepHeight = Mathf.Max(anticipatedStepHeight, _footL.EstimatedStepHeight);
                if (_footR != null && _footR.HasForwardObstruction)
                    anticipatedStepHeight = Mathf.Max(anticipatedStepHeight, _footR.EstimatedStepHeight);

                float hipsHeightError = (_standingHipsHeight + groundContactHeight + anticipatedStepHeight) - _rb.position.y;
                if (hipsHeightError > 0f)
                {
                    float heightScale = commandHeightScale;
                    float heightForce = hipsHeightError * (_heightMaintenanceStrength * heightScale)
                                        - _rb.linearVelocity.y * (_heightMaintenanceDamping * heightScale);
                    heightForce = Mathf.Max(0f, heightForce);
                    _rb.AddForce(Vector3.up * heightForce, ForceMode.Force);
                }
            }

            // STEP 3.7b: Step-up foot lift assist.
            // When the character is grounded and a forward obstruction is detected,
            // apply a direct upward spring-damper force to each non-grounded (swing)
            // foot Rigidbody. This supplements the joint drive target angles by
            // physically lifting the foot box collider above the step face before
            // the spring-based joint drives converge to the clearance target.
            // Also applies a forward carry-over force once the foot is near step
            // height so it clears the lip rather than getting pinned by friction.
            // A grace period continues the assist briefly after ground contact is
            // lost, preventing oscillation stalls at step edges where the foot
            // SphereCast loses contact while resting on the step surface.
            Vector3 stepClimbDirection = Vector3.zero;
            float maxDetectedStepHeight = 0f;
            if (IsGrounded)
            {
                if (_footL != null && _footL.HasForwardObstruction)
                    maxDetectedStepHeight = Mathf.Max(maxDetectedStepHeight, _footL.EstimatedStepHeight);
                if (_footR != null && _footR.HasForwardObstruction)
                    maxDetectedStepHeight = Mathf.Max(maxDetectedStepHeight, _footR.EstimatedStepHeight);

                if (maxDetectedStepHeight > 0f)
                {
                    float groundRef = 0f;
                    if (_footL != null && _footL.IsGrounded)
                        groundRef = Mathf.Max(groundRef, _footL.GroundPoint.y);
                    if (_footR != null && _footR.IsGrounded)
                        groundRef = Mathf.Max(groundRef, _footR.GroundPoint.y);

                    // Planar forward direction for foot carry-over force.
                    stepClimbDirection = Vector3.ProjectOnPlane(_rb.transform.forward, Vector3.up);
                    if (stepClimbDirection.sqrMagnitude > 0.0001f)
                        stepClimbDirection.Normalize();
                    else
                        stepClimbDirection = Vector3.zero;

                    float clearance = maxDetectedStepHeight + StepUpColliderClearance;
                    float targetFootY = groundRef + clearance;
                    ApplyStepUpFootLiftAssist(_footRbL, _footL, targetFootY, stepClimbDirection, clearance);
                    ApplyStepUpFootLiftAssist(_footRbR, _footR, targetFootY, stepClimbDirection, clearance);

                    // Cache parameters and prime the grace timer so assists persist
                    // briefly after ground contact is lost at the step edge.
                    _stepAssistCachedTargetY = targetFootY;
                    _stepAssistCachedClimbDir = stepClimbDirection;
                    _stepAssistCachedClearance = clearance;
                    _stepAssistGraceTimer = StepAssistGracePeriod;
                }
            }
            else if (_stepAssistGraceTimer > 0f)
            {
                _stepAssistGraceTimer -= Time.fixedDeltaTime;
                ApplyStepUpFootLiftAssist(_footRbL, _footL, _stepAssistCachedTargetY,
                    _stepAssistCachedClimbDir, _stepAssistCachedClearance);
                ApplyStepUpFootLiftAssist(_footRbR, _footR, _stepAssistCachedTargetY,
                    _stepAssistCachedClimbDir, _stepAssistCachedClearance);
            }

            // ─── STEP 3.8: Apply director-owned snap recovery boost ─────────────────
            Vector3 supportTravelDirection = _currentBodySupportCommand.TravelDirection;
            if (_currentBodySupportCommand.RecoveryBlend > 0f &&
                _snapRecoveryForceBoost > 0f &&
                supportTravelDirection.sqrMagnitude > 0.001f &&
                uprightAngle < 12f)
            {
                _rb.AddForce(
                    supportTravelDirection.normalized * (_snapRecoveryForceBoost * _currentBodySupportCommand.RecoveryBlend),
                    ForceMode.Force);
            }

            // ─── STEP 3.9: Pelvis expression — acceleration-driven tilt ────────────
            // Compare instantaneous forward speed against a smoothed baseline to
            // detect acceleration (speed rising → forward tilt) or deceleration
            // (speed dropping → backward tilt). This speed-delta approach is much
            // smoother than raw frame-by-frame velocity differentiation in a ragdoll.
            float pelvisTiltTarget = 0f;
            if (_pelvisTiltMaxDeg > 0f && _legAnimator != null && !IsFallen && effectivelyGrounded && !_suppressPelvisExpression)
            {
                Vector3 hipsHorizontalVel = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z);
                Vector3 facingXZ = Vector3.ProjectOnPlane(
                    _currentBodySupportCommand.FacingDirection, Vector3.up);
                if (facingXZ.sqrMagnitude > 0.001f)
                {
                    float forwardSpeed = Vector3.Dot(hipsHorizontalVel, facingXZ.normalized);
                    // Smooth baseline tracks the running average of forward speed.
                    _smoothedForwardSpeed = Mathf.Lerp(_smoothedForwardSpeed, forwardSpeed,
                        Time.fixedDeltaTime * _pelvisTiltSmoothing * 0.5f);
                    float speedDelta = forwardSpeed - _smoothedForwardSpeed;
                    float normalizedDelta = Mathf.Clamp(speedDelta / 1.5f, -1f, 1f);
                    pelvisTiltTarget = normalizedDelta * _pelvisTiltMaxDeg * _legAnimator.SmoothedInputMag;
                }
            }
            _smoothedPelvisTiltDeg = Mathf.Lerp(_smoothedPelvisTiltDeg, pelvisTiltTarget,
                Time.fixedDeltaTime * _pelvisTiltSmoothing);

            // ─── STEP 3.10: Transient accel/decel expression lean ───────────────
            // Decays a one-shot forward (or backward) lean impulse set by
            // OnCharacterStateChanged. Added to the pelvis tilt in STEP 4.
            float transientLeanDeg = 0f;
            if (_transientLeanTimer > 0f)
            {
                _transientLeanTimer -= Time.fixedDeltaTime;
                float t = Mathf.Clamp01(_transientLeanTimer / Mathf.Max(0.001f, _transientLeanDecay));
                transientLeanDeg = _transientLeanDeg * t;
            }

            // ─── STEP 4: Compute upright (pitch + roll) torque ─────────────────
            // We isolate the pitch/roll error by comparing the current Hips up-vector
            // to world up. We use Quaternion.FromToRotation to get the shortest-arc
            // rotation from current-up to world-up, then extract axis-angle.
            // The axis is in world space and lies in the horizontal plane (XZ) for a
            // pure pitch/roll error.
            //
            // Airborne multiplier applies here only (not to yaw).
            //
            // When IsFallen, torque continues to support recovery (ragdoll can get back
            // up via the same correction forces). The GettingUp state (Phase 3C3) may
            // disable balance entirely during a controlled get-up animation, but that is
            // a separate concern.
            // STEP 4a: Layer the director-owned lean command on top of the local
            // expressive pelvis tilt so sprint/turn posture and local expression stack
            // additively instead of overriding one another.
            float totalPelvisTilt = _smoothedPelvisTiltDeg
                                    + transientLeanDeg
                                    + _currentBodySupportCommand.DesiredLeanDegrees;
            Vector3 uprightTarget = Vector3.up;
            if (Mathf.Abs(totalPelvisTilt) > 0.01f)
            {
                Vector3 tiltFacing = Vector3.ProjectOnPlane(
                    _currentBodySupportCommand.FacingDirection, Vector3.up);
                if (tiltFacing.sqrMagnitude > 0.001f)
                {
                    Vector3 tiltAxis = Vector3.Cross(Vector3.up, tiltFacing.normalized);
                    uprightTarget = Quaternion.AngleAxis(totalPelvisTilt, tiltAxis) * Vector3.up;
                }
            }
            Quaternion uprightError = Quaternion.FromToRotation(currentUp, uprightTarget);

            uprightError.ToAngleAxis(out float uprightAngleDeg, out Vector3 uprightAxis);
            if (uprightAngleDeg > 180f) uprightAngleDeg -= 360f;

            // Damp only the pitch/roll (XZ) component of angular velocity to avoid
            // coupling the derivative term with the separate yaw damping below.
            Vector3 angVel = _rb.angularVelocity;
            Vector3 pitchRollAngVel = new Vector3(angVel.x, 0f, angVel.z);
            float effectiveUprightScale = _currentBodySupportCommand.UprightStrengthScale * UprightStrengthScale;

            if (uprightAxis.sqrMagnitude > 0.001f && effectiveUprightScale > 0f)
            {
                // STEP 4a: Reduce kD during snap recovery to bring the over-damped
                // prefab tuning closer to critical damping.  This lets the character
                // correct the post-snap lean faster without the sluggish response that
                // an over-damped PD produces.  kP stays at full strength for stability.
                // Uses its own shorter duration so the character regains full damping
                // before the next turn, preventing the sustained kD reduction from
                // causing falls.  The reduction is gated on lean angle: once the
                // character leans past 40 degrees, full kD is restored to prevent
                // the reduced damping from tipping it into the Fallen state.
                float effectiveKd = _kD;
                if (_currentBodySupportCommand.RecoveryKdBlend > 0f && Mathf.Abs(uprightAngleDeg) < 40f)
                {
                    effectiveKd *= Mathf.Lerp(1f, _snapRecoveryKdScale, _currentBodySupportCommand.RecoveryKdBlend);
                }

                float uprightRad = uprightAngleDeg * Mathf.Deg2Rad;
                Vector3 uprightTorque =
                    (_kP * effectiveUprightScale) * uprightRad * uprightAxis
                    - effectiveKd * pitchRollAngVel;

                float uprightMultiplier = _hasBeenGrounded
                    ? (effectivelyGrounded ? 1f : _airborneMultiplier)
                    : 1f;
                _rb.AddTorque(uprightTorque * uprightMultiplier, ForceMode.Force);
            }

            // Suppress surrender detection while the character is actively getting up
            // or shortly after ClearSurrender was called. Extreme angles are expected
            // during the stand-up transition; re-triggering surrender here would trap
            // the character in an endless dwell→impulse→re-surrender loop.
            if (_surrenderCooldownTimer > 0f)
            {
                _surrenderCooldownTimer -= Time.fixedDeltaTime;
                _surrenderExtremeAngleFrameCount = 0;
            }
            else if (!IsSurrendered)
            {
                if (uprightAngle > _surrenderAngleThreshold)
                {
                    _surrenderExtremeAngleFrameCount++;
                }
                else
                {
                    _surrenderExtremeAngleFrameCount = 0;
                }

                float tiltDirectionalAngularVelocity = GetTiltDirectionalAngularVelocity(currentUp, pitchRollAngVel);
                bool extremeAngleSurrender = _surrenderExtremeAngleFrameCount >= 2;
                bool momentumSurrender = uprightAngle > _surrenderAnglePlusMomentumThreshold &&
                                         tiltDirectionalAngularVelocity > _surrenderAngularVelocityThreshold;
                if (extremeAngleSurrender || momentumSurrender)
                {
                    TriggerSurrender(ComputeSurrenderSeverity(uprightAngle, tiltDirectionalAngularVelocity, hipsHeight));
                }
            }

            // ─── STEP 5: Compute yaw torque ────────────────────────────────────
            // Extract the yaw error: the signed angle around world Y from the current
            // Hips forward direction (projected onto XZ) to the desired facing direction.
            //
            // We compute this independently from upright error so that changing the
            // facing direction never introduces roll instability.
            //
            // Phase 3D2 hardening:
            //   (a) Both vectors are explicitly normalized before SignedAngle to prevent
            //       any floating-point imprecision from a near-unit vector entering the
            //       angle computation. The sqrMagnitude guard below ensures normalization
            //       is safe (no zero-vector normalize).
            //   (b) A dead zone (_yawDeadZoneDeg) suppresses torque for small errors,
            //       preventing micro-oscillation near the target facing direction.
            //   (c) SetFacingDirection already retains the last valid direction on zero
            //       input (guarded by sqrMagnitude check), so no extra guard is needed here.
            //
            // Yaw torque is only applied when NOT fallen: when the character is severely
            // tilted or upside-down, the horizontal projection of the forward vector is
            // unreliable and can produce a spurious 180° yaw error that fights recovery.
            // While fallen or in a confirmed locomotion collapse, we rely solely on the
            // upright torque to assist self-recovery.
            //
            // Airborne multiplier does NOT apply to yaw — the character should always
            // be able to turn in air.
            if (_characterState == null)
            {
                TryGetComponent(out _characterState);
                SubscribeToCharacterState();
            }

            // DESIGN: Dual gate is intentional (C5.4). IsFallen is a fast per-frame
            // angle check; CharacterState is the authoritative FSM that includes
            // transition timing and get-up windows. They can briefly diverge (e.g.
            // CharacterState enters Fallen a few frames after IsFallen flips, or 
            // IsFallen clears before CharacterState exits GettingUp). Keeping both
            // ensures yaw torque is suppressed during the entire fallen/recovery
            // window without coupling the two systems tighter.
            bool suppressTurnDrive = IsFallen ||
                                     (_characterState != null &&
                                      (_characterState.CurrentState == CharacterStateType.Fallen ||
                                       _characterState.CurrentState == CharacterStateType.GettingUp));

            if (!suppressTurnDrive)
            {
                Vector3 currentForwardXZ = Vector3.ProjectOnPlane(currentRot * Vector3.forward, Vector3.up);
                Vector3 targetForwardXZ  = Vector3.ProjectOnPlane(_targetFacingRotation * Vector3.forward, Vector3.up);

                // Guard degenerate cases (character is near-vertical — forward projects near zero).
                // Both sqrMagnitude checks must pass before we normalize; this prevents
                // NaN from normalizing a zero or near-zero vector (Phase 3D2).
                if (currentForwardXZ.sqrMagnitude > 0.001f && targetForwardXZ.sqrMagnitude > 0.001f)
                {
                    float yawErrorDeg = Vector3.SignedAngle(
                        currentForwardXZ.normalized,
                        targetForwardXZ.normalized,
                        Vector3.up);

                    // Clamp to ±170° to prevent sign-flip oscillation at the ±180° boundary.
                    // When the target is almost exactly behind the character, SignedAngle can
                    // alternate between +180 and -180 on consecutive frames, causing the yaw
                    // torque to oscillate direction and the character to freeze. Clamping to
                    // ±170° commits to a rotation direction and prevents the flip.
                    yawErrorDeg = Mathf.Clamp(yawErrorDeg, -170f, 170f);

                    // Phase 3D2: Dead zone — suppress yaw torque for small errors to
                    // prevent micro-oscillation when the character is already nearly facing
                    // the target direction.
                    if (Mathf.Abs(yawErrorDeg) >= _yawDeadZoneDeg)
                    {
                        float yawErrorRad  = yawErrorDeg * Mathf.Deg2Rad;
                        float yawAngVelY   = angVel.y;
                        float yawTorqueY =
                            (_kPYaw * _currentBodySupportCommand.YawStrengthScale) * yawErrorRad
                            - _kDYaw * yawAngVelY;
                        _rb.AddTorque(Vector3.up * yawTorqueY, ForceMode.Force);
                    }
                }
            }
        }

        private void UpdateScaleRamps()
        {
            UprightStrengthScale = UpdateScaleRamp(UprightStrengthScale, ref _uprightStrengthRamp);
            HeightMaintenanceScale = UpdateScaleRamp(HeightMaintenanceScale, ref _heightMaintenanceRamp);
            StabilizationScale = UpdateScaleRamp(StabilizationScale, ref _stabilizationRamp);
        }

        private static SupportScaleRamp CreateScaleRamp(
            float currentValue,
            float targetValue,
            float duration,
            out float nextValue)
        {
            float clampedTarget = Mathf.Max(0f, targetValue);
            float clampedDuration = Mathf.Max(0f, duration);
            if (clampedDuration <= Mathf.Epsilon || Mathf.Approximately(currentValue, clampedTarget))
            {
                nextValue = clampedTarget;
                return default;
            }

            nextValue = currentValue;
            return new SupportScaleRamp
            {
                IsActive = true,
                StartValue = currentValue,
                TargetValue = clampedTarget,
                Duration = clampedDuration,
                Elapsed = 0f,
            };
        }

        private static float UpdateScaleRamp(float currentValue, ref SupportScaleRamp ramp)
        {
            if (!ramp.IsActive)
            {
                return currentValue;
            }

            ramp.Elapsed += Time.fixedDeltaTime;
            float progress = Mathf.Clamp01(ramp.Elapsed / ramp.Duration);
            float nextValue = Mathf.Lerp(ramp.StartValue, ramp.TargetValue, progress);
            if (progress >= 1f)
            {
                nextValue = ramp.TargetValue;
                ramp = default;
            }

            return nextValue;
        }

        private static float CancelScaleRamp(float currentValue, ref SupportScaleRamp ramp)
        {
            if (!ramp.IsActive)
            {
                return currentValue;
            }

            float nextValue = ramp.TargetValue;
            ramp = default;
            return nextValue;
        }

        // ─── Step-Up Foot Lift Assist ─────────────────────────────────────────

        private const float StepUpFootLiftStrength = 400f;
        private const float StepUpFootLiftDamping = 40f;
        private const float StepUpFootForwardStrength = 250f;
        private const float StepUpColliderClearance = 0.08f;
        private const float StepAssistGracePeriod = 0.5f;

        private float _stepAssistGraceTimer;
        private float _stepAssistCachedTargetY;
        private Vector3 _stepAssistCachedClimbDir;
        private float _stepAssistCachedClearance;

        /// <summary>
        /// Applies an upward spring-damper force to a non-grounded foot when a step-up
        /// is detected ahead. Once the foot is near or above the step surface, also
        /// applies a forward carry-over force so the foot clears the lip rather than
        /// getting pinned against the step face by friction.
        /// </summary>
        private static void ApplyStepUpFootLiftAssist(
            Rigidbody footRb,
            GroundSensor foot,
            float targetY,
            Vector3 forwardDir,
            float stepHeight)
        {
            if (footRb == null || foot == null || foot.IsGrounded)
                return;

            float deficit = targetY - footRb.position.y;
            if (deficit <= 0f)
                return;

            float liftForce = deficit * StepUpFootLiftStrength
                              - footRb.linearVelocity.y * StepUpFootLiftDamping;
            liftForce = Mathf.Max(0f, liftForce);
            footRb.AddForce(Vector3.up * liftForce, ForceMode.Force);

            // Forward carry-over: once the foot has risen past roughly half the step
            // height ramp up a forward push to slide the foot over the lip. This
            // counteracts friction from the step face contact normal.
            if (forwardDir.sqrMagnitude > 0.5f && stepHeight > 0f)
            {
                float liftProgress = 1f - Mathf.Clamp01(deficit / stepHeight);
                if (liftProgress > 0.15f)
                {
                    float carryForce = (liftProgress - 0.15f) / 0.85f * StepUpFootForwardStrength;
                    footRb.AddForce(forwardDir * carryForce, ForceMode.Force);
                }
            }
        }

        private float ComputeSurrenderSeverity(float uprightAngle, float angularVelocity, float hipsHeight)
        {
            float standingHeight = Mathf.Max(0.0001f, _standingHipsHeight);
            float angleSeverity = Mathf.Clamp01((uprightAngle - 65f) / 50f) * 0.5f;
            float angularVelocitySeverity = Mathf.Clamp01(angularVelocity / 6f) * 0.3f;
            float heightSeverity = Mathf.Clamp01(1f - (hipsHeight / standingHeight)) * 0.2f;
            return Mathf.Clamp01(angleSeverity + angularVelocitySeverity + heightSeverity);
        }

        private static float GetTiltDirectionalAngularVelocity(Vector3 currentUp, Vector3 pitchRollAngVel)
        {
            Vector3 correctionAxis = Vector3.Cross(currentUp, Vector3.up);
            if (correctionAxis.sqrMagnitude < 0.0001f)
            {
                return 0f;
            }

            return Mathf.Max(0f, -Vector3.Dot(pitchRollAngVel, correctionAxis.normalized));
        }

        private void LogRecoveryTelemetry(
            float hipsHeight,
            float heightError,
            float assistScale,
            bool startupAssistActive,
            bool persistentRecoveryActive,
            float uprightAngle)
        {
            if (!_debugRecoveryTelemetry)
            {
                return;
            }

            if (Time.time < _nextRecoveryTelemetryTime)
            {
                return;
            }

            _nextRecoveryTelemetryTime = Time.time + _debugRecoveryTelemetryInterval;

            bool isSeated = hipsHeight <= _debugSeatedHeightThreshold;
            Debug.Log(
                $"[BalanceController] '{name}' telemetry: hipsY={hipsHeight:F3}, " +
                $"seated={isSeated}, seatedThreshold={_debugSeatedHeightThreshold:F2}, " +
                $"heightError={heightError:F3}, assistScale={assistScale:F3}, " +
                $"startupAssistActive={startupAssistActive}, persistentRecoveryActive={persistentRecoveryActive}, " +
                $"grounded={IsGrounded}, fallen={IsFallen}, angle={uprightAngle:F1}°");
        }

        private struct SupportScaleRamp
        {
            public bool IsActive;
            public float StartValue;
            public float TargetValue;
            public float Duration;
            public float Elapsed;
        }

    }
}
