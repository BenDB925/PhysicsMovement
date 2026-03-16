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
    public class JumpTests
    {
        private const string PlayerRagdollPrefabPath = "Assets/Prefabs/PlayerRagdoll_Skinned.prefab";
        private const int SettleFrames = 80;

        private static readonly Vector3 TestOrigin = new Vector3(1650f, 0f, 0f);

        private GameObject _ground;
        private GameObject _player;
        private Rigidbody _hipsBody;
        private BalanceController _balance;
        private PlayerMovement _movement;
        private CharacterState _characterState;
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
            _ground.name = "JumpTests_Ground";
            _ground.transform.position = TestOrigin + new Vector3(0f, -0.5f, 0f);
            _ground.transform.localScale = new Vector3(40f, 1f, 40f);
            _ground.layer = GameSettings.LayerEnvironment;

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerRagdollPrefabPath);
            Assert.That(prefab, Is.Not.Null, "PlayerRagdoll prefab must be loadable from Assets/Prefabs.");

            _player = UnityEngine.Object.Instantiate(prefab, TestOrigin + new Vector3(0f, 1.1f, 0f), Quaternion.identity);
            _hipsBody = _player.GetComponent<Rigidbody>();
            _balance = _player.GetComponent<BalanceController>();
            _movement = _player.GetComponent<PlayerMovement>();
            _characterState = _player.GetComponent<CharacterState>();

            Assert.That(_hipsBody, Is.Not.Null);
            Assert.That(_balance, Is.Not.Null);
            Assert.That(_movement, Is.Not.Null);
            Assert.That(_characterState, Is.Not.Null);

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

        [UnityTest]
        public IEnumerator Jump_WhenStandingAndGrounded_AppliesUpwardImpulse()
        {
            yield return WaitForPhysicsFrames(SettleFrames);
            yield return PrepareStandingBaseline();
            SetPrivateField(_movement, "_jumpForce", 15f);
            _hipsBody.linearVelocity = Vector3.zero;
            _movement.SetJumpInputForTest(true);
            yield return new WaitForFixedUpdate();

            Assert.That(_characterState.CurrentState, Is.EqualTo(CharacterStateType.Standing));
            Assert.That(_hipsBody.linearVelocity.y, Is.GreaterThan(0.1f));
        }

        [UnityTest]
        public IEnumerator Jump_WhenMovingAndGrounded_AppliesUpwardImpulse()
        {
            yield return WaitForPhysicsFrames(SettleFrames);
            yield return PrepareStandingBaseline();
            SetPrivateField(_movement, "_jumpForce", 15f);
            _movement.SetMoveInputForTest(new Vector2(0.11f, 0f));
            yield return WaitForPhysicsFrames(2);
            Assert.That(_characterState.CurrentState, Is.EqualTo(CharacterStateType.Moving));

            _hipsBody.linearVelocity = new Vector3(_hipsBody.linearVelocity.x, 0f, _hipsBody.linearVelocity.z);
            _movement.SetJumpInputForTest(true);
            yield return new WaitForFixedUpdate();

            Assert.That(_hipsBody.linearVelocity.y, Is.GreaterThan(0.1f));
        }

        [UnityTest]
        public IEnumerator Jump_WhenFallen_DoesNotApplyImpulse()
        {
            yield return WaitForPhysicsFrames(SettleFrames);
            yield return PrepareStandingBaseline();
            SetPrivateField(_movement, "_jumpForce", 15f);
            _balance.SetGroundStateForTest(isGrounded: true, isFallen: true);
            yield return new WaitForFixedUpdate();
            Assert.That(_characterState.CurrentState, Is.EqualTo(CharacterStateType.Fallen));

            _hipsBody.linearVelocity = Vector3.zero;
            _movement.SetJumpInputForTest(true);
            yield return new WaitForFixedUpdate();

            Assert.That(_hipsBody.linearVelocity.y, Is.LessThan(0.5f));
        }

        [UnityTest]
        public IEnumerator Jump_WhenAirborne_DoesNotApplyImpulse()
        {
            yield return WaitForPhysicsFrames(SettleFrames);
            yield return PrepareStandingBaseline();
            SetPrivateField(_movement, "_jumpForce", 15f);
            _balance.SetGroundStateForTest(isGrounded: false, isFallen: false);
            yield return new WaitForFixedUpdate();
            Assert.That(_characterState.CurrentState, Is.EqualTo(CharacterStateType.Airborne));

            _hipsBody.linearVelocity = Vector3.zero;
            _movement.SetJumpInputForTest(true);
            yield return new WaitForFixedUpdate();

            Assert.That(_hipsBody.linearVelocity.y, Is.LessThan(0.5f));
        }

        [UnityTest]
        public IEnumerator Jump_WhenHeldForSecondFrame_DoesNotFireAgain()
        {
            yield return WaitForPhysicsFrames(SettleFrames);
            yield return PrepareStandingBaseline();
            SetPrivateField(_movement, "_jumpForce", 15f);
            _hipsBody.linearVelocity = Vector3.zero;
            _movement.SetJumpInputForTest(true);

            yield return new WaitForFixedUpdate();
            Assert.That(_hipsBody.linearVelocity.y, Is.GreaterThan(0.1f));

            _hipsBody.linearVelocity = Vector3.zero;
            yield return new WaitForFixedUpdate();

            Assert.That(_hipsBody.linearVelocity.y, Is.LessThan(0.5f));
        }

        [UnityTest]
        public IEnumerator Jump_WhileGettingUp_IsNotApplied()
        {
            yield return WaitForPhysicsFrames(SettleFrames);
            yield return PrepareStandingBaseline();
            SetPrivateField(_movement, "_jumpForce", 15f);
            SetPrivateField(_characterState, "_getUpForce", 0f);
            _characterState.SetStateForTest(CharacterStateType.GettingUp);
            _hipsBody.linearVelocity = Vector3.zero;
            _movement.SetJumpInputForTest(true);
            yield return new WaitForFixedUpdate();

            Assert.That(_hipsBody.linearVelocity.y, Is.LessThan(0.5f));
        }

        [UnityTest]
        public IEnumerator Jump_HeldDuringFallen_DoesNotFireOnGetUp()
        {
            yield return WaitForPhysicsFrames(SettleFrames);
            yield return PrepareStandingBaseline();
            SetPrivateField(_movement, "_jumpForce", 15f);
            SetPrivateField(_characterState, "_getUpForce", 0f);
            _balance.SetGroundStateForTest(isGrounded: true, isFallen: true);
            _movement.SetJumpInputForTest(true);
            _hipsBody.linearVelocity = Vector3.zero;

            yield return WaitForPhysicsFrames(5);
            _characterState.SetStateForTest(CharacterStateType.GettingUp);
            yield return WaitForPhysicsFrames(5);

            _balance.SetGroundStateForTest(isGrounded: true, isFallen: false);
            _characterState.SetStateForTest(CharacterStateType.Standing);
            _hipsBody.linearVelocity = Vector3.zero;
            yield return new WaitForFixedUpdate();

            Assert.That(_hipsBody.linearVelocity.y, Is.LessThan(0.5f));
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

        private static void SetPrivateField(object instance, string fieldName, object value)
        {
            FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Could not find private field '{fieldName}' on {instance.GetType().Name}.");
            field.SetValue(instance, value);
        }
    }
}