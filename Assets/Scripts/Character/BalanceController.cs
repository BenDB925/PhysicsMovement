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

        [SerializeField, Range(0f, 2000f)]
        [Tooltip("Proportional gain for the upright (pitch + roll) correction torque. " +
                 "Higher = snappier recovery, lower = softer wobble. " +
                 "Only affects pitch and roll axes; yaw is controlled by _kPYaw separately.")]
        private float _kP = 800f;

        [SerializeField, Range(0f, 500f)]
        [Tooltip("Derivative gain for the upright (pitch + roll) damping term. " +
                 "Increase if the character oscillates, decrease if it is too sluggish. " +
                 "Only affects pitch and roll axes.")]
        private float _kD = 80f;

        [SerializeField, Range(0f, 2000f)]
        [Tooltip("Proportional gain for the yaw correction torque (rotation around world Y). " +
                 "Controls how quickly the character turns to face the desired direction. " +
                 "Airborne multiplier does NOT apply to yaw torque.")]
        private float _kPYaw = 400f;

        [SerializeField, Range(0f, 500f)]
        [Tooltip("Derivative gain for yaw damping. Prevents the character from spinning " +
                 "past the target facing direction. " +
                 "Airborne multiplier does NOT apply to yaw torque.")]
        private float _kDYaw = 40f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Multiplier applied to upright (pitch + roll) PD torque while airborne. " +
                 "Lower values reduce in-air correction and preserve floppy feel. " +
                 "This multiplier does NOT affect yaw torque.")]
        private float _airborneMultiplier = 0.2f;

        [SerializeField, Range(0f, 90f)]
        [Tooltip("World-up deviation in degrees at which the character enters the fallen state. " +
             "Use a higher value than exit threshold to avoid chatter near the boundary.")]
        private float _fallenEnterAngleThreshold = 65f;

        [SerializeField, Range(0f, 90f)]
        [Tooltip("World-up deviation in degrees at which the character exits the fallen state. " +
             "Use a lower value than enter threshold to provide hysteresis.")]
        private float _fallenExitAngleThreshold = 55f;

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
        private float _startupAssistTargetHeight = 0.9f;

           [SerializeField, Range(0f, 2000f)]
           [Tooltip("Maximum upward force (Newtons) applied by startup stand assist.")]
        private float _startupStandAssistForce = 1200f;

           [SerializeField, Range(0.05f, 1f)]
           [Tooltip("Height error range used to scale stand assist force from 0 to max.")]
           private float _startupAssistHeightRange = 0.35f;

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

        [SerializeField]
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
        private float _debugSeatedHeightThreshold = 0.75f;

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

        // ─── Height Maintenance ─────────────────────────────────────────────

        [Header("Height Maintenance")]
        [SerializeField]
        [Tooltip("When grounded, applies upward force to keep hips at standing height. " +
                 "Prevents settling into a seated pose.")]
        private bool _enableHeightMaintenance = true;

        [SerializeField, Range(0.5f, 2f)]
        [Tooltip("Target world-space Y position for the hips when standing. " +
                 "Height force only applies when hips are below this value.")]
        private float _standingHipsHeight = 0.95f;

        [SerializeField, Range(0f, 3000f)]
        [Tooltip("Spring strength for height maintenance. Higher = faster lift from seated.")]
        private float _heightMaintenanceStrength = 1500f;

        [SerializeField, Range(0f, 300f)]
        [Tooltip("Vertical velocity damping for height maintenance. Prevents bouncing.")]
        private float _heightMaintenanceDamping = 120f;

        // ─── Private Fields ──────────────────────────────────────────────────

        private Rigidbody _rb;

        /// <summary>Left-foot GroundSensor, located in Awake via component search.</summary>
        private GroundSensor _footL;

        /// <summary>Right-foot GroundSensor, located in Awake via component search.</summary>
        private GroundSensor _footR;

        /// <summary>
        /// Desired world-space rotation (yaw + upright). Updated via
        /// <see cref="SetFacingDirection"/>. Defaults to forward-facing upright.
        /// </summary>
        private Quaternion _targetFacingRotation = Quaternion.identity;

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

        private ConfigurableJoint[] _startupAssistLegJoints;
        private JointDrive[] _startupAssistLegBaseDrives;
        private Rigidbody[] _startupAssistLegBodies;
        private float _totalBodyMass;
        private float _nextRecoveryTelemetryTime;

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
        /// Sets the desired world-space facing direction. The controller constrains this to
        /// the XZ plane (yaw only) so the upright axis is never affected.
        /// A zero or near-zero vector is silently ignored — the previous facing is kept.
        /// Called by <c>PlayerMovement</c> (Phase 3) when the input direction changes.
        /// </summary>
        /// <param name="dir">Desired facing direction in world space (Y component is ignored).</param>
        public void SetFacingDirection(Vector3 dir)
        {
            // STEP 1: Flatten to XZ and guard against degenerate (zero) vectors.
            Vector3 flatDir = new Vector3(dir.x, 0f, dir.z);
            if (flatDir.sqrMagnitude < 0.001f)
            {
                // Zero or nearly-zero input — keep current facing to avoid LookRotation NaN.
                return;
            }

            // STEP 2: Build a yaw-only world-space rotation (up = Vector3.up guaranteed).
            _targetFacingRotation = Quaternion.LookRotation(flatDir.normalized, Vector3.up);
        }

        // ─── Unity Lifecycle ──────────────────────────────────────────────────

        private void Awake()
        {
            // STEP 1: Cache the Rigidbody on this GameObject (guaranteed by RequireComponent).
            _rb = GetComponent<Rigidbody>();

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

            CacheStartupAssistLegJoints();

            _totalBodyMass = 0f;
            Rigidbody[] bodies = GetComponentsInChildren<Rigidbody>(includeInactive: true);
            for (int i = 0; i < bodies.Length; i++)
            {
                _totalBodyMass += bodies[i].mass;
            }
        }

        private void OnValidate()
        {
            if (_fallenExitAngleThreshold > _fallenEnterAngleThreshold)
            {
                _fallenExitAngleThreshold = _fallenEnterAngleThreshold;
            }

            _startupStandAssistDuration = Mathf.Max(0f, _startupStandAssistDuration);
            _startupAssistHeightRange = Mathf.Max(0.05f, _startupAssistHeightRange);
            _startupAssistMaxRiseSpeed = Mathf.Max(0.05f, _startupAssistMaxRiseSpeed);
            _startupAssistLegForceFraction = Mathf.Clamp01(_startupAssistLegForceFraction);
            _persistentSeatedRecoveryAssistScale = Mathf.Clamp01(_persistentSeatedRecoveryAssistScale);
            _persistentSeatedRecoveryMinAssistScale = Mathf.Clamp01(_persistentSeatedRecoveryMinAssistScale);
            _debugRecoveryTelemetryInterval = Mathf.Max(0.1f, _debugRecoveryTelemetryInterval);
            _debugSeatedHeightThreshold = Mathf.Max(0f, _debugSeatedHeightThreshold);
        }

        private void FixedUpdate()
        {
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
            float uprightAngle = Vector3.Angle(_rb.transform.up, Vector3.up);

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
            bool startupAssistActive = false;
            float startupAssistScale = 0f;
            bool persistentRecoveryActive = false;

            if (_enableStartupStandAssist &&
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
                        if (_startupStandAssistForce > 0f && _totalBodyMass > 0f)
                        {
                            weightBasedScale = (_totalBodyMass * Physics.gravity.magnitude) / _startupStandAssistForce;
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
                        float assistForce = _startupStandAssistForce * assistScale;
                        ApplyStartupAssistForces(assistDirection, assistForce);
                    }
                }
            }

            ApplyStartupAssistLegDrive(startupAssistActive ? startupAssistScale : 0f);

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
            // When grounded or near standing height, pulls the hips back toward the
            // midpoint of the feet to prevent topple that rotational PD torque alone
            // cannot correct.
            if (_enableComStabilization && effectivelyGrounded && _footL != null && _footR != null)
            {
                Vector3 feetCenter = (_footL.transform.position + _footR.transform.position) * 0.5f;
                Vector3 hipsPos = _rb.position;
                Vector3 horizontalOffset = new Vector3(
                    hipsPos.x - feetCenter.x,
                    0f,
                    hipsPos.z - feetCenter.z);
                Vector3 horizontalVel = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z);
                Vector3 comForce = -horizontalOffset * _comStabilizationStrength
                                   - horizontalVel * _comStabilizationDamping;
                _rb.AddForce(comForce, ForceMode.Force);
            }

            // STEP 3.7: Height maintenance.
            // When near ground, pushes hips up toward standing height to prevent the
            // character from settling into a seated basin-of-attraction.
            if (_enableHeightMaintenance && effectivelyGrounded)
            {
                float hipsHeightError = _standingHipsHeight - _rb.position.y;
                if (hipsHeightError > 0f)
                {
                    float heightForce = hipsHeightError * _heightMaintenanceStrength
                                        - _rb.linearVelocity.y * _heightMaintenanceDamping;
                    heightForce = Mathf.Max(0f, heightForce);
                    _rb.AddForce(Vector3.up * heightForce, ForceMode.Force);
                }
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
            Quaternion currentRot   = _rb.rotation;
            Vector3    currentUp    = currentRot * Vector3.up;
            Quaternion uprightError = Quaternion.FromToRotation(currentUp, Vector3.up);

            uprightError.ToAngleAxis(out float uprightAngleDeg, out Vector3 uprightAxis);
            if (uprightAngleDeg > 180f) uprightAngleDeg -= 360f;

            // Damp only the pitch/roll (XZ) component of angular velocity to avoid
            // coupling the derivative term with the separate yaw damping below.
            Vector3 angVel = _rb.angularVelocity;
            Vector3 pitchRollAngVel = new Vector3(angVel.x, 0f, angVel.z);

            if (uprightAxis.sqrMagnitude > 0.001f)
            {
                float   uprightRad    = uprightAngleDeg * Mathf.Deg2Rad;
                Vector3 uprightTorque = _kP * uprightRad * uprightAxis - _kD * pitchRollAngVel;

                float uprightMultiplier = _hasBeenGrounded
                    ? (effectivelyGrounded ? 1f : _airborneMultiplier)
                    : 1f;
                _rb.AddTorque(uprightTorque * uprightMultiplier, ForceMode.Force);
            }

            // ─── STEP 5: Compute yaw torque ────────────────────────────────────
            // Extract the yaw error: the signed angle around world Y from the current
            // Hips forward direction (projected onto XZ) to the desired facing direction.
            //
            // We compute this independently from upright error so that changing the
            // facing direction never introduces roll instability.
            //
            // Yaw torque is only applied when NOT fallen: when the character is severely
            // tilted or upside-down, the horizontal projection of the forward vector is
            // unreliable and can produce a spurious 180° yaw error that fights recovery.
            // While fallen, we rely solely on the upright torque to assist self-recovery.
            //
            // Airborne multiplier does NOT apply to yaw — the character should always
            // be able to turn in air.
            if (!IsFallen)
            {
                Vector3 currentForwardXZ = Vector3.ProjectOnPlane(currentRot * Vector3.forward, Vector3.up);
                Vector3 targetForwardXZ  = Vector3.ProjectOnPlane(_targetFacingRotation * Vector3.forward, Vector3.up);

                // Guard degenerate cases (character is near-vertical — forward projects near zero).
                if (currentForwardXZ.sqrMagnitude > 0.001f && targetForwardXZ.sqrMagnitude > 0.001f)
                {
                    float yawErrorDeg = Vector3.SignedAngle(
                        currentForwardXZ.normalized,
                        targetForwardXZ.normalized,
                        Vector3.up);

                    float yawErrorRad  = yawErrorDeg * Mathf.Deg2Rad;
                    float yawAngVelY   = angVel.y;
                    float yawTorqueY   = _kPYaw * yawErrorRad - _kDYaw * yawAngVelY;
                    _rb.AddTorque(Vector3.up * yawTorqueY, ForceMode.Force);
                }
            }
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

        private void CacheStartupAssistLegJoints()
        {
            ConfigurableJoint[] joints = GetComponentsInChildren<ConfigurableJoint>(includeInactive: true);
            int legCount = 0;
            foreach (ConfigurableJoint joint in joints)
            {
                if (IsStartupAssistLegJointName(joint.gameObject.name))
                {
                    legCount++;
                }
            }

            _startupAssistLegJoints = new ConfigurableJoint[legCount];
            _startupAssistLegBaseDrives = new JointDrive[legCount];
            _startupAssistLegBodies = new Rigidbody[legCount];

            int writeIndex = 0;
            foreach (ConfigurableJoint joint in joints)
            {
                if (!IsStartupAssistLegJointName(joint.gameObject.name))
                {
                    continue;
                }

                _startupAssistLegJoints[writeIndex] = joint;
                _startupAssistLegBaseDrives[writeIndex] = joint.slerpDrive;
                _startupAssistLegBodies[writeIndex] = joint.GetComponent<Rigidbody>();
                writeIndex++;
            }
        }

        private void ApplyStartupAssistForces(Vector3 assistDirection, float assistForce)
        {
            float legForce = assistForce * _startupAssistLegForceFraction;
            float hipsForce = assistForce - legForce;

            if (hipsForce > 0f)
            {
                _rb.AddForce(assistDirection * hipsForce, ForceMode.Force);
            }

            if (_startupAssistLegBodies == null || _startupAssistLegBodies.Length == 0 || legForce <= 0f)
            {
                return;
            }

            float perLegBodyForce = legForce / _startupAssistLegBodies.Length;
            for (int i = 0; i < _startupAssistLegBodies.Length; i++)
            {
                Rigidbody legBody = _startupAssistLegBodies[i];
                if (legBody == null)
                {
                    continue;
                }

                legBody.AddForce(assistDirection * perLegBodyForce, ForceMode.Force);
            }
        }

        private void ApplyStartupAssistLegDrive(float assistScale)
        {
            if (_startupAssistLegJoints == null || _startupAssistLegBaseDrives == null)
            {
                return;
            }

            for (int i = 0; i < _startupAssistLegJoints.Length; i++)
            {
                ConfigurableJoint joint = _startupAssistLegJoints[i];
                if (joint == null)
                {
                    continue;
                }

                JointDrive baseDrive = _startupAssistLegBaseDrives[i];
                JointDrive drive = joint.slerpDrive;
                drive.positionSpring = Mathf.Lerp(
                    baseDrive.positionSpring,
                    baseDrive.positionSpring * _startupLegSpringMultiplier,
                    assistScale);
                drive.positionDamper = Mathf.Lerp(
                    baseDrive.positionDamper,
                    baseDrive.positionDamper * _startupLegDamperMultiplier,
                    assistScale);
                drive.maximumForce = baseDrive.maximumForce;
                joint.slerpDrive = drive;
            }
        }

        private static bool IsStartupAssistLegJointName(string segmentName)
        {
            return segmentName == "UpperLeg_L" ||
                   segmentName == "UpperLeg_R" ||
                   segmentName == "LowerLeg_L" ||
                   segmentName == "LowerLeg_R";
        }
    }
}
