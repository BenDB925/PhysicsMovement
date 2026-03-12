using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Immutable support snapshot that summarizes foot contacts into locomotion-facing support quality signals.
    /// </summary>
    internal readonly struct SupportObservation
    {
        public SupportObservation(
            FootContactObservation leftFoot,
            FootContactObservation rightFoot,
            float supportQuality,
            float contactConfidence,
            float plantedFootConfidence,
            float slipEstimate,
            bool isComOutsideSupport)
        {
            // STEP 1: Preserve the underlying left/right foot contact snapshots.
            LeftFoot = leftFoot;
            RightFoot = rightFoot;

            // STEP 2: Clamp aggregated support metrics into a shared normalized range.
            SupportQuality = Mathf.Clamp01(supportQuality);
            ContactConfidence = Mathf.Clamp01(contactConfidence);
            PlantedFootConfidence = Mathf.Clamp01(plantedFootConfidence);
            SlipEstimate = Mathf.Clamp01(slipEstimate);

            // STEP 3: Preserve the support-polygon classification exactly as issued by the caller.
            IsComOutsideSupport = isComOutsideSupport;
        }

        public FootContactObservation LeftFoot { get; }

        public FootContactObservation RightFoot { get; }

        public float SupportQuality { get; }

        public float ContactConfidence { get; }

        public float PlantedFootConfidence { get; }

        public float SlipEstimate { get; }

        public bool IsComOutsideSupport { get; }

        public bool HasAnyGroundedFoot => LeftFoot.IsGrounded || RightFoot.IsGrounded;

        public bool HasDoubleSupport => LeftFoot.IsGrounded && RightFoot.IsGrounded;
    }
}