namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Lightweight per-leg state payload that pairs an explicit Chapter 3 role with the
    /// reason that role was selected for the current command frame.
    /// </summary>
    internal readonly struct LegStateFrame
    {
        public LegStateFrame(
            LocomotionLeg leg,
            LegStateType state,
            LegStateTransitionReason transitionReason)
        {
            // STEP 1: Preserve the owning leg so downstream logs and tests can attribute roles correctly.
            Leg = leg;

            // STEP 2: Preserve the explicit role and transition reason exactly as chosen by the planner.
            State = state;
            TransitionReason = transitionReason;
        }

        public LocomotionLeg Leg { get; }

        public LegStateType State { get; }

        public LegStateTransitionReason TransitionReason { get; }

        public static LegStateFrame Disabled(LocomotionLeg leg)
        {
            return new LegStateFrame(leg, LegStateType.Stance, LegStateTransitionReason.None);
        }
    }
}