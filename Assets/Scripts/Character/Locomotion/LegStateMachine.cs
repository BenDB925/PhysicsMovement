using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Owns the explicit Chapter 3 locomotion state for one leg while preserving a cycle phase
    /// compatible with the current sinusoidal leg executor.
    /// </summary>
    internal sealed class LegStateMachine
    {
        private const float TwoPi = Mathf.PI * 2f;
        private const float PhaseEpsilon = 0.0001f;
        private const float MinimumTouchdownPhase = Mathf.PI * 0.75f;
        private const float PlantEntryPhase = Mathf.PI * 0.85f;
        private const float EarlyLaunchPhase = Mathf.PI * 1.35f;

        private readonly bool _startsInSwing;

        public LegStateMachine(LocomotionLeg leg, bool startsInSwing)
        {
            // STEP 1: Preserve the leg identity and preferred mirrored starting role.
            Leg = leg;
            _startsInSwing = startsInSwing;

            // STEP 2: Seed the machine in the same mirrored cadence used by the legacy gait.
            ResetForMirroredCadence();
        }

        public LocomotionLeg Leg { get; }

        public LegStateType CurrentState { get; private set; }

        public LegStateTransitionReason TransitionReason { get; private set; }

        public float CyclePhase { get; private set; }

        public void ResetForMirroredCadence()
        {
            // STEP 1: Return the machine to the legacy left/right swing-support split.
            CurrentState = _startsInSwing ? LegStateType.Swing : LegStateType.Stance;
            TransitionReason = LegStateTransitionReason.None;
            CyclePhase = _startsInSwing ? 0f : Mathf.PI;
        }

        public void ResetToIdle()
        {
            // STEP 1: Idle always collapses to a neutral support role.
            CurrentState = LegStateType.Stance;
            TransitionReason = LegStateTransitionReason.None;
            CyclePhase = GetIdleAnchorPhase();
        }

        public void ForceState(LegStateType state, LegStateTransitionReason transitionReason, float cyclePhase)
        {
            // STEP 1: Accept an authoritative external state from the current command frame.
            CurrentState = state;
            TransitionReason = transitionReason;
            CyclePhase = Mathf.Repeat(cyclePhase, TwoPi);
        }

        public void SyncFromLegacyPhase(float cyclePhase, LegStateTransitionReason transitionReason)
        {
            // STEP 1: Mirror the externally supplied legacy phase exactly so old test seams remain valid.
            CyclePhase = Mathf.Repeat(cyclePhase, TwoPi);

            // STEP 2: Infer the explicit state label from that phase instead of assuming a permanent mirror.
            TransitionReason = transitionReason;
            if (transitionReason == LegStateTransitionReason.None)
            {
                CurrentState = LegStateType.Stance;
                CyclePhase = GetIdleAnchorPhase();
                return;
            }

            CurrentState = InferStateFromPhase(CyclePhase);
        }

        public LegStateFrame AdvanceMoving(
            FootContactObservation footObservation,
            LegStateType oppositeLegState,
            LegStateTransitionReason transitionReason,
            float phaseAdvance,
            bool forceCatchStep)
        {
            // STEP 1: Recovery-style catch steps override the normal cadence and immediately claim the leg.
            if (forceCatchStep)
            {
                AdvanceCatchStep(footObservation, phaseAdvance);
                return BuildFrame();
            }

            // STEP 2: Preserve the currently active transition reason so logs explain the chosen role.
            TransitionReason = transitionReason;

            // STEP 3: Advance the state-local timing while consulting the opposite leg before leaving support.
            switch (CurrentState)
            {
                case LegStateType.Swing:
                case LegStateType.RecoveryStep:
                    AdvanceSwing(footObservation, phaseAdvance);
                    break;

                case LegStateType.CatchStep:
                    AdvanceCatchStep(footObservation, phaseAdvance);
                    break;

                case LegStateType.Plant:
                    AdvancePlant(footObservation, phaseAdvance);
                    break;

                default:
                    AdvanceStance(footObservation, oppositeLegState, transitionReason, phaseAdvance);
                    break;
            }

            return BuildFrame();
        }

        public LegStateFrame AdvanceIdle(float phaseDecay)
        {
            // STEP 1: Idle never shows a fake touchdown or swing; it decays toward the neutral support anchor.
            CurrentState = LegStateType.Stance;
            TransitionReason = LegStateTransitionReason.None;
            CyclePhase = Mathf.MoveTowards(CyclePhase, GetIdleAnchorPhase(), Mathf.Max(0f, phaseDecay));
            return BuildFrame();
        }

        private void AdvanceSwing(FootContactObservation footObservation, float phaseAdvance)
        {
            float nextPhase = Mathf.Min(CyclePhase + Mathf.Max(0f, phaseAdvance), Mathf.PI - PhaseEpsilon);
            CyclePhase = nextPhase;

            if (footObservation.IsGrounded && nextPhase >= MinimumTouchdownPhase)
            {
                CurrentState = LegStateType.Plant;
            }
        }

        private void AdvancePlant(FootContactObservation footObservation, float phaseAdvance)
        {
            // STEP 1: Keep the leg near touchdown when contact has not been re-established yet.
            if (!footObservation.IsGrounded && CyclePhase >= Mathf.PI - 0.05f)
            {
                CyclePhase = Mathf.PI - PhaseEpsilon;
                return;
            }

            float nextPhase = Mathf.Min(CyclePhase + Mathf.Max(0f, phaseAdvance), Mathf.PI);
            CyclePhase = nextPhase;

            // STEP 2: Once contact is back, settle into stance at the exact support boundary.
            if (footObservation.IsGrounded &&
                (footObservation.IsPlanted || nextPhase >= Mathf.PI - PhaseEpsilon))
            {
                CurrentState = LegStateType.Stance;
                CyclePhase = Mathf.PI;
            }
        }

        private void AdvanceCatchStep(FootContactObservation footObservation, float phaseAdvance)
        {
            bool wasCatchStep = CurrentState == LegStateType.CatchStep;
            if (CurrentState != LegStateType.CatchStep)
            {
                CurrentState = LegStateType.CatchStep;
                CyclePhase = CyclePhase >= Mathf.PI ? 0f : CyclePhase;
            }

            TransitionReason = LegStateTransitionReason.StumbleRecovery;
            CyclePhase = Mathf.Min(CyclePhase + Mathf.Max(0f, phaseAdvance), Mathf.PI - PhaseEpsilon);

            if (wasCatchStep && footObservation.IsGrounded && CyclePhase >= MinimumTouchdownPhase)
            {
                CurrentState = LegStateType.Plant;
            }
        }

        private void AdvanceStance(
            FootContactObservation footObservation,
            LegStateType oppositeLegState,
            LegStateTransitionReason transitionReason,
            float phaseAdvance)
        {
            if (CyclePhase < Mathf.PI)
            {
                CyclePhase = Mathf.PI;
            }

            float nextPhase = CyclePhase + Mathf.Max(0f, phaseAdvance) * GetStanceAdvanceMultiplier(transitionReason);
            if (CanEnterSwing(oppositeLegState, footObservation, transitionReason, nextPhase))
            {
                CurrentState = LegStateType.Swing;
                CyclePhase = Mathf.Max(0f, nextPhase - TwoPi);
                return;
            }

            CyclePhase = Mathf.Min(nextPhase, TwoPi);
        }

        private bool CanEnterSwing(
            LegStateType oppositeLegState,
            FootContactObservation footObservation,
            LegStateTransitionReason transitionReason,
            float nextPhase)
        {
            // STEP 1: Never release both legs into swing-like states at the same time.
            if (IsOppositeLegBusy(oppositeLegState))
            {
                return false;
            }

            // STEP 2: Allow a normal release at the end of stance or an earlier release when support is already weak.
            if (nextPhase >= TwoPi - PhaseEpsilon)
            {
                return true;
            }

            bool allowEarlyLaunch = transitionReason == LegStateTransitionReason.SpeedUp ||
                transitionReason == LegStateTransitionReason.StumbleRecovery;
            return allowEarlyLaunch && !footObservation.IsPlanted && nextPhase >= EarlyLaunchPhase;
        }

        private static bool IsOppositeLegBusy(LegStateType oppositeLegState)
        {
            return oppositeLegState == LegStateType.Swing ||
                oppositeLegState == LegStateType.CatchStep ||
                oppositeLegState == LegStateType.RecoveryStep;
        }

        private static LegStateType InferStateFromPhase(float cyclePhase)
        {
            if (cyclePhase < PlantEntryPhase)
            {
                return LegStateType.Swing;
            }

            if (cyclePhase < Mathf.PI)
            {
                return LegStateType.Plant;
            }

            return LegStateType.Stance;
        }

        private float GetIdleAnchorPhase()
        {
            return _startsInSwing ? 0f : Mathf.PI;
        }

        private static float GetStanceAdvanceMultiplier(LegStateTransitionReason transitionReason)
        {
            switch (transitionReason)
            {
                case LegStateTransitionReason.SpeedUp:
                case LegStateTransitionReason.StumbleRecovery:
                    return 1.1f;

                case LegStateTransitionReason.Braking:
                    return 0.85f;

                case LegStateTransitionReason.TurnSupport:
                    return 0.9f;

                default:
                    return 1f;
            }
        }

        private LegStateFrame BuildFrame()
        {
            return new LegStateFrame(Leg, CurrentState, TransitionReason);
        }
    }
}