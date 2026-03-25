using System.Collections;
using NUnit.Framework;
using PhysicsDrivenMovement.Character;
using PhysicsDrivenMovement.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// PlayMode regression tests for the completed get-up behavior on the real PlayerRagdoll prefab.
    /// </summary>
    public class GetUpTests
    {
        private const string PlayerRagdollPrefabPath = "Assets/Prefabs/PlayerRagdoll_Skinned.prefab";
        private const int SettleFrames = 20;
        private const int GroundSettleFrames = 5;
        private const int FallenTimeoutFrames = 60;
        private const int StandingTimeoutFrames = 800;
        private const int MaxLaunchSampleFrames = 600;
        private const int GettingUpTimeoutFrames = 300;
        private const int ReEnterFallenTimeoutFrames = 60;

        private static readonly Vector3 TestOrigin = new Vector3(1200f, 0f, 1200f);

        private GameObject _instance;
        private GameObject _ground;
        private GameObject _gameSettingsObject;
        private Transform _hipsTransform;
        private Rigidbody _hipsRb;
        private Rigidbody _torsoBody;
        private BalanceController _balanceController;
        private CharacterState _characterState;
        private ProceduralStandUp _proceduralStandUp;
        private PlayerMovement _playerMovement;
        private float _savedFixedDeltaTime;
        private int _savedSolverIterations;
        private int _savedSolverVelocityIterations;
        private bool[,] _savedLayerCollisionMatrix;

        [SetUp]
        public void SetUp()
        {
            PlayModeSceneIsolation.ResetToEmptyScene();

            _savedFixedDeltaTime = Time.fixedDeltaTime;
            _savedSolverIterations = Physics.defaultSolverIterations;
            _savedSolverVelocityIterations = Physics.defaultSolverVelocityIterations;
            _savedLayerCollisionMatrix = CaptureLayerCollisionMatrix();

            Time.fixedDeltaTime = 0.01f;
            Physics.defaultSolverIterations = 12;
            Physics.defaultSolverVelocityIterations = 4;
        }

        [TearDown]
        public void TearDown()
        {
            if (_instance != null)
            {
                Object.Destroy(_instance);
            }

            if (_ground != null)
            {
                Object.Destroy(_ground);
            }

            if (_gameSettingsObject != null)
            {
                Object.Destroy(_gameSettingsObject);
            }

            PlayModeSceneIsolation.ResetToEmptyScene();

            Time.fixedDeltaTime = _savedFixedDeltaTime;
            Physics.defaultSolverIterations = _savedSolverIterations;
            Physics.defaultSolverVelocityIterations = _savedSolverVelocityIterations;
            RestoreLayerCollisionMatrix(_savedLayerCollisionMatrix);
        }

        [UnityTest]
        public IEnumerator GetUp_FromFaceDown_ReachesStanding_WithinTimeout()
        {
            // Arrange
            yield return SpawnCharacter(TestOrigin);
            yield return WaitPhysicsFrames(GroundSettleFrames);

            // Act
            _balanceController.TriggerSurrender(0.5f);
            ApplyDestabilizingImpulse(_hipsTransform.forward, 600f);

            bool reachedFallen = false;
            for (int frame = 0; frame < FallenTimeoutFrames; frame++)
            {
                yield return new WaitForFixedUpdate();
                if (_characterState.CurrentState == CharacterStateType.Fallen)
                {
                    reachedFallen = true;
                    break;
                }
            }

            // Assert
            Assert.That(reachedFallen, Is.True,
                $"Character did not reach Fallen after surrender. Last state: {_characterState.CurrentState} after {FallenTimeoutFrames} frames");

            bool reachedStanding = false;
            for (int frame = 0; frame < StandingTimeoutFrames; frame++)
            {
                yield return new WaitForFixedUpdate();
                CharacterStateType currentState = _characterState.CurrentState;
                if (currentState == CharacterStateType.Standing || currentState == CharacterStateType.Moving)
                {
                    reachedStanding = true;
                    break;
                }
            }

            Assert.That(reachedStanding, Is.True,
                $"Character did not reach Standing after get-up. Last state: {_characterState.CurrentState} after {StandingTimeoutFrames} frames");
        }

        [UnityTest]
        public IEnumerator GetUp_NeverExceedsMaxLaunchHeight()
        {
            // Arrange
            yield return SpawnCharacter(TestOrigin);

            // Act
            _balanceController.TriggerSurrender(0.8f);

            float maxHipsY = _hipsRb.position.y;
            for (int frame = 0; frame < MaxLaunchSampleFrames; frame++)
            {
                yield return new WaitForFixedUpdate();
                maxHipsY = Mathf.Max(maxHipsY, _hipsRb.position.y);
            }

            float standingHipsHeight = _balanceController.StandingHipsHeight;

            // Assert
            Assert.That(maxHipsY, Is.LessThan(standingHipsHeight * 2.5f),
                $"Hips reached {maxHipsY:F2}m during get-up - exceeds launch threshold {standingHipsHeight * 2.5f:F2}m");
        }

        [UnityTest]
        public IEnumerator GetUp_ReKnockdownDuringStandUp_ReEntersFallen()
        {
            // Arrange
            yield return SpawnCharacter(TestOrigin);

            // Act
            _balanceController.TriggerSurrender(0.5f);
            ApplyDestabilizingImpulse(Vector3.left, 600f);

            bool reachedGettingUp = false;
            for (int frame = 0; frame < GettingUpTimeoutFrames; frame++)
            {
                yield return new WaitForFixedUpdate();
                if (_characterState.CurrentState == CharacterStateType.GettingUp)
                {
                    reachedGettingUp = true;
                    break;
                }
            }

            // Assert
            Assert.That(reachedGettingUp, Is.True,
                $"Character did not enter GettingUp. State: {_characterState.CurrentState}");

            _balanceController.TriggerImpactKnockdownForTest(Vector3.back, 200f);

            bool reEnteredFallen = false;
            for (int frame = 0; frame < ReEnterFallenTimeoutFrames; frame++)
            {
                yield return new WaitForFixedUpdate();
                if (_characterState.CurrentState == CharacterStateType.Fallen)
                {
                    reEnteredFallen = true;
                    break;
                }
            }

            Assert.That(reEnteredFallen, Is.True,
                $"Character did not re-enter Fallen after mid-get-up knockdown. State: {_characterState.CurrentState}");
        }

        private IEnumerator SpawnCharacter(Vector3 origin)
        {
            _gameSettingsObject = new GameObject("GetUpTests_GameSettings");
            _gameSettingsObject.AddComponent<GameSettings>();

            Physics.IgnoreLayerCollision(GameSettings.LayerPlayer1Parts, GameSettings.LayerEnvironment, false);
            Physics.IgnoreLayerCollision(GameSettings.LayerLowerLegParts, GameSettings.LayerEnvironment, true);

            _ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ground.name = "GetUpTests_Ground";
            _ground.transform.position = origin + new Vector3(0f, -0.5f, 0f);
            _ground.transform.localScale = new Vector3(400f, 1f, 400f);
            _ground.layer = GameSettings.LayerEnvironment;

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerRagdollPrefabPath);
            Assert.That(prefab, Is.Not.Null,
                $"PlayerRagdoll prefab was not found at '{PlayerRagdollPrefabPath}'.");

            _instance = Object.Instantiate(prefab, origin + new Vector3(0f, 0.5f, 0f), Quaternion.identity);
            Assert.That(_instance, Is.Not.Null, "Failed to instantiate PlayerRagdoll prefab.");

            _hipsTransform = FindHipsTransform(_instance.transform);
            _hipsRb = _hipsTransform.GetComponent<Rigidbody>();
            _torsoBody = FindChildRigidbody(_hipsTransform, "Torso");
            _balanceController = _hipsTransform.GetComponent<BalanceController>();
            _characterState = _hipsTransform.GetComponent<CharacterState>();
            _proceduralStandUp = _hipsTransform.GetComponent<ProceduralStandUp>();
            _playerMovement = _hipsTransform.GetComponent<PlayerMovement>();

            Assert.That(_hipsRb, Is.Not.Null, "PlayerRagdoll prefab is missing the hips Rigidbody.");
            Assert.That(_balanceController, Is.Not.Null, "PlayerRagdoll prefab is missing BalanceController on Hips.");
            Assert.That(_characterState, Is.Not.Null, "PlayerRagdoll prefab is missing CharacterState on Hips.");
            Assert.That(_proceduralStandUp, Is.Not.Null, "PlayerRagdoll prefab is missing ProceduralStandUp on Hips.");
            Assert.That(_playerMovement, Is.Not.Null, "PlayerRagdoll prefab is missing PlayerMovement on Hips.");

            _playerMovement.SetMoveInputForTest(Vector2.zero);
            yield return WaitPhysicsFrames(SettleFrames);
        }

        private void ApplyDestabilizingImpulse(Vector3 direction, float force)
        {
            Rigidbody targetBody = _torsoBody != null ? _torsoBody : _hipsRb;
            Vector3 forcePoint = targetBody.worldCenterOfMass + Vector3.up * 0.1f;
            targetBody.AddForceAtPosition(direction.normalized * force, forcePoint, ForceMode.Impulse);
        }

        private static Transform FindHipsTransform(Transform root)
        {
            if (root.GetComponent<BalanceController>() != null)
            {
                return root;
            }

            if (root.name == "Hips")
            {
                return root;
            }

            Transform[] transforms = root.GetComponentsInChildren<Transform>(includeInactive: true);
            for (int index = 0; index < transforms.Length; index++)
            {
                if (transforms[index].name == "Hips")
                {
                    return transforms[index];
                }
            }

            Assert.Fail("PlayerRagdoll prefab is missing the Hips transform.");
            return null;
        }

        private static Rigidbody FindChildRigidbody(Transform root, string childName)
        {
            Transform[] transforms = root.GetComponentsInChildren<Transform>(includeInactive: true);
            for (int index = 0; index < transforms.Length; index++)
            {
                if (transforms[index].name == childName)
                {
                    return transforms[index].GetComponent<Rigidbody>();
                }
            }

            return null;
        }

        private static IEnumerator WaitPhysicsFrames(int frameCount)
        {
            for (int frame = 0; frame < frameCount; frame++)
            {
                yield return new WaitForFixedUpdate();
            }
        }

        private static bool[,] CaptureLayerCollisionMatrix()
        {
            bool[,] matrix = new bool[32, 32];
            for (int a = 0; a < 32; a++)
            {
                for (int b = 0; b < 32; b++)
                {
                    matrix[a, b] = Physics.GetIgnoreLayerCollision(a, b);
                }
            }

            return matrix;
        }

        private static void RestoreLayerCollisionMatrix(bool[,] matrix)
        {
            if (matrix == null || matrix.GetLength(0) != 32 || matrix.GetLength(1) != 32)
            {
                return;
            }

            for (int a = 0; a < 32; a++)
            {
                for (int b = 0; b < 32; b++)
                {
                    Physics.IgnoreLayerCollision(a, b, matrix[a, b]);
                }
            }
        }
    }
}