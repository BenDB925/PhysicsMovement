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
    public class ArmSwingVariationTests
    {
        private const string PlayerRagdollPrefabPath = "Assets/Prefabs/PlayerRagdoll_Skinned.prefab";
        private const int SettleFrames = 80;
        private const int WarmUpFramesForPeakSampling = 100;
        private const int PeakWindowCount = 10;
        private const int PeakWindowFrames = 50;
        private const int SymmetryWarmUpFrames = 200;
        private const int SymmetrySampleFrames = 200;
        private const int BoundSampleFrames = 800;
        private const float WalkInputMagnitude = 0.4f;
        private const float PeakDifferenceToleranceDeg = 0.5f;
        private const float SwingClampEpsilonDeg = 0.001f;

        private static readonly Vector3 TestOrigin = new Vector3(3000f, 0f, 0f);

        private GameObject _ground;
        private GameObject _player;
        private GameObject _gameSettingsObject;
        private Rigidbody _hipsBody;
        private BalanceController _balance;
        private PlayerMovement _movement;
        private CharacterState _characterState;
        private LegAnimator _legAnimator;
        private ArmAnimator _armAnimator;
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
        public IEnumerator ArmSwing_WhenWalking_LeftAndRightAmplitudesDiffer()
        {
            // Arrange
            yield return PrepareBaseline();
            SetPrivateField(_legAnimator, "_disableOrganicVariation", false);
            SetPrivateField(_armAnimator, "_armAmplitudeVariation", 0.12f);
            SetPrivateField(_armAnimator, "_leftArmPhaseOffset", 0f);

            BeginWalking();
            yield return WaitForPhysicsFrames(WarmUpFramesForPeakSampling, assertNotFallen: true);

            List<float> leftPeaks = null;
            List<float> rightPeaks = null;

            // Act
            yield return CollectPeakSwingSamples(
                windowCount: PeakWindowCount,
                framesPerWindow: PeakWindowFrames,
                complete: (leftValues, rightValues) =>
                {
                    leftPeaks = leftValues;
                    rightPeaks = rightValues;
                });

            // Assert
            Assert.That(leftPeaks, Has.Count.EqualTo(PeakWindowCount));
            Assert.That(rightPeaks, Has.Count.EqualTo(PeakWindowCount));
            Assert.That(
                leftPeaks.Zip(rightPeaks, (leftPeak, rightPeak) => Mathf.Abs(leftPeak - rightPeak))
                    .Any(delta => delta > PeakDifferenceToleranceDeg),
                Is.True,
                "Per-stride arm amplitude variation should cause at least one sampled stride window to differ between left and right arms.");
            Assert.That(leftPeaks.All(value => value >= 0f && value <= 60f + SwingClampEpsilonDeg), Is.True,
                "Left arm peak swing should stay within the hard 60 degree cap.");
            Assert.That(rightPeaks.All(value => value >= 0f && value <= 60f + SwingClampEpsilonDeg), Is.True,
                "Right arm peak swing should stay within the hard 60 degree cap.");
            Assert.That(_characterState.CurrentState, Is.Not.EqualTo(CharacterStateType.Fallen));
        }

        [UnityTest]
        public IEnumerator ArmSwing_WhenDisableOrganicVariation_AmplitudesAreSymmetric()
        {
            // Arrange
            yield return PrepareBaseline();
            SetPrivateField(_legAnimator, "_disableOrganicVariation", true);
            SetPrivateField(_armAnimator, "_armAmplitudeVariation", 0.12f);
            SetPrivateField(_armAnimator, "_leftArmPhaseOffset", 0.05f);

            BeginWalking();
            yield return WaitForPhysicsFrames(SymmetryWarmUpFrames, assertNotFallen: true);

            List<float> leftSamples = null;
            List<float> rightSamples = null;

            // Act
            yield return CollectSwingSamples(
                frames: SymmetrySampleFrames,
                complete: (leftValues, rightValues) =>
                {
                    leftSamples = leftValues;
                    rightSamples = rightValues;
                });

            // Assert
            Assert.That(leftSamples, Has.Count.EqualTo(SymmetrySampleFrames));
            Assert.That(rightSamples, Has.Count.EqualTo(SymmetrySampleFrames));

            for (int index = 0; index < leftSamples.Count; index++)
            {
                float symmetryError = Mathf.Abs(leftSamples[index] + rightSamples[index]);
                Assert.That(symmetryError, Is.LessThan(0.5f),
                    $"Organic disable should restore perfectly symmetric arm swing at sample {index + 1}, but the sum magnitude was {symmetryError:F3} degrees.");
            }

            Assert.That(_characterState.CurrentState, Is.Not.EqualTo(CharacterStateType.Fallen));
        }

        [UnityTest]
        public IEnumerator ArmSwing_AmplitudeVariation_StaysWithinBounds()
        {
            // Arrange
            yield return PrepareBaseline();
            SetPrivateField(_legAnimator, "_disableOrganicVariation", false);
            SetPrivateField(_armAnimator, "_armAmplitudeVariation", 0.12f);
            SetPrivateField(_armAnimator, "_leftArmPhaseOffset", 0.05f);

            BeginWalking();

            List<float> leftSamples = null;
            List<float> rightSamples = null;

            // Act
            yield return CollectSwingSamples(
                frames: BoundSampleFrames,
                complete: (leftValues, rightValues) =>
                {
                    leftSamples = leftValues;
                    rightSamples = rightValues;
                });

            // Assert
            Assert.That(leftSamples, Has.Count.EqualTo(BoundSampleFrames));
            Assert.That(rightSamples, Has.Count.EqualTo(BoundSampleFrames));

            foreach (float sample in leftSamples.Concat(rightSamples))
            {
                Assert.That(float.IsNaN(sample), Is.False, "Arm swing debug values must never become NaN.");
                Assert.That(Mathf.Abs(sample), Is.LessThanOrEqualTo(60f + SwingClampEpsilonDeg),
                    "Arm swing amplitude variation must stay within the 60 degree hard cap.");
            }

            Assert.That(_characterState.CurrentState, Is.Not.EqualTo(CharacterStateType.Fallen));
        }

        private void CreateRig()
        {
            _gameSettingsObject = new GameObject("ArmSwingVariationTests_GameSettings");
            _gameSettingsObject.AddComponent<GameSettings>();

            _ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ground.name = "ArmSwingVariationTests_Ground";
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
            _armAnimator = _player.GetComponent<ArmAnimator>();

            Assert.That(_hipsBody, Is.Not.Null);
            Assert.That(_balance, Is.Not.Null);
            Assert.That(_movement, Is.Not.Null);
            Assert.That(_characterState, Is.Not.Null);
            Assert.That(_legAnimator, Is.Not.Null);
            Assert.That(_armAnimator, Is.Not.Null);

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

        private void BeginWalking()
        {
            _movement.SetSprintInputForTest(false);
            _movement.SetJumpInputForTest(false);
            _movement.SetMoveInputForTest(new Vector2(0f, WalkInputMagnitude));
        }

        private IEnumerator WaitForPhysicsFrames(int frames, bool assertNotFallen)
        {
            for (int frame = 0; frame < frames; frame++)
            {
                yield return new WaitForFixedUpdate();

                if (assertNotFallen)
                {
                    AssertCharacterNotFallen(frame + 1, "while waiting for the arm swing test state to settle");
                }
            }
        }

        private IEnumerator CollectPeakSwingSamples(int windowCount, int framesPerWindow, Action<List<float>, List<float>> complete)
        {
            var leftPeaks = new List<float>(windowCount);
            var rightPeaks = new List<float>(windowCount);

            for (int windowIndex = 0; windowIndex < windowCount; windowIndex++)
            {
                float leftPeak = 0f;
                float rightPeak = 0f;

                for (int frame = 0; frame < framesPerWindow; frame++)
                {
                    yield return new WaitForFixedUpdate();

                    AssertCharacterNotFallen(frame + 1, "while collecting arm swing peak samples");

                    leftPeak = Mathf.Max(leftPeak, Mathf.Abs(ReadSwingValue("DebugLeftSwingDeg")));
                    rightPeak = Mathf.Max(rightPeak, Mathf.Abs(ReadSwingValue("DebugRightSwingDeg")));
                }

                leftPeaks.Add(leftPeak);
                rightPeaks.Add(rightPeak);
            }

            complete(leftPeaks, rightPeaks);
        }

        private IEnumerator CollectSwingSamples(int frames, Action<List<float>, List<float>> complete)
        {
            var leftSamples = new List<float>(frames);
            var rightSamples = new List<float>(frames);

            for (int frame = 0; frame < frames; frame++)
            {
                yield return new WaitForFixedUpdate();

                AssertCharacterNotFallen(frame + 1, "while collecting arm swing samples");

                leftSamples.Add(ReadSwingValue("DebugLeftSwingDeg"));
                rightSamples.Add(ReadSwingValue("DebugRightSwingDeg"));
            }

            complete(leftSamples, rightSamples);
        }

        private void AssertCharacterNotFallen(int frame, string context)
        {
            Assert.That(
                _characterState.CurrentState,
                Is.Not.EqualTo(CharacterStateType.Fallen),
                $"Character fell at frame {frame} {context}.");
        }

        private float ReadSwingValue(string propertyName)
        {
            PropertyInfo property = typeof(ArmAnimator).GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            Assert.That(property, Is.Not.Null, $"Could not find ArmAnimator swing debug property '{propertyName}'.");
            return (float)property.GetValue(_armAnimator);
        }

        private static void SetPrivateField(object instance, string fieldName, object value)
        {
            FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Could not find private field '{fieldName}' on {instance.GetType().Name}.");
            field.SetValue(instance, value);
        }
    }
}