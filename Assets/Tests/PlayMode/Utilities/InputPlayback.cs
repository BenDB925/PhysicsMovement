using System;
using System.IO;
using PhysicsDrivenMovement.Character;
using UnityEngine;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// Loads recorded fixed-step player input from JSON and replays it frame-for-frame through
    /// the PlayerMovement test seams used by the PlayMode character suites.
    /// </summary>
    public sealed class InputPlayback
    {
        private readonly RecordedFrame[] _frames;

        private InputPlayback(float recordedFixedDeltaTime, RecordedFrame[] frames)
        {
            RecordedFixedDeltaTime = recordedFixedDeltaTime;
            _frames = frames ?? Array.Empty<RecordedFrame>();
        }

        /// <summary>
        /// Total number of recorded fixed-step frames available for playback.
        /// </summary>
        public int FrameCount => _frames.Length;

        /// <summary>
        /// FixedUpdate timestep that was active when the recording was captured.
        /// </summary>
        public float RecordedFixedDeltaTime { get; }

        /// <summary>
        /// Loads a named recording from Assets/Tests/PlayMode/Recordings.
        /// </summary>
        /// <param name="recordingName">Filename without the .json extension.</param>
        /// <returns>The parsed playback wrapper for the recording.</returns>
        public static InputPlayback Load(string recordingName)
        {
            // STEP 1: Resolve and read the recording JSON from the shared PlayMode recordings folder.
            string recordingPath = GetRecordingPath(recordingName);
            if (!File.Exists(recordingPath))
            {
                throw new FileNotFoundException($"Input recording '{recordingName}' was not found.", recordingPath);
            }

            string json = File.ReadAllText(recordingPath);
            RecordingData recording = JsonUtility.FromJson<RecordingData>(json);
            if (recording == null)
            {
                throw new InvalidDataException($"Input recording '{recordingName}' could not be deserialized.");
            }

            // STEP 2: Normalize null frame arrays so tests can still reason about empty recordings.
            return new InputPlayback(recording.fixedDeltaTime, recording.frames ?? Array.Empty<RecordedFrame>());
        }

        /// <summary>
        /// Applies one recorded frame to the supplied PlayerMovement test seams.
        /// </summary>
        /// <param name="frameIndex">Zero-based frame index within the recording.</param>
        /// <param name="movement">Player movement component receiving the test input.</param>
        public void ApplyFrame(int frameIndex, PlayerMovement movement)
        {
            // STEP 1: Guard the playback boundary so malformed callers fail loudly.
            if (movement == null)
            {
                throw new ArgumentNullException(nameof(movement));
            }

            if (frameIndex < 0 || frameIndex >= _frames.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(frameIndex));
            }

            // STEP 2: Push the recorded move vector and one-frame jump pulse into PlayerMovement.
            RecordedFrame frame = _frames[frameIndex];
            movement.SetMoveInputForTest(frame.ToMoveInput());
            movement.SetJumpInputForTest(frame.jump);
        }

        internal static string GetRecordingPath(string recordingName)
        {
            string safeName = string.IsNullOrWhiteSpace(recordingName)
                ? throw new ArgumentException("Recording name is required.", nameof(recordingName))
                : recordingName.Trim();

            return Path.Combine(Application.dataPath, "Tests", "PlayMode", "Recordings", safeName + ".json");
        }

        [Serializable]
        private sealed class RecordingData
        {
            public float fixedDeltaTime = 0f;
            public RecordedFrame[] frames = Array.Empty<RecordedFrame>();
        }

        [Serializable]
        private sealed class RecordedFrame
        {
            public float[] move = Array.Empty<float>();
            public bool jump = false;

            public Vector2 ToMoveInput()
            {
                float moveX = move != null && move.Length > 0 ? move[0] : 0f;
                float moveY = move != null && move.Length > 1 ? move[1] : 0f;
                return new Vector2(moveX, moveY);
            }
        }
    }
}