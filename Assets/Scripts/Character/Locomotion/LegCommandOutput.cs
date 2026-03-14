using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Per-leg command payload emitted by the locomotion coordination layer.
    /// Carries swing parameters, state labels, an optional world-space step target,
    /// and recovery context so executors can combine phase-driven swing with
    /// foothold-aware placement and situation-aware recovery expression.
    /// Collaborators: <see cref="LocomotionDirector"/> (producer), <see cref="LegAnimator"/> (consumer).
    /// </summary>
    internal readonly struct LegCommandOutput
    {
        public LegCommandOutput(
            LocomotionLeg leg,
            LegCommandMode mode,
            LegStateFrame stateFrame,
            float cyclePhase,
            float swingAngleDegrees,
            float kneeAngleDegrees,
            float blendWeight,
            StepTarget stepTarget,
            RecoverySituation recoverySituation = RecoverySituation.None,
            float recoveryBlend = 0f)
        {
            // STEP 1: Preserve the leg identity and requested command mode exactly as issued.
            Leg = leg;
            Mode = mode;
            StateFrame = stateFrame.Leg == leg
                ? stateFrame
                : new LegStateFrame(leg, stateFrame.State, stateFrame.TransitionReason);

            // STEP 2: Normalize numeric payloads so downstream executors can assume sane ranges.
            CyclePhase = mode == LegCommandMode.Disabled ? 0f : Mathf.Repeat(cyclePhase, Mathf.PI * 2f);
            SwingAngleDegrees = swingAngleDegrees;
            KneeAngleDegrees = kneeAngleDegrees;
            BlendWeight = Mathf.Clamp01(blendWeight);

            // STEP 3: Carry the step target through for foothold-aware execution.
            StepTarget = stepTarget;
            FootTarget = stepTarget.IsValid ? stepTarget.LandingPosition : Vector3.zero;

            // STEP 4: Carry recovery context so leg executors can modulate profiles by situation.
            RecoverySituation = recoverySituation;
            RecoveryBlend = Mathf.Clamp01(recoveryBlend);
        }

        public LocomotionLeg Leg { get; }

        public LegCommandMode Mode { get; }

        public LegStateFrame StateFrame { get; }

        public LegStateType State => StateFrame.State;

        public LegStateTransitionReason TransitionReason => StateFrame.TransitionReason;

        public float CyclePhase { get; }

        public float SwingAngleDegrees { get; }

        public float KneeAngleDegrees { get; }

        public float BlendWeight { get; }

        /// <summary>
        /// World-space step target describing the planned landing position, timing, and biases.
        /// Invalid when no step plan has been computed.
        /// </summary>
        public StepTarget StepTarget { get; }

        /// <summary>
        /// Legacy convenience accessor. Returns <see cref="StepTarget.LandingPosition"/> when
        /// the target is valid, or <see cref="Vector3.zero"/> otherwise.
        /// </summary>
        public Vector3 FootTarget { get; }

        /// <summary>
        /// The active recovery situation classified by the director, or
        /// <see cref="RecoverySituation.None"/> during normal locomotion.
        /// </summary>
        public RecoverySituation RecoverySituation { get; }

        /// <summary>
        /// How far into the active recovery the system currently is (0 = just entered,
        /// 1 = fully blended). Zero when no recovery is active.
        /// </summary>
        public float RecoveryBlend { get; }

        /// <summary>
        /// Returns a copy of this command with the recovery context stamped in.
        /// Used by the director to inject recovery state after pass-through command generation.
        /// </summary>
        public LegCommandOutput WithRecoveryContext(RecoverySituation situation, float blend)
        {
            return new LegCommandOutput(
                Leg,
                Mode,
                StateFrame,
                CyclePhase,
                SwingAngleDegrees,
                KneeAngleDegrees,
                BlendWeight,
                StepTarget,
                situation,
                blend);
        }

        public static LegCommandOutput Disabled(LocomotionLeg leg)
        {
            return new LegCommandOutput(
                leg,
                LegCommandMode.Disabled,
                LegStateFrame.Disabled(leg),
                0f,
                0f,
                0f,
                0f,
                StepTarget.Invalid);
        }
    }
}