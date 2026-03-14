using System.Collections;
using System.Reflection;
using NUnit.Framework;
using PhysicsDrivenMovement.Character;
using PhysicsDrivenMovement.Core;
using PhysicsDrivenMovement.Environment;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// Outcome-based gait tests that run in the Arena_01 scene and assert observable
    /// real-world results of sustained movement input. Unlike unit tests, these tests
    /// catch system-level regressions: weak springs, zeroed baselines, broken gait
    /// gating, or any other failure that prevents the character from actually walking.
    ///
    /// Design philosophy:
    ///   These tests exist because subtle bugs (e.g. CaptureBaselineDrives() running
    ///   before RagdollSetup.Awake, causing springs captured as 0) pass all unit tests
    ///   but produce "cat-pulsing-paws" behaviour in the editor — joints receiving
    ///   targetRotation but too limp to act on it. Outcome tests catch that class of
    ///   bug by asserting on physical results, not internal state.
    ///
    /// Timing: 5 seconds = 500 FixedUpdate frames at 100 Hz.
    /// </summary>
    public class GaitOutcomeTests
    {
        // ─── Constants ────────────────────────────────────────────────────────

        private const string ArenaSceneName = "Arena_01";

        /// <summary>100 Hz × 5 seconds = 500 FixedUpdate ticks for the walk runs.</summary>
        private const int WalkFrames = 500;

        /// <summary>Settle before input: let BC stabilise from spawn (1 second @ 100 Hz).</summary>
        private const int SettleFrames = 100;

        /// <summary>
        /// Minimum horizontal displacement expected after 5 seconds of held move input.
        /// At max speed 5 m/s this would be ~25 m; we require 2.5 m as a threshold that
        /// proves continuous forward progress without stalling mid-stride.
        /// </summary>
        private const float MinDisplacementMetres = 2.5f;

        /// <summary>
        /// Minimum peak upper-leg rotation observed during the walk run.
        /// LegAnimator targets stepAngle ~50° on forward swing; we require at least 15°
        /// of actual rotation so the joint is clearly responding to targetRotation.
        /// A value of 0–5° would indicate "pulsing paws" (joint receiving target but
        /// spring too weak to follow it).
        /// </summary>
        private const float MinPeakLegRotationDeg = 15f;

        /// <summary>
        /// Minimum SLERP spring required on leg joints after Start().
        /// Must match or exceed RagdollSetup._lowerLegSpring default so any zeroing
        /// regression is caught before physics even begins.
        /// </summary>
        private const float MinLegSpringAfterStart = 800f;

        private const int StepUpWalkFrames = 700;
        private const float StepUpSpawnRunUpMetres = 1.1f;
        private const float StepUpPlateauClearanceMetres = 0.25f;
        private const float StepUpPlateauHeightTolerance = 0.05f;
        private const float StepUpSurfaceProbeInset = 0.2f;
        private const int StepUpSurfaceSampleCount = 48;
        private const int MaxStepUpConsecutiveFallenFrames = 80;

        private const int SlopeLaneWalkFrames = 500;
        private const float SlopeSpawnRunUpMetres = 1.0f;
        private const float SlopeMinForwardProgress = 4.0f;
        private const int MaxSlopeConsecutiveFallenFrames = 80;

        private const int StepDownWalkFrames = 600;
        private const float StepDownSpawnRunUpMetres = 1.0f;
        private const float StepDownMinForwardProgress = 4.0f;
        private const int MaxStepDownConsecutiveFallenFrames = 80;

        // ─── Scene Setup ──────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator AfterStart_LegJointSprings_AreNonZero()
        {
            // Purpose: catch CaptureBaselineDrives() running before RagdollSetup.Awake
            // sets the authoritative spring values. If LegAnimator captures spring=0
            // and later calls SetLegSpringMultiplier(1f), it restores 0 × 1 = 0.
            // This was the root cause of the "cat pulsing paws" regression in Phase 3F2.
            //
            // Note: we wait SettleFrames (1s) before checking so the ragdoll has time to
            // land, transition Airborne→Standing, and restore springs to baseline.
            // Checking immediately after Start can catch the airborne-multiplier (0.15×)
            // rather than the true baseline, giving a false failure.

            yield return LoadArenaScene();

            // Wait for ragdoll to land and springs to be restored to baseline
            for (int i = 0; i < SettleFrames; i++)
                yield return new WaitForFixedUpdate();

            LegAnimator legAnimator = FindLegAnimator();
            ConfigurableJoint upperLegL = FindJointByName(legAnimator.gameObject, "UpperLeg_L");
            ConfigurableJoint upperLegR = FindJointByName(legAnimator.gameObject, "UpperLeg_R");
            ConfigurableJoint lowerLegL = FindJointByName(legAnimator.gameObject, "LowerLeg_L");
            ConfigurableJoint lowerLegR = FindJointByName(legAnimator.gameObject, "LowerLeg_R");

            Assert.That(upperLegL, Is.Not.Null, "UpperLeg_L joint not found on PlayerRagdoll hierarchy.");
            Assert.That(lowerLegL, Is.Not.Null, "LowerLeg_L joint not found on PlayerRagdoll hierarchy.");

            LogBaseline(
                nameof(AfterStart_LegJointSprings_AreNonZero),
                $"upperLegL={upperLegL.slerpDrive.positionSpring:F0} upperLegR={upperLegR.slerpDrive.positionSpring:F0} " +
                $"lowerLegL={lowerLegL.slerpDrive.positionSpring:F0} lowerLegR={lowerLegR.slerpDrive.positionSpring:F0}");

            Assert.That(upperLegL.slerpDrive.positionSpring, Is.GreaterThanOrEqualTo(MinLegSpringAfterStart),
                $"UpperLeg_L spring={upperLegL.slerpDrive.positionSpring} after settle — must be >= {MinLegSpringAfterStart}. " +
                $"Likely cause: CaptureBaselineDrives() captured spring=0 before RagdollSetup.Awake ran, " +
                $"or springs not restored after Airborne→Standing transition.");

            Assert.That(upperLegR.slerpDrive.positionSpring, Is.GreaterThanOrEqualTo(MinLegSpringAfterStart),
                $"UpperLeg_R spring={upperLegR.slerpDrive.positionSpring} after settle — must be >= {MinLegSpringAfterStart}.");

            Assert.That(lowerLegL.slerpDrive.positionSpring, Is.GreaterThanOrEqualTo(MinLegSpringAfterStart),
                $"LowerLeg_L spring={lowerLegL.slerpDrive.positionSpring} after settle — must be >= {MinLegSpringAfterStart}.");

            Assert.That(lowerLegR.slerpDrive.positionSpring, Is.GreaterThanOrEqualTo(MinLegSpringAfterStart),
                $"LowerLeg_R spring={lowerLegR.slerpDrive.positionSpring} after settle — must be >= {MinLegSpringAfterStart}.");
        }

        [UnityTest]
        public IEnumerator HoldingMoveInput_For5Seconds_CharacterMovesForward()
        {
            // Purpose: catch any regression that causes zero net displacement under
            // sustained input. Covers: broken input gate, isMoving always false,
            // _isAirborne stuck true, BalanceController always fallen, etc.

            yield return LoadArenaScene();

            LegAnimator legAnimator = FindLegAnimator();
            PlayerMovement movement = legAnimator.GetComponent<PlayerMovement>();
            Rigidbody hipsRb = legAnimator.GetComponent<Rigidbody>();

            Assert.That(movement, Is.Not.Null, "PlayerMovement component not found.");
            Assert.That(hipsRb, Is.Not.Null, "Rigidbody not found on hips.");

            // Settle first
            for (int i = 0; i < SettleFrames; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            Vector3 startPos = hipsRb.position;

            // Apply rightward input for WalkFrames
            movement.SetMoveInputForTest(Vector2.right);
            for (int i = 0; i < WalkFrames; i++)
            {
                yield return new WaitForFixedUpdate();
            }
            movement.SetMoveInputForTest(Vector2.zero);

            Vector3 endPos = hipsRb.position;
            float displacement = Vector3.Distance(
                new Vector3(startPos.x, 0f, startPos.z),
                new Vector3(endPos.x, 0f, endPos.z));

            LogBaseline(
                nameof(HoldingMoveInput_For5Seconds_CharacterMovesForward),
                $"displacement={displacement:F2}m start=({startPos.x:F2},{startPos.z:F2}) end=({endPos.x:F2},{endPos.z:F2})");

            Assert.That(displacement, Is.GreaterThanOrEqualTo(MinDisplacementMetres),
                $"After 5s of move input, horizontal displacement was only {displacement:F2}m " +
                $"(minimum expected: {MinDisplacementMetres}m). Character may be frozen, " +
                $"constantly falling, or spring too weak to push off ground.");
        }

        [UnityTest]
        public IEnumerator HoldingMoveInput_For5Seconds_UpperLegsActuallyRotate()
        {
            // Purpose: catch "cat pulsing paws" — joints receive targetRotation but spring
            // is too weak to actually move the limb. This test observes the actual physical
            // rotation of UpperLeg_L during the walk and asserts a meaningful peak angle.

            yield return LoadArenaScene();

            LegAnimator legAnimator = FindLegAnimator();
            PlayerMovement movement = legAnimator.GetComponent<PlayerMovement>();
            ConfigurableJoint upperLegL = FindJointByName(legAnimator.gameObject, "UpperLeg_L");

            Assert.That(upperLegL, Is.Not.Null, "UpperLeg_L joint not found.");

            // Settle
            for (int i = 0; i < SettleFrames; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            float peakRotation = 0f;

            movement.SetMoveInputForTest(Vector2.right);
            for (int i = 0; i < WalkFrames; i++)
            {
                yield return new WaitForFixedUpdate();

                // Measure rotation on the primary (X) axis in local space
                float xAngle = upperLegL.transform.localEulerAngles.x;
                // Normalise to [-180, 180] so forward-swing (positive) and back-swing
                // (negative, wraps to ~310–350°) are both captured correctly.
                if (xAngle > 180f) xAngle -= 360f;
                peakRotation = Mathf.Max(peakRotation, Mathf.Abs(xAngle));
            }
            movement.SetMoveInputForTest(Vector2.zero);

            LogBaseline(
                nameof(HoldingMoveInput_For5Seconds_UpperLegsActuallyRotate),
                $"upperLegLPeakRotation={peakRotation:F1}deg");

            Assert.That(peakRotation, Is.GreaterThanOrEqualTo(MinPeakLegRotationDeg),
                $"UpperLeg_L peak rotation during 5s walk was only {peakRotation:F1}° " +
                $"(minimum expected: {MinPeakLegRotationDeg}°). This suggests the joint is " +
                $"receiving targetRotation but the SLERP spring is too weak to follow it " +
                $"(spring zeroed by CaptureBaselineDrives/SetLegSpringMultiplier bug).");
        }

        [UnityTest]
        public IEnumerator HoldingMoveInput_For5Seconds_LegsAlternate_NotBothPeakingTogether()
        {
            // Purpose: catch "frozen stride" — both legs stuck in the same phase, swinging
            // together instead of alternating. This indicates the gait phase is broken,
            // the sine wave is always returning the same value for both legs, or the
            // left/right phase offset is missing.
            //
            // Method: count frames where left and right upper legs are both simultaneously
            // at a high forward angle (>10°). In correct alternating gait this should be
            // rare (near the crossover point). If it's consistently high, legs are in sync.

            yield return LoadArenaScene();

            LegAnimator legAnimator = FindLegAnimator();
            PlayerMovement movement = legAnimator.GetComponent<PlayerMovement>();
            ConfigurableJoint upperLegL = FindJointByName(legAnimator.gameObject, "UpperLeg_L");
            ConfigurableJoint upperLegR = FindJointByName(legAnimator.gameObject, "UpperLeg_R");

            Assert.That(upperLegL, Is.Not.Null, "UpperLeg_L joint not found.");
            Assert.That(upperLegR, Is.Not.Null, "UpperLeg_R joint not found.");

            for (int i = 0; i < SettleFrames; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            int bothForwardFrames = 0;
            int activeFrames = 0;
            const float syncDetectionThreshold = 10f;

            movement.SetMoveInputForTest(Vector2.right);
            for (int i = 0; i < WalkFrames; i++)
            {
                yield return new WaitForFixedUpdate();

                float lAngle = upperLegL.transform.localEulerAngles.x;
                float rAngle = upperLegR.transform.localEulerAngles.x;
                if (lAngle > 180f) lAngle -= 360f;
                if (rAngle > 180f) rAngle -= 360f;

                if (Mathf.Abs(lAngle) > 5f || Mathf.Abs(rAngle) > 5f)
                {
                    activeFrames++;
                    if (lAngle > syncDetectionThreshold && rAngle > syncDetectionThreshold)
                    {
                        bothForwardFrames++;
                    }
                }
            }
            movement.SetMoveInputForTest(Vector2.zero);

            // Allow up to 10% of active frames to have both legs simultaneously forward
            // (natural at the crossover). More than that indicates synchronised gait.
            float syncFraction = activeFrames > 0 ? (float)bothForwardFrames / activeFrames : 0f;

            LogBaseline(
                nameof(HoldingMoveInput_For5Seconds_LegsAlternate_NotBothPeakingTogether),
                $"bothForwardFrames={bothForwardFrames} activeFrames={activeFrames} syncFraction={syncFraction:P1}");

            Assert.That(syncFraction, Is.LessThan(0.10f),
                $"Both upper legs were simultaneously forward for {syncFraction:P0} of active frames " +
                $"({bothForwardFrames}/{activeFrames}). Expected < 10% — legs should alternate. " +
                $"This suggests the left/right phase offset (π) is missing or the gait phase is broken.");
        }

        [UnityTest]
        public IEnumerator DirectApproachIntoStepUpLane_MakesForwardProgressOverRaisedLanding()
        {
            // Arrange
            yield return LoadArenaScene();

            TerrainScenarioMarker stepUpLane = FindTerrainScenarioMarker(TerrainScenarioType.StepUpLane);
            LegAnimator legAnimator = FindLegAnimator();
            PlayerMovement movement = legAnimator.GetComponent<PlayerMovement>();
            Rigidbody hipsRb = legAnimator.GetComponent<Rigidbody>();
            BalanceController balance = legAnimator.GetComponent<BalanceController>();
            CharacterState characterState = legAnimator.GetComponent<CharacterState>();
            RagdollSetup ragdollSetup = legAnimator.GetComponent<RagdollSetup>();
            LocomotionDirector director = legAnimator.GetComponent<LocomotionDirector>();
            GroundSensor leftSensor = FindGroundSensor(legAnimator.gameObject, "Foot_L");
            GroundSensor rightSensor = FindGroundSensor(legAnimator.gameObject, "Foot_R");

            Assert.That(stepUpLane, Is.Not.Null, "Arena_01 must expose a StepUpLane marker for the Chapter 7 outcome regression.");
            Assert.That(movement, Is.Not.Null, "PlayerMovement component not found on the Arena_01 character.");
            Assert.That(hipsRb, Is.Not.Null, "Rigidbody not found on the Arena_01 character.");
            Assert.That(balance, Is.Not.Null, "BalanceController component not found on the Arena_01 character.");
            Assert.That(characterState, Is.Not.Null, "CharacterState component not found on the Arena_01 character.");
            Assert.That(director, Is.Not.Null, "LocomotionDirector component not found on the Arena_01 character.");
            Assert.That(leftSensor, Is.Not.Null, "Left GroundSensor not found on the Arena_01 character.");
            Assert.That(rightSensor, Is.Not.Null, "Right GroundSensor not found on the Arena_01 character.");

            ResolveAscendingLaneProfile(
                stepUpLane,
                out Vector3 travelDirection,
                out Vector3 lowSidePoint,
                out Vector3 highSidePoint,
                out float highSideHeight);

            float plateauEntryDistance = FindRaisedPlateauEntryDistance(lowSidePoint, highSidePoint, travelDirection, highSideHeight);
            Vector3 desiredSpawnPosition = new Vector3(lowSidePoint.x, hipsRb.position.y, lowSidePoint.z)
                - travelDirection * StepUpSpawnRunUpMetres;
            RepositionRagdoll(ragdollSetup, hipsRb, desiredSpawnPosition);

            for (int i = 0; i < SettleFrames; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            Vector3 startPosition = hipsRb.position;
            Vector2 moveInput = BuildMoveInputForWorldDirection(travelDirection);
            float requiredProgress = plateauEntryDistance + StepUpSpawnRunUpMetres + StepUpPlateauClearanceMetres;
            float maxForwardProgress = 0f;
            float maxHipsHeight = hipsRb.position.y;
            int consecutiveFallenFrames = 0;
            int maxConsecutiveFallenFrames = 0;
            int obstructionFrames = 0;
            int clearanceRequestFrames = 0;
            float maxDetectedStepHeight = 0f;
            float maxRequestedClearanceHeight = 0f;
            float maxFacingAlignment = -1f;
            float maxGroundedSupportHeight = Mathf.Max(leftSensor.GroundPoint.y, rightSensor.GroundPoint.y);
            float maxPlannedLandingProgress = float.NegativeInfinity;
            float minPlannedLandingHeightErrorToPlateau = float.PositiveInfinity;

            // Act
            movement.SetMoveInputForTest(moveInput);
            for (int i = 0; i < StepUpWalkFrames; i++)
            {
                yield return new WaitForFixedUpdate();

                float forwardProgress = Vector3.Dot(hipsRb.position - startPosition, travelDirection);
                maxForwardProgress = Mathf.Max(maxForwardProgress, forwardProgress);
                maxHipsHeight = Mathf.Max(maxHipsHeight, hipsRb.position.y);
                maxFacingAlignment = Mathf.Max(
                    maxFacingAlignment,
                    Vector3.Dot(Vector3.ProjectOnPlane(legAnimator.transform.forward, Vector3.up).normalized, travelDirection));
                maxGroundedSupportHeight = Mathf.Max(
                    maxGroundedSupportHeight,
                    Mathf.Max(leftSensor.GroundPoint.y, rightSensor.GroundPoint.y));

                bool hasForwardObstruction = leftSensor.HasForwardObstruction || rightSensor.HasForwardObstruction;
                if (hasForwardObstruction)
                {
                    obstructionFrames++;
                }

                maxDetectedStepHeight = Mathf.Max(
                    maxDetectedStepHeight,
                    Mathf.Max(leftSensor.EstimatedStepHeight, rightSensor.EstimatedStepHeight));

                float requestedClearanceHeight = GetMaximumRequestedClearanceHeight(director);
                if (requestedClearanceHeight > 0f)
                {
                    clearanceRequestFrames++;
                }

                maxRequestedClearanceHeight = Mathf.Max(maxRequestedClearanceHeight, requestedClearanceHeight);

                if (TryGetPlannedLandingMetrics(
                    director,
                    highSideHeight,
                    startPosition,
                    travelDirection,
                    out float plannedLandingProgress,
                    out float plannedLandingHeightErrorToPlateau))
                {
                    maxPlannedLandingProgress = Mathf.Max(maxPlannedLandingProgress, plannedLandingProgress);
                    minPlannedLandingHeightErrorToPlateau = Mathf.Min(
                        minPlannedLandingHeightErrorToPlateau,
                        plannedLandingHeightErrorToPlateau);
                }

                if (characterState.CurrentState == CharacterStateType.Fallen)
                {
                    consecutiveFallenFrames++;
                    maxConsecutiveFallenFrames = Mathf.Max(maxConsecutiveFallenFrames, consecutiveFallenFrames);
                }
                else
                {
                    consecutiveFallenFrames = 0;
                }
            }

            movement.SetMoveInputForTest(Vector2.zero);

            float finalForwardProgress = Vector3.Dot(hipsRb.position - startPosition, travelDirection);

            LogBaseline(
                nameof(DirectApproachIntoStepUpLane_MakesForwardProgressOverRaisedLanding),
                $"requiredProgress={requiredProgress:F2}m plateauEntry={plateauEntryDistance + StepUpSpawnRunUpMetres:F2}m " +
                $"finalProgress={finalForwardProgress:F2}m maxProgress={maxForwardProgress:F2}m maxHipsHeight={maxHipsHeight:F2}m " +
                $"maxConsecutiveFallenFrames={maxConsecutiveFallenFrames} obstructionFrames={obstructionFrames} " +
                $"clearanceRequestFrames={clearanceRequestFrames} maxDetectedStepHeight={maxDetectedStepHeight:F3}m " +
                $"maxRequestedClearanceHeight={maxRequestedClearanceHeight:F3}m maxFacingAlignment={maxFacingAlignment:F2} " +
                $"maxGroundedSupportHeight={maxGroundedSupportHeight:F3}m maxPlannedLandingProgress={maxPlannedLandingProgress:F2}m " +
                $"minPlannedLandingHeightErrorToPlateau={minPlannedLandingHeightErrorToPlateau:F3}m " +
                $"groundedEnd={balance.IsGrounded} stateEnd={characterState.CurrentState}");

            // Assert
            Assert.That(maxForwardProgress, Is.GreaterThanOrEqualTo(requiredProgress),
                $"Direct movement into the authored StepUpLane should carry the character onto the raised landing instead of stalling at the face. " +
                $"Needed at least {requiredProgress:F2}m of forward progress from spawn, but only reached {maxForwardProgress:F2}m. " +
                $"Final progress={finalForwardProgress:F2}m, final state={characterState.CurrentState}, groundedEnd={balance.IsGrounded}.");

            Assert.That(maxConsecutiveFallenFrames, Is.LessThanOrEqualTo(MaxStepUpConsecutiveFallenFrames),
                $"Direct step-up traversal should stay in planning/execution, not collapse into a long fallen recovery. " +
                $"Observed {maxConsecutiveFallenFrames} consecutive Fallen frames (limit {MaxStepUpConsecutiveFallenFrames}).");

            Assert.That(balance.IsGrounded, Is.True,
                "Step-up traversal should finish with at least one grounded foot on the raised landing.");
        }

        [UnityTest]
        public IEnumerator WalkUpSlopeLane_MaintainsProgressOnInclinedSurface()
        {
            // Purpose: catch regressions where the character oscillates, collapses, or
            // stalls when walking up an inclined surface. The slope ramp degrades
            // GroundNormalUpAlignment below 1.0, activating the partial-contact
            // observation pipeline (C7.3a) and, if steep enough, the bracing planner
            // adjustments (C7.3b). The test asserts physical outcome: forward progress
            // past the ramp and no extended fallen state.

            yield return LoadArenaScene();

            TerrainScenarioMarker slopeLane = FindTerrainScenarioMarker(TerrainScenarioType.SlopeLane);
            LegAnimator legAnimator = FindLegAnimator();
            PlayerMovement movement = legAnimator.GetComponent<PlayerMovement>();
            Rigidbody hipsRb = legAnimator.GetComponent<Rigidbody>();
            BalanceController balance = legAnimator.GetComponent<BalanceController>();
            CharacterState characterState = legAnimator.GetComponent<CharacterState>();
            RagdollSetup ragdollSetup = legAnimator.GetComponent<RagdollSetup>();
            GroundSensor leftSensor = FindGroundSensor(legAnimator.gameObject, "Foot_L");
            GroundSensor rightSensor = FindGroundSensor(legAnimator.gameObject, "Foot_R");

            Assert.That(slopeLane, Is.Not.Null,
                "Arena_01 must expose a SlopeLane marker for the C7.3c partial-contact outcome regression.");
            Assert.That(movement, Is.Not.Null, "PlayerMovement component not found on the Arena_01 character.");
            Assert.That(hipsRb, Is.Not.Null, "Rigidbody not found on the Arena_01 character.");
            Assert.That(balance, Is.Not.Null, "BalanceController component not found on the Arena_01 character.");
            Assert.That(characterState, Is.Not.Null, "CharacterState component not found on the Arena_01 character.");
            Assert.That(leftSensor, Is.Not.Null, "Left GroundSensor not found on the Arena_01 character.");
            Assert.That(rightSensor, Is.Not.Null, "Right GroundSensor not found on the Arena_01 character.");

            ResolveAscendingLaneProfile(
                slopeLane,
                out Vector3 travelDirection,
                out Vector3 lowSidePoint,
                out Vector3 highSidePoint,
                out float highSideHeight);

            Vector3 spawnPosition = new Vector3(lowSidePoint.x, hipsRb.position.y, lowSidePoint.z)
                - travelDirection * SlopeSpawnRunUpMetres;
            RepositionRagdoll(ragdollSetup, hipsRb, spawnPosition);

            for (int i = 0; i < SettleFrames; i++)
                yield return new WaitForFixedUpdate();

            Vector3 startPosition = hipsRb.position;
            Vector2 moveInput = BuildMoveInputForWorldDirection(travelDirection);
            float maxForwardProgress = 0f;
            float maxHipsHeight = hipsRb.position.y;
            float minObservedAlignment = 1f;
            int degradedQualityFrames = 0;
            int consecutiveFallenFrames = 0;
            int maxConsecutiveFallenFrames = 0;

            // Act
            movement.SetMoveInputForTest(moveInput);
            for (int i = 0; i < SlopeLaneWalkFrames; i++)
            {
                yield return new WaitForFixedUpdate();

                float forwardProgress = Vector3.Dot(hipsRb.position - startPosition, travelDirection);
                maxForwardProgress = Mathf.Max(maxForwardProgress, forwardProgress);
                maxHipsHeight = Mathf.Max(maxHipsHeight, hipsRb.position.y);

                float leftAlignment = leftSensor.GroundNormalUpAlignment;
                float rightAlignment = rightSensor.GroundNormalUpAlignment;
                float frameMinAlignment = Mathf.Min(leftAlignment, rightAlignment);
                minObservedAlignment = Mathf.Min(minObservedAlignment, frameMinAlignment);
                if (frameMinAlignment < 1f - 0.001f)
                    degradedQualityFrames++;

                if (characterState.CurrentState == CharacterStateType.Fallen)
                {
                    consecutiveFallenFrames++;
                    maxConsecutiveFallenFrames = Mathf.Max(maxConsecutiveFallenFrames, consecutiveFallenFrames);
                }
                else
                {
                    consecutiveFallenFrames = 0;
                }
            }
            movement.SetMoveInputForTest(Vector2.zero);

            float derivedMinQuality = Mathf.InverseLerp(0.5f, 1f, minObservedAlignment);

            LogBaseline(
                nameof(WalkUpSlopeLane_MaintainsProgressOnInclinedSurface),
                $"maxProgress={maxForwardProgress:F2}m maxHipsHeight={maxHipsHeight:F2}m " +
                $"minAlignment={minObservedAlignment:F3} derivedMinQuality={derivedMinQuality:F3} " +
                $"degradedFrames={degradedQualityFrames} maxConsecutiveFallenFrames={maxConsecutiveFallenFrames} " +
                $"highSideHeight={highSideHeight:F2}m groundedEnd={balance.IsGrounded} stateEnd={characterState.CurrentState}");

            // Assert
            Assert.That(maxForwardProgress, Is.GreaterThanOrEqualTo(SlopeMinForwardProgress),
                $"Walking up the authored SlopeLane should carry the character past the ramp without stalling. " +
                $"Needed at least {SlopeMinForwardProgress:F1}m of forward progress, but only reached {maxForwardProgress:F2}m. " +
                $"Final state={characterState.CurrentState}, groundedEnd={balance.IsGrounded}.");

            Assert.That(maxConsecutiveFallenFrames, Is.LessThanOrEqualTo(MaxSlopeConsecutiveFallenFrames),
                $"Walking up the slope should not collapse into an extended fallen state. " +
                $"Observed {maxConsecutiveFallenFrames} consecutive Fallen frames (limit {MaxSlopeConsecutiveFallenFrames}).");

            Assert.That(minObservedAlignment, Is.LessThan(1f),
                $"At least one frame should register degraded surface-normal alignment on the slope ramp, " +
                $"proving that the partial-contact observation pipeline detects the incline. " +
                $"Observed minimum alignment={minObservedAlignment:F3}.");
        }

        [UnityTest]
        public IEnumerator WalkDownStepDownLane_RecoversThroughDescentWithoutExtendedFall()
        {
            // Purpose: terrain-specific recovery reproduction (C7.4a). Walking down
            // steps naturally destabilises the character as each foot drops off a ledge.
            // The test proves catch-step or stumble recovery keeps the character upright
            // through the descent instead of collapsing into an extended Fallen state.
            // This locks terrain recovery as a focused PlayMode repro so Chapter 7 no
            // longer relies on flat-ground recovery tests alone.

            yield return LoadArenaScene();

            TerrainScenarioMarker stepDownLane = FindTerrainScenarioMarker(TerrainScenarioType.StepDownLane);
            LegAnimator legAnimator = FindLegAnimator();
            PlayerMovement movement = legAnimator.GetComponent<PlayerMovement>();
            Rigidbody hipsRb = legAnimator.GetComponent<Rigidbody>();
            BalanceController balance = legAnimator.GetComponent<BalanceController>();
            CharacterState characterState = legAnimator.GetComponent<CharacterState>();
            RagdollSetup ragdollSetup = legAnimator.GetComponent<RagdollSetup>();
            LocomotionDirector director = legAnimator.GetComponent<LocomotionDirector>();
            GroundSensor leftSensor = FindGroundSensor(legAnimator.gameObject, "Foot_L");
            GroundSensor rightSensor = FindGroundSensor(legAnimator.gameObject, "Foot_R");

            Assert.That(stepDownLane, Is.Not.Null,
                "Arena_01 must expose a StepDownLane marker for the C7.4a terrain recovery repro.");
            Assert.That(movement, Is.Not.Null, "PlayerMovement component not found on the Arena_01 character.");
            Assert.That(hipsRb, Is.Not.Null, "Rigidbody not found on the Arena_01 character.");
            Assert.That(balance, Is.Not.Null, "BalanceController component not found on the Arena_01 character.");
            Assert.That(characterState, Is.Not.Null, "CharacterState component not found on the Arena_01 character.");
            Assert.That(director, Is.Not.Null, "LocomotionDirector component not found on the Arena_01 character.");
            Assert.That(leftSensor, Is.Not.Null, "Left GroundSensor not found on the Arena_01 character.");
            Assert.That(rightSensor, Is.Not.Null, "Right GroundSensor not found on the Arena_01 character.");

            // Allow a few physics frames for the broadphase to register all loaded
            // scene colliders; terrain lane box colliders may not be raycastable in
            // the first FixedUpdate after LoadSceneAsync completes.
            Physics.SyncTransforms();
            for (int i = 0; i < 5; i++)
                yield return new WaitForFixedUpdate();

            ResolveDescendingLaneProfile(
                stepDownLane,
                out Vector3 travelDirection,
                out Vector3 highSidePoint,
                out Vector3 lowSidePoint,
                out float highSideHeight);

            // The marker bounds encode the lane rise as center.y * 2 for step lanes.
            // Use that as a fallback if the raycast-based height resolution cannot
            // distinguish the elevated start platform from the ground plane.
            float boundsRise = stepDownLane.ScenarioBounds.center.y * 2f;
            if (highSideHeight < 0.1f && boundsRise > 0.1f)
            {
                highSideHeight = boundsRise;
            }

            Assert.That(highSideHeight, Is.GreaterThanOrEqualTo(0.1f),
                $"StepDownLane high-side height should be above ground level " +
                $"(got {highSideHeight:F3}m, bounds rise={boundsRise:F3}m). " +
                $"Scene may need rebuilding.");

            // For descending: spawn ON the elevated Start platform, inset from
            // the high-side edge toward the lane interior so the character has
            // solid ground underneath during settle.
            Vector3 spawnPosition = new Vector3(highSidePoint.x, hipsRb.position.y + highSideHeight, highSidePoint.z)
                + travelDirection * StepDownSpawnRunUpMetres;
            RepositionRagdoll(ragdollSetup, hipsRb, spawnPosition);

            for (int i = 0; i < SettleFrames; i++)
                yield return new WaitForFixedUpdate();

            Vector3 startPosition = hipsRb.position;
            Vector2 moveInput = BuildMoveInputForWorldDirection(travelDirection);
            float maxForwardProgress = 0f;
            float maxHipsHeight = hipsRb.position.y;
            int consecutiveFallenFrames = 0;
            int maxConsecutiveFallenFrames = 0;
            int totalFallenTransitions = 0;
            int recoveryActiveFrames = 0;
            float minObservedAlignment = 1f;
            CharacterStateType previousState = characterState.CurrentState;

            // Act
            movement.SetMoveInputForTest(moveInput);
            for (int i = 0; i < StepDownWalkFrames; i++)
            {
                yield return new WaitForFixedUpdate();

                float forwardProgress = Vector3.Dot(hipsRb.position - startPosition, travelDirection);
                maxForwardProgress = Mathf.Max(maxForwardProgress, forwardProgress);
                maxHipsHeight = Mathf.Max(maxHipsHeight, hipsRb.position.y);

                float leftAlignment = leftSensor.GroundNormalUpAlignment;
                float rightAlignment = rightSensor.GroundNormalUpAlignment;
                minObservedAlignment = Mathf.Min(minObservedAlignment, Mathf.Min(leftAlignment, rightAlignment));

                if (director.IsRecoveryActive)
                    recoveryActiveFrames++;

                CharacterStateType currentState = characterState.CurrentState;
                if (currentState == CharacterStateType.Fallen && previousState != CharacterStateType.Fallen)
                    totalFallenTransitions++;
                previousState = currentState;

                if (currentState == CharacterStateType.Fallen)
                {
                    consecutiveFallenFrames++;
                    maxConsecutiveFallenFrames = Mathf.Max(maxConsecutiveFallenFrames, consecutiveFallenFrames);
                }
                else
                {
                    consecutiveFallenFrames = 0;
                }
            }
            movement.SetMoveInputForTest(Vector2.zero);

            LogBaseline(
                nameof(WalkDownStepDownLane_RecoversThroughDescentWithoutExtendedFall),
                $"maxProgress={maxForwardProgress:F2}m maxHipsHeight={maxHipsHeight:F2}m " +
                $"maxConsecutiveFallenFrames={maxConsecutiveFallenFrames} totalFallenTransitions={totalFallenTransitions} " +
                $"recoveryActiveFrames={recoveryActiveFrames} minAlignment={minObservedAlignment:F3} " +
                $"highSideHeight={highSideHeight:F2}m groundedEnd={balance.IsGrounded} stateEnd={characterState.CurrentState}");

            // Assert
            Assert.That(maxForwardProgress, Is.GreaterThanOrEqualTo(StepDownMinForwardProgress),
                $"Walking down the authored StepDownLane should carry the character through the descent. " +
                $"Needed at least {StepDownMinForwardProgress:F1}m of forward progress, but only reached {maxForwardProgress:F2}m. " +
                $"Final state={characterState.CurrentState}, groundedEnd={balance.IsGrounded}.");

            Assert.That(maxConsecutiveFallenFrames, Is.LessThanOrEqualTo(MaxStepDownConsecutiveFallenFrames),
                $"Descent through the StepDownLane should not collapse into an extended fallen state. " +
                $"Observed {maxConsecutiveFallenFrames} consecutive Fallen frames (limit {MaxStepDownConsecutiveFallenFrames}).");
        }

        // ─── Helpers ──────────────────────────────────────────────────────────

        private static IEnumerator LoadArenaScene()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(ArenaSceneName, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null, $"Failed to start async load for scene '{ArenaSceneName}'.");
            while (!load.isDone) yield return null;
            yield return null;
            yield return new WaitForFixedUpdate();
        }

        private static LegAnimator FindLegAnimator()
        {
            LegAnimator[] animators = Object.FindObjectsByType<LegAnimator>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            Assert.That(animators.Length, Is.GreaterThan(0), "No active LegAnimator found in scene.");
            return animators[0];
        }

        private static TerrainScenarioMarker FindTerrainScenarioMarker(TerrainScenarioType scenarioType)
        {
            TerrainScenarioMarker[] markers = Object.FindObjectsByType<TerrainScenarioMarker>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

            for (int i = 0; i < markers.Length; i++)
            {
                if (markers[i].ScenarioType == scenarioType)
                {
                    return markers[i];
                }
            }

            return null;
        }

        private static GroundSensor FindGroundSensor(GameObject root, string footName)
        {
            GroundSensor[] sensors = root.GetComponentsInChildren<GroundSensor>(includeInactive: false);
            for (int i = 0; i < sensors.Length; i++)
            {
                Transform sensorTransform = sensors[i].transform;
                if (sensorTransform != null && sensorTransform.name == footName)
                {
                    return sensors[i];
                }
            }

            return null;
        }

        private static float GetMaximumRequestedClearanceHeight(LocomotionDirector director)
        {
            if (director == null)
            {
                return 0f;
            }

            object leftCommand = typeof(LocomotionDirector)
                .GetField("_leftLegCommand", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(director);
            object rightCommand = typeof(LocomotionDirector)
                .GetField("_rightLegCommand", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(director);

            return Mathf.Max(
                GetRequestedClearanceHeight(leftCommand),
                GetRequestedClearanceHeight(rightCommand));
        }

        private static bool TryGetPlannedLandingMetrics(
            LocomotionDirector director,
            float plateauHeight,
            Vector3 startPosition,
            Vector3 travelDirection,
            out float maximumLandingProgress,
            out float minimumLandingHeightErrorToPlateau)
        {
            maximumLandingProgress = float.NegativeInfinity;
            minimumLandingHeightErrorToPlateau = float.PositiveInfinity;

            if (director == null)
            {
                return false;
            }

            object leftCommand = typeof(LocomotionDirector)
                .GetField("_leftLegCommand", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(director);
            object rightCommand = typeof(LocomotionDirector)
                .GetField("_rightLegCommand", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(director);

            bool foundLanding = false;
            AccumulateLandingMetrics(
                leftCommand,
                plateauHeight,
                startPosition,
                travelDirection,
                ref foundLanding,
                ref maximumLandingProgress,
                ref minimumLandingHeightErrorToPlateau);
            AccumulateLandingMetrics(
                rightCommand,
                plateauHeight,
                startPosition,
                travelDirection,
                ref foundLanding,
                ref maximumLandingProgress,
                ref minimumLandingHeightErrorToPlateau);
            return foundLanding;
        }

        private static void AccumulateLandingMetrics(
            object legCommand,
            float plateauHeight,
            Vector3 startPosition,
            Vector3 travelDirection,
            ref bool foundLanding,
            ref float maximumLandingProgress,
            ref float minimumLandingHeightErrorToPlateau)
        {
            if (!TryGetLandingPosition(legCommand, out Vector3 landingPosition))
            {
                return;
            }

            foundLanding = true;
            maximumLandingProgress = Mathf.Max(
                maximumLandingProgress,
                Vector3.Dot(landingPosition - startPosition, travelDirection));
            minimumLandingHeightErrorToPlateau = Mathf.Min(
                minimumLandingHeightErrorToPlateau,
                Mathf.Abs(landingPosition.y - plateauHeight));
        }

        private static bool TryGetLandingPosition(object legCommand, out Vector3 landingPosition)
        {
            landingPosition = Vector3.zero;
            if (legCommand == null)
            {
                return false;
            }

            object stepTarget = legCommand.GetType().GetProperty("StepTarget")?.GetValue(legCommand);
            if (stepTarget == null)
            {
                return false;
            }

            object isValid = stepTarget.GetType().GetProperty("IsValid")?.GetValue(stepTarget);
            if (isValid is not bool hasValidLanding || !hasValidLanding)
            {
                return false;
            }

            object rawLandingPosition = stepTarget.GetType().GetProperty("LandingPosition")?.GetValue(stepTarget);
            if (rawLandingPosition is not Vector3 landing)
            {
                return false;
            }

            landingPosition = landing;
            return true;
        }

        private static float GetRequestedClearanceHeight(object legCommand)
        {
            if (legCommand == null)
            {
                return 0f;
            }

            object stepTarget = legCommand.GetType().GetProperty("StepTarget")?.GetValue(legCommand);
            if (stepTarget == null)
            {
                return 0f;
            }

            object hasClearanceRequest = stepTarget.GetType().GetProperty("HasClearanceRequest")?.GetValue(stepTarget);
            if (hasClearanceRequest is not bool hasRequest || !hasRequest)
            {
                return 0f;
            }

            object requestedHeight = stepTarget.GetType().GetProperty("RequestedClearanceHeight")?.GetValue(stepTarget);
            return requestedHeight is float height ? height : 0f;
        }

        private static void ResolveDescendingLaneProfile(
            TerrainScenarioMarker marker,
            out Vector3 travelDirection,
            out Vector3 highSidePoint,
            out Vector3 lowSidePoint,
            out float highSideHeight)
        {
            // Sample multiple interior points along the lane to robustly determine
            // the descending direction, even if endpoint raycasts return ground-level
            // heights due to Physics broadphase timing after scene load.
            Bounds bounds = marker.ScenarioBounds;
            bool laneRunsAlongX = bounds.size.x >= bounds.size.z;
            Vector3 laneAxis = laneRunsAlongX ? Vector3.right : Vector3.forward;
            float laneLength = laneRunsAlongX ? bounds.size.x : bounds.size.z;
            float rayOriginHeight = bounds.max.y + 2f;

            float negativeQuarterHeight = 0f;
            float positiveQuarterHeight = 0f;
            int samplesPerSide = 5;

            for (int i = 0; i < samplesPerSide; i++)
            {
                float tNeg = 0.05f + 0.2f * i / (samplesPerSide - 1);
                float tPos = 0.55f + 0.2f * i / (samplesPerSide - 1);
                Vector3 negPt = bounds.center - laneAxis * (laneLength * (0.5f - tNeg));
                Vector3 posPt = bounds.center - laneAxis * (laneLength * (0.5f - tPos));
                negativeQuarterHeight = Mathf.Max(negativeQuarterHeight, SampleEnvironmentHeight(negPt, rayOriginHeight));
                positiveQuarterHeight = Mathf.Max(positiveQuarterHeight, SampleEnvironmentHeight(posPt, rayOriginHeight));
            }

            float sampleInset = Mathf.Min(StepUpSurfaceProbeInset, laneLength * 0.1f);
            Vector3 negativeEndPoint = bounds.center - laneAxis * (laneLength * 0.5f - sampleInset);
            Vector3 positiveEndPoint = bounds.center + laneAxis * (laneLength * 0.5f - sampleInset);

            if (negativeQuarterHeight > positiveQuarterHeight)
            {
                // Negative end is higher — descend from negative toward positive.
                travelDirection = laneAxis;
                highSidePoint = negativeEndPoint;
                lowSidePoint = positiveEndPoint;
                highSideHeight = negativeQuarterHeight;
            }
            else
            {
                // Positive end is higher — descend from positive toward negative.
                travelDirection = -laneAxis;
                highSidePoint = positiveEndPoint;
                lowSidePoint = negativeEndPoint;
                highSideHeight = positiveQuarterHeight;
            }
        }

        private static void ResolveAscendingLaneProfile(
            TerrainScenarioMarker marker,
            out Vector3 travelDirection,
            out Vector3 lowSidePoint,
            out Vector3 highSidePoint,
            out float highSideHeight)
        {
            Bounds bounds = marker.ScenarioBounds;
            bool laneRunsAlongX = bounds.size.x >= bounds.size.z;
            Vector3 laneAxis = laneRunsAlongX ? Vector3.right : Vector3.forward;
            float laneLength = laneRunsAlongX ? bounds.size.x : bounds.size.z;
            float sampleInset = Mathf.Min(StepUpSurfaceProbeInset, laneLength * 0.1f);
            Vector3 negativePoint = bounds.center - laneAxis * (laneLength * 0.5f - sampleInset);
            Vector3 positivePoint = bounds.center + laneAxis * (laneLength * 0.5f - sampleInset);

            float negativeHeight = SampleEnvironmentHeight(negativePoint, bounds.max.y + 2f);
            float positiveHeight = SampleEnvironmentHeight(positivePoint, bounds.max.y + 2f);

            if (positiveHeight >= negativeHeight)
            {
                travelDirection = laneAxis;
                lowSidePoint = negativePoint;
                highSidePoint = positivePoint;
                highSideHeight = positiveHeight;
                return;
            }

            travelDirection = -laneAxis;
            lowSidePoint = positivePoint;
            highSidePoint = negativePoint;
            highSideHeight = negativeHeight;
        }

        private static float FindRaisedPlateauEntryDistance(
            Vector3 lowSidePoint,
            Vector3 highSidePoint,
            Vector3 travelDirection,
            float highSideHeight)
        {
            float scanLength = Vector3.Distance(lowSidePoint, highSidePoint);
            float plateauHeightThreshold = highSideHeight - StepUpPlateauHeightTolerance;

            for (int sampleIndex = 0; sampleIndex <= StepUpSurfaceSampleCount; sampleIndex++)
            {
                float t = sampleIndex / (float)StepUpSurfaceSampleCount;
                float distanceAlongLane = scanLength * t;
                Vector3 samplePoint = lowSidePoint + travelDirection * distanceAlongLane;
                float sampleHeight = SampleEnvironmentHeight(samplePoint, highSidePoint.y + 2f);

                if (sampleHeight >= plateauHeightThreshold)
                {
                    return distanceAlongLane;
                }
            }

            Assert.Fail(
                $"Unable to resolve the raised landing entry for the authored StepUpLane. " +
                $"Expected to find a surface near y={highSideHeight:F2} within {scanLength:F2}m.");
            return 0f;
        }

        private static float SampleEnvironmentHeight(Vector3 samplePoint, float rayOriginHeight)
        {
            Ray ray = new Ray(
                new Vector3(samplePoint.x, rayOriginHeight, samplePoint.z),
                Vector3.down);

            bool hit = Physics.Raycast(
                ray,
                out RaycastHit hitInfo,
                rayOriginHeight + 5f,
                1 << GameSettings.LayerEnvironment,
                QueryTriggerInteraction.Ignore);

            Assert.That(hit, Is.True,
                $"Expected an environment surface below sample point {samplePoint} when resolving the StepUpLane profile.");

            return hitInfo.point.y;
        }

        private static void RepositionRagdoll(RagdollSetup ragdollSetup, Rigidbody hipsBody, Vector3 desiredHipsPosition)
        {
            Vector3 translation = desiredHipsPosition - hipsBody.position;

            if (ragdollSetup != null && ragdollSetup.AllBodies != null && ragdollSetup.AllBodies.Count > 0)
            {
                for (int i = 0; i < ragdollSetup.AllBodies.Count; i++)
                {
                    Rigidbody body = ragdollSetup.AllBodies[i];
                    if (body == null)
                    {
                        continue;
                    }

                    body.position += translation;
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }

                Physics.SyncTransforms();
                return;
            }

            Rigidbody[] bodies = hipsBody.GetComponentsInChildren<Rigidbody>(includeInactive: false);
            for (int i = 0; i < bodies.Length; i++)
            {
                Rigidbody body = bodies[i];
                if (body == null)
                {
                    continue;
                }

                body.position += translation;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            Physics.SyncTransforms();
        }

        private static Vector2 BuildMoveInputForWorldDirection(Vector3 desiredWorldDirection)
        {
            Vector3 planarDirection = Vector3.ProjectOnPlane(desiredWorldDirection, Vector3.up);
            Assert.That(planarDirection.sqrMagnitude, Is.GreaterThan(0.0001f),
                "Step-up traversal requires a valid planar direction.");

            planarDirection.Normalize();

            if (Camera.main == null)
            {
                return new Vector2(planarDirection.x, planarDirection.z).normalized;
            }

            float cameraYaw = Camera.main.transform.eulerAngles.y;
            Vector3 cameraForward = Quaternion.Euler(0f, cameraYaw, 0f) * Vector3.forward;
            Vector3 cameraRight = Quaternion.Euler(0f, cameraYaw, 0f) * Vector3.right;
            Vector2 moveInput = new Vector2(
                Vector3.Dot(planarDirection, cameraRight),
                Vector3.Dot(planarDirection, cameraForward));

            Assert.That(moveInput.sqrMagnitude, Is.GreaterThan(0.0001f),
                "Unable to derive a non-zero move input for the StepUpLane traversal.");

            return moveInput.normalized;
        }

        private static ConfigurableJoint FindJointByName(GameObject root, string name)
        {
            ConfigurableJoint[] joints = root.GetComponentsInChildren<ConfigurableJoint>(false);
            foreach (ConfigurableJoint j in joints)
            {
                if (j.gameObject.name == name) return j;
            }
            return null;
        }

        private static void LogBaseline(string scenario, string summary)
        {
            Debug.Log($"[C1.1 Baseline][GaitOutcome] {scenario} {summary}");
        }
    }
}
