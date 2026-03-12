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

            Vector3 supportCenter = _rig.HipsBody.position - Vector3.forward * 0.45f + Vector3.up * 0.02f;
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

            TeleportFootBody(footLeftBody, footLeftBody.position + Vector3.right * 0.14f);

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

            Vector3 supportCenter = _rig.HipsBody.position - Vector3.forward * 0.55f + Vector3.up * 0.02f;
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

            Vector3 supportCenter = _rig.HipsBody.position - Vector3.forward * 0.55f + Vector3.up * 0.02f;
            TeleportFootBodies(footLeftBody, footRightBody, supportCenter);

            // Act
            _rig.PlayerMovement.SetMoveInputForTest(Vector2.right);
            yield return new WaitForFixedUpdate();

            object desiredInput = GetPropertyValue<object>(_director, "CurrentDesiredInput");
            object supportCommand = GetPropertyValue<object>(_director, "CurrentBodySupportCommand");

            Vector3 moveWorldDirection = GetPropertyValue<Vector3>(desiredInput, "MoveWorldDirection");
            Vector3 travelDirection = GetPropertyValue<Vector3>(supportCommand, "TravelDirection");
            float recoveryBlend = GetPropertyValue<float>(supportCommand, "RecoveryBlend");
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
        public IEnumerator FixedUpdate_WhenLegacyGaitIsAdvancing_EmitsCycleCommandsWithMirroredPhaseOffset()
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
            float leftPhase = GetPropertyValue<float>(leftLegCommand, "CyclePhase");
            float rightPhase = GetPropertyValue<float>(rightLegCommand, "CyclePhase");
            float leftBlendWeight = GetPropertyValue<float>(leftLegCommand, "BlendWeight");
            float rightBlendWeight = GetPropertyValue<float>(rightLegCommand, "BlendWeight");

            // Assert
            Assert.That(leftMode, Is.EqualTo("Cycle"),
                "Pass-through director should mirror legacy gait as Cycle commands while moving.");
            Assert.That(rightMode, Is.EqualTo("Cycle"),
                "Pass-through director should mirror legacy gait as Cycle commands while moving.");
            Assert.That(leftBlendWeight, Is.EqualTo(_rig.LegAnimator.SmoothedInputMag).Within(0.0001f),
                "Left leg command should mirror the legacy gait blend weight.");
            Assert.That(rightBlendWeight, Is.EqualTo(_rig.LegAnimator.SmoothedInputMag).Within(0.0001f),
                "Right leg command should mirror the legacy gait blend weight.");
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(leftPhase * Mathf.Rad2Deg, _rig.LegAnimator.Phase * Mathf.Rad2Deg)),
                Is.LessThan(0.01f),
                "Left leg command should mirror the legacy gait phase.");

            float expectedRightPhase = Mathf.Repeat(_rig.LegAnimator.Phase + Mathf.PI, Mathf.PI * 2f);
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(rightPhase * Mathf.Rad2Deg, expectedRightPhase * Mathf.Rad2Deg)),
                Is.LessThan(0.01f),
                "Right leg command should mirror the legacy gait phase with a half-cycle offset.");
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

        private static void AssertVector2Equal(Vector2 actual, Vector2 expected, string propertyName)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.0001f), $"{propertyName}.x mismatch.");
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.0001f), $"{propertyName}.y mismatch.");
        }

        private static void TeleportFootBodies(Rigidbody footLeftBody, Rigidbody footRightBody, Vector3 supportCenter)
        {
            Vector3 lateralOffset = Vector3.right * 0.18f;

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