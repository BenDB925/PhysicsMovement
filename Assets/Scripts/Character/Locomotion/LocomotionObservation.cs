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
            : this(
                characterState,
                isGrounded,
                isFallen,
                isLocomotionCollapsed,
                isInSnapRecovery,
                uprightAngleDegrees,
                velocity,
                angularVelocity,
                bodyForward,
                bodyUp,
                CreateBaselineSupportObservation(isGrounded),
                0f)
        {
        }

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
            Vector3 bodyUp,
            SupportObservation support,
            float turnSeverity)
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

            // STEP 4: Cache the locomotion-language support and turning signals for the Chapter 2 world model.
            Support = support;
            TurnSeverity = Mathf.Clamp01(turnSeverity);
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

        public SupportObservation Support { get; }

        public FootContactObservation LeftFoot => Support.LeftFoot;

        public FootContactObservation RightFoot => Support.RightFoot;

        // STEP 5: Expose planner-facing terrain-obstruction accessors directly on the observation
        // layer so later step-planning slices do not need to read sensor-specific payloads.
        public bool HasLeftForwardObstruction => LeftFoot.HasForwardObstruction;

        public bool HasRightForwardObstruction => RightFoot.HasForwardObstruction;

        public bool HasAnyForwardObstruction => HasLeftForwardObstruction || HasRightForwardObstruction;

        public float LeftEstimatedStepHeight => LeftFoot.EstimatedStepHeight;

        public float RightEstimatedStepHeight => RightFoot.EstimatedStepHeight;

        public float MaxEstimatedStepHeight => Mathf.Max(LeftEstimatedStepHeight, RightEstimatedStepHeight);

        public float LeftForwardObstructionConfidence => LeftFoot.ForwardObstructionConfidence;

        public float RightForwardObstructionConfidence => RightFoot.ForwardObstructionConfidence;

        public float MaxForwardObstructionConfidence => Mathf.Max(
            LeftForwardObstructionConfidence,
            RightForwardObstructionConfidence);

        public float SupportQuality => Support.SupportQuality;

        public float ContactConfidence => Support.ContactConfidence;

        public float PlantedFootConfidence => Support.PlantedFootConfidence;

        public float SlipEstimate => Support.SlipEstimate;

        public bool IsComOutsideSupport => Support.IsComOutsideSupport;

        public float TurnSeverity { get; }

        private static SupportObservation CreateBaselineSupportObservation(bool isGrounded)
        {
            float groundedConfidence = isGrounded ? 1f : 0f;
            FootContactObservation leftFoot = new FootContactObservation(
                LocomotionLeg.Left,
                isGrounded,
                groundedConfidence,
                groundedConfidence,
                0f);
            FootContactObservation rightFoot = new FootContactObservation(
                LocomotionLeg.Right,
                isGrounded,
                groundedConfidence,
                groundedConfidence,
                0f);

            return new SupportObservation(
                leftFoot,
                rightFoot,
                groundedConfidence,
                groundedConfidence,
                groundedConfidence,
                0f,
                false);
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