using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using PhysicsDrivenMovement.Character;
using PhysicsDrivenMovement.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    public class IdleVerticalBobTests
    {
        private const string PlayerRagdollPrefabPath = "Assets/Prefabs/PlayerRagdoll_Skinned.prefab";
        private const int SettleFrames = 80;
        private const int EstablishIdleFrames = 400;
        private const int IdleMeasurementFrames = 200;
        private const int IdleMeasurementInterval = 10;
        private const int DisabledMeasurementFrames = 100;
        private const int MicroStepObservationFrames = 600;

        private static readonly Vector3 TestOrigin = new Vector3(2000f, 0f, 0f);

        private GameObject _ground;
        private GameObject _player;
        private GameObject _gameSettingsObject;
        private Rigidbody _hipsBody;
        private BalanceController _balance;
        private PlayerMovement _movement;
        private CharacterState _characterState;
        private LegAnimator _legAnimator;
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
        public IEnumerator IdleBob_WhenStandingStill_ProducesVerticalOscillation()
        {
            // Arrange
            yield return PrepareBaseline();
            _legAnimator.SetOrganicVariationSeedForTest(42);
            SetPrivateField(_legAnimator, "_disableOrganicVariation", false);

            // Act
            yield return WaitForPhysicsFrames(EstablishIdleFrames, assertNotFallen: true);

            List<float> samples = null;
            yield return CollectVerticalSamples(
                totalFrames: IdleMeasurementFrames,
                sampleEveryNthFrame: IdleMeasurementInterval,
                complete: values => samples = values);

            // Assert
            float stdDev = StandardDeviation(samples);
            float peakToPeak = samples.Max() - samples.Min();
            Assert.That(stdDev, Is.GreaterThan(0.001f), "Idle bob should create measurable vertical hips oscillation while standing still.");
            Assert.That(peakToPeak, Is.LessThan(0.02f), "Idle bob should stay subtle and never drift into a large vertical excursion.");
            Assert.That(_characterState.CurrentState, Is.Not.EqualTo(CharacterStateType.Fallen));
        }

        [UnityTest]
        public IEnumerator IdleBob_WhenDisableOrganicVariation_NoVerticalDrift()
        {
            // Arrange
            yield return PrepareBaseline();
            _legAnimator.SetOrganicVariationSeedForTest(42);
            SetPrivateField(_legAnimator, "_disableOrganicVariation", true);

            // Act
            yield return WaitForPhysicsFrames(EstablishIdleFrames, assertNotFallen: true);

            List<float> samples = null;
            yield return CollectVerticalSamples(
                totalFrames: DisabledMeasurementFrames,
                sampleEveryNthFrame: IdleMeasurementInterval,
                complete: values => samples = values);

            // Assert
            float stdDev = StandardDeviation(samples);
            Assert.That(stdDev, Is.LessThan(0.0005f), "Disabling organic variation should keep the idle bob offset at zero.");
            Assert.That(_characterState.CurrentState, Is.Not.EqualTo(CharacterStateType.Fallen));
        }

        [UnityTest]
        public IEnumerator MicroStep_WhenIdleLong_FiresAtLeastOnce()
        {
            // Arrange
            yield return PrepareBaseline();
            _legAnimator.SetOrganicVariationSeedForTest(42);
            SetPrivateField(_legAnimator, "_disableOrganicVariation", false);
            SetPrivateField(_legAnimator, "_microStepIdleDelay", 1.0f);
            SetPrivateField(_legAnimator, "_microStepDriftThreshold", 0.001f);

            // Act
            yield return WaitForPhysicsFrames(MicroStepObservationFrames, assertNotFallen: true);

            // Assert
            Assert.That(_legAnimator.MicroStepCount, Is.GreaterThanOrEqualTo(1), "Idle sway drift should eventually request at least one corrective micro-step.");
            Assert.That(_characterState.CurrentState, Is.Not.EqualTo(CharacterStateType.Fallen));
        }

        private void CreateRig()
        {
            _gameSettingsObject = new GameObject("IdleVerticalBobTests_GameSettings");
            _gameSettingsObject.AddComponent<GameSettings>();

            _ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ground.name = "IdleVerticalBobTests_Ground";
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
            _legAnimator = _player.GetComponent<LegAnimator>();

            Assert.That(_hipsBody, Is.Not.Null);
            Assert.That(_balance, Is.Not.Null);
            Assert.That(_movement, Is.Not.Null);
            Assert.That(_characterState, Is.Not.Null);
            Assert.That(_legAnimator, Is.Not.Null);

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
            _balance.SetGroundStateForTest(isGrounded: true, isFallen: false);
            _characterState.SetStateForTest(CharacterStateType.Standing);
            _hipsBody.linearVelocity = Vector3.zero;
            _hipsBody.angularVelocity = Vector3.zero;
            yield return WaitForPhysicsFrames(SettleFrames, assertNotFallen: true);
        }

        private IEnumerator WaitForPhysicsFrames(int frames, bool assertNotFallen)
        {
            for (int frame = 0; frame < frames; frame++)
            {
                yield return new WaitForFixedUpdate();

                if (assertNotFallen)
                {
                    Assert.That(
                        _characterState.CurrentState,
                        Is.Not.EqualTo(CharacterStateType.Fallen),
                        $"Character fell during idle vertical bob verification at frame {frame + 1}.");
                }
            }
        }

        private IEnumerator CollectVerticalSamples(int totalFrames, int sampleEveryNthFrame, Action<List<float>> complete)
        {
            var samples = new List<float>(Mathf.Max(1, totalFrames / Mathf.Max(1, sampleEveryNthFrame)));

            for (int frame = 1; frame <= totalFrames; frame++)
            {
                yield return new WaitForFixedUpdate();

                Assert.That(
                    _characterState.CurrentState,
                    Is.Not.EqualTo(CharacterStateType.Fallen),
                    $"Character fell while collecting idle bob samples at frame {frame}.");

                if (frame % sampleEveryNthFrame == 0)
                {
                    samples.Add(_hipsBody.position.y);
                }
            }

            complete(samples);
        }

        private static void SetPrivateField(object instance, string fieldName, object value)
        {
            FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Could not find private field '{fieldName}' on {instance.GetType().Name}.");
            field.SetValue(instance, value);
        }

        private static float StandardDeviation(IReadOnlyList<float> samples)
        {
            Assert.That(samples, Is.Not.Null);
            Assert.That(samples.Count, Is.GreaterThan(1), "At least two samples are required to compute standard deviation.");

            float mean = samples.Average();
            float variance = 0f;
            for (int i = 0; i < samples.Count; i++)
            {
                float delta = samples[i] - mean;
                variance += delta * delta;
            }

            variance /= samples.Count;
            return Mathf.Sqrt(variance);
        }
    }
}