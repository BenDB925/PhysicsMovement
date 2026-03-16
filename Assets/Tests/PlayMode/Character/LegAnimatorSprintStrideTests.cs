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
    /// Focused PlayMode coverage for sprint-scaled stride blending in <see cref="LegAnimator"/>.
    /// These tests use the prefab-backed character rig plus the runtime pass-through command
    /// builder so the assertions follow the same desired-input and observation path as gameplay.
    /// </summary>
    public class LegAnimatorSprintStrideTests
    {
        private const float WalkStepAngle = 24f;
        private const float SprintStepAngle = 48f;
        private const float WalkUpperLegLiftBoost = 10f;
        private const float SprintUpperLegLiftBoost = 30f;
        private const float WalkKneeAngle = 60f;
        private const float SprintKneeAngle = 70f;
        private const float WalkCadenceCyclesPerSecond = 1.25f;
        private const float CadenceSpeedScale = 0.1f;
        private const float SprintCadenceBoost = 1.2f;
        private const float SprintPlanarSpeed = 9f;
        private const float TestPhase = Mathf.PI * 0.5f;
        private const float RecoveryProfilePhase = Mathf.PI * 0.75f;

        private PlayerPrefabTestRig _rig;
        private LocomotionDirector _director;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _rig = PlayerPrefabTestRig.Create(new PlayerPrefabTestRig.Options
            {
                TestOrigin = new Vector3(2000f, 0f, 2000f),
                GroundName = "LegAnimatorSprintStrideTests_Ground",
                GameSettingsName = "LegAnimatorSprintStrideTests_Settings"
            });

            _director = _rig.Instance.GetComponent<LocomotionDirector>();

            Assert.That(_director, Is.Not.Null,
                "PlayerRagdoll prefab must include LocomotionDirector for sprint stride command coverage.");

            yield return _rig.WarmUp(5);

            _rig.CharacterState.SetStateForTest(CharacterStateType.Moving);
            _rig.BalanceController.SetGroundStateForTest(isGrounded: true, isFallen: false);
            _rig.HipsBody.constraints = RigidbodyConstraints.FreezeAll;
            _rig.PlayerMovement.SetMoveInputForTest(Vector2.up);

            SetPrivateField(_rig.LegAnimator, "_useWorldSpaceSwing", false);
            SetPrivateField(_rig.LegAnimator, "_useStateDrivenExecution", true);
            SetPrivateField(_rig.LegAnimator, "_spinSuppressFrames", 5);
            SetPrivateField(_rig.LegAnimator, "_stepFrequency", 0f);
            SetPrivateField(_rig.LegAnimator, "_stepFrequencyScale", 0f);
        }

        [TearDown]
        public void TearDown()
        {
            if (_rig != null)
            {
                _rig.Dispose();
                _rig = null;
            }
        }

        [UnityTest]
        public IEnumerator SprintStepAngle_FieldExists_AndDefaultsTo75Degrees()
        {
            // Arrange
            yield return null;

            // Act
            FieldInfo field = typeof(LegAnimator).GetField(
                "_sprintStepAngle",
                BindingFlags.Instance | BindingFlags.NonPublic);

            // Assert
            Assert.That(field, Is.Not.Null,
                "LegAnimator must serialize a private '_sprintStepAngle' field so sprint stride amplitude remains tunable.");

            float defaultValue = (float)field.GetValue(_rig.LegAnimator);
            Assert.That(defaultValue, Is.EqualTo(75f).Within(0.001f),
                $"_sprintStepAngle should default to 75° for the first sprint gait pass. Got {defaultValue:F3}°.");
        }

        [UnityTest]
        public IEnumerator SprintUpperLegLiftBoost_FieldExists_AndDefaultsTo42Degrees()
        {
            // Arrange
            yield return null;

            // Act
            FieldInfo field = typeof(LegAnimator).GetField(
                "_sprintUpperLegLiftBoost",
                BindingFlags.Instance | BindingFlags.NonPublic);

            // Assert
            Assert.That(field, Is.Not.Null,
                "LegAnimator must serialize a private '_sprintUpperLegLiftBoost' field so sprint knee lift remains tunable.");

            float defaultValue = (float)field.GetValue(_rig.LegAnimator);
            Assert.That(defaultValue, Is.EqualTo(42f).Within(0.001f),
                $"_sprintUpperLegLiftBoost should default to 42° for the sprint knee-lift pass. Got {defaultValue:F3}°.");
        }

        [UnityTest]
        public IEnumerator SprintCadenceBoost_FieldExists_AndDefaultsTo1Point2()
        {
            // Arrange
            yield return null;

            // Act
            FieldInfo field = typeof(LegAnimator).GetField(
                "_sprintCadenceBoost",
                BindingFlags.Instance | BindingFlags.NonPublic);

            // Assert
            Assert.That(field, Is.Not.Null,
                "LegAnimator must serialize a private '_sprintCadenceBoost' field so sprint cadence remains tunable without retuning walk gait.");

            float defaultValue = (float)field.GetValue(_rig.LegAnimator);
            Assert.That(defaultValue, Is.EqualTo(1.2f).Within(0.001f),
                $"_sprintCadenceBoost should default to 1.2x for the sprint cadence pass. Got {defaultValue:F3}x.");
        }

        [UnityTest]
        public IEnumerator SprintKneeAngle_FieldExists_AndDefaultsTo70Degrees()
        {
            // Arrange
            yield return null;

            // Act
            FieldInfo field = typeof(LegAnimator).GetField(
                "_sprintKneeAngle",
                BindingFlags.Instance | BindingFlags.NonPublic);

            // Assert
            Assert.That(field, Is.Not.Null,
                "LegAnimator must serialize a private '_sprintKneeAngle' field so sprint rear-kick tuning remains adjustable.");

            float defaultValue = (float)field.GetValue(_rig.LegAnimator);
            Assert.That(defaultValue, Is.EqualTo(70f).Within(0.001f),
                $"_sprintKneeAngle should default to 70° for the sprint knee-angle polish pass. Got {defaultValue:F3}°.");
        }

        [UnityTest]
        public IEnumerator BuildPassThroughCommands_WhenSprintNormalizedIsZero_UsesWalkStepAngle()
        {
            // Arrange
            ConfigureStrideBlendAngles();
            yield return CaptureObservationSnapshot();

            object observation = GetPropertyValue<object>(_director, "CurrentObservation");
            object desiredInput = CreateDesiredInput(observation, 0f);
            float sprintNormalized = GetPropertyValue<float>(desiredInput, "SprintNormalized");

            SeedDeterministicStrideSample();

            // Act
            BuildPassThroughCommands(_rig.LegAnimator, desiredInput, observation, out object leftCommand, out _);
            float leftSwingDegrees = GetPropertyValue<float>(leftCommand, "SwingAngleDegrees");

            // Assert
            Assert.That(sprintNormalized, Is.EqualTo(0f).Within(0.0001f),
                "DesiredInput should report zero SprintNormalized for the walk regression sample.");
            Assert.That(leftSwingDegrees, Is.EqualTo(WalkStepAngle).Within(0.5f),
                $"Walk gait should keep the authored walk step angle. Expected {WalkStepAngle:F1}°, got {leftSwingDegrees:F2}°.");
        }

        [UnityTest]
        public IEnumerator BuildPassThroughCommands_WhenSprintNormalizedIsHalf_InterpolatesWalkAndSprintStepAngle()
        {
            // Arrange
            ConfigureStrideBlendAngles();
            yield return CaptureObservationSnapshot();

            object observation = GetPropertyValue<object>(_director, "CurrentObservation");
            object desiredInput = CreateDesiredInput(observation, 0.5f);
            float sprintNormalized = GetPropertyValue<float>(desiredInput, "SprintNormalized");
            float expectedSwingDegrees = Mathf.Lerp(WalkStepAngle, SprintStepAngle, 0.5f);

            SeedDeterministicStrideSample();

            // Act
            BuildPassThroughCommands(_rig.LegAnimator, desiredInput, observation, out object leftCommand, out _);
            float leftSwingDegrees = GetPropertyValue<float>(leftCommand, "SwingAngleDegrees");

            // Assert
            Assert.That(sprintNormalized, Is.EqualTo(0.5f).Within(0.0001f),
                "DesiredInput should carry the same sprint blend that LegAnimator uses for stride interpolation.");
            Assert.That(leftSwingDegrees, Is.EqualTo(expectedSwingDegrees).Within(0.5f),
                $"Mid-blend sprint gait should interpolate the step angle between walk and sprint. Expected {expectedSwingDegrees:F1}°, got {leftSwingDegrees:F2}°.");
        }

        [UnityTest]
        public IEnumerator BuildPassThroughCommands_WhenSprintNormalizedIsOne_UsesSprintStepAngle()
        {
            // Arrange
            ConfigureStrideBlendAngles();
            yield return CaptureObservationSnapshot();

            object observation = GetPropertyValue<object>(_director, "CurrentObservation");
            object desiredInput = CreateDesiredInput(observation, 1f);
            float sprintNormalized = GetPropertyValue<float>(desiredInput, "SprintNormalized");

            SeedDeterministicStrideSample();

            // Act
            BuildPassThroughCommands(_rig.LegAnimator, desiredInput, observation, out object leftCommand, out _);
            float leftSwingDegrees = GetPropertyValue<float>(leftCommand, "SwingAngleDegrees");

            // Assert
            Assert.That(sprintNormalized, Is.EqualTo(1f).Within(0.0001f),
                "DesiredInput should report full SprintNormalized for the sprint stride sample.");
            Assert.That(leftSwingDegrees, Is.EqualTo(SprintStepAngle).Within(0.5f),
                $"Full sprint should use the authored sprint step angle. Expected {SprintStepAngle:F1}°, got {leftSwingDegrees:F2}°.");
        }

        [UnityTest]
        public IEnumerator BuildPassThroughCommands_WhenSprintNormalizedIsZero_UsesWalkUpperLegLiftBoost()
        {
            // Arrange
            ConfigureLiftBlendAngles();
            yield return CaptureObservationSnapshot();

            object leftCommand = BuildLeftCommandForSprintBlend(0f);
            string leftStateName = GetPropertyValue<object>(leftCommand, "State").ToString();
            float cyclePhase = GetPropertyValue<float>(leftCommand, "CyclePhase");
            float leftSwingDegrees = GetPropertyValue<float>(leftCommand, "SwingAngleDegrees");
            float expectedSwingDegrees = ComputeExpectedSwingFromPhase(cyclePhase, 1f, leftStateName, WalkStepAngle, WalkUpperLegLiftBoost);

            // Assert
            Assert.That(leftSwingDegrees, Is.EqualTo(expectedSwingDegrees).Within(0.5f),
                $"Walk sprint blend sample should keep the authored walk lift boost. Expected {expectedSwingDegrees:F2}°, got {leftSwingDegrees:F2}°.");
        }

        [UnityTest]
        public IEnumerator BuildPassThroughCommands_WhenSprintNormalizedIsHalf_InterpolatesWalkAndSprintUpperLegLiftBoost()
        {
            // Arrange
            ConfigureLiftBlendAngles();
            yield return CaptureObservationSnapshot();

            object leftCommand = BuildLeftCommandForSprintBlend(0.5f);
            string leftStateName = GetPropertyValue<object>(leftCommand, "State").ToString();
            float cyclePhase = GetPropertyValue<float>(leftCommand, "CyclePhase");
            float leftSwingDegrees = GetPropertyValue<float>(leftCommand, "SwingAngleDegrees");
            float expectedLiftBoost = Mathf.Lerp(WalkUpperLegLiftBoost, SprintUpperLegLiftBoost, 0.5f);
            float expectedSwingDegrees = ComputeExpectedSwingFromPhase(cyclePhase, 1f, leftStateName, WalkStepAngle, expectedLiftBoost);

            // Assert
            Assert.That(leftSwingDegrees, Is.EqualTo(expectedSwingDegrees).Within(0.5f),
                $"Mid-blend sprint gait should interpolate the upper-leg lift boost between walk and sprint. Expected {expectedSwingDegrees:F2}°, got {leftSwingDegrees:F2}°.");
        }

        [UnityTest]
        public IEnumerator BuildPassThroughCommands_WhenSprintNormalizedIsOne_UsesSprintUpperLegLiftBoost()
        {
            // Arrange
            ConfigureLiftBlendAngles();
            yield return CaptureObservationSnapshot();

            object leftCommand = BuildLeftCommandForSprintBlend(1f);
            string leftStateName = GetPropertyValue<object>(leftCommand, "State").ToString();
            float cyclePhase = GetPropertyValue<float>(leftCommand, "CyclePhase");
            float leftSwingDegrees = GetPropertyValue<float>(leftCommand, "SwingAngleDegrees");
            float expectedSwingDegrees = ComputeExpectedSwingFromPhase(cyclePhase, 1f, leftStateName, WalkStepAngle, SprintUpperLegLiftBoost);

            // Assert
            Assert.That(leftSwingDegrees, Is.EqualTo(expectedSwingDegrees).Within(0.5f),
                $"Full sprint should use the authored sprint lift boost. Expected {expectedSwingDegrees:F2}°, got {leftSwingDegrees:F2}°.");
        }

        [UnityTest]
        public IEnumerator BuildPassThroughCommands_WhenSprintNormalizedIsZero_UsesWalkKneeAngle()
        {
            // Arrange
            ConfigureKneeBlendAngles();
            yield return CaptureObservationSnapshot();

            // Act
            object leftCommand = BuildLeftCommandForKneeBlend(0f);
            float leftKneeDegrees = GetPropertyValue<float>(leftCommand, "KneeAngleDegrees");

            // Assert
            Assert.That(leftKneeDegrees, Is.EqualTo(WalkKneeAngle).Within(0.5f),
                $"Walk gait should keep the authored walk knee angle. Expected {WalkKneeAngle:F1}°, got {leftKneeDegrees:F2}°.");
        }

        [UnityTest]
        public IEnumerator BuildPassThroughCommands_WhenSprintNormalizedIsHalf_InterpolatesWalkAndSprintKneeAngle()
        {
            // Arrange
            ConfigureKneeBlendAngles();
            yield return CaptureObservationSnapshot();

            // Act
            object leftCommand = BuildLeftCommandForKneeBlend(0.5f);
            float leftKneeDegrees = GetPropertyValue<float>(leftCommand, "KneeAngleDegrees");
            float expectedKneeDegrees = Mathf.Lerp(WalkKneeAngle, SprintKneeAngle, 0.5f);

            // Assert
            Assert.That(leftKneeDegrees, Is.EqualTo(expectedKneeDegrees).Within(0.5f),
                $"Mid-blend sprint gait should interpolate the knee angle between walk and sprint. Expected {expectedKneeDegrees:F1}°, got {leftKneeDegrees:F2}°.");
        }

        [UnityTest]
        public IEnumerator BuildPassThroughCommands_WhenSprintNormalizedIsOne_UsesSprintKneeAngle()
        {
            // Arrange
            ConfigureKneeBlendAngles();
            yield return CaptureObservationSnapshot();

            // Act
            object leftCommand = BuildLeftCommandForKneeBlend(1f);
            float leftKneeDegrees = GetPropertyValue<float>(leftCommand, "KneeAngleDegrees");

            // Assert
            Assert.That(leftKneeDegrees, Is.EqualTo(SprintKneeAngle).Within(0.5f),
                $"Full sprint should use the authored sprint knee angle. Expected {SprintKneeAngle:F1}°, got {leftKneeDegrees:F2}°.");
        }

        [UnityTest]
        public IEnumerator SetCommandFrame_WhenSprintNormalizedIsOne_UsesSprintKneeAngleForRecoveryProfile()
        {
            // Arrange
            ConfigureKneeBlendAngles();
            yield return CaptureObservationSnapshot();

            object observation = GetPropertyValue<object>(_director, "CurrentObservation");
            object walkDesiredInput = CreateDesiredInput(observation, 0f);
            object sprintDesiredInput = CreateDesiredInput(observation, 1f);
            object leftRecoveryCommand = CreateLegCommand(
                legName: "Left",
                modeName: "Cycle",
                stateName: "RecoveryStep",
                transitionReasonName: "StumbleRecovery",
                cyclePhase: RecoveryProfilePhase,
                swingAngleDegrees: 0f,
                kneeAngleDegrees: 0f,
                blendWeight: 1f);
            object rightSupportCommand = CreateLegCommand(
                legName: "Right",
                modeName: "Cycle",
                stateName: "Stance",
                transitionReasonName: "None",
                cyclePhase: Mathf.PI,
                swingAngleDegrees: 0f,
                kneeAngleDegrees: 0f,
                blendWeight: 1f);
            ConfigurableJoint lowerLegLJoint = _rig.LowerLegL.GetComponent<ConfigurableJoint>();
            float recoveryProgress = RecoveryProfilePhase / Mathf.PI;
            float expectedKneeTargetIncrease = Mathf.Lerp(
                SprintKneeAngle * 0.12f,
                SprintKneeAngle * 0.7f,
                recoveryProgress) - Mathf.Lerp(
                WalkKneeAngle * 0.12f,
                WalkKneeAngle * 0.7f,
                recoveryProgress);

            Assert.That(lowerLegLJoint, Is.Not.Null,
                "PlayerRagdoll prefab must expose LowerLeg_L ConfigurableJoint for sprint knee profile coverage.");

            // Act
            InvokeNonPublicMethod(_rig.LegAnimator, "ClearCommandFrame");
            SetCommandFrame(_rig.LegAnimator, walkDesiredInput, observation, leftRecoveryCommand, rightSupportCommand);
            float walkKneeTargetAngle = Quaternion.Angle(Quaternion.identity, lowerLegLJoint.targetRotation);

            InvokeNonPublicMethod(_rig.LegAnimator, "ClearCommandFrame");
            SetCommandFrame(_rig.LegAnimator, sprintDesiredInput, observation, leftRecoveryCommand, rightSupportCommand);
            float sprintKneeTargetAngle = Quaternion.Angle(Quaternion.identity, lowerLegLJoint.targetRotation);
            float actualKneeTargetIncrease = sprintKneeTargetAngle - walkKneeTargetAngle;

            // Assert
            Assert.That(walkKneeTargetAngle, Is.GreaterThan(0f),
                $"Walk recovery profile should still produce a visible lower-leg target. Got {walkKneeTargetAngle:F2}°.");
            Assert.That(sprintKneeTargetAngle, Is.GreaterThan(walkKneeTargetAngle + 2f),
                $"Sprint recovery profile should bend the knee more than walk. Walk={walkKneeTargetAngle:F2}°, sprint={sprintKneeTargetAngle:F2}°.");
            Assert.That(actualKneeTargetIncrease, Is.EqualTo(expectedKneeTargetIncrease).Within(1.5f),
                $"Sprint recovery profile should increase the resolved knee target by the authored sprint blend amount. Expected increase {expectedKneeTargetIncrease:F2}°, got {actualKneeTargetIncrease:F2}°.");
        }

        [UnityTest]
        public IEnumerator BuildPassThroughCommands_WhenSprintNormalizedIsZero_KeepsBaseCadenceAtSprintSpeed()
        {
            // Arrange
            ConfigureCadenceBlend();
            yield return null;

            object leftCommand = BuildLeftCommandForCadenceBlend(0f, SprintPlanarSpeed);
            object stepTarget = GetPropertyValue<object>(leftCommand, "StepTarget");
            float leftCyclePhase = GetPropertyValue<float>(leftCommand, "CyclePhase");
            float desiredTiming = GetPropertyValue<float>(stepTarget, "DesiredTiming");
            float expectedCyclesPerSecond = Mathf.Max(WalkCadenceCyclesPerSecond, SprintPlanarSpeed * CadenceSpeedScale);
            float expectedPhaseAdvance = expectedCyclesPerSecond * 2f * Mathf.PI * Time.fixedDeltaTime;
            float expectedDesiredTiming = Mathf.Max(0.02f, (Mathf.PI - expectedPhaseAdvance) / (expectedCyclesPerSecond * 2f * Mathf.PI));

            // Assert
            Assert.That(leftCyclePhase, Is.EqualTo(expectedPhaseAdvance).Within(0.01f),
                $"Walk cadence should stay at the authored base rate when SprintNormalized is zero. Expected phase advance {expectedPhaseAdvance:F4} rad, got {leftCyclePhase:F4} rad.");
            Assert.That(desiredTiming, Is.EqualTo(expectedDesiredTiming).Within(0.01f),
                $"Step target timing should stay aligned with the base cadence when SprintNormalized is zero. Expected {expectedDesiredTiming:F4}s, got {desiredTiming:F4}s.");
        }

        [UnityTest]
        public IEnumerator BuildPassThroughCommands_WhenSprintNormalizedIsOne_AppliesSprintCadenceBoostToPhaseAndStepTiming()
        {
            // Arrange
            ConfigureCadenceBlend();
            yield return null;

            object walkLeftCommand = BuildLeftCommandForCadenceBlend(0f, SprintPlanarSpeed);
            object sprintLeftCommand = BuildLeftCommandForCadenceBlend(1f, SprintPlanarSpeed);
            object sprintStepTarget = GetPropertyValue<object>(sprintLeftCommand, "StepTarget");
            float walkPhaseAdvance = GetPropertyValue<float>(walkLeftCommand, "CyclePhase");
            float sprintPhaseAdvance = GetPropertyValue<float>(sprintLeftCommand, "CyclePhase");
            float sprintDesiredTiming = GetPropertyValue<float>(sprintStepTarget, "DesiredTiming");
            float baseCyclesPerSecond = Mathf.Max(WalkCadenceCyclesPerSecond, SprintPlanarSpeed * CadenceSpeedScale);
            float expectedSprintCyclesPerSecond = baseCyclesPerSecond * SprintCadenceBoost;
            float expectedSprintPhaseAdvance = expectedSprintCyclesPerSecond * 2f * Mathf.PI * Time.fixedDeltaTime;
            float expectedSprintDesiredTiming = Mathf.Max(0.02f, (Mathf.PI - expectedSprintPhaseAdvance) / (expectedSprintCyclesPerSecond * 2f * Mathf.PI));

            // Assert
            Assert.That(sprintPhaseAdvance, Is.GreaterThan(walkPhaseAdvance + 0.01f),
                $"Sprint cadence should advance the gait phase faster than walk at the same planar speed. Walk={walkPhaseAdvance:F4} rad, sprint={sprintPhaseAdvance:F4} rad.");
            Assert.That(sprintPhaseAdvance, Is.EqualTo(expectedSprintPhaseAdvance).Within(0.01f),
                $"Full sprint should apply the authored sprint cadence boost. Expected phase advance {expectedSprintPhaseAdvance:F4} rad, got {sprintPhaseAdvance:F4} rad.");
            Assert.That(sprintDesiredTiming, Is.EqualTo(expectedSprintDesiredTiming).Within(0.01f),
                $"Step target timing should shorten with the sprint cadence boost so planner timing stays aligned. Expected {expectedSprintDesiredTiming:F4}s, got {sprintDesiredTiming:F4}s.");
        }

        private void ConfigureStrideBlendAngles()
        {
            SetPrivateField(_rig.LegAnimator, "_stepAngle", WalkStepAngle);
            SetPrivateField(_rig.LegAnimator, "_sprintStepAngle", SprintStepAngle);
            SetPrivateField(_rig.LegAnimator, "_upperLegLiftBoost", 0f);
            SetPrivateField(_rig.LegAnimator, "_sprintUpperLegLiftBoost", 0f);
        }

        private void ConfigureLiftBlendAngles()
        {
            SetPrivateField(_rig.LegAnimator, "_stepAngle", WalkStepAngle);
            SetPrivateField(_rig.LegAnimator, "_sprintStepAngle", WalkStepAngle);
            SetPrivateField(_rig.LegAnimator, "_upperLegLiftBoost", WalkUpperLegLiftBoost);
            SetPrivateField(_rig.LegAnimator, "_sprintUpperLegLiftBoost", SprintUpperLegLiftBoost);
        }

        private void ConfigureKneeBlendAngles()
        {
            SetPrivateField(_rig.LegAnimator, "_kneeAngle", WalkKneeAngle);
            SetPrivateField(_rig.LegAnimator, "_sprintKneeAngle", SprintKneeAngle);
        }

        private void ConfigureCadenceBlend()
        {
            SetPrivateField(_rig.LegAnimator, "_stepFrequency", WalkCadenceCyclesPerSecond);
            SetPrivateField(_rig.LegAnimator, "_stepFrequencyScale", CadenceSpeedScale);
            SetPrivateField(_rig.LegAnimator, "_sprintCadenceBoost", SprintCadenceBoost);
        }

        private IEnumerator CaptureObservationSnapshot()
        {
            _rig.HipsBody.linearVelocity = Vector3.zero;
            _rig.HipsBody.angularVelocity = Vector3.zero;

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
        }

        private static object CreateDesiredInput(object observation, float sprintNormalized)
        {
            Assembly characterAssembly = typeof(LegAnimator).Assembly;
            Type desiredInputType = characterAssembly.GetType("PhysicsDrivenMovement.Character.DesiredInput");

            Assert.That(desiredInputType, Is.Not.Null,
                "DesiredInput must exist in the Character assembly for sprint stride command coverage.");

            Vector3 bodyForward = GetPropertyValue<Vector3>(observation, "BodyForward");
            Vector3 planarForward = Vector3.ProjectOnPlane(bodyForward, Vector3.up);
            if (planarForward.sqrMagnitude <= 0.0001f)
            {
                planarForward = Vector3.forward;
            }

            return Activator.CreateInstance(
                desiredInputType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                args: new object[]
                {
                    Vector2.up,
                    planarForward.normalized,
                    planarForward.normalized,
                    false,
                    sprintNormalized
                },
                culture: null);
        }

        private void SeedDeterministicStrideSample()
        {
            InvokeNonPublicMethod(_rig.LegAnimator, "ClearCommandFrame");
            SetPrivateField(_rig.LegAnimator, "_phase", TestPhase);
            SetPrivateField(_rig.LegAnimator, "_smoothedInputMag", 1f);
            _rig.HipsBody.linearVelocity = Vector3.zero;
            _rig.HipsBody.angularVelocity = Vector3.zero;
        }

        private void SeedDeterministicCadenceSample()
        {
            InvokeNonPublicMethod(_rig.LegAnimator, "ClearCommandFrame");
            SetPrivateField(_rig.LegAnimator, "_phase", 0f);
            SetPrivateField(_rig.LegAnimator, "_smoothedInputMag", 0f);
            _rig.HipsBody.linearVelocity = Vector3.zero;
            _rig.HipsBody.angularVelocity = Vector3.zero;
        }

        private object BuildLeftCommandForSprintBlend(float sprintNormalized)
        {
            object observation = GetPropertyValue<object>(_director, "CurrentObservation");
            object desiredInput = CreateDesiredInput(observation, sprintNormalized);

            SeedDeterministicStrideSample();
            BuildPassThroughCommands(_rig.LegAnimator, desiredInput, observation, out object leftCommand, out _);
            return leftCommand;
        }

        private object BuildLeftCommandForCadenceBlend(float sprintNormalized, float planarSpeed)
        {
            object observation = CreateObservation(planarSpeed);
            object desiredInput = CreateDesiredInput(observation, sprintNormalized);

            SeedDeterministicCadenceSample();
            BuildPassThroughCommands(_rig.LegAnimator, desiredInput, observation, out object leftCommand, out _);
            return leftCommand;
        }

        private object BuildLeftCommandForKneeBlend(float sprintNormalized)
        {
            object observation = GetPropertyValue<object>(_director, "CurrentObservation");
            object desiredInput = CreateDesiredInput(observation, sprintNormalized);

            SeedDeterministicStrideSample();
            BuildPassThroughCommands(_rig.LegAnimator, desiredInput, observation, out object leftCommand, out _);
            return leftCommand;
        }

        private static void BuildPassThroughCommands(
            LegAnimator legAnimator,
            object desiredInput,
            object observation,
            out object leftCommand,
            out object rightCommand)
        {
            MethodInfo buildPassThroughCommandsMethod = typeof(LegAnimator).GetMethod(
                "BuildPassThroughCommands",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(buildPassThroughCommandsMethod, Is.Not.Null,
                "Expected LegAnimator to expose internal BuildPassThroughCommands for sprint stride coverage.");

            object[] buildArguments = { desiredInput, observation, null, null };
            buildPassThroughCommandsMethod.Invoke(legAnimator, buildArguments);
            leftCommand = buildArguments[2];
            rightCommand = buildArguments[3];
        }

        private static void SetCommandFrame(
            LegAnimator legAnimator,
            object desiredInput,
            object observation,
            object leftCommand,
            object rightCommand)
        {
            MethodInfo setCommandFrameMethod = typeof(LegAnimator).GetMethod(
                "SetCommandFrame",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(setCommandFrameMethod, Is.Not.Null,
                "Expected LegAnimator to expose internal SetCommandFrame for sprint knee profile coverage.");

            setCommandFrameMethod.Invoke(legAnimator, new[] { desiredInput, observation, leftCommand, rightCommand });
        }

        private static object CreateLegCommand(
            string legName,
            string modeName,
            string stateName,
            string transitionReasonName,
            float cyclePhase,
            float swingAngleDegrees,
            float kneeAngleDegrees,
            float blendWeight)
        {
            Assembly characterAssembly = typeof(LegAnimator).Assembly;
            Type legType = RequireLocomotionType(characterAssembly, "LocomotionLeg");
            Type modeType = RequireLocomotionType(characterAssembly, "LegCommandMode");
            Type stateType = RequireLocomotionType(characterAssembly, "LegStateType");
            Type transitionReasonType = RequireLocomotionType(characterAssembly, "LegStateTransitionReason");
            Type stateFrameType = RequireLocomotionType(characterAssembly, "LegStateFrame");
            Type commandType = RequireLocomotionType(characterAssembly, "LegCommandOutput");
            Type stepTargetType = RequireLocomotionType(characterAssembly, "StepTarget");
            Type recoverySituationType = RequireLocomotionType(characterAssembly, "RecoverySituation");

            object leg = Enum.Parse(legType, legName);
            object mode = Enum.Parse(modeType, modeName);
            object state = Enum.Parse(stateType, stateName);
            object transitionReason = Enum.Parse(transitionReasonType, transitionReasonName);

            ConstructorInfo stateFrameConstructor = stateFrameType.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: new[] { legType, stateType, transitionReasonType },
                modifiers: null);

            Assert.That(stateFrameConstructor, Is.Not.Null,
                "LegStateFrame must expose the constructor used by sprint knee profile command tests.");

            object stateFrame = stateFrameConstructor.Invoke(new[] { leg, state, transitionReason });
            PropertyInfo invalidProp = stepTargetType.GetProperty(
                "Invalid",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            object stepTarget = invalidProp?.GetValue(null) ?? Activator.CreateInstance(stepTargetType);

            ConstructorInfo commandConstructor = commandType.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: new[]
                {
                    legType,
                    modeType,
                    stateFrameType,
                    typeof(float),
                    typeof(float),
                    typeof(float),
                    typeof(float),
                    stepTargetType,
                    recoverySituationType,
                    typeof(float),
                },
                modifiers: null);

            Assert.That(commandConstructor, Is.Not.Null,
                "LegCommandOutput must expose the constructor used by sprint knee profile command tests.");

            object recoverySituationNone = Enum.Parse(recoverySituationType, "None");
            return commandConstructor.Invoke(new object[]
            {
                leg,
                mode,
                stateFrame,
                cyclePhase,
                swingAngleDegrees,
                kneeAngleDegrees,
                blendWeight,
                stepTarget,
                recoverySituationNone,
                0f,
            });
        }

        private static object CreateObservation(float planarSpeed)
        {
            Assembly characterAssembly = typeof(LegAnimator).Assembly;
            Type observationType = characterAssembly.GetType("PhysicsDrivenMovement.Character.LocomotionObservation");

            Assert.That(observationType, Is.Not.Null,
                "LocomotionObservation must exist in the Character assembly for sprint cadence coverage.");

            return Activator.CreateInstance(
                observationType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                args: new object[]
                {
                    CharacterStateType.Moving,
                    true,
                    false,
                    false,
                    false,
                    0f,
                    new Vector3(0f, 0f, planarSpeed),
                    Vector3.zero,
                    Vector3.forward,
                    Vector3.up
                },
                culture: null);
        }

        private static T GetPropertyValue<T>(object instance, string propertyName)
        {
            PropertyInfo property = instance.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            Assert.That(property, Is.Not.Null,
                $"Expected type '{instance.GetType().FullName}' to expose property '{propertyName}'.");

            object value = property.GetValue(instance);
            Assert.That(value, Is.Not.Null,
                $"Expected property '{propertyName}' on '{instance.GetType().FullName}' to have a value.");

            return (T)value;
        }

        private static Type RequireLocomotionType(Assembly assembly, string typeName)
        {
            Type resolvedType = assembly.GetType($"PhysicsDrivenMovement.Character.{typeName}");

            Assert.That(resolvedType, Is.Not.Null,
                $"Expected Character assembly to expose type 'PhysicsDrivenMovement.Character.{typeName}'.");

            return resolvedType;
        }

        private static void SetPrivateField(object instance, string fieldName, object value)
        {
            FieldInfo field = instance.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null,
                $"Expected type '{instance.GetType().FullName}' to expose private field '{fieldName}'.");

            field.SetValue(instance, value);
        }

        private static void InvokeNonPublicMethod(object instance, string methodName)
        {
            MethodInfo method = instance.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null,
                $"Expected type '{instance.GetType().FullName}' to expose non-public method '{methodName}'.");

            method.Invoke(instance, null);
        }

        private static float ComputeExpectedSwingFromPhase(
            float cyclePhase,
            float amplitudeScale,
            string stateName,
            float stepAngle,
            float upperLegLiftBoost)
        {
            float swingSin = Mathf.Sin(cyclePhase);
            float liftBoost = swingSin > 0f ? swingSin * upperLegLiftBoost * amplitudeScale : 0f;
            float swingAngle = swingSin * stepAngle * amplitudeScale + liftBoost;

            if ((stateName == "Swing" || stateName == "CatchStep") && amplitudeScale > 0f)
            {
                swingAngle += upperLegLiftBoost * 0.6f * amplitudeScale;

                float minimumForwardArc = stepAngle * 0.55f * amplitudeScale;
                swingAngle = Mathf.Max(swingAngle, minimumForwardArc);
            }

            return swingAngle;
        }
    }
}