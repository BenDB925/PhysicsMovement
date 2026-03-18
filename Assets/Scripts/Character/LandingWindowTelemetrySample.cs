using System.Globalization;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Immutable one-frame snapshot of the landing posture inputs that feed BalanceController.
    /// </summary>
    internal readonly struct LandingWindowTelemetrySample
    {
        internal LandingWindowTelemetrySample(
            int frameNumber,
            float time,
            float uprightAngle,
            float desiredLeanDegrees,
            float landingAbsorbBlend,
            float totalPelvisTilt,
            float recoveryBlend,
            float recoveryKdBlend,
            bool isGrounded,
            bool isSurrendered,
            CharacterStateType characterState)
        {
            FrameNumber = frameNumber;
            Time = time;
            UprightAngle = uprightAngle;
            DesiredLeanDegrees = desiredLeanDegrees;
            LandingAbsorbBlend = landingAbsorbBlend;
            TotalPelvisTilt = totalPelvisTilt;
            RecoveryBlend = recoveryBlend;
            RecoveryKdBlend = recoveryKdBlend;
            IsGrounded = isGrounded;
            IsSurrendered = isSurrendered;
            CharacterState = characterState;
        }

        internal int FrameNumber { get; }

        internal float Time { get; }

        internal float UprightAngle { get; }

        internal float DesiredLeanDegrees { get; }

        internal float LandingAbsorbBlend { get; }

        internal float TotalPelvisTilt { get; }

        internal float RecoveryBlend { get; }

        internal float RecoveryKdBlend { get; }

        internal bool IsGrounded { get; }

        internal bool IsSurrendered { get; }

        internal CharacterStateType CharacterState { get; }

        internal string ToNdjsonLine()
        {
            return "{" +
                "\"FrameNumber\":" + FrameNumber.ToString(CultureInfo.InvariantCulture) + "," +
                "\"Time\":" + Time.ToString(CultureInfo.InvariantCulture) + "," +
                "\"UprightAngle\":" + UprightAngle.ToString(CultureInfo.InvariantCulture) + "," +
                "\"DesiredLeanDegrees\":" + DesiredLeanDegrees.ToString(CultureInfo.InvariantCulture) + "," +
                "\"LandingAbsorbBlend\":" + LandingAbsorbBlend.ToString(CultureInfo.InvariantCulture) + "," +
                "\"TotalPelvisTilt\":" + TotalPelvisTilt.ToString(CultureInfo.InvariantCulture) + "," +
                "\"RecoveryBlend\":" + RecoveryBlend.ToString(CultureInfo.InvariantCulture) + "," +
                "\"RecoveryKdBlend\":" + RecoveryKdBlend.ToString(CultureInfo.InvariantCulture) + "," +
                "\"IsGrounded\":" + (IsGrounded ? "true" : "false") + "," +
                "\"IsSurrendered\":" + (IsSurrendered ? "true" : "false") + "," +
                "\"CharacterState\":\"" + EscapeJsonString(CharacterState.ToString()) + "\"" +
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