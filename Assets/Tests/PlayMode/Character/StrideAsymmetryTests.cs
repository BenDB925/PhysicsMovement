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
    public class StrideAsymmetryTests
    {
        private const string PlayerRagdollPrefabPath = "Assets/Prefabs/PlayerRagdoll_Skinned.prefab";
        private const int SettleFrames = 80;
        private const int StepSampleCountPerLeg = 12;
        private const int CollectionFrameBudget = 2400;
        private const int StabilityFrameBudget = 500;

        private static readonly Vector3 TestOrigin = new Vector3(2000f, 0f, 0f);

        private GameObject _ground;
        private GameObject _player;
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

            Time.fixedDeltaTime = _savedFixedDeltaTime;
            Physics.defaultSolverIterations = _savedSolverIterations;
            Physics.defaultSolverVelocityIterations = _savedSolverVelocityIterations;
        }

        [UnityTest]
        public IEnumerator StrideAsymmetry_LeftStride_FractionallyLongerThanRight()
        {
            yield return PrepareBaseline();

            _legAnimator.SetOrganicVariationSeedForTest(42);
            SetPrivateField(_legAnimator, "_disableOrganicVariation", false);
            SetPrivateField(_legAnimator, "_leftStrideAsymmetry", 1.04f);
            SetPrivateField(_legAnimator, "_rightStrideAsymmetry", 1.0f);

            (List<float> leftAngles, List<float> rightAngles) samples = default;
            yield return CollectPerLegStepAngles(0.5f, samplesOut => samples = samplesOut);

            float meanLeft = samples.leftAngles.Average();
            float meanRight = samples.rightAngles.Average();
            float ratio = meanLeft / meanRight;
            float sprintAtEnd = ReadInternalFloatProperty(_legAnimator, "CurrentSprintNormalizedForTest");

            Assert.That(samples.leftAngles, Has.Count.EqualTo(StepSampleCountPerLeg));
            Assert.That(samples.rightAngles, Has.Count.EqualTo(StepSampleCountPerLeg));
            Assert.That(ratio, Is.InRange(1.02f, 1.06f),
                $"Expected mean left/right stride angle ratio to stay near the 1.04 asymmetry multiplier, but got {ratio:F3} (left={meanLeft:F2}, right={meanRight:F2}, sprintNorm={sprintAtEnd:F3}).");
        }

        [UnityTest]
        public IEnumerator StrideAsymmetry_WhenDisabled_SymmetricStrides()
        {
            yield return PrepareBaseline();

            _legAnimator.SetOrganicVariationSeedForTest(42);
            SetPrivateField(_legAnimator, "_disableOrganicVariation", true);
            SetPrivateField(_legAnimator, "_leftStrideAsymmetry", 1.04f);
            SetPrivateField(_legAnimator, "_rightStrideAsymmetry", 1.0f);

            (List<float> leftAngles, List<float> rightAngles) samples = default;
            yield return CollectPerLegStepAngles(0.5f, samplesOut => samples = samplesOut);

            float meanLeft = samples.leftAngles.Average();
            float meanRight = samples.rightAngles.Average();

            Assert.That(Mathf.Abs(meanLeft - meanRight), Is.LessThan(0.5f),
                $"Disabling organic variation should neutralise stride asymmetry. Left={meanLeft:F2}, right={meanRight:F2}.");
        }

        [UnityTest]
        public IEnumerator StrideAsymmetry_DoesNotDestabilise_WalkStraight()
        {
            yield return PrepareBaseline();

            _legAnimator.SetOrganicVariationSeedForTest(42);
            SetPrivateField(_legAnimator, "_disableOrganicVariation", false);
            SetPrivateField(_legAnimator, "_leftStrideAsymmetry", 1.04f);
            SetPrivateField(_legAnimator, "_rightStrideAsymmetry", 1.0f);

            _movement.SetSprintInputForTest(false);
            _movement.SetMoveInputForTest(Vector2.up * 0.5f);

            for (int frame = 0; frame < StabilityFrameBudget; frame++)
            {
                yield return new WaitForFixedUpdate();
                Assert.That(_balance.IsFallen, Is.False,
                    $"Character should remain stable while walking straight with stride asymmetry enabled. Fell at frame {frame + 1}.");
            }

            _movement.SetMoveInputForTest(Vector2.zero);
        }

        private void CreateRig()
        {
            _ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ground.name = "StrideAsymmetryTests_Ground";
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
        }

        private IEnumerator PrepareBaseline()
        {
            _movement.SetMoveInputForTest(Vector2.zero);
            _movement.SetSprintInputForTest(false);
            _balance.SetGroundStateForTest(isGrounded: true, isFallen: false);
            _characterState.SetStateForTest(CharacterStateType.Standing);
            _hipsBody.linearVelocity = Vector3.zero;
            _hipsBody.angularVelocity = Vector3.zero;
            yield return WaitForPhysicsFrames(SettleFrames);
        }

        private IEnumerator CollectPerLegStepAngles(float moveMagnitude, Action<(List<float> leftAngles, List<float> rightAngles)> complete)
        {
            var leftAngles = new List<float>(StepSampleCountPerLeg);
            var rightAngles = new List<float>(StepSampleCountPerLeg);
            bool previousLeftValid = false;
            bool previousRightValid = false;

            _movement.SetSprintInputForTest(false);
            _movement.SetMoveInputForTest(new Vector2(0f, moveMagnitude));

            for (int frame = 0; frame < CollectionFrameBudget && (leftAngles.Count < StepSampleCountPerLeg || rightAngles.Count < StepSampleCountPerLeg); frame++)
            {
                yield return new WaitForFixedUpdate();

                bool leftValid = TryReadStepTargetValidity(_legAnimator, "_leftLegCommand");
                bool rightValid = TryReadStepTargetValidity(_legAnimator, "_rightLegCommand");

                if (leftValid && !previousLeftValid && leftAngles.Count < StepSampleCountPerLeg)
                {
                    leftAngles.Add(ReadInternalFloatProperty(_legAnimator, "LeftStepAngleDegrees"));
                }

                if (rightValid && !previousRightValid && rightAngles.Count < StepSampleCountPerLeg)
                {
                    rightAngles.Add(ReadInternalFloatProperty(_legAnimator, "RightStepAngleDegrees"));
                }

                previousLeftValid = leftValid;
                previousRightValid = rightValid;
            }

            _movement.SetMoveInputForTest(Vector2.zero);
            complete((leftAngles, rightAngles));

            Assert.That(leftAngles, Has.Count.EqualTo(StepSampleCountPerLeg),
                $"Expected to capture {StepSampleCountPerLeg} left-leg step triggers within {CollectionFrameBudget} frames, but got {leftAngles.Count}.");
            Assert.That(rightAngles, Has.Count.EqualTo(StepSampleCountPerLeg),
                $"Expected to capture {StepSampleCountPerLeg} right-leg step triggers within {CollectionFrameBudget} frames, but got {rightAngles.Count}.");
        }

        private static bool TryReadStepTargetValidity(LegAnimator legAnimator, string commandFieldName)
        {
            object command = GetPrivateField(legAnimator, commandFieldName);
            Assert.That(command, Is.Not.Null, $"LegAnimator command field '{commandFieldName}' must exist.");

            PropertyInfo stepTargetProperty = command.GetType().GetProperty("StepTarget", BindingFlags.Instance | BindingFlags.Public);
            Assert.That(stepTargetProperty, Is.Not.Null, $"{command.GetType().Name} must expose a StepTarget property.");

            object stepTarget = stepTargetProperty.GetValue(command);
            Assert.That(stepTarget, Is.Not.Null, "StepTarget value should never be null.");

            PropertyInfo isValidProperty = stepTarget.GetType().GetProperty("IsValid", BindingFlags.Instance | BindingFlags.Public);
            Assert.That(isValidProperty, Is.Not.Null, $"{stepTarget.GetType().Name} must expose an IsValid property.");

            return (bool)isValidProperty.GetValue(stepTarget);
        }

        private static float ReadInternalFloatProperty(object instance, string propertyName)
        {
            PropertyInfo property = instance.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(property, Is.Not.Null, $"Could not find property '{propertyName}' on {instance.GetType().Name}.");
            return (float)property.GetValue(instance);
        }

        private static object GetPrivateField(object instance, string fieldName)
        {
            FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Could not find private field '{fieldName}' on {instance.GetType().Name}.");
            return field.GetValue(instance);
        }

        private static void SetPrivateField(object instance, string fieldName, object value)
        {
            FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Could not find private field '{fieldName}' on {instance.GetType().Name}.");
            field.SetValue(instance, value);
        }

        private static IEnumerator WaitForPhysicsFrames(int count)
        {
            for (int i = 0; i < count; i++)
            {
                yield return new WaitForFixedUpdate();
            }
        }
    }
}
