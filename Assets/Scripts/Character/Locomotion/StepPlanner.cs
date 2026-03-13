using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Computes world-space step targets from locomotion intent, body state, and observation data.
    /// Part of the Chapter 4 step-planning layer. Produces <see cref="StepTarget"/> values
    /// consumed by <see cref="LegAnimator"/> through <see cref="LegCommandOutput"/>.
    /// Stateless computation unit — all decisions are derived from the inputs passed each call.
    /// Collaborators: <see cref="LegAnimator"/> (caller during BuildPassThroughCommands),
    /// <see cref="LocomotionDirector"/> (provides observation and desired input upstream),
    /// <see cref="LegStateMachine"/> (provides per-leg phase and state for timing).
    /// </summary>
    internal sealed class StepPlanner
    {
        // STEP 1: Base stride geometry — how far forward and how wide a default step lands
        //         relative to the hips, scaled by current planar speed.
        private const float BaseStrideLength = 0.45f;
        private const float MaxStrideLength = 0.85f;
        private const float BaseStepWidth = 0.15f;

        // STEP 2: Speed influence — stride length scales linearly with planar speed
        //         and clamps at a maximum to keep steps physically plausible.
        private const float SpeedStrideScale = 0.12f;

        // STEP 3: Turn influence — sharp turns widen the outside leg and shorten the inside
        //         leg to improve support geometry during direction changes.
        private const float TurnWidthBias = 0.5f;
        private const float TurnBrakingBias = 0.3f;

        // STEP 3b: Turn-specific stride differentiation (C4.3).
        //          The outside turn leg (TurnSupport) gets a longer stride to sweep a wider arc,
        //          while the inside turn leg (SpeedUp during turn) gets a shorter stride to
        //          pivot more tightly. Both scale with TurnSeverity (0-1).
        //          Timing also adjusts: the outside leg uses a slower swing (longer timing)
        //          so it does not rush the wider path, while the inside leg keeps default timing.
        private const float TurnStrideOutsideScale = 0.20f;
        private const float TurnStrideInsideScale = 0.15f;
        private const float TurnTimingOutsideScale = 0.25f;

        // STEP 3c: Braking step differentiation (C4.4).
        //          When the player releases input while the body is still moving, the
        //          Braking transition reason tells the planner to shorten the stride so the
        //          foot plants sooner, and reduce swing timing so the leg reaches the ground
        //          faster. Both scale with the existing BrakingBias magnitude so the
        //          effect ramps with residual speed.
        private const float BrakingStrideScale = 0.35f;
        private const float BrakingTimingScale = 0.25f;

        // STEP 4: COM drift compensation — when the COM leads or trails the support center,
        //         the step target shifts to recapture balance.
        private const float DriftCompensationScale = 0.35f;

        // STEP 5: Timing estimation — the desired landing time is derived from the remaining
        //         swing phase of the leg state machine, converted to seconds via step frequency.
        private const float MinDesiredTiming = 0.02f;

        // STEP 6: Confidence mapping — observation support quality maps directly to step
        //         confidence so the executor knows how much to trust the planned target.
        private const float MinConfidence = 0.2f;

        /// <summary>
        /// Computes a step target for a single leg that is currently in a swing-like state.
        /// Returns <see cref="StepTarget.Invalid"/> when the leg is not in swing or
        /// when insufficient observation data is available.
        /// </summary>
        public StepTarget ComputeSwingTarget(
            LocomotionLeg leg,
            float legPhase,
            LegStateType legState,
            LegStateTransitionReason transitionReason,
            DesiredInput desiredInput,
            LocomotionObservation observation,
            Vector3 hipsPosition,
            Vector3 gaitReferenceDirection,
            float stepFrequency)
        {
            // STEP 1: Only plan for legs that are actively in a swing-like state.
            //         Stance, Plant, and idle legs keep StepTarget.Invalid.
            if (!IsSwingLikeState(legState))
            {
                return StepTarget.Invalid;
            }

            // STEP 2: Compute the forward stride offset from speed, adjusted by turn role (C4.3)
            //         and braking intent (C4.4).
            float planarSpeed = observation.PlanarSpeed;
            float strideOffset = ComputeStrideOffset(planarSpeed);
            strideOffset = ApplyTurnStrideAdjustment(strideOffset, transitionReason, observation.TurnSeverity);
            strideOffset = ApplyBrakingStrideAdjustment(strideOffset, transitionReason, planarSpeed);

            // STEP 3: Compute lateral offset based on which leg.
            float lateralOffset = ComputeLateralOffset(leg);

            // STEP 4: Compute drift compensation from velocity.
            Vector3 driftCompensation = ComputeDriftCompensation(
                hipsPosition, observation.Velocity, gaitReferenceDirection);

            // STEP 5: Build the landing position in world space.
            Vector3 forward = gaitReferenceDirection.sqrMagnitude > 0.0001f
                ? gaitReferenceDirection.normalized
                : Vector3.forward;
            Vector3 lateral = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 landingPosition = new Vector3(hipsPosition.x, hipsPosition.y, hipsPosition.z)
                + forward * strideOffset
                + lateral * lateralOffset
                + driftCompensation;

            // STEP 6: Compute timing, biases, and confidence.
            //         C4.3: Outside turn leg gets extended timing for the wider arc.
            //         C4.4: Braking leg gets shortened timing for quicker plant.
            float desiredTiming = ComputeDesiredTiming(legPhase, stepFrequency);
            desiredTiming = ApplyTurnTimingAdjustment(desiredTiming, transitionReason, observation.TurnSeverity);
            desiredTiming = ApplyBrakingTimingAdjustment(desiredTiming, transitionReason, planarSpeed);
            float widthBias = ComputeWidthBias(leg, observation.TurnSeverity,
                gaitReferenceDirection, observation.BodyForward);
            float brakingBias = ComputeBrakingBias(desiredInput, planarSpeed);
            float confidence = ComputeConfidence(observation);

            return new StepTarget(landingPosition, desiredTiming, widthBias, brakingBias, confidence);
        }

        private static bool IsSwingLikeState(LegStateType state)
        {
            return state == LegStateType.Swing
                || state == LegStateType.CatchStep
                || state == LegStateType.RecoveryStep;
        }

        private static float ComputeStrideOffset(float planarSpeed)
        {
            // STEP 2: Stride scales with speed but saturates at high velocities.
            return Mathf.Min(BaseStrideLength + planarSpeed * SpeedStrideScale, MaxStrideLength);
        }

        private static float ComputeLateralOffset(LocomotionLeg leg)
        {
            // STEP 3: Left leg steps left (negative X), right leg steps right (positive X).
            return leg == LocomotionLeg.Left ? -BaseStepWidth : BaseStepWidth;
        }

        private static Vector3 ComputeDriftCompensation(
            Vector3 hipsPosition,
            Vector3 velocity,
            Vector3 gaitReferenceDirection)
        {
            // STEP 4: Project velocity perpendicular to gait direction to get lateral drift,
            //         then add a small compensation toward that drift to recapture support.
            Vector3 planarVelocity = Vector3.ProjectOnPlane(velocity, Vector3.up);
            if (planarVelocity.sqrMagnitude < 0.01f)
            {
                return Vector3.zero;
            }

            Vector3 forward = gaitReferenceDirection.sqrMagnitude > 0.0001f
                ? gaitReferenceDirection.normalized
                : Vector3.forward;
            Vector3 lateralDrift = planarVelocity - Vector3.Dot(planarVelocity, forward) * forward;
            return lateralDrift * DriftCompensationScale;
        }

        private static float ComputeDesiredTiming(float legPhase, float stepFrequency)
        {
            // STEP 5: Estimate remaining swing time from phase to touchdown (at PI).
            //         Swing occupies [0, PI) so remaining radians = PI - phase.
            //         One full cycle = 2*PI radians at stepFrequency cycles/sec.
            float safeFrequency = Mathf.Max(stepFrequency, 0.5f);
            float radiansPerSecond = safeFrequency * 2f * Mathf.PI;
            float remainingRadians = Mathf.Max(0f, Mathf.PI - legPhase);
            float timing = remainingRadians / radiansPerSecond;
            return Mathf.Max(MinDesiredTiming, timing);
        }

        private static float ComputeWidthBias(
            LocomotionLeg leg,
            float turnSeverity,
            Vector3 gaitReferenceDirection,
            Vector3 bodyForward)
        {
            // STEP 6: During turns, determine if this leg is on the outside (wider step)
            //         or inside (narrower step) of the turn.
            if (turnSeverity < 0.1f)
            {
                return 0f;
            }

            // Cross product tells us which side the turn is going to.
            Vector3 cross = Vector3.Cross(bodyForward, gaitReferenceDirection);
            float turnSign = cross.y; // positive = turning right, negative = turning left

            // Outside leg gets wider, inside leg gets narrower.
            bool isOutsideLeg = (leg == LocomotionLeg.Left && turnSign > 0f)
                             || (leg == LocomotionLeg.Right && turnSign < 0f);

            float rawBias = isOutsideLeg ? TurnWidthBias : -TurnWidthBias;
            return Mathf.Clamp(rawBias * turnSeverity, -1f, 1f);
        }

        private static float ComputeBrakingBias(
            DesiredInput desiredInput,
            float planarSpeed)
        {
            // STEP 7: If the player has little move intent but the body is still moving,
            //         produce a braking bias (shortened step). If accelerating, extend.
            if (!desiredInput.HasMoveIntent && planarSpeed > 0.5f)
            {
                return Mathf.Clamp(-TurnBrakingBias * planarSpeed, -1f, 0f);
            }

            if (desiredInput.MoveMagnitude > 0.8f && planarSpeed < 1f)
            {
                return Mathf.Clamp(TurnBrakingBias, 0f, 1f);
            }

            return 0f;
        }

        private static float ComputeConfidence(LocomotionObservation observation)
        {
            // STEP 8: Map support quality directly to confidence with a minimum floor.
            float raw = observation.SupportQuality;
            return Mathf.Clamp(Mathf.Lerp(MinConfidence, 1f, raw), 0f, 1f);
        }

        private static float ApplyTurnStrideAdjustment(
            float baseStride,
            LegStateTransitionReason reason,
            float turnSeverity)
        {
            // STEP 3b: Outside turn leg (TurnSupport) extends stride to sweep the wider arc.
            //          Inside turn leg (SpeedUp during a turn) shortens stride to pivot tightly.
            //          Both adjustments scale linearly with turn severity.
            if (reason == LegStateTransitionReason.TurnSupport)
            {
                return baseStride + baseStride * TurnStrideOutsideScale * turnSeverity;
            }

            if (reason == LegStateTransitionReason.SpeedUp && turnSeverity > 0.1f)
            {
                return baseStride - baseStride * TurnStrideInsideScale * turnSeverity;
            }

            return baseStride;
        }

        private static float ApplyTurnTimingAdjustment(
            float baseTiming,
            LegStateTransitionReason reason,
            float turnSeverity)
        {
            // STEP 3b: Outside turn leg gets proportionally longer swing timing so it does not
            //          rush through the wider arc. Inside leg keeps the base timing.
            if (reason == LegStateTransitionReason.TurnSupport)
            {
                return baseTiming + baseTiming * TurnTimingOutsideScale * turnSeverity;
            }

            return baseTiming;
        }

        private static float ApplyBrakingStrideAdjustment(
            float baseStride,
            LegStateTransitionReason reason,
            float planarSpeed)
        {
            // STEP 3c: When braking (no player input, body still moving) shorten the stride
            //          so the foot plants closer, anchoring the deceleration. The shortening
            //          scales with how fast the body is still moving (higher residual speed
            //          → stronger shortening) and uses BrakingStrideScale as the ceiling.
            if (reason != LegStateTransitionReason.Braking)
            {
                return baseStride;
            }

            float speedFactor = Mathf.Clamp01(planarSpeed / 3f);
            return baseStride * (1f - BrakingStrideScale * speedFactor);
        }

        private static float ApplyBrakingTimingAdjustment(
            float baseTiming,
            LegStateTransitionReason reason,
            float planarSpeed)
        {
            // STEP 3c: Braking steps also reduce swing timing so the foot reaches the ground
            //          faster, producing a quicker plant. Scales with residual speed.
            if (reason != LegStateTransitionReason.Braking)
            {
                return baseTiming;
            }

            float speedFactor = Mathf.Clamp01(planarSpeed / 3f);
            return baseTiming * (1f - BrakingTimingScale * speedFactor);
        }
    }
}
