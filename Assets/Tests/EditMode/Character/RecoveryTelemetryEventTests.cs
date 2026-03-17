using System;
using System.Reflection;
using NUnit.Framework;
using PhysicsDrivenMovement.Character;

namespace PhysicsDrivenMovement.Tests.EditMode.Character
{
    /// <summary>
    /// Focused EditMode coverage for the Chapter 9 recovery telemetry payload.
    /// </summary>
    [TestFixture]
    public class RecoveryTelemetryEventTests
    {
        private const string RecoveryTelemetryEventTypeName = "PhysicsDrivenMovement.Character.RecoveryTelemetryEvent";
        private const string RecoverySituationTypeName = "PhysicsDrivenMovement.Character.RecoverySituation";

        private static Assembly CharacterAssembly => typeof(PlayerMovement).Assembly;

        [Test]
        public void ToNdjsonLine_ContainsAllFields()
        {
            // Arrange
            Type telemetryEventType = CharacterAssembly.GetType(RecoveryTelemetryEventTypeName);
            Type recoverySituationType = CharacterAssembly.GetType(RecoverySituationTypeName);
            object nearFall = recoverySituationType != null
                ? Enum.Parse(recoverySituationType, "NearFall")
                : null;
            ConstructorInfo constructor = telemetryEventType?.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: new[]
                {
                    typeof(int),
                    typeof(float),
                    recoverySituationType,
                    typeof(string),
                    typeof(float),
                    typeof(float),
                    typeof(float),
                    typeof(float),
                },
                modifiers: null);
            MethodInfo toNdjsonLineMethod = telemetryEventType?.GetMethod(
                "ToNdjsonLine",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            // Act
            object telemetryEvent = constructor?.Invoke(
                new object[] { 123, 4.5f, nearFall, "angle_above_ceiling", 17.25f, 0.35f, 0.72f, 0.81f });
            string ndjsonLine = toNdjsonLineMethod?.Invoke(telemetryEvent, null) as string;

            // Assert
            Assert.That(telemetryEventType, Is.Not.Null,
                $"Expected internal telemetry payload '{RecoveryTelemetryEventTypeName}' to exist in the Character assembly.");
            Assert.That(recoverySituationType, Is.Not.Null,
                $"Expected recovery enum '{RecoverySituationTypeName}' to exist in the Character assembly.");
            Assert.That(constructor, Is.Not.Null,
                "RecoveryTelemetryEvent should expose a constructor that accepts the Chapter 9 telemetry payload fields.");
            Assert.That(toNdjsonLineMethod, Is.Not.Null,
                "RecoveryTelemetryEvent should expose ToNdjsonLine for NDJSON serialization.");
            Assert.That(ndjsonLine, Is.Not.Null.And.Not.Empty,
                "ToNdjsonLine should return a non-empty NDJSON payload.");
            Assert.That(ndjsonLine, Does.Contain("\"FrameNumber\""));
            Assert.That(ndjsonLine, Does.Contain("\"Time\""));
            Assert.That(ndjsonLine, Does.Contain("\"Situation\""));
            Assert.That(ndjsonLine, Does.Contain("\"Reason\""));
            Assert.That(ndjsonLine, Does.Contain("\"UprightAngle\""));
            Assert.That(ndjsonLine, Does.Contain("\"SlipEstimate\""));
            Assert.That(ndjsonLine, Does.Contain("\"SupportQuality\""));
            Assert.That(ndjsonLine, Does.Contain("\"TurnSeverity\""));
        }
    }
}