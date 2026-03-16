using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using PhysicsDrivenMovement.Character;
using UnityEngine;
using UnityEngine.TestTools;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// PlayMode coverage for the pass-through LocomotionDirector seam introduced by
    /// Chapter 1 task C1.3 of the unified locomotion roadmap.
    /// </summary>
    public class LocomotionDirectorTests
    {
        private const int SettleFrames = 100;

        private static readonly Vector3 TestOrigin = new Vector3(2500f, 0f, 2500f);

        private PlayerPrefabTestRig _rig;
        private LocomotionDirector _director;

        [SetUp]
        public void SetUp()
        {
            _rig = PlayerPrefabTestRig.Create(new PlayerPrefabTestRig.Options
            {
                TestOrigin = TestOrigin,
                GroundName = "LocomotionDirectorTests_Ground",
            });

            _director = _rig.Instance.GetComponent<LocomotionDirector>();
        }

        [TearDown]
        public void TearDown()
        {
            _rig?.Dispose();
            _rig = null;
            _director = null;
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WithForwardInput_CapturesDesiredInputObservationAndPassThroughSupportCommand()
        {
            // Arrange
            Assert.That(_director, Is.Not.Null,
                "PlayerRagdoll prefab should include LocomotionDirector for roadmap task C1.3.");

            yield return _rig.WarmUp(SettleFrames);

            // Act
            _rig.PlayerMovement.SetMoveInputForTest(Vector2.up);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            object desiredInput = GetPropertyValue<object>(_director, "CurrentDesiredInput");
            object observation = GetPropertyValue<object>(_director, "CurrentObservation");
            object supportCommand = GetPropertyValue<object>(_director, "CurrentBodySupportCommand");

            Vector2 moveInput = GetPropertyValue<Vector2>(desiredInput, "MoveInput");
            bool hasMoveIntent = GetPropertyValue<bool>(desiredInput, "HasMoveIntent");
            Vector3 desiredFacing = GetPropertyValue<Vector3>(desiredInput, "FacingDirection");
            CharacterStateType characterState = GetPropertyValue<CharacterStateType>(observation, "CharacterState");
            bool isGrounded = GetPropertyValue<bool>(observation, "IsGrounded");
            Vector3 supportFacing = GetPropertyValue<Vector3>(supportCommand, "FacingDirection");

            // Assert
            Assert.That(_director.HasCommandFrame, Is.True,
                "LocomotionDirector should publish a command frame after FixedUpdate.");
            AssertVector2Equal(moveInput, Vector2.up, "DesiredInput.MoveInput");
            Assert.That(hasMoveIntent, Is.True, "DesiredInput should report forward move intent.");
            Assert.That(characterState, Is.EqualTo(_rig.CharacterState.CurrentState),
                "LocomotionDirector should snapshot the authoritative CharacterState value.");
            Assert.That(isGrounded, Is.EqualTo(_rig.BalanceController.IsGrounded),
                "LocomotionDirector should snapshot the grounded observation from BalanceController.");
            Assert.That(Vector3.Dot(desiredFacing, supportFacing), Is.GreaterThan(0.999f),
                "Pass-through body support should preserve the desired facing direction.");
            Assert.That(_director.IsPassThroughMode, Is.True,
                "LocomotionDirector should remain in pass-through mode for roadmap task C1.3.");
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WhenSprintIsActive_AddsForwardLeanToSupportCommand()
        {
            // Arrange
            Assert.That(_director, Is.Not.Null,
                "PlayerRagdoll prefab should include LocomotionDirector for sprint-lean command coverage.");

            yield return _rig.WarmUp(SettleFrames);

            _rig.CharacterState.SetStateForTest(CharacterStateType.Moving);
            _rig.PlayerMovement.SetMoveInputForTest(Vector2.up);

            for (int frame = 0; frame < 20; frame++)
            {
                yield return new WaitForFixedUpdate();
            }

            object walkDesiredInput = GetPropertyValue<object>(_director, "CurrentDesiredInput");
            object walkSupportCommand = GetPropertyValue<object>(_director, "CurrentBodySupportCommand");
            float walkSprintNormalized = GetPropertyValue<float>(walkDesiredInput, "SprintNormalized");
            float walkLeanDegrees = GetPropertyValue<float>(walkSupportCommand, "DesiredLeanDegrees");

            _rig.PlayerMovement.SetSprintInputForTest(true);

            // Act
            for (int frame = 0; frame < 30; frame++)
            {
                yield return new WaitForFixedUpdate();
            }

            object sprintDesiredInput = GetPropertyValue<object>(_director, "CurrentDesiredInput");
            object sprintSupportCommand = GetPropertyValue<object>(_director, "CurrentBodySupportCommand");
            float sprintNormalized = GetPropertyValue<float>(sprintDesiredInput, "SprintNormalized");
            float sprintLeanDegrees = GetPropertyValue<float>(sprintSupportCommand, "DesiredLeanDegrees");

            // Assert
            Assert.That(walkSprintNormalized, Is.EqualTo(0f).Within(0.05f),
                "Walking should keep SprintNormalized near zero before the sprint portion begins.");
            Assert.That(sprintNormalized, Is.EqualTo(1f).Within(0.05f),
                "SprintNormalized should reach full sprint after roughly one blend window.");
            Assert.That(sprintLeanDegrees, Is.GreaterThan(walkLeanDegrees + 1f),
                "Sprint should request more forward lean than walking once the sprint blend has settled.");
            Assert.That(sprintLeanDegrees, Is.GreaterThan(1.5f),
                "Sprint should add a visible amount of forward lean to the support command rather than staying near the walk posture.");
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WhenSprintBlendIsMidRamp_LeanCommandTracksSprintNormalizedOnEntryAndRelease()
        {
            // Arrange
            Assert.That(_director, Is.Not.Null,
                "PlayerRagdoll prefab should include LocomotionDirector for sprint-lean ramp coverage.");

            yield return _rig.WarmUp(SettleFrames);

            _rig.CharacterState.SetStateForTest(CharacterStateType.Moving);
            _rig.PlayerMovement.SetMoveInputForTest(Vector2.up);

            for (int frame = 0; frame < 20; frame++)
            {
                yield return new WaitForFixedUpdate();
            }

            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo sprintLeanField = typeof(LocomotionDirector).GetField("_maxSprintLeanDegrees", flags);
            Assert.That(sprintLeanField, Is.Not.Null,
                "LocomotionDirector should keep its serialized sprint-lean tuning field for the ramp coverage test.");

            float maxSprintLeanDegrees = (float)sprintLeanField.GetValue(_director);
            object walkSupportCommand = GetPropertyValue<object>(_director, "CurrentBodySupportCommand");
            float walkLeanDegrees = GetPropertyValue<float>(walkSupportCommand, "DesiredLeanDegrees");

            _rig.PlayerMovement.SetSprintInputForTest(true);

            for (int frame = 0; frame < 12; frame++)
            {
                yield return new WaitForFixedUpdate();
            }

            object entryDesiredInput = GetPropertyValue<object>(_director, "CurrentDesiredInput");
            object entrySupportCommand = GetPropertyValue<object>(_director, "CurrentBodySupportCommand");
            float entrySprintNormalized = GetPropertyValue<float>(entryDesiredInput, "SprintNormalized");
            float entryLeanDegrees = GetPropertyValue<float>(entrySupportCommand, "DesiredLeanDegrees");

            for (int frame = 0; frame < 20; frame++)
            {
                yield return new WaitForFixedUpdate();
            }

            object fullSprintDesiredInput = GetPropertyValue<object>(_director, "CurrentDesiredInput");
            object fullSprintSupportCommand = GetPropertyValue<object>(_director, "CurrentBodySupportCommand");
            float fullSprintNormalized = GetPropertyValue<float>(fullSprintDesiredInput, "SprintNormalized");
            float fullSprintLeanDegrees = GetPropertyValue<float>(fullSprintSupportCommand, "DesiredLeanDegrees");

            _rig.PlayerMovement.SetSprintInputForTest(false);

            for (int frame = 0; frame < 12; frame++)
            {
                yield return new WaitForFixedUpdate();
            }

            // Act
            object exitDesiredInput = GetPropertyValue<object>(_director, "CurrentDesiredInput");
            object exitSupportCommand = GetPropertyValue<object>(_director, "CurrentBodySupportCommand");
            float exitSprintNormalized = GetPropertyValue<float>(exitDesiredInput, "SprintNormalized");
            float exitLeanDegrees = GetPropertyValue<float>(exitSupportCommand, "DesiredLeanDegrees");

            // Assert
            Assert.That(entrySprintNormalized, Is.GreaterThan(0.2f).And.LessThan(0.8f),
                "SprintNormalized should be mid-ramp shortly after sprint engagement so the lean command can track the same blend window.");
            Assert.That(fullSprintNormalized, Is.EqualTo(1f).Within(0.05f),
                "SprintNormalized should reach full sprint before the release half of the ramp test begins.");
            Assert.That(exitSprintNormalized, Is.GreaterThan(0.2f).And.LessThan(0.8f),
                "SprintNormalized should be mid-ramp shortly after sprint release so the lean command can track the walk recovery window.");
            Assert.That(fullSprintLeanDegrees, Is.EqualTo(maxSprintLeanDegrees).Within(0.75f),
                "Once sprint is fully blended in, the support-command lean should reach the configured max sprint lean budget.");
            Assert.That(entryLeanDegrees, Is.GreaterThan(walkLeanDegrees + 0.25f),
                "During sprint ramp-in, the support-command lean should already rise above the walk posture instead of waiting for full sprint.");
            Assert.That(entryLeanDegrees, Is.LessThan(fullSprintLeanDegrees - 0.5f),
                "The ramp-in sample should still be materially below the fully blended sprint lean posture.");
            Assert.That(exitLeanDegrees, Is.GreaterThan(walkLeanDegrees + 0.25f),
                "During sprint release, the support-command lean should still be decaying through an in-between posture rather than popping straight back to the walk lean.");
            Assert.That(exitLeanDegrees, Is.LessThan(fullSprintLeanDegrees - 0.5f),
                "The ramp-out sample should have already shed a material portion of the full sprint lean budget.");
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WhenSupportCenterFallsBehindHips_MarksObservationAsComOutsideSupport()
        {
            // Arrange
            Assert.That(_director, Is.Not.Null,
                "PlayerRagdoll prefab should include LocomotionDirector for roadmap task C2.2.");

            yield return _rig.WarmUp(SettleFrames);

            Rigidbody footLeftBody = _rig.FootL.GetComponent<Rigidbody>();
            Rigidbody footRightBody = _rig.FootR.GetComponent<Rigidbody>();

            Assert.That(footLeftBody, Is.Not.Null, "Foot_L should expose a Rigidbody on the prefab-backed test rig.");
            Assert.That(footRightBody, Is.Not.Null, "Foot_R should expose a Rigidbody on the prefab-backed test rig.");

            _rig.BalanceController.SetGroundStateForTest(isGrounded: true, isFallen: false);
            _rig.PlayerMovement.SetMoveInputForTest(Vector2.up);
            yield return new WaitForFixedUpdate();

            float groundY = Mathf.Min(footLeftBody.position.y, footRightBody.position.y);
            Vector3 supportCenter = new Vector3(_rig.HipsBody.position.x, groundY, _rig.HipsBody.position.z) - Vector3.forward * 0.15f;
            TeleportFootBodies(footLeftBody, footRightBody, supportCenter);

            // Act
            yield return new WaitForFixedUpdate();

            object observation = GetPropertyValue<object>(_director, "CurrentObservation");
            bool isGrounded = GetPropertyValue<bool>(observation, "IsGrounded");
            bool isComOutsideSupport = GetPropertyValue<bool>(observation, "IsComOutsideSupport");

            // Assert
            Assert.That(isGrounded, Is.True,
                "The director should continue to mirror the authoritative grounded state while sampling support geometry.");
            Assert.That(isComOutsideSupport, Is.True,
                "Chapter 2.2 should classify the hips COM as outside support once both grounded feet trail behind the body.");
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WhenFootSlidesForOneFrame_KeepsPlantedClassificationStable()
        {
            // Arrange
            Assert.That(_director, Is.Not.Null,
                "PlayerRagdoll prefab should include LocomotionDirector for roadmap task C2.3.");

            yield return _rig.WarmUp(SettleFrames);

            Rigidbody footLeftBody = _rig.FootL.GetComponent<Rigidbody>();
            Assert.That(footLeftBody, Is.Not.Null, "Foot_L should expose a Rigidbody on the prefab-backed test rig.");

            yield return new WaitForFixedUpdate();

            object baselineObservation = GetPropertyValue<object>(_director, "CurrentObservation");
            object baselineLeftFoot = GetPropertyValue<object>(baselineObservation, "LeftFoot");
            Assert.That(GetPropertyValue<bool>(baselineLeftFoot, "IsPlanted"), Is.True,
                "Standing on the ground should seed the left foot as planted before the transient slide sample.");

            TeleportFootBody(footLeftBody, footLeftBody.position + Vector3.right * 0.05f);

            // Act
            yield return new WaitForFixedUpdate();

            object observation = GetPropertyValue<object>(_director, "CurrentObservation");
            object leftFoot = GetPropertyValue<object>(observation, "LeftFoot");
            bool isPlanted = GetPropertyValue<bool>(leftFoot, "IsPlanted");
            float plantedConfidence = GetPropertyValue<float>(leftFoot, "PlantedConfidence");

            // Assert
            Assert.That(isPlanted, Is.True,
                "A one-frame planted-confidence dip from foot slide should be absorbed by the observation hysteresis.");
            Assert.That(plantedConfidence, Is.GreaterThan(0.4f),
                "The filtered planted confidence should remain above zero after a one-frame slide spike.");
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WhenSupportFallsBehindHips_PublishesDriftDirectionAndTelemetrySnapshot()
        {
            // Arrange
            Assert.That(_director, Is.Not.Null,
                "PlayerRagdoll prefab should include LocomotionDirector for roadmap task C2.4.");

            yield return _rig.WarmUp(SettleFrames);

            Rigidbody footLeftBody = _rig.FootL.GetComponent<Rigidbody>();
            Rigidbody footRightBody = _rig.FootR.GetComponent<Rigidbody>();

            Assert.That(footLeftBody, Is.Not.Null, "Foot_L should expose a Rigidbody on the prefab-backed test rig.");
            Assert.That(footRightBody, Is.Not.Null, "Foot_R should expose a Rigidbody on the prefab-backed test rig.");

            _rig.BalanceController.SetGroundStateForTest(isGrounded: true, isFallen: false);
            _rig.PlayerMovement.SetMoveInputForTest(Vector2.up);
            yield return new WaitForFixedUpdate();

            float groundY = Mathf.Min(footLeftBody.position.y, footRightBody.position.y);
            Vector3 supportCenter = new Vector3(_rig.HipsBody.position.x, groundY, _rig.HipsBody.position.z) - Vector3.forward * 0.18f;
            TeleportFootBodies(footLeftBody, footRightBody, supportCenter);

            // Act
            yield return new WaitForFixedUpdate();

            Vector3 predictedDriftDirection = GetPropertyValue<Vector3>(_director, "CurrentPredictedDriftDirection");
            string telemetryLine = GetPropertyValue<string>(_director, "CurrentObservationTelemetryLine");
            object sensorSnapshot = GetPropertyValue<object>(_director, "CurrentSensorSnapshot");
            object supportGeometry = GetPropertyValue<object>(sensorSnapshot, "SupportGeometry");
            Vector3 runtimeSupportCenter = GetPropertyValue<Vector3>(supportGeometry, "SupportCenter");
            Vector3 expectedDriftDirection = Vector3.ProjectOnPlane(
                _rig.HipsBody.position - runtimeSupportCenter,
                Vector3.up).normalized;

            // Assert
            Assert.That(predictedDriftDirection.sqrMagnitude, Is.GreaterThan(0.1f),
                "The director should publish a non-zero predicted drift direction when support trails behind the body.");
            Assert.That(Vector3.Dot(predictedDriftDirection.normalized, expectedDriftDirection), Is.GreaterThan(0.8f),
                "The predicted drift direction should point away from the runtime support center toward the body drift risk.");
            Assert.That(telemetryLine, Does.Contain("supportQuality="),
                "The telemetry snapshot should include support quality for Chapter 2.4 confidence visibility.");
            Assert.That(telemetryLine, Does.Contain("contactConfidence="),
                "The telemetry snapshot should include contact confidence for Chapter 2.4 visibility.");
            Assert.That(telemetryLine, Does.Contain("plantedConfidence="),
                "The telemetry snapshot should include planted-foot confidence for Chapter 2.4 visibility.");
            Assert.That(telemetryLine, Does.Contain("slip="),
                "The telemetry snapshot should include slip estimate for Chapter 2.4 visibility.");
            Assert.That(telemetryLine, Does.Contain("turnSeverity="),
                "The telemetry snapshot should include turn severity for Chapter 2.4 visibility.");
            Assert.That(telemetryLine, Does.Contain("driftDir="),
                "The telemetry snapshot should include the predicted drift direction for Chapter 2.4 visibility.");
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WhenObservationShowsTurnRiskWithoutDirectionHistory_StartsRecoveryAndBoostsSupportCommand()
        {
            // Arrange
            Assert.That(_director, Is.Not.Null,
                "PlayerRagdoll prefab should include LocomotionDirector for roadmap task C2.5.");

            yield return _rig.WarmUp(SettleFrames);

            Rigidbody footLeftBody = _rig.FootL.GetComponent<Rigidbody>();
            Rigidbody footRightBody = _rig.FootR.GetComponent<Rigidbody>();

            Assert.That(footLeftBody, Is.Not.Null, "Foot_L should expose a Rigidbody on the prefab-backed test rig.");
            Assert.That(footRightBody, Is.Not.Null, "Foot_R should expose a Rigidbody on the prefab-backed test rig.");

            Vector3 supportCenter = _rig.HipsBody.position - Vector3.forward * 0.18f + Vector3.up * 0.02f;
            TeleportFootBodies(footLeftBody, footRightBody, supportCenter);

            // Minimise debounce so this test focuses on whether the director enters
            // recovery at all, not on hysteresis timing (hysteresis is covered by
            // dedicated C6.3 EditMode tests). Physics can shift feet within the
            // debounce window, so keep the gate at 1 frame.
            SetPrivateField(_director, "_recoveryEntryDebounceFrames", 1);

            // Act — drive perpendicular input while feet sit behind hips.
            _rig.PlayerMovement.SetMoveInputForTest(Vector2.right);
            float recoveryBlend = 0f;
            for (int i = 0; i < 10; i++)
            {
                TeleportFootBodies(footLeftBody, footRightBody, supportCenter);
                yield return new WaitForFixedUpdate();

                object cmd = GetPropertyValue<object>(_director, "CurrentBodySupportCommand");
                recoveryBlend = GetPropertyValue<float>(cmd, "RecoveryBlend");
                if (recoveryBlend > 0f)
                {
                    break;
                }
            }

            object desiredInput = GetPropertyValue<object>(_director, "CurrentDesiredInput");
            object supportCommand = GetPropertyValue<object>(_director, "CurrentBodySupportCommand");

            Vector3 moveWorldDirection = GetPropertyValue<Vector3>(desiredInput, "MoveWorldDirection");
            Vector3 travelDirection = GetPropertyValue<Vector3>(supportCommand, "TravelDirection");
            float yawStrengthScale = GetPropertyValue<float>(supportCommand, "YawStrengthScale");
            float uprightStrengthScale = GetPropertyValue<float>(supportCommand, "UprightStrengthScale");
            float stabilizationStrengthScale = GetPropertyValue<float>(supportCommand, "StabilizationStrengthScale");

            // Assert
            Assert.That(recoveryBlend, Is.GreaterThan(0f),
                "A weak-support sharp-turn observation should start recovery even without legacy previous-direction history.");
            Assert.That(yawStrengthScale, Is.LessThan(1f),
                "Risky support observations should suppress yaw strength so turning torque does not ignore the support model.");
            Assert.That(uprightStrengthScale, Is.GreaterThan(1f),
                "Risky support observations should boost upright strength instead of leaving the body command at neutral pass-through values.");
            Assert.That(stabilizationStrengthScale, Is.GreaterThan(1f),
                "Risky support observations should boost stabilization strength instead of leaving the body command at neutral pass-through values.");
            Assert.That(Vector3.Dot(moveWorldDirection.normalized, travelDirection.normalized), Is.GreaterThan(0.95f),
                "The support command should keep traveling in the requested move direction while observation-driven recovery is active.");
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WhenStandingSupportIsStable_KeepsLowRiskSupportCommandScales()
        {
            // Arrange
            Assert.That(_director, Is.Not.Null,
                "PlayerRagdoll prefab should include LocomotionDirector for roadmap task C2.5.");

            yield return _rig.WarmUp(SettleFrames);

            // Act
            _rig.PlayerMovement.SetMoveInputForTest(Vector2.zero);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            object supportCommand = GetPropertyValue<object>(_director, "CurrentBodySupportCommand");

            float recoveryBlend = GetPropertyValue<float>(supportCommand, "RecoveryBlend");
            float yawStrengthScale = GetPropertyValue<float>(supportCommand, "YawStrengthScale");
            float uprightStrengthScale = GetPropertyValue<float>(supportCommand, "UprightStrengthScale");
            float stabilizationStrengthScale = GetPropertyValue<float>(supportCommand, "StabilizationStrengthScale");

            // Assert
            Assert.That(recoveryBlend, Is.EqualTo(0f).Within(0.0001f),
                "Stable standing support should not enter the observation-driven recovery path.");
            Assert.That(yawStrengthScale, Is.EqualTo(1f).Within(0.01f),
                "Stable standing support should preserve the neutral yaw strength scale.");
            Assert.That(uprightStrengthScale, Is.InRange(1f, 1.15f),
                "Stable standing support should remain in a low-risk upright-strength band rather than escalating into strong recovery support.");
            Assert.That(stabilizationStrengthScale, Is.InRange(1f, 1.15f),
                "Stable standing support should remain in a low-risk stabilization band rather than escalating into strong recovery support.");
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WhenLegacyGaitIsAdvancing_EmitsCycleCommandsWithPerLegStateSeparation()
        {
            // Arrange
            Assert.That(_director, Is.Not.Null,
                "PlayerRagdoll prefab should include LocomotionDirector for roadmap task C1.3.");

            yield return _rig.WarmUp(SettleFrames);

            // Act
            _rig.PlayerMovement.SetMoveInputForTest(Vector2.up);
            for (int frame = 0; frame < 30; frame++)
            {
                yield return new WaitForFixedUpdate();
            }

            object leftLegCommand = GetPropertyValue<object>(_director, "LeftLegCommand");
            object rightLegCommand = GetPropertyValue<object>(_director, "RightLegCommand");

            string leftMode = GetPropertyValue<object>(leftLegCommand, "Mode").ToString();
            string rightMode = GetPropertyValue<object>(rightLegCommand, "Mode").ToString();
            string leftState = GetPropertyValue<object>(leftLegCommand, "State").ToString();
            string rightState = GetPropertyValue<object>(rightLegCommand, "State").ToString();
            string leftTransitionReason = GetPropertyValue<object>(leftLegCommand, "TransitionReason").ToString();
            string rightTransitionReason = GetPropertyValue<object>(rightLegCommand, "TransitionReason").ToString();
            float leftPhase = GetPropertyValue<float>(leftLegCommand, "CyclePhase");
            float rightPhase = GetPropertyValue<float>(rightLegCommand, "CyclePhase");
            float leftBlendWeight = GetPropertyValue<float>(leftLegCommand, "BlendWeight");
            float rightBlendWeight = GetPropertyValue<float>(rightLegCommand, "BlendWeight");

            // Assert
            Assert.That(leftMode, Is.EqualTo("Cycle"),
                "Pass-through director should mirror legacy gait as Cycle commands while moving.");
            Assert.That(rightMode, Is.EqualTo("Cycle"),
                "Pass-through director should mirror legacy gait as Cycle commands while moving.");
            Assert.That(leftTransitionReason, Is.Not.EqualTo("None"),
                "The left leg command should include an explicit transition reason once Chapter 3 state roles are introduced.");
            Assert.That(rightTransitionReason, Is.Not.EqualTo("None"),
                "The right leg command should include an explicit transition reason once Chapter 3 state roles are introduced.");
            Assert.That(leftBlendWeight, Is.EqualTo(_rig.LegAnimator.SmoothedInputMag).Within(0.0001f),
                "Left leg command should mirror the legacy gait blend weight.");
            Assert.That(rightBlendWeight, Is.EqualTo(_rig.LegAnimator.SmoothedInputMag).Within(0.0001f),
                "Right leg command should mirror the legacy gait blend weight.");
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(leftPhase * Mathf.Rad2Deg, _rig.LegAnimator.Phase * Mathf.Rad2Deg)),
                Is.LessThan(0.01f),
                "Left leg command should remain the exposed compatibility phase for legacy collaborators and tests.");

            float phaseSeparation = Mathf.Abs(
                Mathf.DeltaAngle(leftPhase * Mathf.Rad2Deg, rightPhase * Mathf.Rad2Deg)) * Mathf.Deg2Rad;
            Assert.That(phaseSeparation, Is.GreaterThan(0.1f),
                "The right leg should keep a distinct controller phase instead of collapsing onto the left leg phase while moving.");
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WhenSupportFallsBehindHips_PromotesCatchStepStateWithStumbleRecoveryReason()
        {
            // Arrange
            Assert.That(_director, Is.Not.Null,
                "PlayerRagdoll prefab should include LocomotionDirector for roadmap task C3.1.");

            yield return _rig.WarmUp(SettleFrames);

            Rigidbody footLeftBody = _rig.FootL.GetComponent<Rigidbody>();
            Rigidbody footRightBody = _rig.FootR.GetComponent<Rigidbody>();

            Assert.That(footLeftBody, Is.Not.Null, "Foot_L should expose a Rigidbody on the prefab-backed test rig.");
            Assert.That(footRightBody, Is.Not.Null, "Foot_R should expose a Rigidbody on the prefab-backed test rig.");

            _rig.PlayerMovement.SetMoveInputForTest(Vector2.up);
            for (int frame = 0; frame < 20; frame++)
            {
                yield return new WaitForFixedUpdate();
            }

            Vector3 supportCenter = _rig.HipsBody.position - Vector3.forward * 0.18f + Vector3.up * 0.02f;
            TeleportFootBodies(footLeftBody, footRightBody, supportCenter);

            // Act
            yield return new WaitForFixedUpdate();

            object leftLegCommand = GetPropertyValue<object>(_director, "LeftLegCommand");
            object rightLegCommand = GetPropertyValue<object>(_director, "RightLegCommand");

            string leftState = GetPropertyValue<object>(leftLegCommand, "State").ToString();
            string rightState = GetPropertyValue<object>(rightLegCommand, "State").ToString();
            string leftTransitionReason = GetPropertyValue<object>(leftLegCommand, "TransitionReason").ToString();
            string rightTransitionReason = GetPropertyValue<object>(rightLegCommand, "TransitionReason").ToString();
            bool leftIsRecoveryLeg = leftState == "CatchStep";
            bool rightIsRecoveryLeg = rightState == "CatchStep";

            // Assert
            Assert.That(new[] { leftState, rightState }, Does.Contain("CatchStep"),
                "Weak support ahead of the support patch should promote at least one leg into a catch-step role.");
            Assert.That(leftIsRecoveryLeg ^ rightIsRecoveryLeg, Is.True,
                "Chapter 3.4 recovery ownership should assign the catch-step role to one recovery leg instead of tagging both legs identically.");

            if (leftIsRecoveryLeg)
            {
                Assert.That(leftTransitionReason, Is.EqualTo("StumbleRecovery"),
                    "The selected left recovery leg should explain its catch-step role as a stumble-recovery transition.");
                Assert.That(rightTransitionReason, Is.Not.EqualTo("StumbleRecovery"),
                    "The opposite support leg should keep its own cadence reason instead of mirroring stumble recovery.");
            }
            else
            {
                Assert.That(rightTransitionReason, Is.EqualTo("StumbleRecovery"),
                    "The selected right recovery leg should explain its catch-step role as a stumble-recovery transition.");
                Assert.That(leftTransitionReason, Is.Not.EqualTo("StumbleRecovery"),
                    "The opposite support leg should keep its own cadence reason instead of mirroring stumble recovery.");
            }
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WhenSharpTurnRequestsRightwardTravel_AssignsOutsideTurnSupportAndInsideOverride()
        {
            // Arrange
            Assert.That(_director, Is.Not.Null,
                "PlayerRagdoll prefab should include LocomotionDirector for roadmap task C3.4.");

            yield return _rig.WarmUp(SettleFrames);

            _rig.PlayerMovement.SetMoveInputForTest(Vector2.up);
            for (int frame = 0; frame < 25; frame++)
            {
                yield return new WaitForFixedUpdate();
            }

            // Act
            _rig.PlayerMovement.SetMoveInputForTest(Vector2.right);
            for (int frame = 0; frame < 15; frame++)
            {
                yield return new WaitForFixedUpdate();
            }

            object leftLegCommand = GetPropertyValue<object>(_director, "LeftLegCommand");
            object rightLegCommand = GetPropertyValue<object>(_director, "RightLegCommand");
            string leftTransitionReason = GetPropertyValue<object>(leftLegCommand, "TransitionReason").ToString();
            string rightTransitionReason = GetPropertyValue<object>(rightLegCommand, "TransitionReason").ToString();
            float leftPhase = GetPropertyValue<float>(leftLegCommand, "CyclePhase");
            float rightPhase = GetPropertyValue<float>(rightLegCommand, "CyclePhase");
            float mirroredRightPhase = Mathf.Repeat(leftPhase + Mathf.PI, Mathf.PI * 2f);
            float mirrorDeviation = Mathf.Abs(
                Mathf.DeltaAngle(rightPhase * Mathf.Rad2Deg, mirroredRightPhase * Mathf.Rad2Deg)) * Mathf.Deg2Rad;

            // Assert
            Assert.That(leftTransitionReason, Is.EqualTo("TurnSupport"),
                "During a sharp right turn, the outside left leg should stay in a turn-support cadence role.");
            Assert.That(new[] { "SpeedUp", "StumbleRecovery" }, Does.Contain(rightTransitionReason),
                "During a sharp right turn, the inside right leg should stop mirroring outside-leg turn support and use either a faster cadence or a support-risk recovery override.");
            Assert.That(mirrorDeviation, Is.GreaterThan(0.05f),
                "Chapter 3.4 should let the inside and outside turn legs break a strict half-cycle mirror instead of sharing the same cadence timing.");
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WhenOneFootLosesContact_BreaksStrictHalfCyclePhaseMirror()
        {
            // Arrange
            Assert.That(_director, Is.Not.Null,
                "PlayerRagdoll prefab should include LocomotionDirector for roadmap task C3.2.");

            yield return _rig.WarmUp(SettleFrames);

            Rigidbody footRightBody = _rig.FootR.GetComponent<Rigidbody>();
            Assert.That(footRightBody, Is.Not.Null, "Foot_R should expose a Rigidbody on the prefab-backed test rig.");

            _rig.PlayerMovement.SetMoveInputForTest(Vector2.up);
            for (int frame = 0; frame < 20; frame++)
            {
                yield return new WaitForFixedUpdate();
            }

            object baselineLeftLegCommand = GetPropertyValue<object>(_director, "LeftLegCommand");
            object baselineRightLegCommand = GetPropertyValue<object>(_director, "RightLegCommand");
            float baselineLeftPhase = GetPropertyValue<float>(baselineLeftLegCommand, "CyclePhase");
            float baselineRightPhase = GetPropertyValue<float>(baselineRightLegCommand, "CyclePhase");
            float baselinePhaseDelta = Mathf.Abs(
                Mathf.DeltaAngle(baselineLeftPhase * Mathf.Rad2Deg, baselineRightPhase * Mathf.Rad2Deg)) * Mathf.Deg2Rad;

            RigidbodyConstraints originalConstraints = footRightBody.constraints;
            footRightBody.constraints = RigidbodyConstraints.FreezeAll;
            TeleportFootBody(
                footRightBody,
                _rig.HipsBody.position + Vector3.right * 0.03f + Vector3.up * 0.12f + Vector3.forward * 0.013f);

            for (int frame = 0; frame < 10; frame++)
            {
                yield return new WaitForFixedUpdate();
            }

            // Act
            object observation = GetPropertyValue<object>(_director, "CurrentObservation");
            object rightFoot = GetPropertyValue<object>(observation, "RightFoot");
            object leftLegCommand = GetPropertyValue<object>(_director, "LeftLegCommand");
            object rightLegCommand = GetPropertyValue<object>(_director, "RightLegCommand");
            float leftPhase = GetPropertyValue<float>(leftLegCommand, "CyclePhase");
            float rightPhase = GetPropertyValue<float>(rightLegCommand, "CyclePhase");
            float phaseDelta = Mathf.Abs(Mathf.DeltaAngle(leftPhase * Mathf.Rad2Deg, rightPhase * Mathf.Rad2Deg)) * Mathf.Deg2Rad;

            footRightBody.constraints = originalConstraints;

            // Assert
            Assert.That(GetPropertyValue<bool>(rightFoot, "IsGrounded"), Is.False,
                "The lifted right foot should leave ground contact so the per-leg controller sees asymmetric support.");
            float mirroredRightPhase = Mathf.Repeat(_rig.LegAnimator.Phase + Mathf.PI, Mathf.PI * 2f);
            float mirrorDeviation = Mathf.Abs(
                Mathf.DeltaAngle(rightPhase * Mathf.Rad2Deg, mirroredRightPhase * Mathf.Rad2Deg)) * Mathf.Deg2Rad;

            Assert.That(baselinePhaseDelta, Is.GreaterThan(0.1f),
                "Before asymmetric contact is introduced, the stable gait should at least keep both leg phases distinct.");
            Assert.That(mirrorDeviation, Is.GreaterThan(0.012f),
                "Once one foot loses contact, the right leg command should stop looking like a simple left-phase-plus-pi mirror.");
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WhenStateMachineConfidenceDrops_ConvergesTowardMirroredFallbackWithoutOneFrameSnap()
        {
            // Arrange
            Assert.That(_director, Is.Not.Null,
                "PlayerRagdoll prefab should include LocomotionDirector for roadmap task C3.5.");

            yield return _rig.WarmUp(SettleFrames);

            Rigidbody footRightBody = _rig.FootR.GetComponent<Rigidbody>();
            Assert.That(footRightBody, Is.Not.Null, "Foot_R should expose a Rigidbody on the prefab-backed test rig.");

            _rig.PlayerMovement.SetMoveInputForTest(Vector2.up);
            for (int frame = 0; frame < 20; frame++)
            {
                yield return new WaitForFixedUpdate();
            }

            RigidbodyConstraints originalConstraints = footRightBody.constraints;
            footRightBody.constraints = RigidbodyConstraints.FreezeAll;
            TeleportFootBody(
                footRightBody,
                _rig.HipsBody.position + Vector3.right * 0.03f + Vector3.up * 0.12f + Vector3.forward * 0.013f);

            for (int frame = 0; frame < 10; frame++)
            {
                yield return new WaitForFixedUpdate();
            }

            object asymmetricLeftLegCommand = GetPropertyValue<object>(_director, "LeftLegCommand");
            object asymmetricRightLegCommand = GetPropertyValue<object>(_director, "RightLegCommand");
            float asymmetricLeftPhase = GetPropertyValue<float>(asymmetricLeftLegCommand, "CyclePhase");
            float asymmetricRightPhase = GetPropertyValue<float>(asymmetricRightLegCommand, "CyclePhase");
            float asymmetricMirrorDeviation = GetMirrorDeviationRadians(asymmetricLeftPhase, asymmetricRightPhase);
            object desiredInput = GetPropertyValue<object>(_director, "CurrentDesiredInput");
            object observation = GetPropertyValue<object>(_director, "CurrentObservation");

            footRightBody.constraints = originalConstraints;

            Assert.That(asymmetricMirrorDeviation, Is.GreaterThan(0.008f),
                "The precondition for C3.5 requires a genuinely asymmetric gait phase before the fallback path is asked to recover it.");

            SetPrivateField(_rig.LegAnimator, "_minimumStateMachineConfidence", 0.95f);
            SetPrivateField(_rig.LegAnimator, "_fallbackGaitBlendRiseSpeed", 10f);

            // Act
            BuildAndApplyPassThroughCommandFrame(
                _rig.LegAnimator,
                desiredInput,
                observation,
                out object firstFallbackLeftLegCommand,
                out object firstFallbackRightLegCommand);
            float firstFallbackLeftPhase = GetPropertyValue<float>(firstFallbackLeftLegCommand, "CyclePhase");
            float firstFallbackRightPhase = GetPropertyValue<float>(firstFallbackRightLegCommand, "CyclePhase");
            float firstFallbackMirrorDeviation = GetMirrorDeviationRadians(firstFallbackLeftPhase, firstFallbackRightPhase);

            object settledFallbackLeftLegCommand = firstFallbackLeftLegCommand;
            object settledFallbackRightLegCommand = firstFallbackRightLegCommand;

            for (int frame = 0; frame < 8; frame++)
            {
                BuildAndApplyPassThroughCommandFrame(
                    _rig.LegAnimator,
                    desiredInput,
                    observation,
                    out settledFallbackLeftLegCommand,
                    out settledFallbackRightLegCommand);
            }
            float settledFallbackLeftPhase = GetPropertyValue<float>(settledFallbackLeftLegCommand, "CyclePhase");
            float settledFallbackRightPhase = GetPropertyValue<float>(settledFallbackRightLegCommand, "CyclePhase");
            float settledFallbackMirrorDeviation = GetMirrorDeviationRadians(settledFallbackLeftPhase, settledFallbackRightPhase);
            string settledLeftTransitionReason = GetPropertyValue<object>(settledFallbackLeftLegCommand, "TransitionReason").ToString();
            string settledRightTransitionReason = GetPropertyValue<object>(settledFallbackRightLegCommand, "TransitionReason").ToString();

            // Assert
            Assert.That(firstFallbackMirrorDeviation, Is.GreaterThan(0.012f),
                "Low-confidence fallback should not hard-snap the gait straight back into an exact mirrored phase in a single frame.");
            Assert.That(settledFallbackMirrorDeviation, Is.LessThan(0.03f),
                "After the fallback blend has time to settle, the unstable asymmetric gait should converge back toward a stable mirrored cadence.");
            Assert.That(settledFallbackMirrorDeviation, Is.LessThan(asymmetricMirrorDeviation - 0.02f),
                "C3.5 fallback should materially reduce asymmetric phase drift once confidence stays low for several frames.");
            Assert.That(settledLeftTransitionReason, Is.EqualTo("LowConfidenceFallback"),
                "The left leg command should log the graceful fallback reason once the low-confidence gait safety path takes over.");
            Assert.That(settledRightTransitionReason, Is.EqualTo("LowConfidenceFallback"),
                "The right leg command should log the graceful fallback reason once the low-confidence gait safety path takes over.");
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WhenCollapseWatchdogConfirmsButStateRemainsMoving_StillEmitsCycleCommands()
        {
            // Arrange
            Assert.That(_director, Is.Not.Null,
                "PlayerRagdoll prefab should include LocomotionDirector for roadmap task C1.5.");

            yield return _rig.WarmUp(SettleFrames);

            LocomotionCollapseDetector collapseDetector = _rig.Instance.GetComponent<LocomotionCollapseDetector>();
            Assert.That(collapseDetector, Is.Not.Null,
                "PlayerRagdoll prefab must provide LocomotionCollapseDetector for watchdog-boundary coverage.");

            _rig.BalanceController.SetGroundStateForTest(isGrounded: true, isFallen: false);
            _rig.CharacterState.SetStateForTest(CharacterStateType.Moving);
            _rig.CharacterState.enabled = false;
            collapseDetector.enabled = false;
            LocomotionCollapseDetectorTestSeams.SetCollapseConfirmed(collapseDetector, true);

            // Act
            _rig.PlayerMovement.SetMoveInputForTest(Vector2.up);
            for (int frame = 0; frame < 30; frame++)
            {
                yield return new WaitForFixedUpdate();
            }

            object leftLegCommand = GetPropertyValue<object>(_director, "LeftLegCommand");
            object rightLegCommand = GetPropertyValue<object>(_director, "RightLegCommand");

            string leftMode = GetPropertyValue<object>(leftLegCommand, "Mode").ToString();
            string rightMode = GetPropertyValue<object>(rightLegCommand, "Mode").ToString();

            // Assert
            Assert.That(leftMode, Is.EqualTo("Cycle"),
                "Raw collapse confirmation should not disable left-leg cycle commands while CharacterState still labels the character Moving.");
            Assert.That(rightMode, Is.EqualTo("Cycle"),
                "Raw collapse confirmation should not disable right-leg cycle commands while CharacterState still labels the character Moving.");
        }

        private static T GetPropertyValue<T>(object instance, string propertyName)
        {
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            PropertyInfo property = instance.GetType().GetProperty(propertyName, flags);

            Assert.That(property, Is.Not.Null,
                $"Expected type '{instance.GetType().FullName}' to expose property '{propertyName}'.");

            object value = property.GetValue(instance);
            Assert.That(value, Is.Not.Null, $"Property '{propertyName}' should not be null.");
            return (T)value;
        }

        private static void SetPrivateField(object instance, string fieldName, object value)
        {
            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo field = instance.GetType().GetField(fieldName, flags);

            Assert.That(field, Is.Not.Null,
                $"Expected type '{instance.GetType().FullName}' to expose private field '{fieldName}'.");

            field.SetValue(instance, value);
        }

        private static float GetMirrorDeviationRadians(float leftPhase, float rightPhase)
        {
            float mirroredRightPhase = Mathf.Repeat(leftPhase + Mathf.PI, Mathf.PI * 2f);
            return Mathf.Abs(Mathf.DeltaAngle(rightPhase * Mathf.Rad2Deg, mirroredRightPhase * Mathf.Rad2Deg)) * Mathf.Deg2Rad;
        }

        private static void BuildAndApplyPassThroughCommandFrame(
            LegAnimator legAnimator,
            object desiredInput,
            object observation,
            out object leftCommand,
            out object rightCommand)
        {
            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            MethodInfo buildPassThroughCommandsMethod = typeof(LegAnimator).GetMethod("BuildPassThroughCommands", flags);
            MethodInfo setCommandFrameMethod = typeof(LegAnimator).GetMethod("SetCommandFrame", flags);

            Assert.That(buildPassThroughCommandsMethod, Is.Not.Null,
                "Expected LegAnimator to expose internal BuildPassThroughCommands for Chapter 3 command construction.");
            Assert.That(setCommandFrameMethod, Is.Not.Null,
                "Expected LegAnimator to expose internal SetCommandFrame for Chapter 3 command application.");

            object[] buildArguments = { desiredInput, observation, null, null };
            buildPassThroughCommandsMethod.Invoke(legAnimator, buildArguments);
            leftCommand = buildArguments[2];
            rightCommand = buildArguments[3];

            setCommandFrameMethod.Invoke(legAnimator, new[] { desiredInput, observation, leftCommand, rightCommand });
        }

        private static void AssertVector2Equal(Vector2 actual, Vector2 expected, string propertyName)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.0001f), $"{propertyName}.x mismatch.");
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.0001f), $"{propertyName}.y mismatch.");
        }

        private static void TeleportFootBodies(Rigidbody footLeftBody, Rigidbody footRightBody, Vector3 supportCenter)
        {
            Vector3 lateralOffset = Vector3.right * 0.07f;

            footLeftBody.position = supportCenter - lateralOffset;
            footRightBody.position = supportCenter + lateralOffset;
            footLeftBody.linearVelocity = Vector3.zero;
            footRightBody.linearVelocity = Vector3.zero;
            footLeftBody.angularVelocity = Vector3.zero;
            footRightBody.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();
        }

        private static void TeleportFootBody(Rigidbody footBody, Vector3 position)
        {
            footBody.position = position;
            footBody.linearVelocity = Vector3.zero;
            footBody.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();
        }
    }
}