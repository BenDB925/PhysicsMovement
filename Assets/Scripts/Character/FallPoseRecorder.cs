using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Records parseable fall diagnostics around character instability events.
    /// This component belongs on the hips object and samples knee/foot positions
    /// relative to the hips basis at a configurable FixedUpdate tick rate.
    /// It keeps a rolling pre-trigger buffer, can auto-trigger when the character
    /// enters <see cref="CharacterStateType.Fallen"/>, and supports a manual trigger
    /// so investigators can dump the previous and next few seconds of pose data.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class FallPoseRecorder : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Enables rolling fall diagnostics. When disabled, the component stays inert.")]
        private bool _enableDiagnostics = false;

        [SerializeField]
        [Tooltip("When enabled, emits each NDJSON line to Debug.Log as well as any file output.")]
        private bool _logToConsole = false;

        [SerializeField]
        [Tooltip("When enabled, appends NDJSON output under the workspace Logs folder.")]
        private bool _logToFile = true;

        [SerializeField]
        [Tooltip("Clears the output file on Start so each run begins with a fresh trace.")]
        private bool _clearLogOnStart = true;

        [SerializeField]
        [Tooltip("NDJSON filename written under ../Logs relative to the Unity project.")]
        private string _logFileName = "fall-pose-log.ndjson";

        [SerializeField, Range(1, 100)]
        [Tooltip("Capture one sample every N FixedUpdate ticks. 1 = every physics frame.")]
        private int _sampleEveryFixedTicks = 5;

        [SerializeField, Range(0f, 10f)]
        [Tooltip("Seconds of samples to keep before a trigger fires.")]
        private float _preTriggerSeconds = 2f;

        [SerializeField, Range(0f, 10f)]
        [Tooltip("Seconds of samples to keep writing after a trigger fires.")]
        private float _postTriggerSeconds = 2f;

        [SerializeField]
        [Tooltip("When enabled, starts a capture session when CharacterState enters Fallen.")]
        private bool _autoTriggerOnFallen = true;

        [SerializeField]
        [Tooltip("When enabled, writes a low-rate continuous sample stream outside capture sessions.")]
        private bool _recordContinuousSamples = false;

        [SerializeField]
        [Tooltip("When enabled, pressing the manual trigger key starts a capture session.")]
        private bool _allowManualTrigger = true;

        [SerializeField]
        [Tooltip("Input System key used to trigger a manual rolling capture.")]
        private Key _manualTriggerKey = Key.Backquote;

        private readonly List<PoseSample> _rollingSamples = new List<PoseSample>();

        private Rigidbody _hipsBody;
        private BalanceController _balanceController;
        private CharacterState _characterState;
        private PlayerMovement _playerMovement;

        private Transform _lowerLegLTransform;
        private Transform _lowerLegRTransform;
        private Transform _footLTransform;
        private Transform _footRTransform;
        private ConfigurableJoint _lowerLegLJoint;
        private ConfigurableJoint _lowerLegRJoint;

        private string _logFilePath;
        private int _fixedTick;
        private int _sampleSequence;
        private int _activeSessionId;
        private int _completedSessionCount;
        private int _remainingPostSamples;
        private bool _captureActive;

        /// <summary>
        /// True while a rolling capture session is actively writing future samples.
        /// </summary>
        public bool IsCaptureActive => _captureActive;

        /// <summary>
        /// Number of completed capture sessions written by this recorder since startup.
        /// </summary>
        public int CompletedSessionCount => _completedSessionCount;

        /// <summary>
        /// Number of currently buffered pre-trigger samples retained in memory.
        /// </summary>
        public int BufferedSampleCount => _rollingSamples.Count;

        /// <summary>
        /// Absolute path to the active NDJSON output file.
        /// </summary>
        public string LogFilePath => _logFilePath;

        private void Awake()
        {
            // STEP 1: Cache runtime collaborators and the output path.
            TryGetComponent(out _hipsBody);
            TryGetComponent(out _balanceController);
            TryGetComponent(out _characterState);
            TryGetComponent(out _playerMovement);
            RefreshLogFilePath();

            // STEP 2: Resolve the leg and foot transforms by the prefab segment names.
            CacheSegmentReferences();
        }

        private void OnEnable()
        {
            // STEP 1: Subscribe to CharacterState transitions so fall entry is timestamped exactly.
            if (_characterState != null)
            {
                _characterState.OnStateChanged += OnCharacterStateChanged;
            }
        }

        private void Start()
        {
            // STEP 1: Refresh any late-applied inspector or test configuration before logging starts.
            RefreshLogFilePath();
            CacheSegmentReferences();

            // STEP 2: Prepare a fresh log file when file logging is enabled.
            if (_enableDiagnostics && _logToFile && _clearLogOnStart)
            {
                EnsureLogDirectoryExists();
                File.WriteAllText(_logFilePath, string.Empty);
            }
        }

        private void Update()
        {
            // STEP 1: Allow investigators to arm a rolling dump manually during live runs.
            if (!_enableDiagnostics || !_allowManualTrigger)
            {
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard[_manualTriggerKey].wasPressedThisFrame)
            {
                TriggerRollingCapture($"manual:{_manualTriggerKey}");
            }
        }

        private void FixedUpdate()
        {
            // STEP 1: Skip all work when diagnostics are disabled.
            if (!_enableDiagnostics)
            {
                return;
            }

            _fixedTick++;

            // STEP 2: Sample on the configured tick cadence and maintain the rolling pre-buffer.
            if (_fixedTick % _sampleEveryFixedTicks != 0)
            {
                return;
            }

            PoseSample sample = CaptureSample();
            AddRollingSample(sample);

            if (_recordContinuousSamples)
            {
                WriteJsonLine(BuildSampleJson(0, "continuous", string.Empty, sample));
            }

            // STEP 3: When a capture session is active, keep writing future samples until the window expires.
            if (_captureActive)
            {
                WriteJsonLine(BuildSampleJson(_activeSessionId, "post", string.Empty, sample));

                if (_remainingPostSamples > 0)
                {
                    _remainingPostSamples--;
                }

                if (_remainingPostSamples <= 0)
                {
                    EndCaptureSession("post-window-complete");
                }
            }
        }

        private void OnDisable()
        {
            // STEP 1: Unsubscribe cleanly when this component is disabled or destroyed.
            if (_characterState != null)
            {
                _characterState.OnStateChanged -= OnCharacterStateChanged;
            }
        }

        private void OnValidate()
        {
            _sampleEveryFixedTicks = Mathf.Max(1, _sampleEveryFixedTicks);
            _preTriggerSeconds = Mathf.Max(0f, _preTriggerSeconds);
            _postTriggerSeconds = Mathf.Max(0f, _postTriggerSeconds);
            if (string.IsNullOrWhiteSpace(_logFileName))
            {
                _logFileName = "fall-pose-log.ndjson";
            }

            if (Application.isPlaying)
            {
                RefreshLogFilePath();
            }
        }

        /// <summary>
        /// Starts a rolling capture session immediately, writing the current pre-buffer,
        /// the trigger snapshot, and the configured future window.
        /// </summary>
        /// <param name="reason">Short label included in the capture markers.</param>
        public void TriggerRollingCapture(string reason)
        {
            // STEP 1: Ignore manual or automatic triggers when diagnostics are disabled.
            if (!_enableDiagnostics)
            {
                return;
            }

            // STEP 2: If a session is already active, extend the future window instead of starting a duplicate session.
            if (_captureActive)
            {
                _remainingPostSamples = Mathf.Max(_remainingPostSamples, GetPostTriggerSampleCount());
                WriteJsonLine(BuildCaptureMarkerJson("capture-extend", reason, _activeSessionId));
                return;
            }

            // STEP 3: Start a new session, flush the rolling history, and capture an immediate trigger snapshot.
            _activeSessionId++;
            _captureActive = true;
            _remainingPostSamples = GetPostTriggerSampleCount();

            WriteJsonLine(BuildCaptureMarkerJson("capture-start", reason, _activeSessionId));
            for (int i = 0; i < _rollingSamples.Count; i++)
            {
                WriteJsonLine(BuildSampleJson(_activeSessionId, "pre", reason, _rollingSamples[i]));
            }

            PoseSample triggerSample = CaptureSample();
            AddRollingSample(triggerSample);
            WriteJsonLine(BuildSampleJson(_activeSessionId, "trigger", reason, triggerSample));

            if (_remainingPostSamples <= 0)
            {
                EndCaptureSession("no-post-window");
            }
        }

        [ContextMenu("Trigger Rolling Capture")]
        private void TriggerRollingCaptureFromContextMenu()
        {
            TriggerRollingCapture("context-menu");
        }

        private void OnCharacterStateChanged(CharacterStateType previousState, CharacterStateType newState)
        {
            // STEP 1: Log state transitions as markers so the NDJSON stream is self-describing.
            if (!_enableDiagnostics)
            {
                return;
            }

            WriteJsonLine(BuildStateTransitionJson(previousState, newState));

            // STEP 2: Auto-trigger on Fallen entry so instability windows are captured without manual timing.
            if (_autoTriggerOnFallen && newState == CharacterStateType.Fallen && previousState != CharacterStateType.Fallen)
            {
                TriggerRollingCapture("state-entered-fallen");
            }
        }

        private void CacheSegmentReferences()
        {
            _lowerLegLTransform = null;
            _lowerLegRTransform = null;
            _footLTransform = null;
            _footRTransform = null;
            _lowerLegLJoint = null;
            _lowerLegRJoint = null;

            Transform[] transforms = GetComponentsInChildren<Transform>(includeInactive: true);
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform segment = transforms[i];
                switch (segment.name)
                {
                    case "LowerLeg_L":
                        _lowerLegLTransform = segment;
                        break;
                    case "LowerLeg_R":
                        _lowerLegRTransform = segment;
                        break;
                    case "Foot_L":
                        _footLTransform = segment;
                        break;
                    case "Foot_R":
                        _footRTransform = segment;
                        break;
                }
            }

            if (_lowerLegLTransform != null)
            {
                _lowerLegLJoint = _lowerLegLTransform.GetComponent<ConfigurableJoint>();
            }

            if (_lowerLegRTransform != null)
            {
                _lowerLegRJoint = _lowerLegRTransform.GetComponent<ConfigurableJoint>();
            }
        }

        private void RefreshLogFilePath()
        {
            _logFilePath = Path.Combine(Application.dataPath, "..", "Logs", _logFileName);
        }

        private PoseSample CaptureSample()
        {
            Vector3 hipsPosition = _hipsBody != null ? _hipsBody.position : transform.position;
            Vector3 hipsForward = transform.forward;
            Vector3 hipsRight = transform.right;
            Vector3 hipsUp = transform.up;

            Vector3 leftKneeWorld = ResolveKneeWorldPosition(_lowerLegLTransform, _lowerLegLJoint);
            Vector3 rightKneeWorld = ResolveKneeWorldPosition(_lowerLegRTransform, _lowerLegRJoint);
            Vector3 leftFootWorld = _footLTransform != null ? _footLTransform.position : Vector3.zero;
            Vector3 rightFootWorld = _footRTransform != null ? _footRTransform.position : Vector3.zero;

            return new PoseSample
            {
                SampleSequence = ++_sampleSequence,
                FixedTick = _fixedTick,
                Frame = Time.frameCount,
                Time = Time.time,
                State = _characterState != null ? _characterState.CurrentState : CharacterStateType.Standing,
                Grounded = _balanceController != null && _balanceController.IsGrounded,
                Fallen = _balanceController != null && _balanceController.IsFallen,
                UprightAngleDeg = Vector3.Angle(hipsUp, Vector3.up),
                MoveInput = _playerMovement != null ? _playerMovement.CurrentMoveInput : Vector2.zero,
                HipsWorld = hipsPosition,
                HipsForward = hipsForward,
                HipsRight = hipsRight,
                HipsUp = hipsUp,
                Velocity = _hipsBody != null ? _hipsBody.linearVelocity : Vector3.zero,
                AngularVelocity = _hipsBody != null ? _hipsBody.angularVelocity : Vector3.zero,
                LeftKneeWorld = leftKneeWorld,
                LeftKneeRelative = ToHipsBasis(leftKneeWorld - hipsPosition, hipsForward, hipsRight, hipsUp),
                RightKneeWorld = rightKneeWorld,
                RightKneeRelative = ToHipsBasis(rightKneeWorld - hipsPosition, hipsForward, hipsRight, hipsUp),
                LeftFootWorld = leftFootWorld,
                LeftFootRelative = ToHipsBasis(leftFootWorld - hipsPosition, hipsForward, hipsRight, hipsUp),
                RightFootWorld = rightFootWorld,
                RightFootRelative = ToHipsBasis(rightFootWorld - hipsPosition, hipsForward, hipsRight, hipsUp)
            };
        }

        private void AddRollingSample(PoseSample sample)
        {
            int capacity = GetPreTriggerSampleCapacity();
            if (capacity <= 0)
            {
                return;
            }

            while (_rollingSamples.Count >= capacity)
            {
                _rollingSamples.RemoveAt(0);
            }

            _rollingSamples.Add(sample);
        }

        private int GetPreTriggerSampleCapacity()
        {
            if (_preTriggerSeconds <= 0f)
            {
                return 0;
            }

            float samplePeriod = Mathf.Max(0.0001f, Time.fixedDeltaTime * _sampleEveryFixedTicks);
            return Mathf.Max(1, Mathf.CeilToInt(_preTriggerSeconds / samplePeriod));
        }

        private int GetPostTriggerSampleCount()
        {
            if (_postTriggerSeconds <= 0f)
            {
                return 0;
            }

            float samplePeriod = Mathf.Max(0.0001f, Time.fixedDeltaTime * _sampleEveryFixedTicks);
            return Mathf.Max(1, Mathf.CeilToInt(_postTriggerSeconds / samplePeriod));
        }

        private void EndCaptureSession(string reason)
        {
            WriteJsonLine(BuildCaptureMarkerJson("capture-end", reason, _activeSessionId));
            _captureActive = false;
            _remainingPostSamples = 0;
            _completedSessionCount++;
        }

        private Vector3 ResolveKneeWorldPosition(Transform lowerLegTransform, ConfigurableJoint lowerLegJoint)
        {
            if (lowerLegJoint != null)
            {
                return lowerLegJoint.transform.TransformPoint(lowerLegJoint.anchor);
            }

            return lowerLegTransform != null ? lowerLegTransform.position : Vector3.zero;
        }

        private static Vector3 ToHipsBasis(Vector3 offset, Vector3 hipsForward, Vector3 hipsRight, Vector3 hipsUp)
        {
            return new Vector3(
                Vector3.Dot(offset, hipsForward),
                Vector3.Dot(offset, hipsRight),
                Vector3.Dot(offset, hipsUp));
        }

        private string BuildCaptureMarkerJson(string markerType, string reason, int sessionId)
        {
            return "{" +
                   $"\"type\":\"{EscapeJson(markerType)}\"," +
                   $"\"session\":{sessionId}," +
                   $"\"reason\":\"{EscapeJson(reason)}\"," +
                   $"\"frame\":{Time.frameCount}," +
                   $"\"fixedTick\":{_fixedTick}," +
                   $"\"time\":{FormatFloat(Time.time)}," +
                   $"\"sampleEveryFixedTicks\":{_sampleEveryFixedTicks}," +
                   $"\"preTriggerSeconds\":{FormatFloat(_preTriggerSeconds)}," +
                   $"\"postTriggerSeconds\":{FormatFloat(_postTriggerSeconds)}" +
                   "}";
        }

        private string BuildStateTransitionJson(CharacterStateType previousState, CharacterStateType newState)
        {
            return "{" +
                   "\"type\":\"state-transition\"," +
                   $"\"frame\":{Time.frameCount}," +
                   $"\"fixedTick\":{_fixedTick}," +
                   $"\"time\":{FormatFloat(Time.time)}," +
                   $"\"from\":\"{EscapeJson(previousState.ToString())}\"," +
                   $"\"to\":\"{EscapeJson(newState.ToString())}\"" +
                   "}";
        }

        private string BuildSampleJson(int sessionId, string phase, string reason, PoseSample sample)
        {
            return "{" +
                   "\"type\":\"pose-sample\"," +
                   $"\"session\":{sessionId}," +
                   $"\"phase\":\"{EscapeJson(phase)}\"," +
                   $"\"reason\":\"{EscapeJson(reason)}\"," +
                   $"\"sample\":{sample.SampleSequence}," +
                   $"\"frame\":{sample.Frame}," +
                   $"\"fixedTick\":{sample.FixedTick}," +
                   $"\"time\":{FormatFloat(sample.Time)}," +
                   $"\"state\":\"{EscapeJson(sample.State.ToString())}\"," +
                   $"\"grounded\":{FormatBool(sample.Grounded)}," +
                   $"\"fallen\":{FormatBool(sample.Fallen)}," +
                   $"\"uprightAngleDeg\":{FormatFloat(sample.UprightAngleDeg)}," +
                   $"\"moveInput\":{FormatVector2(sample.MoveInput)}," +
                   "\"hips\":{" +
                       $"\"world\":{FormatVector3(sample.HipsWorld)}," +
                       $"\"forward\":{FormatVector3(sample.HipsForward)}," +
                       $"\"right\":{FormatVector3(sample.HipsRight)}," +
                       $"\"up\":{FormatVector3(sample.HipsUp)}," +
                       $"\"velocity\":{FormatVector3(sample.Velocity)}," +
                       $"\"angularVelocity\":{FormatVector3(sample.AngularVelocity)}" +
                   "}," +
                   "\"leftKnee\":{" +
                       $"\"world\":{FormatVector3(sample.LeftKneeWorld)}," +
                       $"\"relative\":{FormatVector3(sample.LeftKneeRelative)}" +
                   "}," +
                   "\"rightKnee\":{" +
                       $"\"world\":{FormatVector3(sample.RightKneeWorld)}," +
                       $"\"relative\":{FormatVector3(sample.RightKneeRelative)}" +
                   "}," +
                   "\"leftFoot\":{" +
                       $"\"world\":{FormatVector3(sample.LeftFootWorld)}," +
                       $"\"relative\":{FormatVector3(sample.LeftFootRelative)}" +
                   "}," +
                   "\"rightFoot\":{" +
                       $"\"world\":{FormatVector3(sample.RightFootWorld)}," +
                       $"\"relative\":{FormatVector3(sample.RightFootRelative)}" +
                   "}" +
                   "}";
        }

        private void WriteJsonLine(string line)
        {
            if (_logToConsole)
            {
                Debug.Log(line, this);
            }

            if (!_logToFile)
            {
                return;
            }

            try
            {
                EnsureLogDirectoryExists();
                File.AppendAllText(_logFilePath, line + Environment.NewLine);
            }
            catch (IOException ex)
            {
                Debug.LogWarning($"[FallPoseRecorder] Failed to write diagnostics: {ex.Message}", this);
            }
        }

        private void EnsureLogDirectoryExists()
        {
            string directory = Path.GetDirectoryName(_logFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string FormatBool(bool value)
        {
            return value ? "true" : "false";
        }

        private static string FormatFloat(float value)
        {
            return value.ToString("0.0000", CultureInfo.InvariantCulture);
        }

        private static string FormatVector2(Vector2 value)
        {
            return $"[{FormatFloat(value.x)},{FormatFloat(value.y)}]";
        }

        private static string FormatVector3(Vector3 value)
        {
            return $"[{FormatFloat(value.x)},{FormatFloat(value.y)},{FormatFloat(value.z)}]";
        }

        private struct PoseSample
        {
            public int SampleSequence;
            public int FixedTick;
            public int Frame;
            public float Time;
            public CharacterStateType State;
            public bool Grounded;
            public bool Fallen;
            public float UprightAngleDeg;
            public Vector2 MoveInput;
            public Vector3 HipsWorld;
            public Vector3 HipsForward;
            public Vector3 HipsRight;
            public Vector3 HipsUp;
            public Vector3 Velocity;
            public Vector3 AngularVelocity;
            public Vector3 LeftKneeWorld;
            public Vector3 LeftKneeRelative;
            public Vector3 RightKneeWorld;
            public Vector3 RightKneeRelative;
            public Vector3 LeftFootWorld;
            public Vector3 LeftFootRelative;
            public Vector3 RightFootWorld;
            public Vector3 RightFootRelative;
        }
    }
}