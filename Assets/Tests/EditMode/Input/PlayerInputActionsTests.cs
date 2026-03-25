using NUnit.Framework;
using PhysicsDrivenMovement.Input;
using UnityEngine.InputSystem;

namespace PhysicsDrivenMovement.Tests.EditMode.Input
{
    public class PlayerInputActionsTests
    {
        [Test]
        public void SprintAction_ExistsButHasNoBindings()
        {
            // Arrange
            using PlayerInputActions actions = new PlayerInputActions();

            // Act
            InputAction sprint = actions.Player.Get().FindAction("Sprint");

            // Assert
            Assert.That(sprint, Is.Not.Null, "Sprint action should still exist in the action map.");
            Assert.That(sprint.bindings.Count, Is.EqualTo(0),
                "Sprint action should have no bindings - auto-sprint replaces button input.");
        }
    }
}