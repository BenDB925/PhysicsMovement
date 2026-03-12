using System;
using System.Reflection;
using NUnit.Framework;
using PhysicsDrivenMovement.Character;
using UnityEngine;

namespace PhysicsDrivenMovement.Tests.EditMode.Character
{
    /// <summary>
    /// EditMode tests that define the internal locomotion contract surface introduced by
    /// Chapter 1 task C1.2 of the unified locomotion roadmap.
    /// These tests use reflection so the contracts can remain internal to the Character
    /// assembly while still being validated from the test assemblies.
    /// </summary>
    [TestFixture]
    public class LocomotionContractsTests
    {
        private const string DesiredInputTypeName = "PhysicsDrivenMovement.Character.DesiredInput";
        private const string FootContactObservationTypeName = "PhysicsDrivenMovement.Character.FootContactObservation";
        private const string SupportObservationFilterTypeName = "PhysicsDrivenMovement.Character.SupportObservationFilter";
        private const string SupportGeometryTypeName = "PhysicsDrivenMovement.Character.SupportGeometry";
        private const string LocomotionSensorSnapshotTypeName = "PhysicsDrivenMovement.Character.LocomotionSensorSnapshot";
        private const string LocomotionSensorAggregatorTypeName = "PhysicsDrivenMovement.Character.LocomotionSensorAggregator";
        private const string SupportObservationTypeName = "PhysicsDrivenMovement.Character.SupportObservation";
        private const string LocomotionObservationTypeName = "PhysicsDrivenMovement.Character.LocomotionObservation";
        private const string BodySupportCommandTypeName = "PhysicsDrivenMovement.Character.BodySupportCommand";
        private const string LegCommandOutputTypeName = "PhysicsDrivenMovement.Character.LegCommandOutput";
        private const string LocomotionLegTypeName = "PhysicsDrivenMovement.Character.LocomotionLeg";
        private const string LegCommandModeTypeName = "PhysicsDrivenMovement.Character.LegCommandMode";

        private static Assembly CharacterAssembly => typeof(PlayerMovement).Assembly;

        [Test]
        public void ContractTypes_QueriedFromCharacterAssembly_AllRoadmapContractsExistAndRemainInternal()
        {
            // Arrange
            string[] typeNames =
            {
                DesiredInputTypeName,
                FootContactObservationTypeName,
                SupportObservationFilterTypeName,
                SupportGeometryTypeName,
                LocomotionSensorSnapshotTypeName,
                LocomotionSensorAggregatorTypeName,
                SupportObservationTypeName,
                LocomotionObservationTypeName,
                BodySupportCommandTypeName,
                LegCommandOutputTypeName,
                LocomotionLegTypeName,
                LegCommandModeTypeName,
            };

            // Act / Assert
            foreach (string typeName in typeNames)
            {
                Type contractType = CharacterAssembly.GetType(typeName);

                Assert.That(contractType, Is.Not.Null, $"Expected internal locomotion contract '{typeName}' to exist.");
                Assert.That(contractType.IsNotPublic, Is.True,
                    $"Locomotion contract '{typeName}' should remain internal to the Character assembly for C1.2.");
            }
        }

