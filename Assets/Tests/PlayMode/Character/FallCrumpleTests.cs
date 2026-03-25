using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using PhysicsDrivenMovement.Character;
using PhysicsDrivenMovement.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// PlayMode regression tests for surrender crumple behavior on the real PlayerRagdoll prefab.
    /// </summary>
    public class FallCrumpleTests
    {
        private const string PlayerRagdollPrefabPath = "Assets/Prefabs/PlayerRagdoll_Skinned.prefab";
        private const int SettleFrames = 20;
        private const int BuildSpeedFrames = 30;
        private const int RampSampleStrideFrames = 5;
        private const int RampSampleWindowFrames = 30;
        private const int FaceplantSampleFrames = 15;

        private static readonly Vector3 TestOrigin = new Vector3(1400f, 0f, 1400f);

        private GameObject _instance;
        private GameObject _ground;
        private GameObject _gameSettingsObject;
        private Transform _hipsTransform;
        private Rigidbody _hipsRb;
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
        public IEnumerator Fall_JointStiffnessRampsMonotonicallyOnSurrender()
        {
            // Arrange
            yield return SpawnCharacter(TestOrigin);
            float previousSample = _balanceController.UprightStrengthScale;
            List<float> samples = new List<float>();

            // Act
            _balanceController.TriggerSurrender(0.5f);

            int sampleCount = RampSampleWindowFrames / RampSampleStrideFrames;
            for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                yield return WaitPhysicsFrames(RampSampleStrideFrames);
                float currentSample = _balanceController.UprightStrengthScale;
                samples.Add(currentSample);

                // Assert
                Assert.That(currentSample, Is.LessThanOrEqualTo(previousSample + 0.0001f),
                    $"UprightStrengthScale increased from {previousSample:F3} to {currentSample:F3} at sample {sampleIndex} - not a smooth ramp");

                previousSample = currentSample;
            }

            Assert.That(samples[samples.Count - 1], Is.LessThan(0.1f),
                $"UprightStrengthScale never reached near-zero after surrender ramp. Final: {samples[samples.Count - 1]:F3}");
        }

        [UnityTest]
        public IEnumerator Fall_DoesNotSnapToFaceplantAngle()
        {
            // Arrange
            yield return SpawnCharacter(TestOrigin);
            _playerMovement.SetMoveInputForTest(new Vector2(0f, 1f));
            yield return WaitPhysicsFrames(BuildSpeedFrames);

            // Act
            _balanceController.TriggerSurrender(0.5f);
            float previousAngle = ComputeHipsUprightAngle();

            for (int frame = 1; frame <= FaceplantSampleFrames; frame++)
            {
                yield return new WaitForFixedUpdate();
                float currentAngle = ComputeHipsUprightAngle();
                float delta = Mathf.Abs(currentAngle - previousAngle);

                // Assert
                Assert.That(delta, Is.LessThanOrEqualTo(20f),
                    $"Hips snapped {delta:F1} deg in one frame at frame {frame} - crumple ramp not working");

                previousAngle = currentAngle;
            }
        }

        private IEnumerator SpawnCharacter(Vector3 origin)
        {
            _gameSettingsObject = new GameObject("FallCrumpleTests_GameSettings");
            _gameSettingsObject.AddComponent<GameSettings>();

            Physics.IgnoreLayerCollision(GameSettings.LayerPlayer1Parts, GameSettings.LayerEnvironment, false);
            Physics.IgnoreLayerCollision(GameSettings.LayerLowerLegParts, GameSettings.LayerEnvironment, true);

            _ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ground.name = "FallCrumpleTests_Ground";
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

        private float ComputeHipsUprightAngle()
        {
            float dot = Mathf.Clamp(Vector3.Dot(_hipsTransform.up, Vector3.up), -1f, 1f);
            return Mathf.Acos(dot) * Mathf.Rad2Deg;
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