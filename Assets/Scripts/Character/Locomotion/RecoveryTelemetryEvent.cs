using System.Globalization;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Immutable single-event payload for structured locomotion recovery telemetry.
    /// </summary>
    internal readonly struct RecoveryTelemetryEvent
    {
        /// <summary>
        /// Creates a structured recovery telemetry payload from the current observation snapshot.
        /// </summary>
        public RecoveryTelemetryEvent(
            int frameNumber,
            float time,
            RecoverySituation situation,
            string reason,
            float uprightAngle,
            float slipEstimate,
            float supportQuality,
            float turnSeverity,
            float recoveryDurationSoFar,
            bool wasSurrender)
        {
            FrameNumber = frameNumber;
            Time = time;
            Situation = situation;
            Reason = reason ?? string.Empty;
            UprightAngle = uprightAngle;
            SlipEstimate = slipEstimate;
            SupportQuality = supportQuality;
            TurnSeverity = turnSeverity;
            RecoveryDurationSoFar = recoveryDurationSoFar < 0f ? 0f : recoveryDurationSoFar;
            WasSurrender = wasSurrender;
        }

        /// <summary>The fixed-update frame index when the event was emitted.</summary>
        public int FrameNumber { get; }

        /// <summary>The Unity time in seconds when the event was emitted.</summary>
        public float Time { get; }

        /// <summary>The classified recovery situation active for the event.</summary>
        public RecoverySituation Situation { get; }

        /// <summary>Short reason tag describing why the event was emitted.</summary>
        public string Reason { get; }

        /// <summary>The current upright-angle observation in degrees.</summary>
        public float UprightAngle { get; }

        /// <summary>The current slip estimate in the 0..1 risk band.</summary>
        public float SlipEstimate { get; }

        /// <summary>The current support-quality observation in the 0..1 band.</summary>
        public float SupportQuality { get; }

        /// <summary>The current turn-severity observation in the 0..1 band.</summary>
        public float TurnSeverity { get; }

        /// <summary>The elapsed recovery duration in seconds when the event was emitted.</summary>
        public float RecoveryDurationSoFar { get; }

        /// <summary>True when the event records a recovery ending in surrender.</summary>
        public bool WasSurrender { get; }

        /// <summary>
        /// Serializes the event into a single NDJSON line without taking a JSON dependency.
        /// </summary>
        public string ToNdjsonLine()
        {
            return "{" +
                "\"FrameNumber\":" + FrameNumber.ToString(CultureInfo.InvariantCulture) + "," +
                "\"Time\":" + Time.ToString(CultureInfo.InvariantCulture) + "," +
                "\"Situation\":\"" + EscapeJsonString(Situation.ToString()) + "\"," +
                "\"Reason\":\"" + EscapeJsonString(Reason) + "\"," +
                "\"UprightAngle\":" + UprightAngle.ToString(CultureInfo.InvariantCulture) + "," +
                "\"SlipEstimate\":" + SlipEstimate.ToString(CultureInfo.InvariantCulture) + "," +
                "\"SupportQuality\":" + SupportQuality.ToString(CultureInfo.InvariantCulture) + "," +
                "\"TurnSeverity\":" + TurnSeverity.ToString(CultureInfo.InvariantCulture) + "," +
                "\"RecoveryDurationSoFar\":" + RecoveryDurationSoFar.ToString(CultureInfo.InvariantCulture) + "," +
                "\"WasSurrender\":" + (WasSurrender ? "true" : "false") +
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