using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Per-situation strength overrides applied by <see cref="LocomotionDirector"/>
    /// while a typed <see cref="RecoverySituation"/> is active.
    /// Each profile multiplies or clamps the base risk-driven scales so different
    /// recovery situations produce distinct executor behavior without requiring
    /// the executors to interpret the enum directly.
    /// Collaborators: <see cref="LocomotionDirector"/>, <see cref="RecoverySituation"/>.
    /// </summary>
    internal readonly struct RecoveryResponseProfile
    {
        /// <summary>Multiplier on the base upright-boost scale (1.0 = no change).</summary>
        public readonly float UprightBoostMultiplier;

        /// <summary>Floor for yaw-strength scale during this recovery (lower = more yaw suppression).</summary>
        public readonly float MinYawStrengthScale;

        /// <summary>Multiplier on the base stabilization-boost scale (1.0 = no change).</summary>
        public readonly float StabilizationBoostMultiplier;

        /// <summary>Multiplier on the desired lean degrees (1.0 = no change, 0 = suppress lean).</summary>
        public readonly float LeanDegreesMultiplier;

        public RecoveryResponseProfile(
            float uprightBoostMultiplier,
            float minYawStrengthScale,
            float stabilizationBoostMultiplier,
            float leanDegreesMultiplier)
        {
            UprightBoostMultiplier = uprightBoostMultiplier;
            MinYawStrengthScale = Mathf.Clamp01(minYawStrengthScale);
            StabilizationBoostMultiplier = stabilizationBoostMultiplier;
            LeanDegreesMultiplier = Mathf.Max(0f, leanDegreesMultiplier);
        }

        /// <summary>Neutral profile that does not alter any base scale.</summary>
        public static RecoveryResponseProfile Neutral => new RecoveryResponseProfile(1f, 0f, 1f, 1f);

        /// <summary>
        /// Returns the dedicated response profile for the given <paramref name="situation"/>.
        /// </summary>
        public static RecoveryResponseProfile For(RecoverySituation situation)
        {
            switch (situation)
            {
                // DESIGN: HardTurn suppresses yaw to let the body swing through the turn
                // and adds moderate lean to shift COM into the new heading.
                case RecoverySituation.HardTurn:
                    return new RecoveryResponseProfile(
                        uprightBoostMultiplier: 1.0f,
                        minYawStrengthScale: 0.35f,
                        stabilizationBoostMultiplier: 1.1f,
                        leanDegreesMultiplier: 1.4f);

                // DESIGN: Reversal needs stronger yaw suppression than a hard turn because
                // the heading delta is extreme, plus extra upright boost to resist the
                // momentum whiplash.
                case RecoverySituation.Reversal:
                    return new RecoveryResponseProfile(
                        uprightBoostMultiplier: 1.3f,
                        minYawStrengthScale: 0.25f,
                        stabilizationBoostMultiplier: 1.3f,
                        leanDegreesMultiplier: 0.6f);

                // DESIGN: Slip recovery needs strong COM stabilization and upright correction
                // but should keep yaw responsive so the character can reorient quickly.
                case RecoverySituation.Slip:
                    return new RecoveryResponseProfile(
                        uprightBoostMultiplier: 1.4f,
                        minYawStrengthScale: 0.5f,
                        stabilizationBoostMultiplier: 1.5f,
                        leanDegreesMultiplier: 0.3f);

                // DESIGN: NearFall is a high-severity event — maximize upright and stabilization,
                // suppress lean (leaning while nearly falling is dangerous), and keep yaw somewhat
                // free so recovery steps can reorient.
                case RecoverySituation.NearFall:
                    return new RecoveryResponseProfile(
                        uprightBoostMultiplier: 1.6f,
                        minYawStrengthScale: 0.4f,
                        stabilizationBoostMultiplier: 1.7f,
                        leanDegreesMultiplier: 0.1f);

                // DESIGN: Stumble is the most severe — maximum upright and stabilization boost,
                // no lean at all, and moderate yaw suppression to avoid fighting the stumble direction.
                case RecoverySituation.Stumble:
                    return new RecoveryResponseProfile(
                        uprightBoostMultiplier: 1.8f,
                        minYawStrengthScale: 0.3f,
                        stabilizationBoostMultiplier: 2.0f,
                        leanDegreesMultiplier: 0f);

                default:
                    return Neutral;
            }
        }
    }
}
