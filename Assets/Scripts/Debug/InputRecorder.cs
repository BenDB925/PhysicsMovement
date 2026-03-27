using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Records fixed-step move and jump input from the Player action map and writes a JSON
    /// playthrough that PlayMode tests can replay deterministically.
    /// </summary>
    public class InputRecorder : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Output filename written under Assets/Tests/PlayMode/Recordings without the .json extension.")]
        private string _recordingName = "recording";

        [SerializeField]
        [Tooltip("Starts recording automatically when play mode begins.")]
        private bool _autoStart = false;

        private readonly List<RecordedFrame> _frames = new List<RecordedFrame>();

        private InputActionAsset _inputAsset;
        private InputActionMap _playerMap;
        private InputAction _moveAction;
        private InputAction _jumpAction;
        private bool _recording;
        private bool _jumpPressedSinceLastFixedUpdate;
        private bool _hasSavedCurrentSession;

        private void Awake()
        {
            // STEP 1: Create and enable a local Player input wrapper so the recorder samples
            // the same bindings as gameplay.
            CreateInputActions();

            // STEP 2: Arm the recorder immediately when auto-start is enabled.
            if (_autoStart)
            {
                StartRecording();
            }
        }

        private void Update()
        {
            // STEP 1: Latch jump press edges between physics ticks so playback can feed the
            // one-frame SetJumpInputForTest seam instead of a held button state.
            if (!_recording || _jumpAction == null)
            {
                return;
            }

            _jumpPressedSinceLastFixedUpdate |= _jumpAction.WasPressedThisFrame();
        }

        private void FixedUpdate()
        {
            // STEP 1: Capture one deterministic input frame per physics tick while recording.
            if (!_recording || _moveAction == null)
            {
                return;
            }

            Vector2 moveInput = _moveAction.ReadValue<Vector2>();
            bool jumpInput = _jumpPressedSinceLastFixedUpdate;
            _jumpPressedSinceLastFixedUpdate = false;

            _frames.Add(new RecordedFrame(moveInput.x, moveInput.y, jumpInput));
            _hasSavedCurrentSession = false;
        }

        [ContextMenu("Start Recording")]
        private void StartRecording()
        {
            // STEP 1: Reset the buffered frames so each recording session starts clean.
            _frames.Clear();
            _jumpPressedSinceLastFixedUpdate = false;
            _recording = true;
            _hasSavedCurrentSession = false;
        }

        [ContextMenu("Stop Recording")]
        private void StopRecording()
        {
            // STEP 1: Stop sampling and flush the current session to disk once.
            if (!_recording && (_hasSavedCurrentSession || _frames.Count == 0))
            {
                return;
            }

            _recording = false;
            SaveRecording();
        }

        private void OnApplicationQuit()
        {
            // STEP 1: Persist an in-progress recording before play mode exits.
            if (_recording && !_hasSavedCurrentSession)
            {
                _recording = false;
                SaveRecording();
            }
        }

        private void OnDestroy()
        {
            // STEP 1: Dispose input actions so the recorder releases Input System resources.
            if (_inputAsset != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_inputAsset);
                }
                else
                {
                    DestroyImmediate(_inputAsset);
                }

                _inputAsset = null;
                _playerMap = null;
                _moveAction = null;
                _jumpAction = null;
            }
        }

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(_recordingName))
            {
                _recordingName = "recording";
            }
        }

        private void SaveRecording()
        {
            // STEP 1: Serialize the recorded fixed-step inputs into the PlayMode recordings folder.
            string recordingPath = GetRecordingPath();

            try
            {
                string directoryPath = Path.GetDirectoryName(recordingPath);
                if (!string.IsNullOrWhiteSpace(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }

                InputRecording recording = new InputRecording(Time.fixedDeltaTime, _frames);
                string json = JsonUtility.ToJson(recording, true);
                File.WriteAllText(recordingPath, json);
                _hasSavedCurrentSession = true;

                Debug.Log($"[InputRecorder] Saved {_frames.Count} frames to '{recordingPath}'.", this);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[InputRecorder] Failed to save recording to '{recordingPath}': {ex.Message}", this);
            }
        }

        private string GetRecordingPath()
        {
            string safeName = SanitizeRecordingName(_recordingName);
            return Path.Combine(Application.dataPath, "Tests", "PlayMode", "Recordings", safeName + ".json");
        }

        private void CreateInputActions()
        {
            // STEP 1: Mirror the project Player/Move and Player/Jump bindings locally so this
            // recorder can sample the same input surface without depending on another assembly.
            _inputAsset = ScriptableObject.CreateInstance<InputActionAsset>();
            _playerMap = new InputActionMap("Player");

            _moveAction = _playerMap.AddAction("Move", InputActionType.Value);
            _moveAction.expectedControlType = "Vector2";
            _moveAction.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            _moveAction.AddBinding("<Gamepad>/leftStick");

            _jumpAction = _playerMap.AddAction("Jump", InputActionType.Button);
            _jumpAction.expectedControlType = "Button";
            _jumpAction.AddBinding("<Keyboard>/space");
            _jumpAction.AddBinding("<Gamepad>/buttonSouth");

            _inputAsset.AddActionMap(_playerMap);
            _inputAsset.Enable();
        }

        private static string SanitizeRecordingName(string recordingName)
        {
            if (string.IsNullOrWhiteSpace(recordingName))
            {
                return "recording";
            }

            string sanitizedName = recordingName.Trim();
            char[] invalidFileNameChars = Path.GetInvalidFileNameChars();
            for (int i = 0; i < invalidFileNameChars.Length; i++)
            {
                sanitizedName = sanitizedName.Replace(invalidFileNameChars[i].ToString(), string.Empty);
            }

            return string.IsNullOrWhiteSpace(sanitizedName) ? "recording" : sanitizedName;
        }

        [Serializable]
        private sealed class InputRecording
        {
            public float fixedDeltaTime;
            public RecordedFrame[] frames;

            public InputRecording(float fixedDeltaTime, List<RecordedFrame> frames)
            {
                this.fixedDeltaTime = fixedDeltaTime;
                this.frames = frames.ToArray();
            }
        }

        [Serializable]
        private sealed class RecordedFrame
        {
            public float[] move;
            public bool jump;

            public RecordedFrame(float moveX, float moveY, bool jump)
            {
                move = new[] { moveX, moveY };
                this.jump = jump;
            }
        }
    }
}