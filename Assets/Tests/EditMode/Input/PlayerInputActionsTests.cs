using System.Linq;
using NUnit.Framework;
using PhysicsDrivenMovement.Input;
using UnityEngine.InputSystem;

namespace PhysicsDrivenMovement.Tests.EditMode.Input
{
    public class PlayerInputActionsTests
    {
        [Test]
        public void PlayerActionMap_ContainsSprintButtonAction()
        {
            // Arrange
            using PlayerInputActions actions = new PlayerInputActions();

            // Act
            InputAction sprint = actions.Player.Get().FindAction("Sprint");

            // Assert
            Assert.That(sprint, Is.Not.Null);
            Assert.That(sprint.type, Is.EqualTo(InputActionType.Button));
            Assert.That(sprint.expectedControlType, Is.EqualTo("Button"));
        }

        [Test]
        public void SprintAction_UsesShiftAndLeftStickPressBindings()
        {
            // Arrange
            using PlayerInputActions actions = new PlayerInputActions();
            InputAction sprint = actions.Player.Get().FindAction("Sprint");
            Assert.That(sprint, Is.Not.Null);

            // Act
            string[] bindingPaths = sprint.bindings.Select(binding => binding.path).ToArray();

            // Assert
            Assert.That(bindingPaths, Does.Contain("<Keyboard>/leftShift"));
            Assert.That(bindingPaths, Does.Contain("<Gamepad>/leftStickPress"));
        }
    }
}