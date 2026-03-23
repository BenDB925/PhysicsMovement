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
    public class OrganicGaitVariationTests
    {
        private const string PlayerRagdollPrefabPath = "Assets/Prefabs/PlayerRagdoll_Skinned.prefab";
        private const int SettleFrames = 80;
        private const int StepSampleCount = 12;
        private const int CollectionFrameBudget = 2200;

        private static readonly Vector3 TestOrigin = new Vector3(1850f, 0f, 0f);

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


        private void CreateRig()
        {
            _ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ground.name = "OrganicGaitVariationTests_Ground";
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

        private IEnumerator RecreateRig()
        {
            DestroyRig();
            yield return null;
            CreateRig();
        }

        [UnityTest]
        public IEnumerator StepAngleNoise_AtWalkSpeed_ShowsVariationWithinBounds()
        {
            yield return PrepareBaseline();

            const int seed = 42;
            const float moveMagnitude = 0.4f;
            _legAnimator.SetOrganicVariationSeedForTest(seed);
            SetPrivateField(_legAnimator, "_disableOrganicVariation", false);

            List<float> recordedAngles = null;
            yield return CollectStepAngles(moveMagnitude, sprintHeld: false, angles => recordedAngles = angles);

            float nominalWalkAngle = GetPrivateFloat(_legAnimator, "_stepAngle");
            Assert.That(recordedAngles, Has.Count.EqualTo(StepSampleCount));
            Assert.That(StandardDeviation(recordedAngles), Is.GreaterThan(1f), "Walk stride angles should vary once organic noise is enabled.");
            Assert.That(recordedAngles.All(angle => angle >= 30f && angle <= 90f), Is.True, "Every recorded walk stride angle must stay within the safe clamp bounds.");
            Assert.That(recordedAngles.All(angle => Mathf.Abs(angle - nominalWalkAngle) <= 8f + 0.001f), Is.True, "Walk stride noise must stay within the +/-8 degree design cap.");
        }

        [UnityTest]
        public IEnumerator StepAngleNoise_AtSprintSpeed_TighterThanWalk()
        {
            yield return PrepareBaseline();

            const int seed = 42;
            _legAnimator.SetOrganicVariationSeedForTest(seed);
            SetPrivateField(_legAnimator, "_disableOrganicVariation", false);

            // Ramp up to full sprint before collecting -- noise magnitude lerps from
            // +/-8 (walk) to +/-4 (sprint) based on SprintNormalized. Collecting during
            // the ramp-up would include walk-level noise and fail the +/-4 assertion.
            _movement.SetMoveInputForTest(Vector2.up);
            _movement.SetSprintInputForTest(true);
            for (int warmUp = 0; warmUp < 600; warmUp++)
            {
                yield return new WaitForFixedUpdate();
                float sprintNorm = (float)typeof(PlayerMovement)
                    .GetProperty("SprintNormalized", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                    ?.GetValue(_movement);
                if (sprintNorm >= 0.95f) break;
            }
            // Reset RNG after warm-up so seed is at a clean start for collection.
            _legAnimator.SetOrganicVariationSeedForTest(seed);

            List<float> recordedAngles = null;
            yield return CollectStepAngles(1f, sprintHeld: true, angles => recordedAngles = angles);

            float nominalSprintAngle = GetPrivateFloat(_legAnimator, "_sprintStepAngle");
            Assert.That(recordedAngles, Has.Count.EqualTo(StepSampleCount));
            Assert.That(recordedAngles.All(angle => angle >= 30f && angle <= 90f), Is.True, "Every recorded sprint stride angle must stay within the safe clamp bounds.");
            Assert.That(recordedAngles.All(angle => Mathf.Abs(angle - nominalSprintAngle) <= 4f + 0.001f), Is.True, "Sprint stride noise must stay within the tighter +/-4 degree cap.");
        }

        [UnityTest]
        public IEnumerator StepAngleNoise_Deterministic_WithSameSeed()
        {
            yield return PrepareBaseline();

            const int seed = 99;
            SetPrivateField(_legAnimator, "_disableOrganicVariation", false);

            // Run 1: collect angles with known seed.
            (List<float> firstLeftAngles, List<float> firstRightAngles) firstRun = default;
            _legAnimator.SetOrganicVariationSeedForTest(seed);
            yield return CollectPerLegStepAngles(0.4f, sprintHeld: false, result => firstRun = result);

            // Run 2: reset the RNG to the same seed on the SAME rig (same physics state).
            // Using RecreateRig() caused flakiness because the physics settling lands in a
            // slightly different position each time, shifting step trigger ordering by run 2.
            _legAnimator.SetOrganicVariationSeedForTest(seed);
            (List<float> secondLeftAngles, List<float> secondRightAngles) secondRun = default;
            yield return CollectPerLegStepAngles(0.4f, sprintHeld: false, result => secondRun = result);

            Assert.That(firstRun.firstLeftAngles, Has.Count.EqualTo(StepSampleCount / 2));
            Assert.That(firstRun.firstRightAngles, Has.Count.EqualTo(StepSampleCount / 2));
            Assert.That(secondRun.secondLeftAngles, Has.Count.EqualTo(StepSampleCount / 2));
            Assert.That(secondRun.secondRightAngles, Has.Count.EqualTo(StepSampleCount / 2));
            Assert.That(secondRun.secondLeftAngles, Is.EqualTo(firstRun.firstLeftAngles), "Resetting the organic RNG to the same seed should reproduce the exact left-leg step-angle sequence.");
            Assert.That(secondRun.secondRightAngles, Is.EqualTo(firstRun.firstRightAngles), "Resetting the organic RNG to the same seed should reproduce the exact right-leg step-angle sequence.");
        }

        [UnityTest]
        public IEnumerator StepAngleNoise_DisabledByFlag_ProducesIdenticalStrides()
        {
            yield return PrepareBaseline();

            _legAnimator.SetOrganicVariationSeedForTest(42);
            SetPrivateField(_legAnimator, "_disableOrganicVariation", true);

            List<float> recordedAngles = null;
            yield return CollectStepAngles(0.4f, sprintHeld: false, angles => recordedAngles = angles);

            Assert.That(recordedAngles, Has.Count.EqualTo(StepSampleCount));
            Assert.That(StandardDeviation(recordedAngles), Is.LessThan(0.01f), "Disabling organic variation should restore effectively identical stride angles.");
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


        private IEnumerator CollectPerLegStepAngles(float moveMagnitude, bool sprintHeld, Action<(List<float> leftAngles, List<float> rightAngles)> complete)
        {
            var leftAngles = new List<float>(StepSampleCount / 2);
            var rightAngles = new List<float>(StepSampleCount / 2);
            bool previousLeftValid = false;
            bool previousRightValid = false;

            _movement.SetSprintInputForTest(sprintHeld);
            _movement.SetMoveInputForTest(new Vector2(0f, moveMagnitude));

            for (int frame = 0; frame < CollectionFrameBudget && (leftAngles.Count < StepSampleCount / 2 || rightAngles.Count < StepSampleCount / 2); frame++)
            {
                yield return new WaitForFixedUpdate();

                bool leftValid = TryReadStepTargetValidity(_legAnimator, "_leftLegCommand");
                bool rightValid = TryReadStepTargetValidity(_legAnimator, "_rightLegCommand");

                if (leftValid && !previousLeftValid && leftAngles.Count < StepSampleCount / 2)
                {
                    leftAngles.Add(ReadInternalFloatProperty(_legAnimator, "LeftStepAngleDegrees"));
                }

                if (rightValid && !previousRightValid && rightAngles.Count < StepSampleCount / 2)
                {
                    rightAngles.Add(ReadInternalFloatProperty(_legAnimator, "RightStepAngleDegrees"));
                }

                previousLeftValid = leftValid;
                previousRightValid = rightValid;
            }

            _movement.SetMoveInputForTest(Vector2.zero);
            _movement.SetSprintInputForTest(false);
            complete((leftAngles, rightAngles));

            Assert.That(leftAngles, Has.Count.EqualTo(StepSampleCount / 2),
                $"Expected to capture {StepSampleCount / 2} left-leg step triggers within {CollectionFrameBudget} frames, but got {leftAngles.Count}.");
            Assert.That(rightAngles, Has.Count.EqualTo(StepSampleCount / 2),
                $"Expected to capture {StepSampleCount / 2} right-leg step triggers within {CollectionFrameBudget} frames, but got {rightAngles.Count}.");
        }

        private IEnumerator CollectStepAngles(float moveMagnitude, bool sprintHeld, Action<List<float>> complete)
        {
            var recordedAngles = new List<float>(StepSampleCount);
            bool previousLeftValid = false;
            bool previousRightValid = false;

            _movement.SetSprintInputForTest(sprintHeld);
            _movement.SetMoveInputForTest(new Vector2(0f, moveMagnitude));

            for (int frame = 0; frame < CollectionFrameBudget && recordedAngles.Count < StepSampleCount; frame++)
            {
                yield return new WaitForFixedUpdate();

                bool leftValid = TryReadStepTargetValidity(_legAnimator, "_leftLegCommand");
                bool rightValid = TryReadStepTargetValidity(_legAnimator, "_rightLegCommand");

                if (leftValid && !previousLeftValid)
                {
                    recordedAngles.Add(ReadInternalFloatProperty(_legAnimator, "LeftStepAngleDegrees"));
                }

                if (recordedAngles.Count >= StepSampleCount)
                {
                    break;
                }

                if (rightValid && !previousRightValid)
                {
                    recordedAngles.Add(ReadInternalFloatProperty(_legAnimator, "RightStepAngleDegrees"));
                }

                previousLeftValid = leftValid;
                previousRightValid = rightValid;
            }

            _movement.SetMoveInputForTest(Vector2.zero);
            _movement.SetSprintInputForTest(false);
            complete(recordedAngles);

            Assert.That(recordedAngles, Has.Count.EqualTo(StepSampleCount),
                $"Expected to capture {StepSampleCount} step triggers within {CollectionFrameBudget} frames, but got {recordedAngles.Count}.");
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

        private static float GetPrivateFloat(object instance, string fieldName)
        {
            FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Could not find private field '{fieldName}' on {instance.GetType().Name}.");
            return (float)field.GetValue(instance);
        }

        private static void SetPrivateField(object instance, string fieldName, object value)
        {
            FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Could not find private field '{fieldName}' on {instance.GetType().Name}.");
            field.SetValue(instance, value);
        }

        private static float StandardDeviation(IReadOnlyList<float> samples)
        {
            Assert.That(samples, Is.Not.Null.And.Count.GreaterThan(0));
            double mean = samples.Average();
            double variance = samples.Select(sample => Math.Pow(sample - mean, 2d)).Average();
            return (float)Math.Sqrt(variance);
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
