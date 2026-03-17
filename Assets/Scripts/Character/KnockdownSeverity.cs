using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Static utility for computing a 0–1 knockdown severity value.
    /// Consumed by <see cref="CharacterState"/> to scale floor-dwell duration
    /// and stand-up difficulty.
    /// </summary>
    public static class KnockdownSeverity
    {
        /// <summary>
        /// Compute severity from the character's posture at the moment of surrender.
        /// Mirrors the formula in <see cref="BalanceController.ComputeSurrenderSeverity"/>.
        /// </summary>
        /// <param name="uprightAngle">Current tilt angle in degrees (0 = upright, 180 = inverted).</param>
        /// <param name="angularVelocity">Tilt-directional angular velocity in rad/s.</param>
        /// <param name="hipsHeight">Current world-space height of the hips.</param>
        /// <param name="standingHeight">Reference standing hips height.</param>
        /// <returns>Severity in [0, 1].</returns>
        public static float ComputeFromSurrender(float uprightAngle, float angularVelocity, float hipsHeight, float standingHeight)
        {
            float safeStandingHeight = Mathf.Max(0.0001f, standingHeight);
            float angleSeverity = Mathf.Clamp01((uprightAngle - 65f) / 50f) * 0.5f;
            float angularVelocitySeverity = Mathf.Clamp01(angularVelocity / 6f) * 0.3f;
            float heightSeverity = Mathf.Clamp01(1f - (hipsHeight / safeStandingHeight)) * 0.2f;
            return Mathf.Clamp01(angleSeverity + angularVelocitySeverity + heightSeverity);
        }

        /// <summary>
        /// Compute severity from an external impact's effective delta-V.
        /// Used by the future <c>ImpactKnockdownDetector</c> (Chapter 2).
        /// </summary>
        /// <param name="effectiveDeltaV">Impact delta-V after direction weighting (m/s).</param>
        /// <param name="knockdownThreshold">Minimum delta-V that causes a knockdown (m/s).</param>
        /// <returns>Severity in [0, 1].</returns>
        public static float ComputeFromImpact(float effectiveDeltaV, float knockdownThreshold)
        {
            float safeThreshold = Mathf.Max(0.0001f, knockdownThreshold);
            return Mathf.Clamp01((effectiveDeltaV - safeThreshold) / safeThreshold + 0.3f);
        }
    }
}
