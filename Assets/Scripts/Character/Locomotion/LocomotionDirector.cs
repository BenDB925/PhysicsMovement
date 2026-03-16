using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Coordinates locomotion data flow on the Hips root by reading desired input and runtime
    /// observations, then publishing observation-driven body-support commands plus pass-through
    /// leg commands that future locomotion slices can hand to the execution systems.
    /// </summary>
    [DefaultExecutionOrder(250)]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(PlayerMovement))]
    [RequireComponent(typeof(BalanceController))]
    [RequireComponent(typeof(CharacterState))]
    public class LocomotionDirector : MonoBehaviour
    {
        private const float CommandEpsilon = 0.0001f;

        [SerializeField]
        [Tooltip("Keeps the director on the legacy pass-through path while ownership is migrated.")]
        private bool _passThroughMode = true;

        [Header("Observation Filtering")]
        [SerializeField, Range(1f, 60f)]
        [Tooltip("How quickly per-foot contact confidence rises toward a newly grounded sample. Higher values reacquire contact faster.")]
        private float _contactConfidenceRiseSpeed = 20f;

        [SerializeField, Range(1f, 60f)]
        [Tooltip("How quickly per-foot contact confidence falls after contact is lost. Higher values clear stale support faster.")]
        private float _contactConfidenceFallSpeed = 30f;

        [SerializeField, Range(1f, 40f)]
        [Tooltip("How quickly planted confidence rises once a foot re-establishes stable support.")]
        private float _plantedConfidenceRiseSpeed = 12f;

        [SerializeField, Range(1f, 40f)]
        [Tooltip("How quickly planted confidence falls once a foot is truly slipping or lifting away.")]
        private float _plantedConfidenceFallSpeed = 20f;

        [SerializeField, Range(0.5f, 0.98f)]
        [Tooltip("Planted-confidence threshold required to enter the planted state. Keep this above the exit threshold to prevent frame-to-frame chatter.")]
        private float _plantedEnterThreshold = 0.75f;

        [SerializeField, Range(0.02f, 0.9f)]
        [Tooltip("Planted-confidence threshold required to leave the planted state. Keep this below the enter threshold to preserve hysteresis.")]
        private float _plantedExitThreshold = 0.55f;

        [Header("Observation Decisions")]
        [SerializeField, Range(0.05f, 1f)]
        [Tooltip("Minimum turn-severity observation required before the director enters support recovery from the world model.")]
        private float _turnRecoveryThreshold = 0.45f;

        [SerializeField, Range(0.05f, 1f)]
        [Tooltip("Minimum support-risk observation required before the director escalates from neutral support into recovery-strength commands.")]
        private float _supportRiskRecoveryThreshold = 0.45f;

        [SerializeField, Range(0.1f, 1f)]
        [Tooltip("Lowest yaw-strength scale the director may request while support risk and turn severity are both high.")]
        private float _minimumRiskYawStrengthScale = 0.45f;

        [SerializeField, Range(0f, 2f)]
        [Tooltip("Additional upright-strength scale applied as support risk rises so body support reacts to the observation model.")]
        private float _supportRiskUprightBoost = 0.5f;

        [SerializeField, Range(0f, 2f)]
        [Tooltip("Additional COM-stabilization scale applied as support risk rises so weak support receives stronger body support.")]
        private float _supportRiskStabilizationBoost = 0.75f;

        [SerializeField, Range(0f, 15f)]
        [Tooltip("Maximum lean angle (degrees) the director requests during turns. Scales with turn severity so the COM target shifts toward the turn.")]
        private float _maxTurnLeanDegrees = 5f;

        [SerializeField, Range(0f, 15f)]
        [Tooltip("Maximum extra forward lean angle (degrees) requested at full sprint. Scales with SprintNormalized so sprint posture ramps through the same blend window as sprint speed.")]
        private float _maxSprintLeanDegrees = 8f;

        [Header("Recovery Transition Hysteresis")]
        [SerializeField, Range(1, 10)]
        [Tooltip("Minimum consecutive frames a recovery situation must persist before entering recovery. NearFall and Stumble use half this value.")]
        private int _recoveryEntryDebounceFrames = 3;

        [SerializeField, Range(1, 60)]
        [Tooltip("Frames to block re-entry of the same-or-lower-priority recovery situation after the previous recovery expires.")]
        private int _recoveryExitCooldownFrames = 20;

        [SerializeField, Range(1, 20)]
        [Tooltip("Frames over which recovery blend ramps from 0 to its natural value, preventing snap-on of support adjustments.")]
        private int _recoveryRampInFrames = 8;

        [Header("Debug Visibility")]
        [SerializeField]
        [Tooltip("Draws the current support geometry, COM offset, and predicted drift direction in the Scene view while the game is running.")]
        private bool _debugObservationDraw = false;

        [SerializeField]
        [Tooltip("Draws planned step targets for each leg as colored markers in the Scene view. Color reflects step confidence (red=low, green=high).")]
        private bool _debugStepTargetDraw = false;

        [SerializeField]
        [Tooltip("Logs throttled locomotion-observation telemetry including support geometry, drift direction, and confidence values.")]
        private bool _debugObservationTelemetry = false;

        [SerializeField, Range(0.05f, 2f)]
        [Tooltip("Seconds between locomotion-observation telemetry logs while running.")]
        private float _debugObservationTelemetryInterval = 0.25f;

        [SerializeField, Range(0.01f, 0.3f)]
        [Tooltip("Vertical offset applied to support debug geometry so the observation draw stays readable above the floor.")]
        private float _debugObservationDrawHeight = 0.05f;

        private Rigidbody _hipsBody;
        private PlayerMovement _playerMovement;
        private BalanceController _balanceController;
        private CharacterState _characterState;
        private LocomotionCollapseDetector _collapseDetector;
        private LegAnimator _legAnimator;
        private LocomotionSensorAggregator _sensorAggregator;
        private SupportObservationFilter _supportObservationFilter;
        private GroundSensor _leftGroundSensor;
        private GroundSensor _rightGroundSensor;
        private Transform _leftFootTransform;
        private Transform _rightFootTransform;

        private DesiredInput _currentDesiredInput;
        private LocomotionSensorSnapshot _currentSensorSnapshot;
        private LocomotionObservation _currentObservation;
        private BodySupportCommand _currentBodySupportCommand;
        private LegCommandOutput _leftLegCommand;
        private LegCommandOutput _rightLegCommand;
        private Vector3 _currentPredictedDriftDirection;
        private string _currentObservationTelemetryLine;
        private float _nextObservationTelemetryTime;
        private RecoveryState _currentRecoveryState;
        private RecoveryTransitionGuard _transitionGuard;

        public bool HasCommandFrame { get; private set; }

        public bool IsPassThroughMode => _passThroughMode;

        /// <summary>
        /// True when the director is actively running a recovery strategy for
        /// a classified situation. <see cref="CharacterState"/> reads this to
        /// defer collapse-triggered Fallen transitions while recovery has a
        /// chance to save the character.
        /// </summary>
        public bool IsRecoveryActive => _currentRecoveryState.IsActive;

        internal DesiredInput CurrentDesiredInput => _currentDesiredInput;

        internal LocomotionSensorSnapshot CurrentSensorSnapshot => _currentSensorSnapshot;

        internal LocomotionObservation CurrentObservation => _currentObservation;

        internal BodySupportCommand CurrentBodySupportCommand => _currentBodySupportCommand;

        internal LegCommandOutput LeftLegCommand => _leftLegCommand;

        internal LegCommandOutput RightLegCommand => _rightLegCommand;

        internal Vector3 CurrentPredictedDriftDirection => _currentPredictedDriftDirection;

        internal string CurrentObservationTelemetryLine => _currentObservationTelemetryLine;

        private void Awake()
        {
            // STEP 1: Cache the Hips-side locomotion collaborators required to build a single-frame snapshot.
            TryResolveDependencies();

            // STEP 2: Seed neutral pass-through commands so runtime consumers have a defined baseline before the first FixedUpdate.
            ResetOutputs();
        }

        private void FixedUpdate()
        {
            // STEP 1: Refresh the desired-input snapshot from PlayerMovement after the existing input loop has run.
            if (!TryResolveDependencies())
            {
                ResetOutputs();
                return;
            }

            _currentDesiredInput = _playerMovement.CurrentDesiredInput;

            // STEP 2: Gather a shared sensor snapshot, then promote it into the locomotion observation frame.
            if (!_sensorAggregator.TryCollect(out _currentSensorSnapshot))
            {
                ResetOutputs();
                return;
            }

            SupportObservation filteredSupport = _supportObservationFilter.Filter(
                _currentSensorSnapshot.Support,
                Time.fixedDeltaTime);

            _currentObservation = BuildObservation(_currentSensorSnapshot, filteredSupport);
            RefreshObservationDebugVisibility();

            // STEP 3: Emit observation-driven support commands and pass-through leg commands.
            EmitObservationDrivenCommands();
            PushCommandsToExecutors();
            HasCommandFrame = true;

            // STEP 4: Draw optional step target debug visualization after commands are finalized.
            if (_debugStepTargetDraw)
            {
                LocomotionDebugDraw.DrawStepTargetDebug(_leftLegCommand, _rightLegCommand, Time.fixedDeltaTime);
            }
        }

        private void OnDisable()
        {
            if (_balanceController != null)
            {
                _balanceController.ClearBodySupportCommand();
            }

            if (_legAnimator != null)
            {
                _legAnimator.ClearCommandFrame();
            }

            _currentPredictedDriftDirection = Vector3.zero;
            _currentObservationTelemetryLine = string.Empty;
            _nextObservationTelemetryTime = 0f;
            _currentRecoveryState = RecoveryState.Inactive;
            _transitionGuard = null;
            _supportObservationFilter = null;
            ResetOutputs();
        }

        private LocomotionObservation BuildObservation(
            LocomotionSensorSnapshot sensorSnapshot,
            SupportObservation filteredSupport)
        {
            bool isLocomotionCollapsed = _collapseDetector != null && _collapseDetector.IsCollapseConfirmed;
            float turnSeverity = ComputeTurnSeverity(sensorSnapshot);

            return new LocomotionObservation(
                _characterState.CurrentState,
                _balanceController.IsGrounded,
                _balanceController.IsFallen,
                isLocomotionCollapsed,
                _balanceController.IsInSnapRecovery,
                _balanceController.UprightAngle,
                sensorSnapshot.HipsVelocity,
                sensorSnapshot.HipsAngularVelocity,
                transform.forward,
                transform.up,
                filteredSupport,
                turnSeverity);
        }

        private float ComputeTurnSeverity(LocomotionSensorSnapshot sensorSnapshot)
        {
            // STEP 1: Choose the currently requested horizontal direction as the turn target.
            Vector3 requestedDirection = _currentDesiredInput.HasMoveIntent
                ? _currentDesiredInput.MoveWorldDirection
                : _currentDesiredInput.FacingDirection;
            if (requestedDirection.sqrMagnitude < CommandEpsilon)
            {
                return 0f;
            }

            // STEP 2: Compare requested heading against current body heading.
            Vector3 bodyForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (bodyForward.sqrMagnitude < CommandEpsilon)
            {
                bodyForward = Vector3.forward;
            }
            else
            {
                bodyForward.Normalize();
            }

            float headingDelta = Vector3.Angle(requestedDirection, bodyForward);

            // STEP 3: Blend heading disagreement with live yaw rate into a normalized turn severity scalar.
            float headingSeverity = Mathf.InverseLerp(0f, 90f, headingDelta);
            float yawRateSeverity = Mathf.InverseLerp(0f, 8f, Mathf.Abs(sensorSnapshot.YawRate));
            return Mathf.Clamp01(Mathf.Max(headingSeverity, yawRateSeverity));
        }

        private void RefreshObservationDebugVisibility()
        {
            // STEP 1: Promote the observation and support geometry into a single debug-facing drift estimate.
            _currentPredictedDriftDirection = ComputePredictedDriftDirection(_currentSensorSnapshot, _currentObservation);

            // STEP 2: Cache the current observation telemetry line so tests and runtime logging share the same source.
            _currentObservationTelemetryLine = LocomotionDebugDraw.BuildObservationTelemetryLine(
                name,
                _currentSensorSnapshot,
                _currentObservation,
                _currentPredictedDriftDirection);

            // STEP 3: Emit optional draw and log visibility without affecting the control path.
            if (_debugObservationDraw)
            {
                LocomotionDebugDraw.DrawObservationDebug(_currentSensorSnapshot, _currentObservation, _currentPredictedDriftDirection, _debugObservationDrawHeight);
            }

            LogObservationTelemetry();
        }

        private Vector3 ComputePredictedDriftDirection(
            LocomotionSensorSnapshot sensorSnapshot,
            LocomotionObservation observation)
        {
            // STEP 1: Start from the planar hips offset relative to the active support patch.
            Vector3 supportToHips = Vector3.ProjectOnPlane(
                sensorSnapshot.HipsPosition - sensorSnapshot.SupportGeometry.SupportCenter,
                Vector3.up);

            Vector3 supportToCom = Vector3.ProjectOnPlane(
                sensorSnapshot.CenterOfMassPosition - sensorSnapshot.SupportGeometry.SupportCenter,
                Vector3.up);

            Vector3 driftVector = supportToHips;

            if (supportToCom.sqrMagnitude > CommandEpsilon)
            {
                driftVector += supportToCom * 0.5f;
            }

            // STEP 2: Blend in live planar body velocity so the debug direction reflects ongoing slip or topple momentum.
            Vector3 planarVelocity = Vector3.ProjectOnPlane(sensorSnapshot.HipsVelocity, Vector3.up);
            if (planarVelocity.sqrMagnitude > CommandEpsilon)
            {
                driftVector += planarVelocity * 0.2f;
            }

            // STEP 3: Fall back to the intended move direction when the body is centered but the gait still carries directional risk.
            if (driftVector.sqrMagnitude <= CommandEpsilon && _currentDesiredInput.HasMoveIntent)
            {
                driftVector = _currentDesiredInput.MoveWorldDirection;
            }

            if (driftVector.sqrMagnitude <= CommandEpsilon)
            {
                return Vector3.zero;
            }

            float driftWeight = Mathf.Clamp01(
                observation.SlipEstimate +
                observation.TurnSeverity * 0.5f +
                (observation.IsComOutsideSupport ? 0.5f : 0f));

            if (driftWeight <= 0f)
            {
                driftWeight = 1f;
            }

            return driftVector.normalized * driftWeight;
        }

        private void LogObservationTelemetry()
        {
            if (!_debugObservationTelemetry || string.IsNullOrEmpty(_currentObservationTelemetryLine))
            {
                return;
            }

            if (Time.time < _nextObservationTelemetryTime)
            {
                return;
            }

            _nextObservationTelemetryTime = Time.time + Mathf.Max(0.05f, _debugObservationTelemetryInterval);
            Debug.Log(_currentObservationTelemetryLine, this);
        }

        private void EmitObservationDrivenCommands()
        {
            // STEP 1: Classify the current locomotion risk directly from the promoted observation model.
            float supportRisk = ComputeSupportRisk(_currentObservation);
            float turnRisk = _currentDesiredInput.HasMoveIntent ? _currentObservation.TurnSeverity : 0f;
            float turnSupportRisk = Mathf.Clamp01(turnRisk * supportRisk);

            // STEP 2: Classify the current situation and promote observation risk into the typed recovery state.
            UpdateObservationRecoveryState(supportRisk);
            int recoveryFramesThisStep = _currentRecoveryState.FramesRemaining;
            float recoveryBlend = ComputeRecoveryBlend(recoveryFramesThisStep);

            // Apply C6.3 ramp-in so recovery blend increases gradually from 0 at entry.
            float rampInBlend = _transitionGuard.ComputeRampInBlend(_recoveryRampInFrames);
            recoveryBlend *= rampInBlend;

            int kdStartOffset = _balanceController.SnapRecoveryDurationFrames - _balanceController.SnapRecoveryKdDurationFrames;
            int recoveryFramesAfterSupport = recoveryFramesThisStep > 0 ? recoveryFramesThisStep - 1 : 0;
            int kdFrames = recoveryFramesAfterSupport - kdStartOffset;
            float recoveryKdBlend = ComputeRecoveryBlend(kdFrames) * rampInBlend;

            // STEP 3: Map observation severity onto the support-command surface, with per-situation
            // response profiles blended in while recovery is active.
            Vector3 supportFacing = GetSupportFacingDirection();
            Vector3 supportTravel = GetSupportTravelDirection(supportFacing);
            float baseYawStrengthScale = Mathf.Lerp(1f, _minimumRiskYawStrengthScale, turnSupportRisk);
            float baseUprightStrengthScale = 1f + supportRisk * _supportRiskUprightBoost;
            float baseStabilizationStrengthScale = 1f + supportRisk * _supportRiskStabilizationBoost;
            float heightMaintenanceScale = ComputeHeightMaintenanceScale();

            // STEP 3a: Keep the director as the single owner of locomotion posture bias.
            // Turn severity contributes the existing recovery/support lean, and sprint
            // blend adds a forward lean budget that ramps over the same sprint window.
            float turnLeanDegrees = _currentDesiredInput.HasMoveIntent
                ? _currentObservation.TurnSeverity * _maxTurnLeanDegrees
                : 0f;
            float sprintLeanDegrees = _currentDesiredInput.HasMoveIntent
                ? _currentDesiredInput.SprintNormalized * _maxSprintLeanDegrees
                : 0f;
            float baseLeanDegrees = turnLeanDegrees + sprintLeanDegrees;

            // Apply per-situation response profile blended by the current recovery envelope.
            RecoveryResponseProfile profile = RecoveryResponseProfile.For(_currentRecoveryState.Situation);
            float uprightStrengthScale = Mathf.Lerp(baseUprightStrengthScale,
                baseUprightStrengthScale * profile.UprightBoostMultiplier, recoveryBlend);
            float yawStrengthScale = Mathf.Max(
                Mathf.Lerp(baseYawStrengthScale, profile.MinYawStrengthScale, recoveryBlend),
                profile.MinYawStrengthScale * recoveryBlend);
            float stabilizationStrengthScale = Mathf.Lerp(baseStabilizationStrengthScale,
                baseStabilizationStrengthScale * profile.StabilizationBoostMultiplier, recoveryBlend);
            float desiredLeanDegrees = Mathf.Lerp(baseLeanDegrees,
                baseLeanDegrees * profile.LeanDegreesMultiplier, recoveryBlend);

            _currentBodySupportCommand = new BodySupportCommand(
                supportFacing,
                Vector3.up,
                supportTravel,
                desiredLeanDegrees,
                uprightStrengthScale,
                yawStrengthScale,
                stabilizationStrengthScale,
                recoveryBlend,
                recoveryKdBlend,
                heightMaintenanceScale: heightMaintenanceScale,
                recoverySituation: _currentRecoveryState.Situation);

            // STEP 4: Keep the existing pass-through leg-command seam until the dedicated gait slices replace it.
            if (_legAnimator != null && _passThroughMode)
            {
                _legAnimator.BuildPassThroughCommands(
                    _currentDesiredInput,
                    _currentObservation,
                    out _leftLegCommand,
                    out _rightLegCommand);

                // STEP 4b: Stamp recovery context so leg executors can modulate
                //          recovery/catch-step profiles by situation urgency.
                if (_currentRecoveryState.IsActive)
                {
                    _leftLegCommand = _leftLegCommand.WithRecoveryContext(
                        _currentRecoveryState.Situation,
                        recoveryBlend);
                    _rightLegCommand = _rightLegCommand.WithRecoveryContext(
                        _currentRecoveryState.Situation,
                        recoveryBlend);
                }
            }
            else
            {
                _leftLegCommand = LegCommandOutput.Disabled(LocomotionLeg.Left);
                _rightLegCommand = LegCommandOutput.Disabled(LocomotionLeg.Right);
            }

            // STEP 5: Advance the recovery state after publishing the command frame for this step.
            // Tick the transition guard ramp-in and detect recovery expiry for the exit cooldown.
            if (_currentRecoveryState.IsActive)
            {
                _transitionGuard.TickRampIn();
                RecoverySituation situationBeforeTick = _currentRecoveryState.Situation;
                _currentRecoveryState = _currentRecoveryState.Tick();
                if (!_currentRecoveryState.IsActive)
                {
                    _transitionGuard.OnRecoveryExpired(situationBeforeTick, _recoveryExitCooldownFrames);
                }
            }
        }

        private void UpdateObservationRecoveryState(float supportRisk)
        {
            // STEP 1: Skip recovery escalation when the current locomotion state cannot execute a support response.
            CharacterStateType state = _currentObservation.CharacterState;
            if (!_currentDesiredInput.HasMoveIntent ||
                state == CharacterStateType.Fallen ||
                state == CharacterStateType.GettingUp)
            {
                // Feed None to the guard so candidate tracking resets.
                _transitionGuard.ShouldEnter(RecoverySituation.None, _recoveryEntryDebounceFrames, _recoveryExitCooldownFrames);
                return;
            }

            // STEP 2: Classify the current situation.
            RecoverySituation situation = ClassifyRecoverySituation(supportRisk);

            // STEP 3: When recovery is already active, allow direct extension or
            // situation-priority upgrades without re-debouncing. The guard only
            // gates the initial entry from idle so the ramp-in counter can advance
            // uninterrupted. Reset the guard's candidate tracking so no stale
            // debounce count carries over into the next idle→recovery transition.
            if (_currentRecoveryState.IsActive)
            {
                if (situation != RecoverySituation.None)
                {
                    int duration = GetRecoveryDuration(situation);
                    _currentRecoveryState = _currentRecoveryState.Enter(
                        situation,
                        duration,
                        supportRisk,
                        _currentObservation.TurnSeverity);
                }

                _transitionGuard.ShouldEnter(RecoverySituation.None, _recoveryEntryDebounceFrames, _recoveryExitCooldownFrames);
                return;
            }

            // STEP 4: Recovery is not active — use the guard for debounce and cooldown.
            bool shouldEnter = _transitionGuard.ShouldEnter(
                situation,
                _recoveryEntryDebounceFrames,
                _recoveryExitCooldownFrames);

            if (shouldEnter)
            {
                int duration = GetRecoveryDuration(situation);
                _currentRecoveryState = _currentRecoveryState.Enter(
                    situation,
                    duration,
                    supportRisk,
                    _currentObservation.TurnSeverity);
            }
        }

        /// <summary>
        /// Classifies the current observation into a named recovery situation.
        /// Returns <see cref="RecoverySituation.None"/> when no recovery is needed.
        /// Priority order (highest first): Stumble, NearFall, Slip, Reversal, HardTurn.
        /// </summary>
        private RecoverySituation ClassifyRecoverySituation(float supportRisk)
        {
            LocomotionObservation obs = _currentObservation;

            // STEP 1: Confirmed collapse watchdog → Stumble (highest priority).
            if (obs.IsLocomotionCollapsed)
            {
                return RecoverySituation.Stumble;
            }

            // STEP 2: Critical support deficit without collapse → NearFall.
            // Uses a higher threshold than the general recovery entry to catch
            // the pre-collapse regime where an aggressive catch-step can still save the character.
            const float NearFallSupportRiskThreshold = 0.7f;
            if (supportRisk >= NearFallSupportRiskThreshold && obs.SupportQuality < 0.3f)
            {
                return RecoverySituation.NearFall;
            }

            // STEP 3: High slip estimate while support is compromised → Slip.
            const float SlipThreshold = 0.4f;
            if (obs.SlipEstimate >= SlipThreshold && supportRisk >= _supportRiskRecoveryThreshold)
            {
                return RecoverySituation.Slip;
            }

            // STEP 4: Near-180° reversal with degraded support → Reversal.
            const float ReversalTurnSeverityThreshold = 0.85f;
            if (obs.TurnSeverity >= ReversalTurnSeverityThreshold &&
                supportRisk >= _supportRiskRecoveryThreshold)
            {
                return RecoverySituation.Reversal;
            }

            // STEP 5: Sharp turn with combined risk → HardTurn.
            bool turnRiskExceedsThreshold = obs.TurnSeverity >= _turnRecoveryThreshold;
            bool supportRiskExceedsThreshold = supportRisk >= _supportRiskRecoveryThreshold;

            if (obs.IsComOutsideSupport &&
                obs.ContactConfidence > 0f &&
                turnRiskExceedsThreshold)
            {
                return RecoverySituation.HardTurn;
            }

            if (turnRiskExceedsThreshold && supportRiskExceedsThreshold)
            {
                return RecoverySituation.HardTurn;
            }

            return RecoverySituation.None;
        }

        /// <summary>
        /// Returns the recovery duration in physics frames for a given situation.
        /// Situations with longer expected resolution get more time.
        /// </summary>
        private int GetRecoveryDuration(RecoverySituation situation)
        {
            int baseFrames = _balanceController.SnapRecoveryDurationFrames;
            switch (situation)
            {
                case RecoverySituation.HardTurn:
                    return baseFrames;
                case RecoverySituation.Reversal:
                    return Mathf.RoundToInt(baseFrames * 1.3f);
                case RecoverySituation.Slip:
                    return Mathf.RoundToInt(baseFrames * 1.2f);
                case RecoverySituation.NearFall:
                    return Mathf.RoundToInt(baseFrames * 1.5f);
                case RecoverySituation.Stumble:
                    return Mathf.RoundToInt(baseFrames * 1.5f);
                default:
                    return baseFrames;
            }
        }

        private float ComputeSupportRisk(LocomotionObservation observation)
        {
            // STEP 1: Translate the promoted support metrics into a single risk scalar that the command builder can reason about.
            float supportDeficit = 1f - observation.SupportQuality;
            float contactDeficit = 1f - observation.ContactConfidence;
            float plantedDeficit = 1f - observation.PlantedFootConfidence;
            float comOutsideRisk = observation.IsComOutsideSupport
                ? Mathf.Max(
                    observation.TurnSeverity,
                    observation.SlipEstimate,
                    plantedDeficit)
                : 0f;
            float collapseRisk = observation.IsLocomotionCollapsed ? 1f : 0f;

            return Mathf.Clamp01(Mathf.Max(
                supportDeficit,
                contactDeficit * 0.75f,
                plantedDeficit,
                observation.SlipEstimate,
                comOutsideRisk,
                collapseRisk,
                0f));
        }

        private float ComputeHeightMaintenanceScale()
        {
            // STEP 1: Compare hips height to the executor's standing target so the
            // director can boost height recovery when the character is seated or low.
            float standingHeight = _balanceController.StandingHipsHeight;
            float hipsY = _hipsBody.position.y;
            float deficit = standingHeight - hipsY;

            if (deficit <= 0f)
            {
                return 1f;
            }

            // STEP 2: Scale linearly from 1.0 at standing height to a configurable
            // maximum when the hips are well below the target. The range covers
            // the typical seated-to-standing gap.
            const float deficitRange = 0.15f;
            const float maxBoost = 1.5f;
            float t = Mathf.Clamp01(deficit / deficitRange);
            return Mathf.Lerp(1f, maxBoost, t);
        }

        private Vector3 GetSupportFacingDirection()
        {
            // STEP 1: Prefer the commanded facing direction while falling back to the current body heading when there is no explicit request.
            Vector3 supportFacing = _currentDesiredInput.FacingDirection;
            if (supportFacing.sqrMagnitude > CommandEpsilon)
            {
                return supportFacing;
            }

            return _currentObservation.BodyForward;
        }

        private Vector3 GetSupportTravelDirection(Vector3 supportFacing)
        {
            // STEP 1: Travel in the requested move direction when intent exists; otherwise keep the command aligned with facing.
            Vector3 moveDirection = _currentDesiredInput.MoveWorldDirection;
            if (moveDirection.sqrMagnitude > CommandEpsilon)
            {
                return moveDirection;
            }

            return supportFacing;
        }

        private void PushCommandsToExecutors()
        {
            _balanceController.SetBodySupportCommand(_currentBodySupportCommand);

            if (_legAnimator == null)
            {
                return;
            }

            _legAnimator.SetCommandFrame(_currentDesiredInput, _currentObservation, _leftLegCommand, _rightLegCommand);
        }

        private bool TryResolveDependencies()
        {
            if (_hipsBody == null && !TryGetComponent(out _hipsBody))
            {
                return false;
            }

            if (_playerMovement == null && !TryGetComponent(out _playerMovement))
            {
                return false;
            }

            if (_balanceController == null && !TryGetComponent(out _balanceController))
            {
                return false;
            }

            if (_characterState == null && !TryGetComponent(out _characterState))
            {
                return false;
            }

            if (_legAnimator == null)
            {
                TryGetComponent(out _legAnimator);
            }

            if (_collapseDetector == null)
            {
                TryGetComponent(out _collapseDetector);
            }

            if (_leftGroundSensor == null || _rightGroundSensor == null)
            {
                CacheFootReferences();
            }

            if (_sensorAggregator == null && _leftFootTransform != null && _rightFootTransform != null)
            {
                _sensorAggregator = new LocomotionSensorAggregator(
                    _hipsBody,
                    _balanceController,
                    _leftFootTransform,
                    _rightFootTransform,
                    _leftGroundSensor,
                    _rightGroundSensor);
            }

            if (_supportObservationFilter == null)
            {
                _supportObservationFilter = new SupportObservationFilter(
                    _contactConfidenceRiseSpeed,
                    _contactConfidenceFallSpeed,
                    _plantedConfidenceRiseSpeed,
                    _plantedConfidenceFallSpeed,
                    _plantedEnterThreshold,
                    _plantedExitThreshold);
            }

            if (_transitionGuard == null)
            {
                _transitionGuard = new RecoveryTransitionGuard();
            }

            return _sensorAggregator != null && _supportObservationFilter != null;
        }

        private void CacheFootReferences()
        {
            Transform[] children = GetComponentsInChildren<Transform>(includeInactive: true);
            for (int i = 0; i < children.Length; i++)
            {
                Transform child = children[i];
                if (_leftFootTransform == null && child.name == "Foot_L")
                {
                    _leftFootTransform = child;
                    child.TryGetComponent(out _leftGroundSensor);
                }
                else if (_rightFootTransform == null && child.name == "Foot_R")
                {
                    _rightFootTransform = child;
                    child.TryGetComponent(out _rightGroundSensor);
                }
            }
        }

        private void ResetOutputs()
        {
            Vector3 fallbackFacing = GetFallbackFacing();
            CharacterStateType fallbackState = _characterState != null
                ? _characterState.CurrentState
                : CharacterStateType.Standing;

            _currentDesiredInput = new DesiredInput(Vector2.zero, Vector3.zero, fallbackFacing, false);
            _currentSensorSnapshot = default;
            _currentObservation = new LocomotionObservation(
                fallbackState,
                false,
                false,
                false,
                false,
                0f,
                Vector3.zero,
                Vector3.zero,
                fallbackFacing,
                Vector3.up);
            _currentBodySupportCommand = BodySupportCommand.PassThrough(fallbackFacing);
            _leftLegCommand = LegCommandOutput.Disabled(LocomotionLeg.Left);
            _rightLegCommand = LegCommandOutput.Disabled(LocomotionLeg.Right);
            _currentPredictedDriftDirection = Vector3.zero;
            _currentObservationTelemetryLine = string.Empty;
            _nextObservationTelemetryTime = 0f;
            _currentRecoveryState = RecoveryState.Inactive;
            _transitionGuard?.Reset();
            HasCommandFrame = false;
        }

        private static float ComputeRecoveryBlend(int framesRemaining)
        {
            const int RampFrames = 10;
            if (framesRemaining <= 0)
            {
                return 0f;
            }

            if (framesRemaining >= RampFrames)
            {
                return 1f;
            }

            return (float)framesRemaining / RampFrames;
        }

        private Vector3 GetFallbackFacing()
        {
            Vector3 planarForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (planarForward.sqrMagnitude > CommandEpsilon)
            {
                return planarForward.normalized;
            }

            return Vector3.forward;
        }
    }
}