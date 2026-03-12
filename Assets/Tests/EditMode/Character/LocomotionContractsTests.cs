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