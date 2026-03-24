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
    public class LateralPlacementNoiseTests
    {
        private const string PlayerRagdollPrefabPath = "Assets/Prefabs/PlayerRagdoll_Skinned.prefab";
        private const int SettleFrames = 80;
        private const int StepSampleCount = 10;
        private const int CollectionFrameBudget = 2200;
        private const int StabilityFrameBudget = 500;

        private static readonly Vector3 TestOrigin = new Vector3(1925f, 0f, 0f);

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
        public IEnumerator LateralNoise_AtWalkSpeed_ShowsVariationWithinBounds()
        {
            yield return PrepareBaseline();

            _legAnimator.SetOrganicVariationSeedForTest(42);
            SetPrivateField(_legAnimator, "_disableOrganicVariation", false);

            List<float> noiseSamples = null;
            yield return CollectLeftLateralNoiseSamples(
                moveMagnitude: 0.5f,
                sprintHeld: false,
                requireSprintNormalized: false,
                complete: samples => noiseSamples = samples);

            Assert.That(noiseSamples, Has.Count.EqualTo(StepSampleCount));
            Assert.That(StandardDeviation(noiseSamples), Is.GreaterThan(0.005f), "Walk foot placement noise should vary once organic variation is enabled.");
            Assert.That(noiseSamples.All(sample => sample >= -0.1501f && sample <= 0.1501f), Is.True, "Walk lateral noise must stay within the +/-0.15m design cap.");
        }

        [UnityTest]
        public IEnumerator LateralNoise_AtSprintSpeed_TighterThanWalk()
        {
            yield return PrepareBaseline();

            const int seed = 42;
            _legAnimator.SetOrganicVariationSeedForTest(seed);
            SetPrivateField(_legAnimator, "_disableOrganicVariation", false);

            List<float> walkNoise = null;
            yield return CollectLeftLateralNoiseSamples(
                moveMagnitude: 0.5f,
                sprintHeld: false,
                requireSprintNormalized: false,
                complete: samples => walkNoise = samples);

            yield return PrepareBaseline();
            _legAnimator.SetOrganicVariationSeedForTest(seed);
            SetPrivateField(_legAnimator, "_disableOrganicVariation", false);

            List<float> sprintNoise = null;
            yield return CollectLeftLateralNoiseSamples(
                moveMagnitude: 1f,
                sprintHeld: true,
                requireSprintNormalized: true,
                complete: samples => sprintNoise = samples);

            float walkStdDev = StandardDeviation(walkNoise);
            float sprintStdDev = StandardDeviation(sprintNoise);

            Assert.That(walkNoise, Has.Count.EqualTo(StepSampleCount));
            Assert.That(sprintNoise, Has.Count.EqualTo(StepSampleCount));
            Assert.That(sprintStdDev, Is.LessThan(walkStdDev), "Sprint lateral placement noise should be tighter than walk by design.");
        }

        [UnityTest]
        public IEnumerator LateralNoise_DisabledByFlag_ProducesConsistentFootPlacement()
        {
            yield return PrepareBaseline();

            _legAnimator.SetOrganicVariationSeedForTest(42);
            SetPrivateField(_legAnimator, "_disableOrganicVariation", true);

            List<float> noiseSamples = null;
            yield return CollectLeftLateralNoiseSamples(
                moveMagnitude: 0.5f,
                sprintHeld: false,
                requireSprintNormalized: false,
                complete: samples => noiseSamples = samples);

            Assert.That(noiseSamples, Has.Count.EqualTo(StepSampleCount));
            Assert.That(StandardDeviation(noiseSamples), Is.LessThan(0.005f), "Disabling organic variation should restore effectively identical lateral noise samples.");
            Assert.That(noiseSamples.All(sample => Mathf.Abs(sample) < 0.0001f), Is.True, "Disabling organic variation should zero out per-stride lateral noise.");
        }

        [UnityTest]
        public IEnumerator LateralNoise_DoesNotDestabilise_WalkStraight()
        {
            yield return PrepareBaseline();

            _legAnimator.SetOrganicVariationSeedForTest(42);
            SetPrivateField(_legAnimator, "_disableOrganicVariation", false);
            _movement.SetSprintInputForTest(false);
            _movement.SetMoveInputForTest(Vector2.up * 0.5f);

            for (int frame = 0; frame < StabilityFrameBudget; frame++)
            {
                yield return new WaitForFixedUpdate();
                Assert.That(_balance.IsFallen, Is.False, $"Character should remain stable while walking straight with lateral noise enabled. Fell at frame {frame + 1}.");
            }

            _movement.SetMoveInputForTest(Vector2.zero);
        }

        private void CreateRig()
        {
            _ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ground.name = "LateralPlacementNoiseTests_Ground";
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

        private IEnumerator CollectLeftLateralNoiseSamples(float moveMagnitude, bool sprintHeld, bool requireSprintNormalized, Action<List<float>> complete)
        {
            var recordedOffsets = new List<float>(StepSampleCount);
            bool previousLeftValid = false;

            _movement.SetSprintInputForTest(sprintHeld);
            _movement.SetMoveInputForTest(new Vector2(0f, moveMagnitude));

            if (requireSprintNormalized)
            {
                for (int warmUp = 0; warmUp < 600; warmUp++)
                {
                    yield return new WaitForFixedUpdate();
                    if (_movement.SprintNormalized >= 0.95f)
                    {
                        break;
                    }
                }

                int seed = GetPrivateInt(_legAnimator, "_noiseSeed");
                _legAnimator.SetOrganicVariationSeedForTest(seed);
                previousLeftValid = false;
            }

            for (int frame = 0; frame < CollectionFrameBudget && recordedOffsets.Count < StepSampleCount; frame++)
            {
                yield return new WaitForFixedUpdate();

                bool leftValid = TryReadStepTargetValidity(_legAnimator, "_leftLegCommand");
                if (leftValid && !previousLeftValid)
                {
                    recordedOffsets.Add(GetPrivateFloat(_legAnimator, "_leftLateralNoise"));
                }

                previousLeftValid = leftValid;
            }

            _movement.SetMoveInputForTest(Vector2.zero);
            _movement.SetSprintInputForTest(false);
            complete(recordedOffsets);

            Assert.That(recordedOffsets, Has.Count.EqualTo(StepSampleCount),
                $"Expected to capture {StepSampleCount} left-leg step triggers within {CollectionFrameBudget} frames, but got {recordedOffsets.Count}.");
        }

        private float ReadLeftFootLateralOffset()
        {
            Vector3 hipsPos = _player.transform.position;
            Vector3 landingPos = ReadStepTargetLandingPosition(_legAnimator, "_leftLegCommand");
            Vector3 forward = _player.transform.forward;
            forward = Vector3.ProjectOnPlane(forward, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.forward;
            }

            Vector3 lateralAxis = Vector3.Cross(Vector3.up, forward).normalized;
            return Vector3.Dot(landingPos - hipsPos, lateralAxis);
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

        private static object GetPrivateField(object instance, string fieldName)
        {
            FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Could not find private field '{fieldName}' on {instance.GetType().Name}.");
            return field.GetValue(instance);
        }

        private static int GetPrivateInt(object instance, string fieldName)
        {
            FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Could not find private field '{fieldName}' on {instance.GetType().Name}.");
            return (int)field.GetValue(instance);
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

        private static Vector3 ReadStepTargetLandingPosition(LegAnimator legAnimator, string commandFieldName)
        {
            object command = GetPrivateField(legAnimator, commandFieldName);
            Assert.That(command, Is.Not.Null, $"LegAnimator command field '{commandFieldName}' must exist.");

            PropertyInfo stepTargetProperty = command.GetType().GetProperty("StepTarget", BindingFlags.Instance | BindingFlags.Public);
            Assert.That(stepTargetProperty, Is.Not.Null, $"{command.GetType().Name} must expose a StepTarget property.");

            object stepTarget = stepTargetProperty.GetValue(command);
            Assert.That(stepTarget, Is.Not.Null, "StepTarget value should never be null.");

            PropertyInfo landingPositionProperty = stepTarget.GetType().GetProperty("LandingPosition", BindingFlags.Instance | BindingFlags.Public);
            Assert.That(landingPositionProperty, Is.Not.Null, $"{stepTarget.GetType().Name} must expose a LandingPosition property.");
            return (Vector3)landingPositionProperty.GetValue(stepTarget);
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
