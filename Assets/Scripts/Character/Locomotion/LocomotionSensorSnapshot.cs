using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Low-level per-step sensor frame gathered from the character runtime before the
    /// locomotion director promotes it into higher-level locomotion observations.
    /// </summary>
    internal readonly struct LocomotionSensorSnapshot
    {
        public LocomotionSensorSnapshot(
            Vector3 hipsPosition,
            Vector3 centerOfMassPosition,
            Vector3 hipsVelocity,
            Vector3 hipsAngularVelocity,
            SupportGeometry supportGeometry,
            SupportObservation support)
        {
            // STEP 1: Preserve the raw body-motion sample for this physics step.
            HipsPosition = hipsPosition;
            CenterOfMassPosition = centerOfMassPosition;
            HipsVelocity = hipsVelocity;
            HipsAngularVelocity = hipsAngularVelocity;
            YawRate = hipsAngularVelocity.y;

            // STEP 2: Preserve the aggregated support data so downstream systems share one snapshot.
            SupportGeometry = supportGeometry;
            Support = support;
        }

        public Vector3 HipsPosition { get; }

        public Vector3 CenterOfMassPosition { get; }

        public Vector3 HipsVelocity { get; }

        public Vector3 HipsAngularVelocity { get; }

        public float YawRate { get; }

        public SupportGeometry SupportGeometry { get; }

        public SupportObservation Support { get; }
    }
}