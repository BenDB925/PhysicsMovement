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
    public class MomentumLeanTests
    {
        private const string PlayerRagdollPrefabPath = "Assets/Prefabs/PlayerRagdoll_Skinned.prefab";
        private const int SettleFrames = 120;
        private const int StraightWarmUpFrames = 300;
        private const int StraightSampleFrames = 100;
        private const int TurnWarmUpFrames = 200;
        private const int TurnSampleFrames = 150;
        private const int IdleWarmUpFrames = 200;
        private const int IdleSampleFrames = 100;
        private const float NonZeroMomentumLeanThresholdDeg = 0.05f;

        private static readonly Vector3 TestOrigin = new Vector3(4000f, 0f, 0f);

        private GameObject _ground;
        private GameObject _player;
        private GameObject _gameSettingsObject;
        private BalanceController _balance;
        private PlayerMovement _movement;
        private CharacterState _characterState;
        private float _savedFixedDeltaTime;
        private int _savedSolverIterations;
        private int _savedSolverVelocityIterations;

        [SetUp]
        public void SetUp()
        {
            PlayModeSceneIsolation.ResetToEmptyScene();

            _savedFixedDeltaTime = Time.fixedDeltaTime;
            _savedSolverIterations = Physics.defaultSolverIterations;
            _savedSolverVelocityIterations = Physics.defaultSolverVelocityIterations;

            Time.fixedDeltaTime = 0.01f;
            Physics.defaultSolverIterations = 12;
            Physics.defaultSolverVelocityIterations = 4;

            CreateRig();
        }

        [TearDown]
        public void TearDown()
        {
            DestroyRig();
            PlayModeSceneIsolation.ResetToEmptyScene();

            Time.fixedDeltaTime = _savedFixedDeltaTime;
            Physics.defaultSolverIterations = _savedSolverIterations;
            Physics.defaultSolverVelocityIterations = _savedSolverVelocityIterations;
        }

        [UnityTest]
        public IEnumerator MomentumLean_WhenTurningLeft_LateralLeanIsNonZero()
        {
            // Arrange
            yield return PrepareBaseline();
            _movement.SetMoveInputForTest(Vector2.up);
            yield return WaitForPhysicsFrames(TurnWarmUpFrames, assertNotFallen: true);

            _movement.SetMoveInputForTest(Vector2.left);

            float maxAbsYawRate = 0f;
            float maxAbsMomentumLean = 0f;

            // Act
            for (int frame = 0; frame < TurnSampleFrames; frame++)
            {
                yield return new WaitForFixedUpdate();
                AssertCharacterNotFallen(frame + 1, "while sampling turning momentum lean");

                maxAbsYawRate = Mathf.Max(maxAbsYawRate, Mathf.Abs(ReadBalanceDebugFloat("DebugSmoothedYawRate")));
                maxAbsMomentumLean = Mathf.Max(maxAbsMomentumLean, Mathf.Abs(ReadBalanceDebugFloat("DebugMomentumLeanDeg")));
            }

            // Assert
            Assert.That(maxAbsYawRate, Is.GreaterThan(15f),
                "A sharp 90 degree turn should produce a measurable smoothed yaw-rate signal.");
            Assert.That(maxAbsMomentumLean, Is.GreaterThan(NonZeroMomentumLeanThresholdDeg),
                "A sharp turn should produce a visible non-zero lateral momentum lean contribution.");
            Assert.That(_characterState.CurrentState, Is.Not.EqualTo(CharacterStateType.Fallen));
        }

        [UnityTest]
        public IEnumerator MomentumLean_WhenStraight_LeanIsNearZero()
        {
            // Arrange
            yield return PrepareBaseline();
            _movement.SetMoveInputForTest(Vector2.up);
            yield return WaitForPhysicsFrames(StraightWarmUpFrames, assertNotFallen: true);

            float maxAbsYawRate = 0f;

            // Act
            for (int frame = 0; frame < StraightSampleFrames; frame++)
            {
                yield return new WaitForFixedUpdate();
                AssertCharacterNotFallen(frame + 1, "while sampling straight-walk yaw rate");

                maxAbsYawRate = Mathf.Max(maxAbsYawRate, Mathf.Abs(ReadBalanceDebugFloat("DebugSmoothedYawRate")));
            }

            // Assert
            Assert.That(maxAbsYawRate, Is.LessThan(30f),
                "Straight walking should keep the smoothed yaw-rate signal within the expected tolerance.");
            Assert.That(_characterState.CurrentState, Is.Not.EqualTo(CharacterStateType.Fallen));
        }

        [UnityTest]
        public IEnumerator MomentumLean_WhenIdle_LeanIsZero()
        {
            // Arrange
            yield return PrepareBaseline();
            yield return WaitForPhysicsFrames(IdleWarmUpFrames, assertNotFallen: true);

            float maxAbsMomentumLean = 0f;

            // Act
            for (int frame = 0; frame < IdleSampleFrames; frame++)
            {
                yield return new WaitForFixedUpdate();
                AssertCharacterNotFallen(frame + 1, "while sampling idle momentum lean");

                maxAbsMomentumLean = Mathf.Max(maxAbsMomentumLean, Mathf.Abs(ReadBalanceDebugFloat("DebugMomentumLeanDeg")));
            }

            // Assert
            Assert.That(maxAbsMomentumLean, Is.LessThan(0.01f),
                "Momentum lean should decay fully to zero while the character remains idle.");
            Assert.That(_characterState.CurrentState, Is.Not.EqualTo(CharacterStateType.Fallen));
        }

        private void CreateRig()
        {
            _gameSettingsObject = new GameObject("MomentumLeanTests_GameSettings");
            _gameSettingsObject.AddComponent<GameSettings>();

            _ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ground.name = "MomentumLeanTests_Ground";
            _ground.transform.position = TestOrigin + new Vector3(0f, -0.5f, 0f);
            _ground.transform.localScale = new Vector3(40f, 1f, 40f);
            _ground.layer = GameSettings.LayerEnvironment;

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerRagdollPrefabPath);
            Assert.That(prefab, Is.Not.Null, "PlayerRagdoll prefab must be loadable from Assets/Prefabs.");

            _player = UnityEngine.Object.Instantiate(prefab, TestOrigin + new Vector3(0f, 1.1f, 0f), Quaternion.identity);
            _balance = _player.GetComponent<BalanceController>();
            _movement = _player.GetComponent<PlayerMovement>();
            _characterState = _player.GetComponent<CharacterState>();

            Assert.That(_balance, Is.Not.Null);
            Assert.That(_movement, Is.Not.Null);
            Assert.That(_characterState, Is.Not.Null);

            _movement.SetMoveInputForTest(Vector2.zero);
            _movement.SetSprintInputForTest(false);
            _movement.SetJumpInputForTest(false);
        }

        private void DestroyRig()
        {
            if (_player != null)
            {
                UnityEngine.Object.Destroy(_player);
                _player = null;
            }

            if (_ground != null)
            {
                UnityEngine.Object.Destroy(_ground);
                _ground = null;
            }

            if (_gameSettingsObject != null)
            {
                UnityEngine.Object.Destroy(_gameSettingsObject);
                _gameSettingsObject = null;
            }
        }

        private IEnumerator PrepareBaseline()
        {
            _movement.SetMoveInputForTest(Vector2.zero);
            _movement.SetSprintInputForTest(false);
            _movement.SetJumpInputForTest(false);
            yield return WaitForPhysicsFrames(SettleFrames, assertNotFallen: true);
        }

        private IEnumerator WaitForPhysicsFrames(int frames, bool assertNotFallen)
        {
            for (int frame = 0; frame < frames; frame++)
            {
                yield return new WaitForFixedUpdate();

                if (assertNotFallen)
                {
                    AssertCharacterNotFallen(frame + 1, "while waiting for the momentum lean test state to settle");
                }
            }
        }

        private void AssertCharacterNotFallen(int frame, string context)
        {
            Assert.That(
                _characterState.CurrentState,
                Is.Not.EqualTo(CharacterStateType.Fallen),
                $"Character fell at frame {frame} {context}.");
        }

        private float ReadBalanceDebugFloat(string propertyName)
        {
            PropertyInfo property = typeof(BalanceController).GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            Assert.That(property, Is.Not.Null, $"Could not find BalanceController debug property '{propertyName}'.");
            return (float)property.GetValue(_balance);
        }
    }
}