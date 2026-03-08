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
    public class PlayerMovementTests
    {
        private const string PlayerRagdollPrefabPath = "Assets/Prefabs/PlayerRagdoll.prefab";
        private const int SettleFrames = 80;
        private const float TestEpsilon = 0.001f;

        private static readonly int[] CardinalYaws = { 0, 90, 180, 270 };
        private static readonly Vector3 TestOrigin = new Vector3(1500f, 0f, 0f);

        private GameObject _ground;
        private GameObject _player;
        private Camera _camera;
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
            _ground.name = "PlayerMovementTests_Ground";
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

            GameObject cameraObject = new GameObject("PlayerMovementTests_Camera");
            _camera = cameraObject.AddComponent<Camera>();
            _camera.transform.position = TestOrigin + new Vector3(0f, 4f, -6f);
            _camera.transform.rotation = Quaternion.identity;
            SetPrivateField(_movement, "_camera", _camera);
            _movement.SetMoveInputForTest(Vector2.zero);
        }

        [TearDown]
        public void TearDown()
        {
            if (_camera != null)
            {
                UnityEngine.Object.Destroy(_camera.gameObject);
            }

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
        public IEnumerator ApplyMovementForces_WhenFallen_DoesNotApplyHorizontalForce()
        {
            yield return WaitForPhysicsFrames(SettleFrames);
            yield return PrepareStandingBaseline();

            _balance.SetGroundStateForTest(isGrounded: true, isFallen: true);
            _characterState.SetStateForTest(CharacterStateType.Fallen);
            _movement.SetMoveInputForTest(Vector2.up);
            _hipsBody.linearVelocity = Vector3.zero;
            Vector3 startPosition = _player.transform.position;

            yield return WaitForPhysicsFrames(12);

            Vector3 horizontalVelocity = Horizontal(_hipsBody.linearVelocity);
            Vector3 horizontalDisplacement = Horizontal(_player.transform.position - startPosition);
            Assert.That(horizontalVelocity.sqrMagnitude, Is.LessThanOrEqualTo(TestEpsilon));
            Assert.That(horizontalDisplacement.magnitude, Is.LessThan(0.2f));
        }

        [UnityTest]
        public IEnumerator ApplyMovementForces_WithForwardInput_UsesCameraRelativeDirection()
        {
            yield return WaitForPhysicsFrames(SettleFrames);
            yield return PrepareStandingBaseline();

            SetPrivateField(_movement, "_moveForce", 1500f);
            SetPrivateField(_movement, "_maxSpeed", 8f);
            _camera.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

            Vector3 startPosition = _player.transform.position;
            _movement.SetMoveInputForTest(Vector2.up);
            yield return WaitForPhysicsFrames(80);

            Vector3 displacement = Horizontal(_player.transform.position - startPosition);
            Assert.That(displacement.magnitude, Is.GreaterThan(1f));
            Assert.That(Mathf.Abs(displacement.normalized.x), Is.GreaterThan(Mathf.Abs(displacement.normalized.z)));
        }

        [UnityTest]
        public IEnumerator ApplyMovementForces_WhenAtOrAboveSpeedCap_DoesNotIncreaseHorizontalSpeed()
        {
            yield return WaitForPhysicsFrames(SettleFrames);
            yield return PrepareStandingBaseline();

            SetPrivateField(_movement, "_maxSpeed", 5f);
            _hipsBody.linearVelocity = new Vector3(8f, 0f, 0f);
            float speedBefore = Horizontal(_hipsBody.linearVelocity).magnitude;
            _movement.SetMoveInputForTest(Vector2.right);
            yield return WaitForPhysicsFrames(8);

            float speedAfter = Horizontal(_hipsBody.linearVelocity).magnitude;
            Assert.That(speedAfter, Is.LessThanOrEqualTo(speedBefore + 0.05f));
        }

        [UnityTest]
        public IEnumerator ApplyMovementForces_WithFacingTurnRateLimit_SlewsFacingTargetAcrossFrames()
        {
            yield return WaitForPhysicsFrames(SettleFrames);
            yield return PrepareStandingBaseline();

            SetPrivateField(_movement, "_maxFacingTurnRateDegPerSecond", 90f);
            _camera.transform.rotation = Quaternion.identity;
            _movement.SetMoveInputForTest(Vector2.up);
            yield return WaitForPhysicsFrames(2);

            _camera.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            yield return new WaitForFixedUpdate();

            Vector3 immediateFacing = GetTargetFacingForward();
            Assert.That(Vector3.Angle(immediateFacing, Vector3.forward), Is.GreaterThan(0.5f));
            Assert.That(Vector3.Angle(immediateFacing, Vector3.right), Is.GreaterThan(45f));

            int settleFrames = Mathf.CeilToInt(90f / (90f * Time.fixedDeltaTime)) + 10;
            yield return WaitForPhysicsFrames(settleFrames);

            Vector3 settledFacing = GetTargetFacingForward();
            Assert.That(Vector3.Angle(settledFacing, Vector3.right), Is.LessThan(5f));
        }

        [UnityTest]
        public IEnumerator CameraRelativeMovement_AtSteepPitchMinus60_CharacterStillMovesForward()
        {
            yield return WaitForPhysicsFrames(SettleFrames);
            yield return PrepareStandingBaseline();

            SetPrivateField(_movement, "_moveForce", 1500f);
            SetPrivateField(_movement, "_maxSpeed", 20f);
            _camera.transform.rotation = Quaternion.Euler(60f, 0f, 0f);

            Vector3 startPosition = _player.transform.position;
            _movement.SetMoveInputForTest(Vector2.up);
            yield return WaitForPhysicsFrames(200);

            Vector3 displacement = Horizontal(_player.transform.position - startPosition);
            Assert.That(displacement.magnitude, Is.GreaterThanOrEqualTo(1.5f));
            Assert.That(float.IsNaN(_hipsBody.position.x), Is.False);
            Assert.That(float.IsNaN(_hipsBody.linearVelocity.x), Is.False);
        }

        [UnityTest]
        public IEnumerator CameraRelativeMovement_AtAllCardinalYawAngles_CharacterMovesInCorrectDirection(
            [ValueSource(nameof(CardinalYaws))] int yawDeg)
        {
            yield return WaitForPhysicsFrames(SettleFrames);
            yield return PrepareStandingBaseline();

            SetPrivateField(_movement, "_moveForce", 1500f);
            SetPrivateField(_movement, "_maxSpeed", 20f);
            _camera.transform.rotation = Quaternion.Euler(0f, yawDeg, 0f);

            Vector3 expectedForward = Quaternion.Euler(0f, yawDeg, 0f) * Vector3.forward;
            expectedForward.y = 0f;
            expectedForward.Normalize();

            Vector3 startPosition = _player.transform.position;
            _movement.SetMoveInputForTest(Vector2.up);
            yield return WaitForPhysicsFrames(180);

            Vector3 horizontalDisplacement = Horizontal(_player.transform.position - startPosition);
            Assert.That(horizontalDisplacement.magnitude, Is.GreaterThanOrEqualTo(1f));
            Assert.That(Vector3.Dot(horizontalDisplacement.normalized, expectedForward), Is.GreaterThanOrEqualTo(0.7f));
            Assert.That(float.IsNaN(_hipsBody.position.x), Is.False);
        }

        [UnityTest]
        public IEnumerator UnderSustainedInput_HorizontalSpeedDoesNotExceedMaxSpeed()
        {
            yield return WaitForPhysicsFrames(SettleFrames);
            yield return PrepareStandingBaseline();

            const float maxSpeed = 5f;
            SetPrivateField(_movement, "_moveForce", 1500f);
            SetPrivateField(_movement, "_maxSpeed", maxSpeed);
            _movement.SetMoveInputForTest(Vector2.up);

            float maxObservedHorizontalSpeed = 0f;
            for (int frame = 0; frame < 600; frame++)
            {
                yield return new WaitForFixedUpdate();

                if (frame % 5 == 0)
                {
                    float horizontalSpeed = Horizontal(_hipsBody.linearVelocity).magnitude;
                    if (horizontalSpeed > maxObservedHorizontalSpeed)
                    {
                        maxObservedHorizontalSpeed = horizontalSpeed;
                    }
                }
            }

            Assert.That(maxObservedHorizontalSpeed, Is.LessThanOrEqualTo(maxSpeed * 1.2f));
            Assert.That(maxObservedHorizontalSpeed, Is.LessThanOrEqualTo(maxSpeed * 2f));
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

        private Vector3 GetTargetFacingForward()
        {
            FieldInfo field = typeof(BalanceController).GetField(
                "_targetFacingRotation",
                BindingFlags.Instance | BindingFlags.NonPublic);

            if (field == null)
            {
                throw new InvalidOperationException("BalanceController._targetFacingRotation must exist for tests.");
            }

            Quaternion rotation = (Quaternion)field.GetValue(_balance);
            Vector3 forward = Vector3.ProjectOnPlane(rotation * Vector3.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.001f)
            {
                return Vector3.forward;
            }

            return forward.normalized;
        }

        private static IEnumerator WaitForPhysicsFrames(int count)
        {
            for (int i = 0; i < count; i++)
            {
                yield return new WaitForFixedUpdate();
            }
        }

        private static Vector3 Horizontal(Vector3 value)
        {
            return new Vector3(value.x, 0f, value.z);
        }

        private static void SetPrivateField(object instance, string fieldName, object value)
        {
            FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new InvalidOperationException($"Missing private field '{fieldName}' on {instance.GetType().Name}.");
            }

            field.SetValue(instance, value);
        }
    }
}