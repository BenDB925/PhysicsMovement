using System.Reflection;
using NUnit.Framework;
using PhysicsDrivenMovement.Character;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    internal static class LocomotionCollapseDetectorTestSeams
    {
        public static void SetCollapseConfirmed(LocomotionCollapseDetector detector, bool value)
        {
            FieldInfo field = typeof(LocomotionCollapseDetector).GetField(
                $"<{nameof(LocomotionCollapseDetector.IsCollapseConfirmed)}>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null,
                "LocomotionCollapseDetector.IsCollapseConfirmed backing field must exist for tests.");

            field.SetValue(detector, value);
        }
    }
}