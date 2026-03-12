using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Lightweight snapshot of locomotion-relevant runtime state gathered from the current
    /// character systems before a locomotion coordinator decides on the next command set.
    /// </summary>
    internal readonly struct LocomotionObservation
    {
        private const float DirectionEpsilon = 0.0001f;

        public LocomotionObservation(
            CharacterStateType characterState,
            bool isGrounded,
            bool isFallen,
            bool isLocomotionCollapsed,
            bool isInSnapRecovery,
            float uprightAngleDegrees,
            Vector3 velocity,
            Vector3 angularVelocity,
            Vector3 bodyForward,
            Vector3 bodyUp)
        {
            // STEP 1: Preserve authoritative posture/state flags exactly as reported by runtime systems.
            CharacterState = characterState;
            IsGrounded = isGrounded;
            IsFallen = isFallen;
            IsLocomotionCollapsed = isLocomotionCollapsed;
            IsInSnapRecovery = isInSnapRecovery;
            UprightAngleDegrees = Mathf.Max(0f, uprightAngleDegrees);

            // STEP 2: Cache the raw motion vectors plus a planar speed projection for locomotion logic.
            Velocity = velocity;
            AngularVelocity = angularVelocity;
            PlanarVelocity = Vector3.ProjectOnPlane(velocity, Vector3.up);
            PlanarSpeed = PlanarVelocity.magnitude;

            // STEP 3: Normalize body basis vectors so future consumers share the same reference frame.
            BodyForward = NormalizePlanarDirection(bodyForward, Vector3.forward);
            BodyUp = NormalizeDirection(bodyUp, Vector3.up);
        }

        public CharacterStateType CharacterState { get; }

        public bool IsGrounded { get; }

        public bool IsFallen { get; }

        public bool IsLocomotionCollapsed { get; }

        public bool IsInSnapRecovery { get; }

        public float UprightAngleDegrees { get; }

        public Vector3 Velocity { get; }

        public Vector3 AngularVelocity { get; }

        public Vector3 PlanarVelocity { get; }

        public float PlanarSpeed { get; }

        public Vector3 BodyForward { get; }

        public Vector3 BodyUp { get; }

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