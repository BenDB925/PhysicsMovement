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
    /// PlayMode tests covering the Phase 3C1 CharacterState API scaffold contract.
    /// </summary>
    public class CharacterStateTests
    {
        private GameObject _root;
        private CharacterState _characterState;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("CharacterState_TestRoot");
            _root.AddComponent<Rigidbody>();
            _root.AddComponent<BalanceController>();
            _root.AddComponent<PlayerMovement>();
            _characterState = _root.AddComponent<CharacterState>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
            }
        }

        [UnityTest]
        public IEnumerator Awake_WhenComponentInitializes_CurrentStateStartsStanding()
        {
            // Arrange
            yield return null;

            // Act
            CharacterStateType currentState = _characterState.CurrentState;

            // Assert
            Assert.That(currentState, Is.EqualTo(CharacterStateType.Standing),
                "CharacterState must initialize to Standing for deterministic state-machine startup.");
        }

        [UnityTest]
        public IEnumerator Awake_WhenComponentInitializes_CachesRequiredDependencies()
        {
            // Arrange
            yield return null;

            // Act
            object cachedBalance = GetPrivateField(_characterState, "_balanceController");
            object cachedMovement = GetPrivateField(_characterState, "_playerMovement");
            object cachedRigidbody = GetPrivateField(_characterState, "_rb");

            // Assert
            Assert.That(cachedBalance, Is.Not.Null,
                "CharacterState must cache BalanceController in Awake.");
            Assert.That(cachedMovement, Is.Not.Null,
                "CharacterState must cache PlayerMovement in Awake.");
            Assert.That(cachedRigidbody, Is.Not.Null,
                "CharacterState must cache Rigidbody in Awake.");
        }

        private static object GetPrivateField(object instance, string fieldName)
        {
            FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new InvalidOperationException(
                    $"Missing expected private field '{fieldName}' on {instance.GetType().Name}.");
            }

            return field.GetValue(instance);
        }
    }
}
