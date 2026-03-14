using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Stateful support filter that smooths per-foot contact signals and applies hysteresis to
    /// the planted/unplanted classification so locomotion observations stay stable across transient noise.
    /// </summary>
    internal sealed class SupportObservationFilter
    {
        private const float ThresholdGap = 0.01f;

        private readonly float _contactConfidenceRiseSpeed;
        private readonly float _contactConfidenceFallSpeed;
        private readonly float _plantedConfidenceRiseSpeed;
        private readonly float _plantedConfidenceFallSpeed;
        private readonly float _plantedEnterThreshold;
        private readonly float _plantedExitThreshold;

        private FootFilterState _leftFootState;
        private FootFilterState _rightFootState;

        public SupportObservationFilter(
            float contactConfidenceRiseSpeed,
            float contactConfidenceFallSpeed,
            float plantedConfidenceRiseSpeed,
            float plantedConfidenceFallSpeed,
            float plantedEnterThreshold,
            float plantedExitThreshold)
        {
            // STEP 1: Clamp the smoothing rates and hysteresis thresholds into safe runtime ranges.
            _contactConfidenceRiseSpeed = Mathf.Max(0f, contactConfidenceRiseSpeed);
            _contactConfidenceFallSpeed = Mathf.Max(0f, contactConfidenceFallSpeed);
            _plantedConfidenceRiseSpeed = Mathf.Max(0f, plantedConfidenceRiseSpeed);
            _plantedConfidenceFallSpeed = Mathf.Max(0f, plantedConfidenceFallSpeed);

            float clampedEnterThreshold = Mathf.Clamp01(plantedEnterThreshold);
            float clampedExitThreshold = Mathf.Clamp01(plantedExitThreshold);
            if (clampedEnterThreshold <= clampedExitThreshold)
            {
                clampedEnterThreshold = Mathf.Clamp01(clampedExitThreshold + ThresholdGap);
            }

            _plantedEnterThreshold = clampedEnterThreshold;
            _plantedExitThreshold = Mathf.Min(clampedExitThreshold, _plantedEnterThreshold - ThresholdGap);
        }

        public SupportObservation Filter(SupportObservation instantaneousSupport, float deltaTime)
        {
            // STEP 1: Filter the per-foot contact and planted signals with the configured rates.
            FootContactObservation leftFoot = FilterFoot(
                instantaneousSupport.LeftFoot,
                ref _leftFootState,
                deltaTime);
            FootContactObservation rightFoot = FilterFoot(
                instantaneousSupport.RightFoot,
                ref _rightFootState,
                deltaTime);

            // STEP 2: Rebuild the aggregate support observation from the filtered foot states.
            float contactConfidence = 0.5f * (leftFoot.ContactConfidence + rightFoot.ContactConfidence);
            float plantedFootConfidence = Mathf.Max(leftFoot.PlantedConfidence, rightFoot.PlantedConfidence);
            float slipEstimate = Mathf.Max(leftFoot.SlipEstimate, rightFoot.SlipEstimate);

            return new SupportObservation(
                leftFoot,
                rightFoot,
                instantaneousSupport.SupportQuality,
                contactConfidence,
                plantedFootConfidence,
                slipEstimate,
                instantaneousSupport.IsComOutsideSupport);
        }

        private FootContactObservation FilterFoot(
            FootContactObservation instantaneousFoot,
            ref FootFilterState footState,
            float deltaTime)
        {
            // STEP 1: Smooth contact confidence so brief contact changes do not instantly zero out support.
            footState.ContactConfidence = MoveTowards(
                footState.ContactConfidence,
                instantaneousFoot.ContactConfidence,
                instantaneousFoot.ContactConfidence >= footState.ContactConfidence
                    ? _contactConfidenceRiseSpeed
                    : _contactConfidenceFallSpeed,
                deltaTime);

            // STEP 2: Smooth the planted signal so one-frame slip spikes do not immediately erase support.
            float rawPlantedSignal = instantaneousFoot.IsGrounded
                ? Mathf.Clamp01(instantaneousFoot.PlantedConfidence)
                : 0f;
            footState.PlantedConfidence = MoveTowards(
                footState.PlantedConfidence,
                rawPlantedSignal,
                rawPlantedSignal >= footState.PlantedConfidence
                    ? _plantedConfidenceRiseSpeed
                    : _plantedConfidenceFallSpeed,
                deltaTime);

            float plantedConfidence = Mathf.Min(footState.ContactConfidence, footState.PlantedConfidence);

            // STEP 3: Maintain a stable planted/unplanted latch across the hysteresis band using the smoothed signal.
            if (!instantaneousFoot.IsGrounded)
            {
                footState.IsPlanted = false;
            }
            else if (footState.IsPlanted)
            {
                footState.IsPlanted = plantedConfidence > _plantedExitThreshold;
            }
            else if (plantedConfidence >= _plantedEnterThreshold)
            {
                footState.IsPlanted = true;
            }

            // STEP 4: Derive slip from the remaining contact budget after the smoothed planted confidence has been applied.
            float slipEstimate = Mathf.Max(0f, footState.ContactConfidence - plantedConfidence);

            return new FootContactObservation(
                instantaneousFoot.Leg,
                instantaneousFoot.IsGrounded,
                footState.IsPlanted,
                footState.ContactConfidence,
                plantedConfidence,
                slipEstimate,
                instantaneousFoot.HasForwardObstruction,
                instantaneousFoot.EstimatedStepHeight,
                instantaneousFoot.ForwardObstructionConfidence,
                instantaneousFoot.ForwardObstructionTopSurfacePoint);
        }

        private static float MoveTowards(float current, float target, float speed, float deltaTime)
        {
            if (deltaTime <= 0f || speed <= 0f)
            {
                return current;
            }

            return Mathf.MoveTowards(current, target, speed * deltaTime);
        }

        private struct FootFilterState
        {
            public float ContactConfidence;
            public float PlantedConfidence;
            public bool IsPlanted;
        }
    }
}