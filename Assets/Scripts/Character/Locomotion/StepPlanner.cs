using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Computes world-space step targets from locomotion intent, body state, and observation data,
    /// including explicit clearance requests for detected step-up approaches.
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
        private const float BaseStrideLength = 0.15f;
        private const float MaxStrideLength = 0.30f;
        private const float BaseStepWidth = 0.17f;

        // STEP 2: Speed influence — stride length scales linearly with planar speed
        //         and clamps at a maximum to keep steps physically plausible.
        private const float SpeedStrideScale = 0.04f;

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

        // STEP 3d: Catch-step differentiation (C4.5).
        //          When support quality drops and the leg enters a StumbleRecovery catch-step,
        //          the planner extends stride, widens the lateral offset, and shortens timing
        //          to plant a broader, farther anchor that recaptures support.
        //          All three scale with support urgency (1 - SupportQuality) so a mild dip
        //          produces a small correction while a near-airborne catch-step reaches aggressively.
        private const float CatchStepStrideScale = 0.30f;
        private const float CatchStepWidenScale = 0.04f;
        private const float CatchStepTimingScale = 0.20f;

        // STEP 3d-terrain: Surface-aware catch-step modulation (C7.4b).
        //          On degraded surfaces, catch-step stride extension is reduced because the
        //          landing surface is unreliable, while lateral widening is boosted to broaden
        //          the support polygon. Both scale with surface instability so flat-ground
        //          catch-steps remain unchanged.
        private const float TerrainCatchStepStrideRetention = 0.50f;
        private const float TerrainCatchStepExtraWiden = 0.015f;

        // STEP 3e: Partial-contact bracing (C7.3b).
        //          When surface normal quality drops (slope, edge, uneven surface) the planner
        //          shortens stride, widens lateral offset, and shortens timing to plant a more
        //          conservative step that broadens the support polygon. Unlike catch-steps,
        //          bracing applies to every swing-like state and scales with surface instability
        //          (1 - MinSurfaceNormalQuality). A quality floor avoids triggering on noise.
        private const float BracingSurfaceQualityFloor = 0.85f;
        private const float BracingStrideScale = 0.15f;
        private const float BracingWidenScale = 0.02f;
        private const float BracingTimingScale = 0.10f;

        // STEP 4: COM drift compensation — when the COM leads or trails the support center,
        //         the step target shifts to recapture balance.
        private const float DriftCompensationScale = 0.35f;

        // STEP 5: Timing estimation — the desired landing time is derived from the remaining
        //         swing phase of the leg state machine, converted to seconds via step frequency.
        private const float MinDesiredTiming = 0.02f;

        // STEP 6: Confidence mapping — observation support quality maps directly to step
        //         confidence so the executor knows how much to trust the planned target.
        private const float MinConfidence = 0.2f;

        // STEP 6b: Clearance request gating — only promote terrain clearance intent when
        //          the obstruction sample is tall and confident enough to represent a real step-up.
        private const float MinimumClearanceRequestHeight = 0.05f;
        private const float MinimumClearanceRequestConfidence = 0.35f;
        private const float MinimumStepUpLandingCarryDistance = 0.03f;
        private const float SevereStepCarryStartHeight = 0.06f;
        private const float SevereStepCarryFullHeight = 0.12f;

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

            // STEP 2: Compute the forward stride offset from speed, adjusted by turn role (C4.3),
            //         braking intent (C4.4), catch-step urgency (C4.5), and surface bracing (C7.3b).
            float planarSpeed = observation.PlanarSpeed;
            float strideOffset = ComputeStrideOffset(planarSpeed);
            strideOffset = ApplyTurnStrideAdjustment(strideOffset, transitionReason, observation.TurnSeverity);
            strideOffset = ApplyBrakingStrideAdjustment(strideOffset, transitionReason, planarSpeed);
            strideOffset = ApplyCatchStepStrideAdjustment(strideOffset, transitionReason, observation.SupportQuality, observation.MinSurfaceNormalQuality);
            strideOffset = ApplyBracingStrideAdjustment(strideOffset, observation.MinSurfaceNormalQuality);

            // STEP 3: Compute lateral offset based on which leg, widened for catch-steps (C4.5)
            //         and surface bracing (C7.3b).
            float lateralOffset = ComputeLateralOffset(leg);
            lateralOffset = ApplyCatchStepLateralAdjustment(lateralOffset, transitionReason, observation.SupportQuality, observation.MinSurfaceNormalQuality);
            lateralOffset = ApplyBracingLateralAdjustment(lateralOffset, observation.MinSurfaceNormalQuality);

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

            float confidence = ComputeConfidence(observation);
            float requestedClearanceHeight = ComputeRequestedClearanceHeight(leg, observation);
            if (TryGetPreferredStepUpLandingSample(
                leg,
                observation,
                out Vector3 topSurfacePoint,
                out float landingStepHeight,
                out float landingObstructionConfidence))
            {
                landingPosition = ApplyStepUpLandingPlacement(
                    hipsPosition,
                    forward,
                    lateral,
                    lateralOffset,
                    driftCompensation,
                    strideOffset,
                    topSurfacePoint,
                    landingStepHeight,
                    confidence,
                    landingObstructionConfidence);
            }

            // STEP 6: Compute timing, biases, and confidence.
            //         C4.3: Outside turn leg gets extended timing for the wider arc.
            //         C4.4: Braking leg gets shortened timing for quicker plant.
            //         C4.5: Catch-step leg gets shortened timing to anchor sooner.
            //         C7.3b: Bracing shortens timing on degraded surfaces.
            float desiredTiming = ComputeDesiredTiming(legPhase, stepFrequency);
            desiredTiming = ApplyTurnTimingAdjustment(desiredTiming, transitionReason, observation.TurnSeverity);
            desiredTiming = ApplyBrakingTimingAdjustment(desiredTiming, transitionReason, planarSpeed);
            desiredTiming = ApplyCatchStepTimingAdjustment(desiredTiming, transitionReason, observation.SupportQuality);
            desiredTiming = ApplyBracingTimingAdjustment(desiredTiming, observation.MinSurfaceNormalQuality);
            float widthBias = ComputeWidthBias(leg, observation.TurnSeverity,
                gaitReferenceDirection, observation.BodyForward);
            float brakingBias = ComputeBrakingBias(desiredInput, planarSpeed);

            return new StepTarget(
                landingPosition,
                desiredTiming,
                widthBias,
                brakingBias,
                confidence,
                requestedClearanceHeight);
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

        private static float ComputeRequestedClearanceHeight(
            LocomotionLeg swingLeg,
            LocomotionObservation observation)
        {
            // STEP 6b: Clearance requests stay tied to height/confidence even when the touch-down
            //          top-surface sample is not yet available. That preserves the original contract
            //          while letting the planner optionally use richer touchdown data when present.
            float swingLegClearance = GetValidClearanceHeightForLeg(swingLeg, observation);
            float oppositeLegClearance = GetValidClearanceHeightForLeg(GetOppositeLeg(swingLeg), observation);
            return Mathf.Max(swingLegClearance, oppositeLegClearance);
        }

        private static bool TryGetPreferredStepUpLandingSample(
            LocomotionLeg swingLeg,
            LocomotionObservation observation,
            out Vector3 topSurfacePoint,
            out float stepHeight,
            out float obstructionConfidence)
        {
            // STEP 6b: Prefer the swing leg's own obstruction sample when it is valid, but also
            //          consider the opposite foot because the planted foot often sees the step face
            //          one stride earlier than the next swing foot. Pick the strongest valid sample
            //          so the request remains tied to a real measured obstruction instead of a global gait mode.
            bool hasSwingLegSample = TryGetValidStepUpLandingSampleForLeg(
                swingLeg,
                observation,
                out Vector3 swingLegTopSurfacePoint,
                out float swingLegStepHeight,
                out float swingLegConfidence);
            bool hasOppositeLegSample = TryGetValidStepUpLandingSampleForLeg(
                GetOppositeLeg(swingLeg),
                observation,
                out Vector3 oppositeLegTopSurfacePoint,
                out float oppositeLegStepHeight,
                out float oppositeLegConfidence);

            if (!hasSwingLegSample && !hasOppositeLegSample)
            {
                topSurfacePoint = Vector3.zero;
                stepHeight = 0f;
                obstructionConfidence = 0f;
                return false;
            }

            if (hasSwingLegSample && (!hasOppositeLegSample || swingLegConfidence >= oppositeLegConfidence))
            {
                topSurfacePoint = swingLegTopSurfacePoint;
                stepHeight = swingLegStepHeight;
                obstructionConfidence = swingLegConfidence;
                return true;
            }

            topSurfacePoint = oppositeLegTopSurfacePoint;
            stepHeight = oppositeLegStepHeight;
            obstructionConfidence = oppositeLegConfidence;
            return true;
        }

        private static float GetValidClearanceHeightForLeg(
            LocomotionLeg leg,
            LocomotionObservation observation)
        {
            bool hasForwardObstruction;
            float estimatedStepHeight;
            float obstructionConfidence;

            if (leg == LocomotionLeg.Left)
            {
                hasForwardObstruction = observation.HasLeftForwardObstruction;
                estimatedStepHeight = observation.LeftEstimatedStepHeight;
                obstructionConfidence = observation.LeftForwardObstructionConfidence;
            }
            else
            {
                hasForwardObstruction = observation.HasRightForwardObstruction;
                estimatedStepHeight = observation.RightEstimatedStepHeight;
                obstructionConfidence = observation.RightForwardObstructionConfidence;
            }

            if (!hasForwardObstruction ||
                estimatedStepHeight < MinimumClearanceRequestHeight ||
                obstructionConfidence < MinimumClearanceRequestConfidence)
            {
                return 0f;
            }

            return Mathf.Max(0f, estimatedStepHeight);
        }

        private static bool TryGetValidStepUpLandingSampleForLeg(
            LocomotionLeg leg,
            LocomotionObservation observation,
            out Vector3 topSurfacePoint,
            out float stepHeight,
            out float obstructionConfidence)
        {
            bool hasForwardObstruction;
            float estimatedStepHeight;
            bool hasTopSurfacePoint;

            if (leg == LocomotionLeg.Left)
            {
                hasForwardObstruction = observation.HasLeftForwardObstruction;
                estimatedStepHeight = observation.LeftEstimatedStepHeight;
                obstructionConfidence = observation.LeftForwardObstructionConfidence;
                hasTopSurfacePoint = observation.HasLeftForwardObstructionTopSurfacePoint;
                topSurfacePoint = observation.LeftForwardObstructionTopSurfacePoint;
            }
            else
            {
                hasForwardObstruction = observation.HasRightForwardObstruction;
                estimatedStepHeight = observation.RightEstimatedStepHeight;
                obstructionConfidence = observation.RightForwardObstructionConfidence;
                hasTopSurfacePoint = observation.HasRightForwardObstructionTopSurfacePoint;
                topSurfacePoint = observation.RightForwardObstructionTopSurfacePoint;
            }

            if (!hasForwardObstruction ||
                !hasTopSurfacePoint ||
                estimatedStepHeight < MinimumClearanceRequestHeight ||
                obstructionConfidence < MinimumClearanceRequestConfidence)
            {
                topSurfacePoint = Vector3.zero;
                stepHeight = 0f;
                obstructionConfidence = 0f;
                return false;
            }

            stepHeight = Mathf.Max(0f, estimatedStepHeight);
            return true;
        }

        private static Vector3 ApplyStepUpLandingPlacement(
            Vector3 hipsPosition,
            Vector3 forward,
            Vector3 lateral,
            float lateralOffset,
            Vector3 driftCompensation,
            float defaultStrideOffset,
            Vector3 topSurfacePoint,
            float stepHeight,
            float plannerConfidence,
            float obstructionConfidence)
        {
            float obstructionForwardOffset = Vector3.Dot(
                Vector3.ProjectOnPlane(topSurfacePoint - hipsPosition, Vector3.up),
                forward);
            float baseForwardOffset = Mathf.Max(defaultStrideOffset, obstructionForwardOffset);
            float carryTrust = Mathf.Sqrt(Mathf.Clamp01(plannerConfidence) * Mathf.Clamp01(obstructionConfidence));
            float severeStepBlend = Mathf.InverseLerp(SevereStepCarryStartHeight, SevereStepCarryFullHeight, stepHeight);
            float carryBaseDistance = Mathf.Lerp(
                Mathf.Max(defaultStrideOffset * 0.5f, stepHeight * 1.5f),
                defaultStrideOffset + stepHeight,
                severeStepBlend);
            float carryDistance = Mathf.Max(
                MinimumStepUpLandingCarryDistance,
                carryBaseDistance) * carryTrust;
            float landingForwardOffset = baseForwardOffset + carryDistance;

            return new Vector3(hipsPosition.x, topSurfacePoint.y, hipsPosition.z)
                + forward * landingForwardOffset
                + lateral * lateralOffset
                + driftCompensation;
        }

        private static LocomotionLeg GetOppositeLeg(LocomotionLeg leg)
        {
            return leg == LocomotionLeg.Left ? LocomotionLeg.Right : LocomotionLeg.Left;
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

        private static float ApplyCatchStepStrideAdjustment(
            float baseStride,
            LegStateTransitionReason reason,
            float supportQuality,
            float minSurfaceNormalQuality)
        {
            // STEP 3d: Catch-step (StumbleRecovery) extends stride so the recovery foot
            //          lands farther forward, providing a wider base to arrest the fall.
            //          The extension scales with support urgency (1 - SupportQuality).
            //          C7.4b: On degraded surfaces the extension is reduced because the
            //          landing surface is unreliable — a shorter, more controlled catch-step
            //          is safer than a long reach onto a slope or step edge.
            if (reason != LegStateTransitionReason.StumbleRecovery)
            {
                return baseStride;
            }

            float urgency = 1f - Mathf.Clamp01(supportQuality);
            float surfaceRetention = Mathf.Lerp(
                TerrainCatchStepStrideRetention, 1f,
                Mathf.Clamp01(minSurfaceNormalQuality / BracingSurfaceQualityFloor));
            return baseStride + baseStride * CatchStepStrideScale * urgency * surfaceRetention;
        }

        private static float ApplyCatchStepLateralAdjustment(
            float baseLateral,
            LegStateTransitionReason reason,
            float supportQuality,
            float minSurfaceNormalQuality)
        {
            // STEP 3d: Catch-step widens the lateral offset (away from center) to broaden
            //          the support polygon. The sign of baseLateral already encodes left/right.
            //          C7.4b: On degraded surfaces, additional widening is applied because
            //          a broader support polygon is more valuable on unreliable footing.
            if (reason != LegStateTransitionReason.StumbleRecovery)
            {
                return baseLateral;
            }

            float urgency = 1f - Mathf.Clamp01(supportQuality);
            float instability = ComputeBracingInstability(minSurfaceNormalQuality);
            float widen = CatchStepWidenScale * urgency
                + TerrainCatchStepExtraWiden * instability;
            return baseLateral + Mathf.Sign(baseLateral) * widen;
        }

        private static float ApplyCatchStepTimingAdjustment(
            float baseTiming,
            LegStateTransitionReason reason,
            float supportQuality)
        {
            // STEP 3d: Catch-step shortens timing so the recovery foot plants sooner,
            //          anchoring the body before the fall progresses.
            if (reason != LegStateTransitionReason.StumbleRecovery)
            {
                return baseTiming;
            }

            float urgency = 1f - Mathf.Clamp01(supportQuality);
            return baseTiming * (1f - CatchStepTimingScale * urgency);
        }

        // ── C7.3b Partial-contact bracing adjustments ─────────────────────────

        private static float ComputeBracingInstability(float minSurfaceNormalQuality)
        {
            // Remap quality below the floor into a 0..1 instability metric.
            // Quality >= BracingSurfaceQualityFloor → 0 (no bracing).
            // Quality == 0 → 1 (maximum bracing).
            float clamped = Mathf.Clamp01(minSurfaceNormalQuality);
            return Mathf.Clamp01(1f - Mathf.InverseLerp(0f, BracingSurfaceQualityFloor, clamped));
        }

        private static float ApplyBracingStrideAdjustment(
            float baseStride,
            float minSurfaceNormalQuality)
        {
            // STEP 3e: Shorten stride when the surface is degraded so the foot plants
            //          closer, keeping the support polygon compact and controllable.
            float instability = ComputeBracingInstability(minSurfaceNormalQuality);
            if (instability <= 0f)
            {
                return baseStride;
            }

            return baseStride * (1f - BracingStrideScale * instability);
        }

        private static float ApplyBracingLateralAdjustment(
            float baseLateral,
            float minSurfaceNormalQuality)
        {
            // STEP 3e: Widen lateral offset away from center to broaden the support
            //          polygon on degraded surfaces. The sign of baseLateral encodes leg side.
            float instability = ComputeBracingInstability(minSurfaceNormalQuality);
            if (instability <= 0f)
            {
                return baseLateral;
            }

            float widen = BracingWidenScale * instability;
            return baseLateral + Mathf.Sign(baseLateral) * widen;
        }

        private static float ApplyBracingTimingAdjustment(
            float baseTiming,
            float minSurfaceNormalQuality)
        {
            // STEP 3e: Shorten timing so the foot reaches the ground faster, establishing
            //          support sooner on unstable surfaces.
            float instability = ComputeBracingInstability(minSurfaceNormalQuality);
            if (instability <= 0f)
            {
                return baseTiming;
            }

            return baseTiming * (1f - BracingTimingScale * instability);
        }
    }
}
