using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Evaluates whether the explicit per-leg gait controller has enough observation
    /// confidence to keep divergent gait roles active, and blends emitted leg commands
    /// toward a stable mirrored fallback gait when confidence stays low.
    ///
    /// Owns the <see cref="FallbackBlend"/> state and the hysteresis latch that prevents
    /// frame-to-frame flicker between explicit and mirrored modes.
    /// </summary>
    internal class GaitConfidenceEvaluator
    {
        private float _fallbackGaitBlend;
        private bool _isFallbackGaitLatched;

        internal float FallbackBlend => _fallbackGaitBlend;

        internal void Reset()
        {
            _fallbackGaitBlend = 0f;
            _isFallbackGaitLatched = false;
        }

        /// <summary>
        /// Computes a 0–1 confidence scalar reflecting how much to trust the explicit
        /// per-leg controller this frame. Lower values push toward mirrored fallback.
        /// </summary>
        internal float ComputeConfidence(
            LocomotionObservation observation,
            bool hasTurnAsymmetry,
            bool forceCatchStep,
            LegCommandOutput leftCommand,
            LegCommandOutput rightCommand,
            float minimumConfidenceExit)
        {
            float supportConfidence = Mathf.Min(
                observation.SupportQuality,
                observation.ContactConfidence);
            float stabilityConfidence = Mathf.Min(
                supportConfidence,
                Mathf.Min(observation.PlantedFootConfidence, 1f - observation.SlipEstimate));
            bool preserveTurnSupportOwnership = hasTurnAsymmetry &&
                !forceCatchStep &&
                !observation.IsLocomotionCollapsed;
            bool developingTurn = observation.TurnSeverity > 0.15f &&
                !forceCatchStep &&
                !observation.IsLocomotionCollapsed;

            if (observation.IsComOutsideSupport)
            {
                stabilityConfidence *= (preserveTurnSupportOwnership || developingTurn) ? 0.85f : 0.6f;
            }

            if (observation.IsInSnapRecovery)
            {
                stabilityConfidence *= 0.75f;
            }

            if (observation.IsLocomotionCollapsed)
            {
                stabilityConfidence *= 0.45f;
            }

            if (!preserveTurnSupportOwnership && !forceCatchStep)
            {
                float mirroredRightPhase = Mathf.Repeat(leftCommand.CyclePhase + Mathf.PI, Mathf.PI * 2f);
                float mirrorDeviation = Mathf.Abs(
                    Mathf.DeltaAngle(rightCommand.CyclePhase * Mathf.Rad2Deg, mirroredRightPhase * Mathf.Rad2Deg)) * Mathf.Deg2Rad;
                float footAsymmetry = Mathf.Max(
                    Mathf.Abs(observation.LeftFoot.ContactConfidence - observation.RightFoot.ContactConfidence),
                    Mathf.Abs(observation.LeftFoot.PlantedConfidence - observation.RightFoot.PlantedConfidence));
                float unexpectedAsymmetry = Mathf.InverseLerp(0.05f, 0.3f, mirrorDeviation) * footAsymmetry;
                float turnAttenuation = 1f - Mathf.InverseLerp(0.15f, 0.45f, observation.TurnSeverity);
                stabilityConfidence *= Mathf.Lerp(1f, 0.25f, unexpectedAsymmetry * turnAttenuation);
            }

            if (hasTurnAsymmetry)
            {
                stabilityConfidence *= preserveTurnSupportOwnership
                    ? Mathf.Lerp(1f, 0.95f, observation.TurnSeverity)
                    : Mathf.Lerp(1f, 0.85f, observation.TurnSeverity);
            }

            if (forceCatchStep)
            {
                stabilityConfidence *= 0.75f;
            }

            if (preserveTurnSupportOwnership)
            {
                float plantedSupportConfidence = Mathf.Min(
                    Mathf.Max(observation.LeftFoot.PlantedConfidence, observation.RightFoot.PlantedConfidence),
                    1f - observation.SlipEstimate);
                float floorValue = plantedSupportConfidence * 0.7f;

                if (hasTurnAsymmetry)
                {
                    floorValue = Mathf.Max(floorValue, minimumConfidenceExit);
                }

                stabilityConfidence = Mathf.Max(stabilityConfidence, floorValue);
            }

            return Mathf.Clamp01(stabilityConfidence);
        }

        /// <summary>
        /// Advances the fallback blend toward or away from 1 based on the current
        /// confidence value and hysteresis thresholds.
        /// </summary>
        internal void UpdateFallbackBlend(
            float confidence,
            float minimumConfidence,
            float minimumConfidenceExit,
            float riseSpeed,
            float fallSpeed)
        {
            if (_isFallbackGaitLatched)
            {
                if (confidence >= minimumConfidenceExit)
                {
                    _isFallbackGaitLatched = false;
                }
            }
            else if (confidence <= minimumConfidence)
            {
                _isFallbackGaitLatched = true;
            }

            float targetBlend = _isFallbackGaitLatched ? 1f : 0f;
            float blendSpeed = _isFallbackGaitLatched ? riseSpeed : fallSpeed;
            _fallbackGaitBlend = Mathf.MoveTowards(
                _fallbackGaitBlend,
                targetBlend,
                Mathf.Max(0f, blendSpeed) * Time.fixedDeltaTime);
        }

        /// <summary>
        /// Blends the two explicit leg commands toward a stable mirrored fallback gait
        /// by the current <see cref="FallbackBlend"/> amount.
        /// </summary>
        internal void ApplyFallback(
            ref LegCommandOutput leftCommand,
            ref LegCommandOutput rightCommand,
            Vector3 gaitReferenceDirection,
            bool applyStrandedBias,
            float stepAngle,
            float kneeAngle,
            float upperLegLiftBoost)
        {
            if (_fallbackGaitBlend <= 0.0001f)
            {
                return;
            }

            LegCommandOutput fallbackLeft = BuildFallbackCycleCommand(
                LocomotionLeg.Left,
                leftCommand.CyclePhase,
                leftCommand.BlendWeight,
                applyStrandedBias,
                stepAngle,
                kneeAngle,
                upperLegLiftBoost);
            LegCommandOutput fallbackRight = BuildFallbackCycleCommand(
                LocomotionLeg.Right,
                Mathf.Repeat(leftCommand.CyclePhase + Mathf.PI, Mathf.PI * 2f),
                rightCommand.BlendWeight,
                applyStrandedBias,
                stepAngle,
                kneeAngle,
                upperLegLiftBoost);

            leftCommand = BlendTowardFallbackCommand(leftCommand, fallbackLeft);
            rightCommand = BlendTowardFallbackCommand(rightCommand, fallbackRight);
        }

        // ── Private Helpers ─────────────────────────────────────────────────

        private LegCommandOutput BuildFallbackCycleCommand(
            LocomotionLeg leg,
            float cyclePhase,
            float blendWeight,
            bool applyStrandedBias,
            float stepAngle,
            float kneeAngle,
            float upperLegLiftBoost)
        {
            LegStateFrame stateFrame = new LegStateFrame(
                leg,
                InferFallbackStateFromPhase(cyclePhase),
                LegStateTransitionReason.LowConfidenceFallback);
            float swingAngleDegrees = LegExecutionProfileResolver.BuildSwingAngleFromPhase(
                cyclePhase, blendWeight, stateFrame.State, stepAngle, upperLegLiftBoost);
            if (applyStrandedBias)
            {
                swingAngleDegrees += stepAngle * blendWeight;
            }

            return new LegCommandOutput(
                leg,
                LegCommandMode.Cycle,
                stateFrame,
                cyclePhase,
                swingAngleDegrees,
                kneeAngle * blendWeight,
                blendWeight,
                StepTarget.Invalid);
        }

        private LegCommandOutput BlendTowardFallbackCommand(
            LegCommandOutput explicitCommand,
            LegCommandOutput fallbackCommand)
        {
            float blendedCyclePhase = LerpWrappedPhase(
                explicitCommand.CyclePhase,
                fallbackCommand.CyclePhase,
                _fallbackGaitBlend);

            LegStateFrame blendedStateFrame = _fallbackGaitBlend >= 0.35f
                ? fallbackCommand.StateFrame
                : explicitCommand.StateFrame;

            return new LegCommandOutput(
                explicitCommand.Leg,
                explicitCommand.Mode,
                blendedStateFrame,
                blendedCyclePhase,
                Mathf.Lerp(explicitCommand.SwingAngleDegrees, fallbackCommand.SwingAngleDegrees, _fallbackGaitBlend),
                Mathf.Lerp(explicitCommand.KneeAngleDegrees, fallbackCommand.KneeAngleDegrees, _fallbackGaitBlend),
                Mathf.Lerp(explicitCommand.BlendWeight, fallbackCommand.BlendWeight, _fallbackGaitBlend),
                explicitCommand.StepTarget);
        }

        private static float LerpWrappedPhase(float fromPhase, float toPhase, float blend)
        {
            float deltaDegrees = Mathf.DeltaAngle(fromPhase * Mathf.Rad2Deg, toPhase * Mathf.Rad2Deg);
            return Mathf.Repeat(fromPhase + deltaDegrees * Mathf.Deg2Rad * Mathf.Clamp01(blend), Mathf.PI * 2f);
        }

        private static LegStateType InferFallbackStateFromPhase(float cyclePhase)
        {
            if (cyclePhase < Mathf.PI * 0.85f)
            {
                return LegStateType.Swing;
            }

            if (cyclePhase < Mathf.PI)
            {
                return LegStateType.Plant;
            }

            return LegStateType.Stance;
        }
    }
}
