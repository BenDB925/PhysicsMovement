using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Per-leg command payload emitted by the locomotion coordination layer.
    /// Carries swing parameters, state labels, and an optional world-space step target
    /// so executors can combine phase-driven swing with foothold-aware placement.
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
            StepTarget stepTarget)
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