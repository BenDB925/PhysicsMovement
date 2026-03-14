using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Shapes per-leg upper-leg and knee targets based on the explicit leg state
    /// (Swing, Stance, Plant, RecoveryStep, CatchStep). Each profile method
    /// modulates the raw pass-through gait command so the joint executor receives
    /// state-appropriate targets.
    ///
    /// Also provides <see cref="BuildSwingAngleFromPhase"/> — the shared pure-math
    /// utility that converts a cycle phase into a signed swing angle. Used by both
    /// the main gait pipeline and the low-confidence fallback path.
    /// </summary>
    internal static class LegExecutionProfileResolver
    {
        /// <summary>
        /// Resolves final swing and knee angles for a single leg command, optionally
        /// shaping them through the state-driven execution profiles.
        /// </summary>
        internal static void Resolve(
            LegCommandOutput command,
            bool useStateDrivenExecution,
            float stepAngle,
            float kneeAngle,
            out float swingAngleDegrees,
            out float kneeAngleDegrees)
        {
            if (command.Mode == LegCommandMode.Disabled)
            {
                swingAngleDegrees = 0f;
                kneeAngleDegrees = 0f;
                return;
            }

            swingAngleDegrees = command.SwingAngleDegrees;
            kneeAngleDegrees = command.KneeAngleDegrees;

            if (!useStateDrivenExecution)
            {
                return;
            }

            switch (command.State)
            {
                case LegStateType.Swing:
                    ApplySwingProfile(command, stepAngle, kneeAngle, ref swingAngleDegrees, ref kneeAngleDegrees);
                    break;

                case LegStateType.Stance:
                    ApplyStanceProfile(command, kneeAngle, ref swingAngleDegrees, ref kneeAngleDegrees);
                    break;

                case LegStateType.Plant:
                    ApplyPlantProfile(command, kneeAngle, ref kneeAngleDegrees);
                    break;

                case LegStateType.RecoveryStep:
                    ApplyRecoveryStepProfile(command, stepAngle, kneeAngle, ref swingAngleDegrees, ref kneeAngleDegrees);
                    break;

                case LegStateType.CatchStep:
                    ApplyCatchStepProfile(command, stepAngle, kneeAngle, ref swingAngleDegrees, ref kneeAngleDegrees);
                    break;
            }
        }

        /// <summary>
        /// Converts a cycle phase and amplitude into a signed swing angle including
        /// the optional lift boost for forward-swing phases. Pure function with no
        /// side effects.
        /// </summary>
        internal static float BuildSwingAngleFromPhase(
            float cyclePhase,
            float amplitudeScale,
            LegStateType state,
            float stepAngle,
            float upperLegLiftBoost)
        {
            float swingSin = Mathf.Sin(cyclePhase);
            float liftBoost = swingSin > 0f ? swingSin * upperLegLiftBoost * amplitudeScale : 0f;
            float swingAngle = swingSin * stepAngle * amplitudeScale + liftBoost;

            if ((state == LegStateType.Swing || state == LegStateType.CatchStep) && amplitudeScale > 0f)
            {
                swingAngle += upperLegLiftBoost * 0.6f * amplitudeScale;

                float minimumForwardArc = stepAngle * 0.55f * amplitudeScale;
                swingAngle = Mathf.Max(swingAngle, minimumForwardArc);
            }

            return swingAngle;
        }

        // ── Profile Methods ─────────────────────────────────────────────────

        private static void ApplySwingProfile(
            LegCommandOutput command,
            float stepAngle,
            float kneeAngle,
            ref float swingAngleDegrees,
            ref float kneeAngleDegrees)
        {
            float swingProgress = Mathf.InverseLerp(0f, Mathf.PI, Mathf.Min(command.CyclePhase, Mathf.PI));
            float swingForwardTarget = Mathf.Lerp(
                stepAngle * 0.58f,
                stepAngle * 0.68f,
                Mathf.SmoothStep(0f, 1f, swingProgress)) * command.BlendWeight;

            swingAngleDegrees = Mathf.Max(swingAngleDegrees, swingForwardTarget);
            kneeAngleDegrees = Mathf.Max(kneeAngleDegrees, kneeAngle * 0.35f * command.BlendWeight);
        }

        private static void ApplyStanceProfile(
            LegCommandOutput command,
            float kneeAngle,
            ref float swingAngleDegrees,
            ref float kneeAngleDegrees)
        {
            float stanceProgress = Mathf.InverseLerp(Mathf.PI, Mathf.PI * 2f, command.CyclePhase);
            float supportKneeTarget = Mathf.Lerp(kneeAngle * 0.2f, kneeAngle * 0.08f, stanceProgress) * command.BlendWeight;

            kneeAngleDegrees = Mathf.Min(kneeAngleDegrees, supportKneeTarget);

            if (command.TransitionReason == LegStateTransitionReason.None)
            {
                swingAngleDegrees = Mathf.Lerp(swingAngleDegrees, 0f, 0.85f);
                return;
            }

            if (swingAngleDegrees > 0f)
            {
                swingAngleDegrees *= 1f - stanceProgress * 0.5f;
            }
        }

        private static void ApplyPlantProfile(
            LegCommandOutput command,
            float kneeAngle,
            ref float kneeAngleDegrees)
        {
            float plantProgress = Mathf.InverseLerp(Mathf.PI * 0.85f, Mathf.PI, command.CyclePhase);
            float easedPlantProgress = Mathf.SmoothStep(0f, 1f, plantProgress);
            float touchdownKneeTarget = Mathf.Lerp(
                kneeAngleDegrees,
                kneeAngle * 0.1f * command.BlendWeight,
                easedPlantProgress);

            kneeAngleDegrees = Mathf.Min(kneeAngleDegrees, touchdownKneeTarget);
        }

        private static void ApplyRecoveryStepProfile(
            LegCommandOutput command,
            float stepAngle,
            float kneeAngle,
            ref float swingAngleDegrees,
            ref float kneeAngleDegrees)
        {
            float situationUrgency = GetSituationUrgency(command.RecoverySituation);
            float urgencyScale = Mathf.Lerp(1f, 1f + situationUrgency * 0.3f, command.RecoveryBlend);

            float recoveryStepProgress = Mathf.InverseLerp(0f, Mathf.PI, Mathf.Min(command.CyclePhase, Mathf.PI));
            float easedRecoveryProgress = Mathf.SmoothStep(0f, 1f, recoveryStepProgress);
            float recoverySwingTarget = Mathf.Lerp(
                -stepAngle * 0.28f,
                stepAngle * 0.72f * urgencyScale,
                easedRecoveryProgress) * command.BlendWeight;
            float recoveryKneeTarget = Mathf.Lerp(
                kneeAngle * 0.12f,
                kneeAngle * 0.7f * urgencyScale,
                easedRecoveryProgress) * command.BlendWeight;

            swingAngleDegrees = command.Mode == LegCommandMode.HoldPose
                ? recoverySwingTarget
                : Mathf.Lerp(swingAngleDegrees, recoverySwingTarget, 0.85f);
            kneeAngleDegrees = Mathf.Max(kneeAngleDegrees, recoveryKneeTarget);
        }

        private static void ApplyCatchStepProfile(
            LegCommandOutput command,
            float stepAngle,
            float kneeAngle,
            ref float swingAngleDegrees,
            ref float kneeAngleDegrees)
        {
            float situationUrgency = GetSituationUrgency(command.RecoverySituation);
            float urgencyScale = Mathf.Lerp(1f, 1f + situationUrgency * 0.25f, command.RecoveryBlend);

            float catchStepProgress = Mathf.InverseLerp(0f, Mathf.PI, Mathf.Min(command.CyclePhase, Mathf.PI));
            float catchStepForwardTarget = Mathf.Lerp(
                stepAngle * 0.64f,
                stepAngle * 0.78f * urgencyScale,
                Mathf.SmoothStep(0f, 1f, catchStepProgress)) * command.BlendWeight;
            float catchStepKneeTarget = kneeAngle * 0.65f * urgencyScale * command.BlendWeight;

            swingAngleDegrees = Mathf.Max(swingAngleDegrees, catchStepForwardTarget);
            kneeAngleDegrees = Mathf.Max(kneeAngleDegrees, catchStepKneeTarget);
        }

        /// <summary>
        /// Maps a <see cref="RecoverySituation"/> to a 0–1 urgency scalar used to scale
        /// recovery and catch-step profiles.
        /// </summary>
        internal static float GetSituationUrgency(RecoverySituation situation)
        {
            switch (situation)
            {
                case RecoverySituation.HardTurn:  return 0.2f;
                case RecoverySituation.Reversal:  return 0.4f;
                case RecoverySituation.Slip:      return 0.6f;
                case RecoverySituation.NearFall:  return 0.8f;
                case RecoverySituation.Stumble:   return 1.0f;
                default:                          return 0f;
            }
        }
    }
}
