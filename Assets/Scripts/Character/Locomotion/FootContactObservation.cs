using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Immutable per-foot contact snapshot promoted into locomotion-language confidence values.
    /// </summary>
    internal readonly struct FootContactObservation
    {
        public FootContactObservation(
            LocomotionLeg leg,
            bool isGrounded,
            float contactConfidence,
            float plantedConfidence,
            float slipEstimate)
            : this(
                leg,
                isGrounded,
                isGrounded && plantedConfidence >= 0.5f,
                contactConfidence,
                plantedConfidence,
                slipEstimate,
                1f,
                false,
                0f,
                0f,
                false,
                Vector3.zero)
        {
        }

        public FootContactObservation(
            LocomotionLeg leg,
            bool isGrounded,
            float contactConfidence,
            float plantedConfidence,
            float slipEstimate,
            bool hasForwardObstruction,
            float estimatedStepHeight,
            float forwardObstructionConfidence)
            : this(
                leg,
                isGrounded,
                isGrounded && plantedConfidence >= 0.5f,
                contactConfidence,
                plantedConfidence,
                slipEstimate,
                1f,
                hasForwardObstruction,
                estimatedStepHeight,
                forwardObstructionConfidence,
                false,
                Vector3.zero)
        {
        }

        public FootContactObservation(
            LocomotionLeg leg,
            bool isGrounded,
            float contactConfidence,
            float plantedConfidence,
            float slipEstimate,
            bool hasForwardObstruction,
            float estimatedStepHeight,
            float forwardObstructionConfidence,
            Vector3 forwardObstructionTopSurfacePoint)
            : this(
                leg,
                isGrounded,
                isGrounded && plantedConfidence >= 0.5f,
                contactConfidence,
                plantedConfidence,
                slipEstimate,
                1f,
                hasForwardObstruction,
                estimatedStepHeight,
                forwardObstructionConfidence,
                hasForwardObstruction,
                forwardObstructionTopSurfacePoint)
        {
        }

        public FootContactObservation(
            LocomotionLeg leg,
            bool isGrounded,
            float contactConfidence,
            float plantedConfidence,
            float slipEstimate,
            float surfaceNormalQuality,
            bool hasForwardObstruction,
            float estimatedStepHeight,
            float forwardObstructionConfidence,
            Vector3 forwardObstructionTopSurfacePoint)
            : this(
                leg,
                isGrounded,
                isGrounded && plantedConfidence >= 0.5f,
                contactConfidence,
                plantedConfidence,
                slipEstimate,
                surfaceNormalQuality,
                hasForwardObstruction,
                estimatedStepHeight,
                forwardObstructionConfidence,
                hasForwardObstruction,
                forwardObstructionTopSurfacePoint)
        {
        }

        public FootContactObservation(
            LocomotionLeg leg,
            bool isGrounded,
            bool isPlanted,
            float contactConfidence,
            float plantedConfidence,
            float slipEstimate)
            : this(
                leg,
                isGrounded,
                isPlanted,
                contactConfidence,
                plantedConfidence,
                slipEstimate,
                1f,
                false,
                0f,
                0f,
                false,
                Vector3.zero)
        {
        }

        public FootContactObservation(
            LocomotionLeg leg,
            bool isGrounded,
            bool isPlanted,
            float contactConfidence,
            float plantedConfidence,
            float slipEstimate,
            bool hasForwardObstruction,
            float estimatedStepHeight,
            float forwardObstructionConfidence)
            : this(
                leg,
                isGrounded,
                isPlanted,
                contactConfidence,
                plantedConfidence,
                slipEstimate,
                1f,
                hasForwardObstruction,
                estimatedStepHeight,
                forwardObstructionConfidence,
                false,
                Vector3.zero)
        {
        }

        public FootContactObservation(
            LocomotionLeg leg,
            bool isGrounded,
            bool isPlanted,
            float contactConfidence,
            float plantedConfidence,
            float slipEstimate,
            bool hasForwardObstruction,
            float estimatedStepHeight,
            float forwardObstructionConfidence,
            Vector3 forwardObstructionTopSurfacePoint)
            : this(
                leg,
                isGrounded,
                isPlanted,
                contactConfidence,
                plantedConfidence,
                slipEstimate,
                1f,
                hasForwardObstruction,
                estimatedStepHeight,
                forwardObstructionConfidence,
                hasForwardObstruction,
                forwardObstructionTopSurfacePoint)
        {
        }

        public FootContactObservation(
            LocomotionLeg leg,
            bool isGrounded,
            bool isPlanted,
            float contactConfidence,
            float plantedConfidence,
            float slipEstimate,
            float surfaceNormalQuality,
            bool hasForwardObstruction,
            float estimatedStepHeight,
            float forwardObstructionConfidence,
            Vector3 forwardObstructionTopSurfacePoint)
            : this(
                leg,
                isGrounded,
                isPlanted,
                contactConfidence,
                plantedConfidence,
                slipEstimate,
                surfaceNormalQuality,
                hasForwardObstruction,
                estimatedStepHeight,
                forwardObstructionConfidence,
                hasForwardObstruction,
                forwardObstructionTopSurfacePoint)
        {
        }

        private FootContactObservation(
            LocomotionLeg leg,
            bool isGrounded,
            bool isPlanted,
            float contactConfidence,
            float plantedConfidence,
            float slipEstimate,
            float surfaceNormalQuality,
            bool hasForwardObstruction,
            float estimatedStepHeight,
            float forwardObstructionConfidence,
            bool hasForwardObstructionTopSurfacePoint,
            Vector3 forwardObstructionTopSurfacePoint)
        {
            // STEP 1: Preserve the authoritative leg identity and grounded state exactly as reported.
            Leg = leg;
            IsGrounded = isGrounded;
            IsPlanted = isGrounded && isPlanted;

            // STEP 2: Clamp the locomotion confidence metrics into a shared normalized range.
            ContactConfidence = Mathf.Clamp01(contactConfidence);
            PlantedConfidence = Mathf.Clamp01(plantedConfidence);
            SlipEstimate = Mathf.Clamp01(slipEstimate);
            SurfaceNormalQuality = Mathf.Clamp01(surfaceNormalQuality);

            // STEP 3: Preserve forward terrain-obstruction sensing independently from downward grounded state.
            HasForwardObstruction = hasForwardObstruction && estimatedStepHeight > 0f;
            EstimatedStepHeight = HasForwardObstruction ? Mathf.Max(0f, estimatedStepHeight) : 0f;
            ForwardObstructionConfidence = HasForwardObstruction
                ? Mathf.Clamp01(forwardObstructionConfidence)
                : 0f;
            HasForwardObstructionTopSurfacePoint = HasForwardObstruction && hasForwardObstructionTopSurfacePoint;
            ForwardObstructionTopSurfacePoint = HasForwardObstructionTopSurfacePoint
                ? forwardObstructionTopSurfacePoint
                : Vector3.zero;
        }

        public LocomotionLeg Leg { get; }

        public bool IsGrounded { get; }

        public bool IsPlanted { get; }

        public float ContactConfidence { get; }

        public float PlantedConfidence { get; }

        public float SlipEstimate { get; }

        public bool HasForwardObstruction { get; }

        public float EstimatedStepHeight { get; }

        public float ForwardObstructionConfidence { get; }

        public float SurfaceNormalQuality { get; }

        public bool HasForwardObstructionTopSurfacePoint { get; }

        public Vector3 ForwardObstructionTopSurfacePoint { get; }
    }
}