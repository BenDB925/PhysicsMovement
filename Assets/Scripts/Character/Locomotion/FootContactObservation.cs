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
                slipEstimate)
        {
        }

        public FootContactObservation(
            LocomotionLeg leg,
            bool isGrounded,
            bool isPlanted,
            float contactConfidence,
            float plantedConfidence,
            float slipEstimate)
        {
            // STEP 1: Preserve the authoritative leg identity and grounded state exactly as reported.
            Leg = leg;
            IsGrounded = isGrounded;
            IsPlanted = isGrounded && isPlanted;

            // STEP 2: Clamp the locomotion confidence metrics into a shared normalized range.
            ContactConfidence = Mathf.Clamp01(contactConfidence);
            PlantedConfidence = Mathf.Clamp01(plantedConfidence);
            SlipEstimate = Mathf.Clamp01(slipEstimate);
        }

        public LocomotionLeg Leg { get; }

        public bool IsGrounded { get; }

        public bool IsPlanted { get; }

        public float ContactConfidence { get; }

        public float PlantedConfidence { get; }

        public float SlipEstimate { get; }
    }
}