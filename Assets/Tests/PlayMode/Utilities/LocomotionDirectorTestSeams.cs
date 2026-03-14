using System.Reflection;
using NUnit.Framework;
using PhysicsDrivenMovement.Character;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// Reflection-based test seam for controlling <see cref="LocomotionDirector"/>
    /// internal state from PlayMode tests without widening the public API.
    /// </summary>
    internal static class LocomotionDirectorTestSeams
    {
        private static readonly FieldInfo RecoveryStateField =
            typeof(LocomotionDirector).GetField(
                "_currentRecoveryState",
                BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>
        /// Injects a recovery state into the director so <see cref="LocomotionDirector.IsRecoveryActive"/>
        /// returns the desired value for the next evaluation frame.
        /// </summary>
        public static void SetRecoveryState(LocomotionDirector director, bool active)
        {
            Assert.That(RecoveryStateField, Is.Not.Null,
                "LocomotionDirector._currentRecoveryState field must exist for C6.4 test seam.");

            // RecoveryState is internal, so we construct via the Character assembly.
            Assembly characterAssembly = typeof(LocomotionDirector).Assembly;
            System.Type recoveryStateType = characterAssembly.GetType("PhysicsDrivenMovement.Character.RecoveryState");
            Assert.That(recoveryStateType, Is.Not.Null, "RecoveryState type must exist in the Character assembly.");

            if (active)
            {
                // RecoverySituation.HardTurn=1, 100 frames remaining, 100 total, severity=0.5, turnSeverity=0.5
                System.Type situationType = characterAssembly.GetType("PhysicsDrivenMovement.Character.RecoverySituation");
                object hardTurn = System.Enum.ToObject(situationType, 1);
                object state = System.Activator.CreateInstance(
                    recoveryStateType,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                    null,
                    new object[] { hardTurn, 100, 100, 0.5f, 0.5f },
                    null);
                RecoveryStateField.SetValue(director, state);
            }
            else
            {
                // Use RecoveryState.Inactive via the static field.
                FieldInfo inactiveField = recoveryStateType.GetField("Inactive",
                    BindingFlags.Static | BindingFlags.Public);
                Assert.That(inactiveField, Is.Not.Null, "RecoveryState.Inactive must exist.");
                RecoveryStateField.SetValue(director, inactiveField.GetValue(null));
            }
        }
    }
}
