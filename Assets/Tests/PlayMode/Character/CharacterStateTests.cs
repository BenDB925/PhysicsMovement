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
        private BalanceController _balance;
        private PlayerMovement _movement;
        private CharacterState _characterState;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("CharacterState_TestRoot");
            _root.AddComponent<Rigidbody>();
            _balance = _root.AddComponent<BalanceController>();
            _movement = _root.AddComponent<PlayerMovement>();
            _characterState = _root.AddComponent<CharacterState>();

            // Inject default state immediately so FixedUpdate can't overwrite before tests run.
            _balance.SetGroundStateForTest(isGrounded: true, isFallen: false);
            _movement.SetMoveInputForTest(Vector2.zero);

            // Disable physics-driven components so tests have full deterministic control.
            _balance.enabled = false;
            _movement.enabled = false;
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
            object cachedMovement = GetPrivateField(_characterState, "_movementInput");
            object cachedRigidbody = GetPrivateField(_characterState, "_rb");

            // Assert
            Assert.That(cachedBalance, Is.Not.Null,
                "CharacterState must cache BalanceController in Awake.");
            Assert.That(cachedMovement, Is.Not.Null,
                "CharacterState must cache PlayerMovement in Awake.");
            Assert.That(cachedRigidbody, Is.Not.Null,
                "CharacterState must cache Rigidbody in Awake.");
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WhenGroundedAndMoveInputExceedsEnterThreshold_TransitionsStandingToMoving()
        {
            // Arrange
            yield return null;
            SetBalanceSignals(isGrounded: true, isFallen: false);
            SetCurrentMoveInput(new Vector2(0.11f, 0f));

            // Act
            yield return new WaitForFixedUpdate();

            // Assert
            Assert.That(_characterState.CurrentState, Is.EqualTo(CharacterStateType.Moving),
                "State should transition Standing -> Moving when grounded and move magnitude exceeds enter threshold.");
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WhenMovingAndMoveInputDropsBelowExitThreshold_TransitionsMovingToStanding()
        {
            // Arrange
            yield return null;
            SetBalanceSignals(isGrounded: true, isFallen: false);
            SetCurrentMoveInput(new Vector2(0.11f, 0f));
            yield return new WaitForFixedUpdate();
            Assert.That(_characterState.CurrentState, Is.EqualTo(CharacterStateType.Moving),
                "Precondition failed: expected Moving after enter-threshold input.");

            SetCurrentMoveInput(new Vector2(0.04f, 0f));

            // Act
            yield return new WaitForFixedUpdate();

            // Assert
            Assert.That(_characterState.CurrentState, Is.EqualTo(CharacterStateType.Standing),
                "State should transition Moving -> Standing when grounded and move magnitude is below exit threshold.");
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WhenGroundedSignalLost_TransitionsToAirborne()
        {
            // Arrange
            yield return null;
            SetBalanceSignals(isGrounded: false, isFallen: false);
            SetCurrentMoveInput(Vector2.zero);

            // Act
            yield return new WaitForFixedUpdate();

            // Assert
            Assert.That(_characterState.CurrentState, Is.EqualTo(CharacterStateType.Airborne),
                "State should transition to Airborne when grounded signal is lost.");
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WhenFallenSignalTrue_TransitionsToFallen()
        {
            // Arrange
            yield return null;
            SetBalanceSignals(isGrounded: true, isFallen: true);
            SetCurrentMoveInput(new Vector2(0.11f, 0f));

            // Act
            yield return new WaitForFixedUpdate();

            // Assert
            Assert.That(_characterState.CurrentState, Is.EqualTo(CharacterStateType.Fallen),
                "Fallen signal must take transition priority over grounded/move transitions.");
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WhenRecoveredFromFallenAndGroundedBeforeTimer_StaysFallen()
        {
            // Arrange
            yield return null;
            SetPrivateField(_characterState, "_getUpDelay", 0.5f);
            SetPrivateField(_characterState, "_knockoutDuration", 1.5f);
            SetBalanceSignals(isGrounded: true, isFallen: true);
            SetCurrentMoveInput(Vector2.zero);
            yield return new WaitForFixedUpdate();
            Assert.That(_characterState.CurrentState, Is.EqualTo(CharacterStateType.Fallen),
                "Precondition failed: expected Fallen before recovery transition.");

            SetBalanceSignals(isGrounded: true, isFallen: false);

            // Act
            yield return new WaitForFixedUpdate();

            // Assert
            Assert.That(_characterState.CurrentState, Is.EqualTo(CharacterStateType.Fallen),
                "Recovered grounded fallen state should remain Fallen until timer gates are satisfied.");
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WhenStateUnchanged_OnStateChangedFiresOnlyForActualChanges()
        {
            // Arrange
            yield return null;
            int eventCount = 0;
            CharacterStateType lastPrevious = CharacterStateType.Standing;
            CharacterStateType lastNext = CharacterStateType.Standing;

            _characterState.OnStateChanged += (previous, next) =>
            {
                eventCount++;
                lastPrevious = previous;
                lastNext = next;
            };

            SetBalanceSignals(isGrounded: true, isFallen: false);
            SetCurrentMoveInput(Vector2.zero);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.That(eventCount, Is.EqualTo(0),
                "No event should fire while remaining in Standing with unchanged inputs.");

            SetCurrentMoveInput(new Vector2(0.11f, 0f));

            // Act
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            // Assert
            Assert.That(eventCount, Is.EqualTo(1),
                "OnStateChanged should fire exactly once for a single actual state change.");
            Assert.That(lastPrevious, Is.EqualTo(CharacterStateType.Standing),
                "Event previous-state argument should match prior state.");
            Assert.That(lastNext, Is.EqualTo(CharacterStateType.Moving),
                "Event new-state argument should match transitioned state.");
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WhenFallenGroundedBeforeDelay_RemainsFallen()
        {
            // Arrange
            yield return null;
            SetPrivateField(_characterState, "_getUpDelay", 0.2f);
            SetPrivateField(_characterState, "_knockoutDuration", 0.5f);

            SetBalanceSignals(isGrounded: true, isFallen: true);
            SetCurrentMoveInput(Vector2.zero);
            yield return new WaitForFixedUpdate();
            Assert.That(_characterState.CurrentState, Is.EqualTo(CharacterStateType.Fallen),
                "Precondition failed: expected Fallen after fallen signal.");

            SetBalanceSignals(isGrounded: true, isFallen: false);

            // Act
            yield return new WaitForFixedUpdate();

            // Assert
            Assert.That(_characterState.CurrentState, Is.EqualTo(CharacterStateType.Fallen),
                "State should remain Fallen until get-up delay and knockout duration gates are both satisfied.");
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WhenFallenGroundedPastDelayAndKnockout_TransitionsToGettingUp()
        {
            // Arrange
            yield return null;
            SetPrivateField(_characterState, "_getUpDelay", 0.02f);
            SetPrivateField(_characterState, "_knockoutDuration", 0.02f);
            bool transitionedToGettingUp = false;

            _characterState.OnStateChanged += (_, next) =>
            {
                if (next == CharacterStateType.GettingUp)
                {
                    transitionedToGettingUp = true;
                }
            };

            SetBalanceSignals(isGrounded: true, isFallen: true);
            SetCurrentMoveInput(Vector2.zero);
            yield return new WaitForFixedUpdate();
            Assert.That(_characterState.CurrentState, Is.EqualTo(CharacterStateType.Fallen),
                "Precondition failed: expected Fallen after fallen signal.");

            SetBalanceSignals(isGrounded: true, isFallen: false);
            yield return new WaitForSeconds(0.05f);

            // Act
            yield return new WaitForFixedUpdate();

            // Assert
            Assert.That(transitionedToGettingUp, Is.True,
                "State machine should enter GettingUp only after both timer gates are satisfied while grounded.");
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WhenEnteringGettingUp_AppliesSingleUpwardImpulse()
        {
            // Arrange
            yield return null;
            SetPrivateField(_characterState, "_getUpDelay", 0.01f);
            SetPrivateField(_characterState, "_knockoutDuration", 0.01f);
            SetPrivateField(_characterState, "_getUpForce", 5f);

            Rigidbody rb = (Rigidbody)GetPrivateField(_characterState, "_rb");
            rb.linearVelocity = Vector3.zero;

            SetBalanceSignals(isGrounded: true, isFallen: true);
            SetCurrentMoveInput(Vector2.zero);
            yield return new WaitForFixedUpdate();
            Assert.That(_characterState.CurrentState, Is.EqualTo(CharacterStateType.Fallen),
                "Precondition failed: expected Fallen after fallen signal.");

            SetBalanceSignals(isGrounded: true, isFallen: false);
            yield return new WaitForSeconds(0.03f);

            // Act
            yield return new WaitForFixedUpdate();
            float firstVelocityY = rb.linearVelocity.y;
            int firstImpulseCount = (int)GetPrivateField(_characterState, "_getUpImpulseAppliedCount");

            rb.linearVelocity = Vector3.zero;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            int secondImpulseCount = (int)GetPrivateField(_characterState, "_getUpImpulseAppliedCount");

            // Assert
            Assert.That(firstVelocityY, Is.GreaterThan(0f),
                "Entering GettingUp should apply an upward impulse exactly once.");
            Assert.That(firstImpulseCount, Is.EqualTo(1),
                "Get-up impulse count should be 1 after entering GettingUp.");
            Assert.That(secondImpulseCount, Is.EqualTo(1),
                "Get-up impulse count should remain unchanged across later GettingUp ticks.");
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WhenGettingUpExceedsTimeout_TransitionsToStanding()
        {
            // Arrange
            yield return null;
            SetPrivateField(_characterState, "_getUpTimeout", 0.05f);
            SetCurrentState(CharacterStateType.GettingUp);
            SetBalanceSignals(isGrounded: true, isFallen: false);
            SetCurrentMoveInput(Vector2.zero);

            // Act
            yield return new WaitForSeconds(0.06f);
            yield return new WaitForFixedUpdate();

            // Assert
            Assert.That(_characterState.CurrentState, Is.EqualTo(CharacterStateType.Standing),
                "GettingUp should timeout back to Standing after the configured safety duration.");
        }

        private void SetBalanceSignals(bool isGrounded, bool isFallen)
        {
            _balance.SetGroundStateForTest(isGrounded, isFallen);
        }

        private void SetCurrentMoveInput(Vector2 moveInput)
        {
            _movement.SetMoveInputForTest(moveInput);
        }

        private static void SetAutoPropertyBackingField(object instance, string propertyName, object value)
        {
            string backingFieldName = $"<{propertyName}>k__BackingField";
            FieldInfo field = instance.GetType().GetField(backingFieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new InvalidOperationException(
                    $"Missing expected auto-property backing field '{backingFieldName}' on {instance.GetType().Name}.");
            }

            field.SetValue(instance, value);
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

        private static void SetPrivateField(object instance, string fieldName, object value)
        {
            FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new InvalidOperationException(
                    $"Missing expected private field '{fieldName}' on {instance.GetType().Name}.");
            }

            field.SetValue(instance, value);
        }

        private void SetCurrentState(CharacterStateType state)
        {
            SetAutoPropertyBackingField(_characterState, nameof(CharacterState.CurrentState), state);
        }
    }
}