        [Test]
        public void DesiredInput_ConstructedWithRawDirections_NormalizesVectorsAndPreservesIntentFlags()
        {
            // Arrange
            Type desiredInputType = RequireType(DesiredInputTypeName);
            object desiredInput = CreateInstance(
                desiredInputType,
                new Vector2(0.8f, 0.6f),
                new Vector3(4f, 0f, 0f),
                new Vector3(0f, 0f, 7f),
                true);

            // Act
            Vector3 moveWorldDirection = GetPropertyValue<Vector3>(desiredInput, "MoveWorldDirection");
            Vector3 facingDirection = GetPropertyValue<Vector3>(desiredInput, "FacingDirection");
            float moveMagnitude = GetPropertyValue<float>(desiredInput, "MoveMagnitude");
            bool hasMoveIntent = GetPropertyValue<bool>(desiredInput, "HasMoveIntent");
            bool jumpRequested = GetPropertyValue<bool>(desiredInput, "JumpRequested");

            // Assert
            AssertVector3Equal(moveWorldDirection, Vector3.right, "MoveWorldDirection");
            AssertVector3Equal(facingDirection, Vector3.forward, "FacingDirection");
            Assert.That(moveMagnitude, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(hasMoveIntent, Is.True);
            Assert.That(jumpRequested, Is.True);
        }

        [Test]
        public void LocomotionObservation_ConstructedWithRuntimeState_ReportsPlanarSpeedAndNormalizedBasis()
        {
            // Arrange
            Type observationType = RequireType(LocomotionObservationTypeName);
            object observation = CreateInstance(
                observationType,
                CharacterStateType.Moving,
                true,
                false,
                true,
                false,
                12.5f,
                new Vector3(3f, 8f, 4f),
                new Vector3(0f, 2f, 0f),
                new Vector3(0f, 0f, 5f),
                new Vector3(0f, 3f, 0f));

            // Act
            float planarSpeed = GetPropertyValue<float>(observation, "PlanarSpeed");
            Vector3 bodyForward = GetPropertyValue<Vector3>(observation, "BodyForward");
            Vector3 bodyUp = GetPropertyValue<Vector3>(observation, "BodyUp");
            bool isLocomotionCollapsed = GetPropertyValue<bool>(observation, "IsLocomotionCollapsed");

            // Assert
            Assert.That(planarSpeed, Is.EqualTo(5f).Within(0.0001f));
            AssertVector3Equal(bodyForward, Vector3.forward, "BodyForward");
            AssertVector3Equal(bodyUp, Vector3.up, "BodyUp");
            Assert.That(isLocomotionCollapsed, Is.True);
        }

        [Test]
        public void FootContactObservation_ConstructedWithOutOfRangeSignals_ClampsConfidenceValuesIntoSchemaRange()
        {
            // Arrange
            Type footObservationType = RequireType(FootContactObservationTypeName);
            Type legType = RequireType(LocomotionLegTypeName);
            object leftLeg = Enum.Parse(legType, "Left");

            // Act
            object footObservation = CreateInstance(
                footObservationType,
                leftLeg,
                true,
                1.4f,
                -0.25f,
                2.2f);

            float contactConfidence = GetPropertyValue<float>(footObservation, "ContactConfidence");
            float plantedConfidence = GetPropertyValue<float>(footObservation, "PlantedConfidence");
            float slipEstimate = GetPropertyValue<float>(footObservation, "SlipEstimate");

            // Assert
            Assert.That(GetPropertyValue<object>(footObservation, "Leg"), Is.EqualTo(leftLeg));
            Assert.That(GetPropertyValue<bool>(footObservation, "IsGrounded"), Is.True);
            Assert.That(contactConfidence, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(plantedConfidence, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(slipEstimate, Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void SupportObservation_ConstructedWithFootStates_ExposesAggregatedSupportSignals()
        {
            // Arrange
            Type footObservationType = RequireType(FootContactObservationTypeName);
            Type supportObservationType = RequireType(SupportObservationTypeName);
            Type legType = RequireType(LocomotionLegTypeName);

            object leftFoot = CreateInstance(
                footObservationType,
                Enum.Parse(legType, "Left"),
                true,
                0.9f,
                0.75f,
                0.2f);
            object rightFoot = CreateInstance(
                footObservationType,
                Enum.Parse(legType, "Right"),
                false,
                0.15f,
                0.25f,
                0.6f);

            // Act
            object supportObservation = CreateInstance(
                supportObservationType,
                leftFoot,
                rightFoot,
                1.25f,
                0.8f,
                0.65f,
                -0.3f,
                true);

            float supportQuality = GetPropertyValue<float>(supportObservation, "SupportQuality");
            float contactConfidence = GetPropertyValue<float>(supportObservation, "ContactConfidence");
            float plantedFootConfidence = GetPropertyValue<float>(supportObservation, "PlantedFootConfidence");
            float slipEstimate = GetPropertyValue<float>(supportObservation, "SlipEstimate");
            bool isComOutsideSupport = GetPropertyValue<bool>(supportObservation, "IsComOutsideSupport");

            // Assert
            Assert.That(supportQuality, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(contactConfidence, Is.EqualTo(0.8f).Within(0.0001f));
            Assert.That(plantedFootConfidence, Is.EqualTo(0.65f).Within(0.0001f));
            Assert.That(slipEstimate, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(isComOutsideSupport, Is.True);
            Assert.That(GetPropertyValue<object>(supportObservation, "LeftFoot"), Is.EqualTo(leftFoot));
            Assert.That(GetPropertyValue<object>(supportObservation, "RightFoot"), Is.EqualTo(rightFoot));
        }

        [Test]
        public void SupportGeometry_ConstructedWithTrailingFeet_ComputesSupportCenterAndOutsideClassification()
        {
            // Arrange
            Type supportGeometryType = RequireType(SupportGeometryTypeName);
            object supportGeometry = CreateInstance(
                supportGeometryType,
                new Vector3(-0.18f, 0f, -0.42f),
                true,
                new Vector3(0.18f, 0f, -0.42f),
                true);

            MethodInfo supportBehindMethod = RequireInstanceMethod(
                supportGeometryType,
                "GetSupportBehindDistance",
                typeof(Vector3),
                typeof(Vector3));
            MethodInfo outsideSupportMethod = RequireInstanceMethod(
                supportGeometryType,
                "IsPointOutsideSupport",
                typeof(Vector3));

            // Act
            Vector3 supportCenter = GetPropertyValue<Vector3>(supportGeometry, "SupportCenter");
            float supportSpan = GetPropertyValue<float>(supportGeometry, "SupportSpan");
            int groundedFootCount = GetPropertyValue<int>(supportGeometry, "GroundedFootCount");
            float supportBehindDistance = (float)supportBehindMethod.Invoke(
                supportGeometry,
                new object[] { Vector3.zero, Vector3.forward });
            bool isPointOutsideSupport = (bool)outsideSupportMethod.Invoke(
                supportGeometry,
                new object[] { Vector3.zero });

            // Assert
            AssertVector3Equal(supportCenter, new Vector3(0f, 0f, -0.42f), "SupportCenter");
            Assert.That(supportSpan, Is.EqualTo(0.36f).Within(0.0001f));
            Assert.That(groundedFootCount, Is.EqualTo(2));
            Assert.That(supportBehindDistance, Is.EqualTo(0.42f).Within(0.0001f));
            Assert.That(isPointOutsideSupport, Is.True,
                "Support geometry should classify a point ahead of a trailing support segment as outside support.");
        }

        [Test]
        public void LocomotionObservation_ConstructedWithSupportModel_ExposesWorldModelSignalsAndClampedTurnSeverity()
        {
            // Arrange
            Type footObservationType = RequireType(FootContactObservationTypeName);
            Type supportObservationType = RequireType(SupportObservationTypeName);
            Type observationType = RequireType(LocomotionObservationTypeName);
            Type legType = RequireType(LocomotionLegTypeName);

            object leftFoot = CreateInstance(
                footObservationType,
                Enum.Parse(legType, "Left"),
                true,
                0.9f,
                0.8f,
                0.1f);
            object rightFoot = CreateInstance(
                footObservationType,
                Enum.Parse(legType, "Right"),
                true,
                0.85f,
                0.7f,
                0.3f);
            object supportObservation = CreateInstance(
                supportObservationType,
                leftFoot,
                rightFoot,
                0.9f,
                0.95f,
                0.8f,
                0.25f,
                true);

            // Act
            object observation = CreateInstance(
                observationType,
                CharacterStateType.Moving,
                true,
                false,
                true,
                false,
                12.5f,
                new Vector3(3f, 8f, 4f),
                new Vector3(0f, 2f, 0f),
                new Vector3(0f, 0f, 5f),
                new Vector3(0f, 3f, 0f),
                supportObservation,
                1.4f);

            float supportQuality = GetPropertyValue<float>(observation, "SupportQuality");
            float contactConfidence = GetPropertyValue<float>(observation, "ContactConfidence");
            float plantedFootConfidence = GetPropertyValue<float>(observation, "PlantedFootConfidence");
            float slipEstimate = GetPropertyValue<float>(observation, "SlipEstimate");
            float turnSeverity = GetPropertyValue<float>(observation, "TurnSeverity");
            bool isComOutsideSupport = GetPropertyValue<bool>(observation, "IsComOutsideSupport");

            // Assert
            Assert.That(GetPropertyValue<object>(observation, "Support"), Is.EqualTo(supportObservation));
            Assert.That(supportQuality, Is.EqualTo(0.9f).Within(0.0001f));
            Assert.That(contactConfidence, Is.EqualTo(0.95f).Within(0.0001f));
            Assert.That(plantedFootConfidence, Is.EqualTo(0.8f).Within(0.0001f));
            Assert.That(slipEstimate, Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(turnSeverity, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(isComOutsideSupport, Is.True);
        }

        [Test]
        public void SupportObservationFilter_WhenPlantedSignalDipsInsideHysteresis_KeepsFootPlanted()
        {
            // Arrange
            Type footObservationType = RequireType(FootContactObservationTypeName);
            Type supportObservationType = RequireType(SupportObservationTypeName);
            Type filterType = RequireType(SupportObservationFilterTypeName);
            Type legType = RequireType(LocomotionLegTypeName);
            MethodInfo filterMethod = RequireInstanceMethod(filterType, "Filter", supportObservationType, typeof(float));
            object filter = CreateInstance(filterType, 20f, 30f, 12f, 20f, 0.75f, 0.55f);

            object settledSupport = CreateSupportObservation(
                footObservationType,
                supportObservationType,
                legType,
                leftPlantedConfidence: 1f,
                rightPlantedConfidence: 1f);
            object jitterSupport = CreateSupportObservation(
                footObservationType,
                supportObservationType,
                legType,
                leftPlantedConfidence: 0.62f,
                rightPlantedConfidence: 1f);

            filterMethod.Invoke(filter, new object[] { settledSupport, 0.1f });

            // Act
            object filteredSupport = filterMethod.Invoke(filter, new object[] { jitterSupport, 0.02f });
            object leftFoot = GetPropertyValue<object>(filteredSupport, "LeftFoot");
            bool isPlanted = GetPropertyValue<bool>(leftFoot, "IsPlanted");
            float plantedConfidence = GetPropertyValue<float>(leftFoot, "PlantedConfidence");

            // Assert
            Assert.That(isPlanted, Is.True,
                "A planted foot should stay planted when the confidence dip remains inside the hysteresis band.");
            Assert.That(plantedConfidence, Is.GreaterThan(0.55f),
                "The filtered planted confidence should not collapse to the unplanted state from a single in-band dip.");
        }

        [Test]
        public void SupportObservationFilter_WhenUnplantedSignalRisesInsideHysteresis_DoesNotReplantUntilEnterThreshold()
        {
            // Arrange
            Type footObservationType = RequireType(FootContactObservationTypeName);
            Type supportObservationType = RequireType(SupportObservationTypeName);
            Type filterType = RequireType(SupportObservationFilterTypeName);
            Type legType = RequireType(LocomotionLegTypeName);
            MethodInfo filterMethod = RequireInstanceMethod(filterType, "Filter", supportObservationType, typeof(float));
            object filter = CreateInstance(filterType, 20f, 30f, 12f, 20f, 0.75f, 0.55f);

            object unplantedSupport = CreateSupportObservation(
                footObservationType,
                supportObservationType,
                legType,
                leftPlantedConfidence: 0.2f,
                rightPlantedConfidence: 1f);
            object borderlineSupport = CreateSupportObservation(
                footObservationType,
                supportObservationType,
                legType,
                leftPlantedConfidence: 0.7f,
                rightPlantedConfidence: 1f);

            filterMethod.Invoke(filter, new object[] { unplantedSupport, 0.1f });

            // Act
            object filteredSupport = filterMethod.Invoke(filter, new object[] { borderlineSupport, 0.02f });
            object leftFoot = GetPropertyValue<object>(filteredSupport, "LeftFoot");
            bool isPlanted = GetPropertyValue<bool>(leftFoot, "IsPlanted");
            float plantedConfidence = GetPropertyValue<float>(leftFoot, "PlantedConfidence");

            // Assert
            Assert.That(isPlanted, Is.False,
                "An unplanted foot should not re-enter the planted state until the enter threshold is crossed.");
            Assert.That(plantedConfidence, Is.LessThan(0.75f),
                "The filtered planted confidence should stay below the replant threshold while the raw signal remains in-band.");
        }

        [Test]
        public void BodySupportCommand_PassThroughRequested_UsesWorldUpUnitScalesAndFallbackFacing()
        {
            // Arrange
            Type commandType = RequireType(BodySupportCommandTypeName);
            MethodInfo passThroughMethod = RequireStaticMethod(commandType, "PassThrough", typeof(Vector3));

            // Act
            object command = passThroughMethod.Invoke(null, new object[] { Vector3.zero });
            Vector3 facingDirection = GetPropertyValue<Vector3>(command, "FacingDirection");
            Vector3 uprightDirection = GetPropertyValue<Vector3>(command, "UprightDirection");
            float uprightStrengthScale = GetPropertyValue<float>(command, "UprightStrengthScale");
            float yawStrengthScale = GetPropertyValue<float>(command, "YawStrengthScale");
            float stabilizationStrengthScale = GetPropertyValue<float>(command, "StabilizationStrengthScale");
            float desiredLeanDegrees = GetPropertyValue<float>(command, "DesiredLeanDegrees");

            // Assert
            AssertVector3Equal(facingDirection, Vector3.forward, "FacingDirection");
            AssertVector3Equal(uprightDirection, Vector3.up, "UprightDirection");
            Assert.That(uprightStrengthScale, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(yawStrengthScale, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(stabilizationStrengthScale, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(desiredLeanDegrees, Is.EqualTo(0f).Within(0.0001f));
        }

        [Test]
        public void LegCommandOutput_DisabledRequested_UsesDisabledModeAndZeroedExecutionPayload()
        {
            // Arrange
            Type legType = RequireType(LocomotionLegTypeName);
            Type commandModeType = RequireType(LegCommandModeTypeName);
            Type commandOutputType = RequireType(LegCommandOutputTypeName);
            MethodInfo disabledMethod = RequireStaticMethod(commandOutputType, "Disabled", legType);
            object leftLeg = Enum.Parse(legType, "Left");

            // Act
            object command = disabledMethod.Invoke(null, new[] { leftLeg });
            object leg = GetPropertyValue<object>(command, "Leg");
            object mode = GetPropertyValue<object>(command, "Mode");
            float cyclePhase = GetPropertyValue<float>(command, "CyclePhase");
            float blendWeight = GetPropertyValue<float>(command, "BlendWeight");
            Vector3 footTarget = GetPropertyValue<Vector3>(command, "FootTarget");

            // Assert
            Assert.That(leg, Is.EqualTo(leftLeg));
            Assert.That(mode, Is.EqualTo(Enum.Parse(commandModeType, "Disabled")));
            Assert.That(cyclePhase, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(blendWeight, Is.EqualTo(0f).Within(0.0001f));
            AssertVector3Equal(footTarget, Vector3.zero, "FootTarget");
        }

        private static Type RequireType(string typeName)
        {
            Type contractType = CharacterAssembly.GetType(typeName);
            Assert.That(contractType, Is.Not.Null, $"Expected type '{typeName}' to exist in the Character assembly.");
            return contractType;
        }

        private static object CreateInstance(Type type, params object[] args)
        {
            Type[] argumentTypes = Array.ConvertAll(args, argument => argument.GetType());
            ConstructorInfo constructor = type.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                argumentTypes,
                modifiers: null);

            Assert.That(constructor, Is.Not.Null,
                $"Expected type '{type.FullName}' to expose a constructor matching ({DescribeTypes(argumentTypes)}).");

            return constructor.Invoke(args);
        }

        private static void AssertVector3Equal(Vector3 actual, Vector3 expected, string propertyName)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.0001f), $"{propertyName}.x mismatch.");
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.0001f), $"{propertyName}.y mismatch.");
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(0.0001f), $"{propertyName}.z mismatch.");
        }

        private static string DescribeTypes(Type[] types)
        {
            return string.Join(", ", Array.ConvertAll(types, type => type.Name));
        }

        private static object CreateSupportObservation(
            Type footObservationType,
            Type supportObservationType,
            Type legType,
            float leftPlantedConfidence,
            float rightPlantedConfidence,
            bool leftGrounded = true,
            bool rightGrounded = true)
        {
            float leftClampedConfidence = leftGrounded ? Mathf.Clamp01(leftPlantedConfidence) : 0f;
            float rightClampedConfidence = rightGrounded ? Mathf.Clamp01(rightPlantedConfidence) : 0f;
            float leftContactConfidence = leftGrounded ? 1f : 0f;
            float rightContactConfidence = rightGrounded ? 1f : 0f;

            object leftFoot = CreateInstance(
                footObservationType,
                Enum.Parse(legType, "Left"),
                leftGrounded,
                leftContactConfidence,
                leftClampedConfidence,
                leftGrounded ? 1f - leftClampedConfidence : 0f);
            object rightFoot = CreateInstance(
                footObservationType,
                Enum.Parse(legType, "Right"),
                rightGrounded,
                rightContactConfidence,
                rightClampedConfidence,
                rightGrounded ? 1f - rightClampedConfidence : 0f);

            float supportQuality = leftGrounded && rightGrounded
                ? 1f
                : leftGrounded || rightGrounded
                    ? 0.5f
                    : 0f;
            float contactConfidence = 0.5f * (leftContactConfidence + rightContactConfidence);
            float plantedFootConfidence = Mathf.Max(leftClampedConfidence, rightClampedConfidence);
            float slipEstimate = Mathf.Max(
                leftGrounded ? 1f - leftClampedConfidence : 0f,
                rightGrounded ? 1f - rightClampedConfidence : 0f);

            return CreateInstance(
                supportObservationType,
                leftFoot,
                rightFoot,
                supportQuality,
                contactConfidence,
                plantedFootConfidence,
                slipEstimate,
                false);
        }

        private static MethodInfo RequireStaticMethod(Type type, string methodName, params Type[] argumentTypes)
        {
            MethodInfo method = type.GetMethod(
                methodName,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: argumentTypes,
                modifiers: null);

            Assert.That(method, Is.Not.Null,
                $"Expected type '{type.FullName}' to expose static method '{methodName}'.");

            return method;
        }

        private static MethodInfo RequireInstanceMethod(Type type, string methodName, params Type[] argumentTypes)
        {
            MethodInfo method = type.GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: argumentTypes,
                modifiers: null);

            Assert.That(method, Is.Not.Null,
                $"Expected type '{type.FullName}' to expose instance method '{methodName}'.");

            return method;
        }

        private static T GetPropertyValue<T>(object instance, string propertyName)
        {
            PropertyInfo property = instance.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            Assert.That(property, Is.Not.Null,
                $"Expected type '{instance.GetType().FullName}' to expose property '{propertyName}'.");

            object value = property.GetValue(instance);
            Assert.That(value, Is.Not.Null, $"Property '{propertyName}' should not be null.");
            return (T)value;
        }
    }
}