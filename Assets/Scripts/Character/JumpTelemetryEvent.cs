using System.Globalization;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Named stages emitted while PlayerMovement evaluates and executes jump attempts.
    /// </summary>
    internal enum JumpTelemetryEventType
    {
        RequestRejected = 0,
        JumpAccepted = 1,
        WindUpEntered = 2,
        WindUpAborted = 3,
        LaunchFired = 4,
    }

    /// <summary>
    /// Immutable single-event payload for jump-request and jump-wind-up telemetry.
    /// </summary>
    internal readonly struct JumpTelemetryEvent
    {
        internal JumpTelemetryEvent(
            int attemptId,
            int frameNumber,
            float time,
            JumpTelemetryEventType eventType,
            string reason,
            CharacterStateType characterState,
            bool isGrounded,
            bool isFallen,
            JumpPhase jumpPhase)
        {
            AttemptId = attemptId;
            FrameNumber = frameNumber;
            Time = time;
            EventType = eventType;
            Reason = reason ?? string.Empty;
            CharacterState = characterState;
            IsGrounded = isGrounded;
            IsFallen = isFallen;
            JumpPhase = jumpPhase;
        }

        internal int AttemptId { get; }

        internal int FrameNumber { get; }

        internal float Time { get; }

        internal JumpTelemetryEventType EventType { get; }

        internal string Reason { get; }

        internal CharacterStateType CharacterState { get; }

        internal bool IsGrounded { get; }

        internal bool IsFallen { get; }

        internal JumpPhase JumpPhase { get; }

        internal string ToNdjsonLine()
        {
            return "{" +
                "\"AttemptId\":" + AttemptId.ToString(CultureInfo.InvariantCulture) + "," +
                "\"FrameNumber\":" + FrameNumber.ToString(CultureInfo.InvariantCulture) + "," +
                "\"Time\":" + Time.ToString(CultureInfo.InvariantCulture) + "," +
                "\"EventType\":\"" + EscapeJsonString(EventType.ToString()) + "\"," +
                "\"Reason\":\"" + EscapeJsonString(Reason) + "\"," +
                "\"CharacterState\":\"" + EscapeJsonString(CharacterState.ToString()) + "\"," +
                "\"IsGrounded\":" + (IsGrounded ? "true" : "false") + "," +
                "\"IsFallen\":" + (IsFallen ? "true" : "false") + "," +
                "\"JumpPhase\":\"" + EscapeJsonString(JumpPhase.ToString()) + "\"" +
                "}";
        }

        private static string EscapeJsonString(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}