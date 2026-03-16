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
        private const float TestPhase = Mathf.PI * 0.5f;

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

        private void ConfigureStrideBlendAngles()
        {
            SetPrivateField(_rig.LegAnimator, "_stepAngle", WalkStepAngle);
            SetPrivateField(_rig.LegAnimator, "_sprintStepAngle", SprintStepAngle);
            SetPrivateField(_rig.LegAnimator, "_upperLegLiftBoost", 0f);
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
    }
}