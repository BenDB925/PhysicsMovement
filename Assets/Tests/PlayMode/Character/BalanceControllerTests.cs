using System.Collections;
using System.Reflection;
using NUnit.Framework;
using PhysicsDrivenMovement.Character;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// PlayMode regression tests for BalanceController using the real PlayerRagdoll prefab.
    /// These checks keep the original posture and API intent while avoiding hips-only rigs.
    /// </summary>
    public class BalanceControllerTests
    {
        private const string PlayerRagdollPrefabPath = "Assets/Prefabs/PlayerRagdoll.prefab";
        private static readonly Vector3 TestOrigin = new Vector3(0f, 0f, 2200f);

        private GameObject _instance;
        private BalanceController _balance;
        private PlayerMovement _movement;
        private CharacterState _characterState;
        private LegAnimator _legAnimator;
        private ArmAnimator _armAnimator;
        private RagdollSetup _ragdollSetup;
        private Rigidbody _hipsRb;

        private float _originalFixedDeltaTime;
        private int _originalSolverIterations;
        private int _originalSolverVelocityIterations;
        private bool[,] _originalLayerCollisionMatrix;

        [SetUp]
        public void SetUp()
        {
            _originalFixedDeltaTime = Time.fixedDeltaTime;
            _originalSolverIterations = Physics.defaultSolverIterations;
            _originalSolverVelocityIterations = Physics.defaultSolverVelocityIterations;
            _originalLayerCollisionMatrix = CaptureLayerCollisionMatrix();

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

            Time.fixedDeltaTime = _originalFixedDeltaTime;
            Physics.defaultSolverIterations = _originalSolverIterations;
            Physics.defaultSolverVelocityIterations = _originalSolverVelocityIterations;
            RestoreLayerCollisionMatrix(_originalLayerCollisionMatrix);
        }

        [UnityTest]
        public IEnumerator KP_DefaultValue_IsAtLeast2000()
        {
            SpawnCharacter(TestOrigin + new Vector3(0f, 6f, 0f), Quaternion.identity);
            yield return WaitPhysicsFrames(2);

            FieldInfo field = typeof(BalanceController).GetField(
                "_kP",
                BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.That(field, Is.Not.Null,
                "_kP field must exist in BalanceController. If it was renamed, update this test.");

            float kP = (float)field.GetValue(_balance);
            Assert.That(kP, Is.GreaterThanOrEqualTo(2000f),
                $"BalanceController._kP default must be >= 2000 for production upright authority. Found {kP}.");
        }

        [UnityTest]
        public IEnumerator IsGrounded_WhenNoFootOnGround_ReturnsFalse()
        {
            SpawnCharacter(TestOrigin + new Vector3(0f, 6f, 0f), Quaternion.identity);
            yield return WaitPhysicsFrames(2);

            Assert.That(_balance.IsGrounded, Is.False,
                "The real ragdoll should report not grounded when spawned high above any environment ground.");
        }

        [UnityTest]
        public IEnumerator IsFallen_WhenHipsUpright_ReturnsFalse()
        {
            SpawnCharacter(TestOrigin + new Vector3(0f, 6f, 0f), Quaternion.identity);
            yield return EvaluateFallenStateAtRotation(Quaternion.identity);

            Assert.That(_balance.IsFallen, Is.False,
                "An upright PlayerRagdoll root should not be considered fallen.");
        }

        [UnityTest]
        public IEnumerator IsFallen_WhenHipsTiltedBeyondThreshold_ReturnsTrue()
        {
            SpawnCharacter(TestOrigin + new Vector3(0f, 6f, 0f), Quaternion.identity);
            float enterThreshold = GetPrivateFloat("_fallenEnterAngleThreshold");
            yield return EvaluateFallenStateAtRotation(Quaternion.AngleAxis(enterThreshold + 5f, Vector3.forward));

            Assert.That(_balance.IsFallen, Is.True,
                $"A tilt above the prefab's fallen enter threshold ({enterThreshold:F1} degrees) must enter the fallen state.");
        }

        [UnityTest]
        public IEnumerator IsFallen_WhenHipsTiltedBelowEnterThreshold_ReturnsFalse()
        {
            SpawnCharacter(TestOrigin + new Vector3(0f, 6f, 0f), Quaternion.identity);
            float enterThreshold = GetPrivateFloat("_fallenEnterAngleThreshold");
            float belowEnterThreshold = Mathf.Max(0f, enterThreshold - 1f);
            yield return EvaluateFallenStateAtRotation(Quaternion.AngleAxis(belowEnterThreshold, Vector3.forward));

            Assert.That(_balance.IsFallen, Is.False,
                $"A tilt below the prefab's fallen enter threshold ({enterThreshold:F1} degrees) should not mark the character as fallen.");
        }

        [UnityTest]
        public IEnumerator IsFallen_Hysteresis_HoldsBetweenEnterAndExitThresholds()
        {
            SpawnCharacter(TestOrigin + new Vector3(0f, 6f, 0f), Quaternion.identity);
            float enterThreshold = GetPrivateFloat("_fallenEnterAngleThreshold");
            float exitThreshold = GetPrivateFloat("_fallenExitAngleThreshold");
            float betweenThresholds = (enterThreshold + exitThreshold) * 0.5f;
            float belowExitThreshold = Mathf.Max(0f, exitThreshold - 1f);

            yield return EvaluateFallenStateAtRotation(Quaternion.AngleAxis(enterThreshold + 5f, Vector3.forward));
            Assert.That(_balance.IsFallen, Is.True,
                $"Above the prefab's fallen enter threshold ({enterThreshold:F1} degrees), the real controller should enter fallen state.");

            yield return EvaluateFallenStateAtRotation(Quaternion.AngleAxis(betweenThresholds, Vector3.forward));
            Assert.That(_balance.IsFallen, Is.True,
                "Between enter and exit thresholds, fallen hysteresis should hold.");

            yield return EvaluateFallenStateAtRotation(Quaternion.AngleAxis(belowExitThreshold, Vector3.forward));
            Assert.That(_balance.IsFallen, Is.False,
                $"Below the prefab's fallen exit threshold ({exitThreshold:F1} degrees), hysteresis should clear.");
        }

        [UnityTest]
        public IEnumerator SetFacingDirection_WithValidDirection_DoesNotThrow()
        {
            SpawnCharacter(TestOrigin + new Vector3(0f, 6f, 0f), Quaternion.identity);
            yield return WaitPhysicsFrames(1);

            Assert.DoesNotThrow(() => _balance.SetFacingDirection(Vector3.forward),
                "SetFacingDirection(Vector3.forward) must remain safe on the real prefab instance.");
        }

        [UnityTest]
        public IEnumerator SetFacingDirection_WithZeroVector_IsIgnoredSafely()
        {
            SpawnCharacter(TestOrigin + new Vector3(0f, 6f, 0f), Quaternion.identity);
            yield return WaitPhysicsFrames(1);

            Assert.DoesNotThrow(() => _balance.SetFacingDirection(Vector3.zero),
                "SetFacingDirection(Vector3.zero) must be ignored safely on the real prefab instance.");

            Assert.That(float.IsNaN(_balance.IsGrounded ? 1f : 0f), Is.False);
            Assert.That(float.IsNaN(_balance.IsFallen ? 1f : 0f), Is.False);
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WhenUprightAndTilted_AppliesCorrectionTorque()
        {
            SpawnCharacter(TestOrigin + new Vector3(0f, 6f, 0f), Quaternion.AngleAxis(30f, Vector3.forward));
            _hipsRb.angularVelocity = Vector3.zero;
            Vector3 initialAngularVelocity = _hipsRb.angularVelocity;

            yield return WaitPhysicsFrames(3);

            Assert.That(_hipsRb.angularVelocity, Is.Not.EqualTo(initialAngularVelocity),
                "A 30 degree tilt on the real ragdoll should produce corrective angular motion.");
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WhenFallen_DoesNotChangeKinematicAngularVelocity()
        {
            SpawnCharacter(TestOrigin + new Vector3(0f, 6f, 0f), Quaternion.AngleAxis(80f, Vector3.forward));
            _hipsRb.isKinematic = true;
            Vector3 angularVelocityBefore = _hipsRb.angularVelocity;

            yield return WaitPhysicsFrames(1);

            Assert.That(_hipsRb.angularVelocity, Is.EqualTo(angularVelocityBefore),
                "A kinematic hips body should keep its angular velocity unchanged during fallen-pose updates.");
        }

        private void SpawnCharacter(Vector3 position, Quaternion rotation)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerRagdollPrefabPath);
            Assert.That(prefab, Is.Not.Null,
                $"PlayerRagdoll prefab was not found at '{PlayerRagdollPrefabPath}'.");

            _instance = Object.Instantiate(prefab, position, rotation);
            Assert.That(_instance, Is.Not.Null, "Failed to instantiate PlayerRagdoll prefab.");

            _balance = _instance.GetComponent<BalanceController>();
            _movement = _instance.GetComponent<PlayerMovement>();
            _characterState = _instance.GetComponent<CharacterState>();
            _legAnimator = _instance.GetComponent<LegAnimator>();
            _armAnimator = _instance.GetComponent<ArmAnimator>();
            _ragdollSetup = _instance.GetComponent<RagdollSetup>();
            _hipsRb = _instance.GetComponent<Rigidbody>();

            Assert.That(_balance, Is.Not.Null, "PlayerRagdoll prefab is missing BalanceController.");
            Assert.That(_movement, Is.Not.Null, "PlayerRagdoll prefab is missing PlayerMovement.");
            Assert.That(_characterState, Is.Not.Null, "PlayerRagdoll prefab is missing CharacterState.");
            Assert.That(_legAnimator, Is.Not.Null, "PlayerRagdoll prefab is missing LegAnimator.");
            Assert.That(_armAnimator, Is.Not.Null, "PlayerRagdoll prefab is missing ArmAnimator.");
            Assert.That(_ragdollSetup, Is.Not.Null, "PlayerRagdoll prefab is missing RagdollSetup.");
            Assert.That(_hipsRb, Is.Not.Null, "PlayerRagdoll prefab is missing the hips Rigidbody.");

            _movement.SetMoveInputForTest(Vector2.zero);
        }

        private static IEnumerator WaitPhysicsFrames(int frameCount)
        {
            for (int i = 0; i < frameCount; i++)
            {
                yield return new WaitForFixedUpdate();
            }
        }

        private IEnumerator EvaluateFallenStateAtRotation(Quaternion rotation)
        {
            _hipsRb.angularVelocity = Vector3.zero;
            _hipsRb.isKinematic = true;
            _hipsRb.rotation = rotation;
            yield return WaitPhysicsFrames(1);
        }

        private float GetPrivateFloat(string fieldName)
        {
            FieldInfo field = typeof(BalanceController).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(field, Is.Not.Null,
                $"{fieldName} field must exist in BalanceController. If it was renamed, update this test.");

            return (float)field.GetValue(_balance);
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
