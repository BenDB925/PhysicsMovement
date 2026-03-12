using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using PhysicsDrivenMovement.Character;
using PhysicsDrivenMovement.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    public class CharacterStateTests
    {
        private const string PlayerRagdollPrefabPath = "Assets/Prefabs/PlayerRagdoll.prefab";
        private const int SettleFrames = 80;

        private static readonly Vector3 TestOrigin = new Vector3(1800f, 0f, 0f);

        private GameObject _ground;
        private GameObject _player;
        private BalanceController _balance;
        private PlayerMovement _movement;
        private CharacterState _characterState;
        private Rigidbody _hipsBody;
        private float _savedFixedDeltaTime;
        private int _savedSolverIterations;
        private int _savedSolverVelocityIterations;

        [SetUp]
        public void SetUp()
        {
            _savedFixedDeltaTime = Time.fixedDeltaTime;
            _savedSolverIterations = Physics.defaultSolverIterations;
            _savedSolverVelocityIterations = Physics.defaultSolverVelocityIterations;

            Time.fixedDeltaTime = 0.01f;
            Physics.defaultSolverIterations = 12;
            Physics.defaultSolverVelocityIterations = 4;

            _ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ground.name = "CharacterStateTests_Ground";
            _ground.transform.position = TestOrigin + new Vector3(0f, -0.5f, 0f);
            _ground.transform.localScale = new Vector3(40f, 1f, 40f);
            _ground.layer = GameSettings.LayerEnvironment;

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerRagdollPrefabPath);
            Assert.That(prefab, Is.Not.Null, "PlayerRagdoll prefab must be loadable from Assets/Prefabs.");

            _player = UnityEngine.Object.Instantiate(prefab, TestOrigin + new Vector3(0f, 1.1f, 0f), Quaternion.identity);
            _balance = _player.GetComponent<BalanceController>();
            _movement = _player.GetComponent<PlayerMovement>();
            _characterState = _player.GetComponent<CharacterState>();
            _hipsBody = _player.GetComponent<Rigidbody>();

            Assert.That(_balance, Is.Not.Null, "PlayerRagdoll prefab must provide BalanceController.");
            Assert.That(_movement, Is.Not.Null, "PlayerRagdoll prefab must provide PlayerMovement.");
            Assert.That(_characterState, Is.Not.Null, "PlayerRagdoll prefab must provide CharacterState.");
            Assert.That(_hipsBody, Is.Not.Null, "PlayerRagdoll prefab must provide a hips Rigidbody.");

            _movement.SetMoveInputForTest(Vector2.zero);
        }

        [TearDown]
        public void TearDown()
        {
            if (_player != null)
            {
                UnityEngine.Object.Destroy(_player);
            }

            if (_ground != null)
            {
                UnityEngine.Object.Destroy(_ground);
            }

            Time.fixedDeltaTime = _savedFixedDeltaTime;
            Physics.defaultSolverIterations = _savedSolverIterations;
            Physics.defaultSolverVelocityIterations = _savedSolverVelocityIterations;
        }

        [Test]
        public void Awake_WhenComponentInitializes_CurrentStateStartsStanding()
        {
            Assert.That(_characterState.CurrentState, Is.EqualTo(CharacterStateType.Standing),
                "CharacterState must initialize to Standing for deterministic startup.");
        }

        [UnityTest]
        public IEnumerator Awake_WhenComponentInitializes_CachesRequiredDependencies()
        {
            yield return null;

            object cachedBalance = GetPrivateField(_characterState, "_balanceController");
            object cachedMovement = GetPrivateField(_characterState, "_playerMovement");
            object cachedRigidbody = GetPrivateField(_characterState, "_rb");

            Assert.That(cachedBalance, Is.Not.Null, "CharacterState must cache BalanceController in Awake.");
            Assert.That(cachedMovement, Is.Not.Null, "CharacterState must cache PlayerMovement in Awake.");
            Assert.That(cachedRigidbody, Is.Not.Null, "CharacterState must cache Rigidbody in Awake.");
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WhenGroundedAndMoveInputExceedsEnterThreshold_TransitionsStandingToMoving()
        {
            yield return WaitForPhysicsFrames(SettleFrames);
            yield return PrepareStandingBaseline();
            _movement.SetMoveInputForTest(new Vector2(0.11f, 0f));
            yield return new WaitForFixedUpdate();

            Assert.That(_characterState.CurrentState, Is.EqualTo(CharacterStateType.Moving),
                "State should transition Standing -> Moving when grounded and move magnitude exceeds enter threshold.");
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WhenMovingAndMoveInputDropsBelowExitThreshold_TransitionsMovingToStanding()
        {
            yield return WaitForPhysicsFrames(SettleFrames);
            yield return PrepareStandingBaseline();
            _movement.SetMoveInputForTest(new Vector2(0.11f, 0f));
            yield return new WaitForFixedUpdate();
            Assert.That(_characterState.CurrentState, Is.EqualTo(CharacterStateType.Moving),
                "Precondition failed: expected Moving after enter-threshold input.");

            _movement.SetMoveInputForTest(new Vector2(0.04f, 0f));
            yield return new WaitForFixedUpdate();

            Assert.That(_characterState.CurrentState, Is.EqualTo(CharacterStateType.Standing),
                "State should transition Moving -> Standing when grounded and move magnitude is below exit threshold.");
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WhenGroundedSignalLost_TransitionsToAirborne()
        {
            yield return WaitForPhysicsFrames(SettleFrames);
            yield return PrepareStandingBaseline();
            _balance.SetGroundStateForTest(isGrounded: false, isFallen: false);
            _movement.SetMoveInputForTest(Vector2.zero);
            yield return new WaitForFixedUpdate();

            Assert.That(_characterState.CurrentState, Is.EqualTo(CharacterStateType.Airborne),
                "State should transition to Airborne when grounded signal is lost.");
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WhenFallenSignalTrue_TransitionsToFallen()
        {
            yield return WaitForPhysicsFrames(SettleFrames);
            yield return PrepareStandingBaseline();
            _balance.SetGroundStateForTest(isGrounded: true, isFallen: true);
            _movement.SetMoveInputForTest(new Vector2(0.11f, 0f));
            yield return new WaitForFixedUpdate();

            Assert.That(_characterState.CurrentState, Is.EqualTo(CharacterStateType.Fallen),
                "Fallen signal must take transition priority over grounded and move transitions.");
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WhenRecoveredFromFallenAndGroundedBeforeTimer_StaysFallen()
        {
            yield return WaitForPhysicsFrames(SettleFrames);
            yield return PrepareStandingBaseline();
            SetPrivateField(_characterState, "_getUpDelay", 0.5f);
            SetPrivateField(_characterState, "_knockoutDuration", 1.5f);
            _balance.SetGroundStateForTest(isGrounded: true, isFallen: true);
            yield return new WaitForFixedUpdate();
            Assert.That(_characterState.CurrentState, Is.EqualTo(CharacterStateType.Fallen),
                "Precondition failed: expected Fallen before recovery transition.");

            _balance.SetGroundStateForTest(isGrounded: true, isFallen: false);
            yield return new WaitForFixedUpdate();

            Assert.That(_characterState.CurrentState, Is.EqualTo(CharacterStateType.Fallen),
                "Recovered grounded fallen state should remain Fallen until timer gates are satisfied.");
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WhenStateUnchanged_OnStateChangedFiresOnlyForActualChanges()
        {
            yield return WaitForPhysicsFrames(SettleFrames);
            yield return PrepareStandingBaseline();

            int eventCount = 0;
            CharacterStateType lastPrevious = CharacterStateType.Standing;
            CharacterStateType lastNext = CharacterStateType.Standing;

            _characterState.OnStateChanged += (previous, next) =>
            {
                eventCount++;
                lastPrevious = previous;
                lastNext = next;
            };

            _movement.SetMoveInputForTest(Vector2.zero);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.That(eventCount, Is.EqualTo(0),
                "No event should fire while remaining in Standing with unchanged inputs.");

            _movement.SetMoveInputForTest(new Vector2(0.11f, 0f));
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Assert.That(eventCount, Is.EqualTo(1),
                "OnStateChanged should fire exactly once for a single actual state change.");
            Assert.That(lastPrevious, Is.EqualTo(CharacterStateType.Standing));
            Assert.That(lastNext, Is.EqualTo(CharacterStateType.Moving));
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WhenFallenGroundedBeforeDelay_RemainsFallen()
        {
            yield return WaitForPhysicsFrames(SettleFrames);
            yield return PrepareStandingBaseline();
            SetPrivateField(_characterState, "_getUpDelay", 0.2f);
            SetPrivateField(_characterState, "_knockoutDuration", 0.5f);
            _balance.SetGroundStateForTest(isGrounded: true, isFallen: true);
            yield return new WaitForFixedUpdate();
            Assert.That(_characterState.CurrentState, Is.EqualTo(CharacterStateType.Fallen));

            _balance.SetGroundStateForTest(isGrounded: true, isFallen: false);
            yield return new WaitForFixedUpdate();

            Assert.That(_characterState.CurrentState, Is.EqualTo(CharacterStateType.Fallen),
                "State should remain Fallen until get-up delay and knockout duration gates are both satisfied.");
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WhenFallenGroundedPastDelayAndKnockout_TransitionsToGettingUp()
        {
            yield return WaitForPhysicsFrames(SettleFrames);
            yield return PrepareStandingBaseline();
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

            _balance.SetGroundStateForTest(isGrounded: true, isFallen: true);
            yield return new WaitForFixedUpdate();
            Assert.That(_characterState.CurrentState, Is.EqualTo(CharacterStateType.Fallen));

            _balance.SetGroundStateForTest(isGrounded: true, isFallen: false);
            yield return new WaitForSeconds(0.05f);
            yield return new WaitForFixedUpdate();

            Assert.That(transitionedToGettingUp, Is.True,
                "State machine should enter GettingUp only after both timer gates are satisfied while grounded.");
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WhenEnteringGettingUp_AppliesSingleUpwardImpulse()
        {
            yield return WaitForPhysicsFrames(SettleFrames);
            yield return PrepareStandingBaseline();
            SetPrivateField(_characterState, "_getUpDelay", 0.01f);
            SetPrivateField(_characterState, "_knockoutDuration", 0.01f);
            SetPrivateField(_characterState, "_getUpForce", 5f);
            _hipsBody.linearVelocity = Vector3.zero;
            int initialImpulseCount = (int)GetPrivateField(_characterState, "_getUpImpulseAppliedCount");

            _balance.SetGroundStateForTest(isGrounded: true, isFallen: true);
            yield return new WaitForFixedUpdate();
            Assert.That(_characterState.CurrentState, Is.EqualTo(CharacterStateType.Fallen));

            _balance.SetGroundStateForTest(isGrounded: true, isFallen: false);
            yield return new WaitForSeconds(0.03f);
            yield return new WaitForFixedUpdate();
            int firstImpulseCount = (int)GetPrivateField(_characterState, "_getUpImpulseAppliedCount");

            _hipsBody.linearVelocity = Vector3.zero;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            int secondImpulseCount = (int)GetPrivateField(_characterState, "_getUpImpulseAppliedCount");

            Assert.That(firstImpulseCount, Is.EqualTo(initialImpulseCount + 1),
                "Entering GettingUp should apply exactly one get-up impulse on the transition frame.");
            Assert.That(secondImpulseCount, Is.EqualTo(firstImpulseCount),
                "Remaining in or exiting GettingUp should not apply a second get-up impulse automatically.");
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WhenGettingUpExceedsTimeout_TransitionsToStanding()
        {
            yield return WaitForPhysicsFrames(SettleFrames);
            yield return PrepareStandingBaseline();
            SetPrivateField(_characterState, "_getUpTimeout", 0.05f);
            SetCurrentState(CharacterStateType.GettingUp);
            _balance.SetGroundStateForTest(isGrounded: true, isFallen: false);
            _movement.SetMoveInputForTest(Vector2.zero);

            yield return new WaitForSeconds(0.06f);
            yield return new WaitForFixedUpdate();

            Assert.That(_characterState.CurrentState, Is.EqualTo(CharacterStateType.Standing));
        }

        private IEnumerator PrepareStandingBaseline()
        {
            _movement.SetMoveInputForTest(Vector2.zero);
            _balance.SetGroundStateForTest(isGrounded: true, isFallen: false);
            _characterState.SetStateForTest(CharacterStateType.Standing);
            _hipsBody.linearVelocity = Vector3.zero;
            _hipsBody.angularVelocity = Vector3.zero;
            yield return new WaitForFixedUpdate();
        }

        private static IEnumerator WaitForPhysicsFrames(int count)
        {
            for (int i = 0; i < count; i++)
            {
                yield return new WaitForFixedUpdate();
            }
        }

        private static object GetPrivateField(object instance, string fieldName)
        {
            FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new InvalidOperationException($"Missing expected private field '{fieldName}' on {instance.GetType().Name}.");
            }

            return field.GetValue(instance);
        }

        private static void SetPrivateField(object instance, string fieldName, object value)
        {
            FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new InvalidOperationException($"Missing expected private field '{fieldName}' on {instance.GetType().Name}.");
            }

            field.SetValue(instance, value);
        }

        private void SetCurrentState(CharacterStateType state)
        {
            FieldInfo field = typeof(CharacterState).GetField(
                $"<{nameof(CharacterState.CurrentState)}>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic);

            if (field == null)
            {
                throw new InvalidOperationException("CharacterState.CurrentState backing field must exist for tests.");
            }

            field.SetValue(_characterState, state);
        }
    }
}