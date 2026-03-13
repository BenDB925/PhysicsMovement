using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Support command emitted for body-level execution systems such as BalanceController.
    /// It describes the intended facing, upright bias, and support strength without committing
    /// to any specific actuator implementation yet. Includes the classified recovery situation
    /// so executors can adapt their response without re-deriving the cause.
    /// </summary>
    internal readonly struct BodySupportCommand
    {
        private const float DirectionEpsilon = 0.0001f;

        public BodySupportCommand(
            Vector3 facingDirection,
            Vector3 uprightDirection,
            Vector3 travelDirection,
            float desiredLeanDegrees,
            float uprightStrengthScale,
            float yawStrengthScale,
            float stabilizationStrengthScale,
            float recoveryBlend,
            float recoveryKdBlend,
            float heightMaintenanceScale = 1f,
            RecoverySituation recoverySituation = RecoverySituation.None)
        {
            // STEP 1: Normalize the command frame so executors can consume a stable facing/up basis.
            FacingDirection = NormalizePlanarDirection(facingDirection, Vector3.forward);
            UprightDirection = NormalizeDirection(uprightDirection, Vector3.up);
            TravelDirection = NormalizePlanarDirection(travelDirection, FacingDirection);

            // STEP 2: Preserve the requested posture bias while clamping strength scales into safe ranges.
            DesiredLeanDegrees = desiredLeanDegrees;
            UprightStrengthScale = Mathf.Max(0f, uprightStrengthScale);
            YawStrengthScale = Mathf.Max(0f, yawStrengthScale);
            StabilizationStrengthScale = Mathf.Max(0f, stabilizationStrengthScale);
            HeightMaintenanceScale = Mathf.Max(0f, heightMaintenanceScale);
            RecoveryBlend = Mathf.Clamp01(recoveryBlend);
            RecoveryKdBlend = Mathf.Clamp01(recoveryKdBlend);
            RecoverySituation = recoverySituation;
        }

        public Vector3 FacingDirection { get; }

        public Vector3 UprightDirection { get; }

        public Vector3 TravelDirection { get; }

        public float DesiredLeanDegrees { get; }

        public float UprightStrengthScale { get; }

        public float YawStrengthScale { get; }

        public float StabilizationStrengthScale { get; }

        /// <summary>
        /// Multiplier for vertical height-maintenance force that keeps the hips at standing
        /// height. Values above 1 boost height recovery; 0 disables height maintenance.
        /// </summary>
        public float HeightMaintenanceScale { get; }

        public float RecoveryBlend { get; }

        public float RecoveryKdBlend { get; }

        /// <summary>The classified recovery situation driving this command, or None for normal locomotion.</summary>
        public RecoverySituation RecoverySituation { get; }

        public static BodySupportCommand PassThrough(Vector3 facingDirection)
        {
            return PassThrough(facingDirection, facingDirection, 0f, 0f);
        }

        public static BodySupportCommand PassThrough(
            Vector3 facingDirection,
            Vector3 travelDirection,
            float recoveryBlend,
            float recoveryKdBlend)
        {
            return new BodySupportCommand(
                facingDirection,
                Vector3.up,
                travelDirection,
                0f,
                1f,
                1f,
                1f,
                recoveryBlend,
                recoveryKdBlend);
        }

        private static Vector3 NormalizePlanarDirection(Vector3 rawDirection, Vector3 fallback)
        {
            Vector3 planarDirection = Vector3.ProjectOnPlane(rawDirection, Vector3.up);
            if (planarDirection.sqrMagnitude > DirectionEpsilon)
            {
                return planarDirection.normalized;
            }

            return fallback;
        }

        private static Vector3 NormalizeDirection(Vector3 rawDirection, Vector3 fallback)
        {
            if (rawDirection.sqrMagnitude > DirectionEpsilon)
            {
                return rawDirection.normalized;
            }

            return fallback;
        }
    }
}