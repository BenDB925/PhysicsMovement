using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Per-leg command payload emitted by future locomotion coordination logic.
    /// The contract stays lightweight in C1.2 so the next slice can wire it through
    /// without changing locomotion behaviour yet.
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
            Vector3 footTarget)
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

            // STEP 3: Preserve any future world-space target without interpretation in this slice.
            FootTarget = footTarget;
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
                Vector3.zero);
        }
    }
}