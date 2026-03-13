#pragma warning disable CS0618 // SetFacingDirection is obsolete but still tested for legacy coverage
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
    /// PlayMode turning and yaw-stability tests using the real PlayerRagdoll prefab.
    /// Facing intent is driven through PlayerMovement where practical so the production
    /// turning path is exercised instead of a synthetic hips-only object.
    /// </summary>
    public class BalanceControllerTurningTests
    {
        private const string PlayerRagdollPrefabPath = "Assets/Prefabs/PlayerRagdoll.prefab";
        private static readonly Vector3 TestOrigin = new Vector3(0f, 0f, 2600f);

        private GameObject _instance;
        private BalanceController _balance;
        private PlayerMovement _movement;
        private CharacterState _characterState;
        private LegAnimator _legAnimator;
        private LocomotionDirector _director;
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
            DestroyCharacter();

            Time.fixedDeltaTime = _originalFixedDeltaTime;
            Physics.defaultSolverIterations = _originalSolverIterations;
            Physics.defaultSolverVelocityIterations = _originalSolverVelocityIterations;
            RestoreLayerCollisionMatrix(_originalLayerCollisionMatrix);
        }

        [Test]
        public void BalanceController_HasKPYawField()
        {
            FieldInfo fieldInfo = typeof(BalanceController).GetField(
                "_kPYaw",
                BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.That(fieldInfo, Is.Not.Null,
                "_kPYaw must exist as a private instance field on BalanceController.");
            Assert.That(fieldInfo.FieldType, Is.EqualTo(typeof(float)),
                "_kPYaw must be of type float.");
        }

        [Test]
        public void BalanceController_HasKDYawField()
        {
            FieldInfo fieldInfo = typeof(BalanceController).GetField(
                "_kDYaw",
                BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.That(fieldInfo, Is.Not.Null,
                "_kDYaw must exist as a private instance field on BalanceController.");
            Assert.That(fieldInfo.FieldType, Is.EqualTo(typeof(float)),
                "_kDYaw must be of type float.");
        }

        [UnityTest]
        public IEnumerator YawCorrection_WhenUprightAndFacingWrong_AppliesTorquePredominantlyAroundWorldY()
        {
            yield return SpawnTurningCharacter(Quaternion.identity);

            _balance.SetGroundStateForTest(isGrounded: true, isFallen: false);
            _movement.SetMoveInputForTest(Vector2.right);
            _hipsRb.angularVelocity = Vector3.zero;

            yield return WaitPhysicsFrames(2);

            Vector3 angularVelocity = _hipsRb.angularVelocity;
            float yawMagnitude = Mathf.Abs(angularVelocity.y);
            float pitchMagnitude = Mathf.Abs(angularVelocity.x);
            float rollMagnitude = Mathf.Abs(angularVelocity.z);

            Assert.That(yawMagnitude, Is.GreaterThan(0.001f),
                $"A 90 degree yaw error should produce measurable yaw angular velocity. angularVelocity={angularVelocity}");
            Assert.That(yawMagnitude, Is.GreaterThan(pitchMagnitude * 2f),
                $"Yaw should dominate pitch when only facing error is present. angularVelocity={angularVelocity}");
            Assert.That(yawMagnitude, Is.GreaterThan(rollMagnitude * 2f),
                $"Yaw should dominate roll when only facing error is present. angularVelocity={angularVelocity}");
        }

        [UnityTest]
        public IEnumerator UprightCorrection_WhenTiltedAndAlreadyFacingCorrectly_AppliesTorqueAroundPitchRollAxes()
        {
            yield return SpawnTurningCharacter(Quaternion.AngleAxis(30f, Vector3.forward));

            _balance.SetGroundStateForTest(isGrounded: true, isFallen: false);
            _movement.SetMoveInputForTest(Vector2.up);
            _hipsRb.angularVelocity = Vector3.zero;

            yield return WaitPhysicsFrames(2);

            Vector3 angularVelocity = _hipsRb.angularVelocity;
            float pitchRollMagnitude = Mathf.Sqrt(angularVelocity.x * angularVelocity.x + angularVelocity.z * angularVelocity.z);
            float yawMagnitude = Mathf.Abs(angularVelocity.y);

            Assert.That(pitchRollMagnitude, Is.GreaterThan(0.001f),
                $"A roll error should produce measurable pitch or roll correction. angularVelocity={angularVelocity}");
            Assert.That(pitchRollMagnitude, Is.GreaterThan(yawMagnitude * 2f),
                $"Pitch and roll correction should dominate when the facing direction already matches the input intent. angularVelocity={angularVelocity}");
        }

        [UnityTest]
        public IEnumerator AirborneMultiplier_DoesNotAffectYawTorque()
        {
            yield return SpawnTurningCharacter(Quaternion.identity);

            _balance.SetGroundStateForTest(isGrounded: true, isFallen: false);
            yield return WaitPhysicsFrames(1);

            _balance.SetGroundStateForTest(isGrounded: true, isFallen: false);
            _movement.SetMoveInputForTest(Vector2.right);
            _hipsRb.angularVelocity = Vector3.zero;
            yield return WaitPhysicsFrames(2);
            float groundedYawAngularVelocity = Mathf.Abs(_hipsRb.angularVelocity.y);

            yield return SpawnTurningCharacter(Quaternion.identity);

            _balance.SetGroundStateForTest(isGrounded: true, isFallen: false);
            yield return WaitPhysicsFrames(1);

            _balance.SetGroundStateForTest(isGrounded: false, isFallen: false);
            _movement.SetMoveInputForTest(Vector2.right);
            _hipsRb.angularVelocity = Vector3.zero;
            yield return WaitPhysicsFrames(2);
            float airborneYawAngularVelocity = Mathf.Abs(_hipsRb.angularVelocity.y);

            Assert.That(groundedYawAngularVelocity, Is.GreaterThan(0.0001f));
            Assert.That(airborneYawAngularVelocity, Is.GreaterThan(0.0001f));

            float ratio = airborneYawAngularVelocity / groundedYawAngularVelocity;
            Assert.That(ratio, Is.GreaterThan(0.8f),
                $"Yaw torque should remain effectively full-strength while airborne. Ratio={ratio:F3}.");
        }

        [UnityTest]
        public IEnumerator AirborneMultiplier_ReducesUprightTorqueWhenAirborne()
        {
            yield return SpawnTurningCharacter(Quaternion.AngleAxis(30f, Vector3.forward));

            _balance.SetGroundStateForTest(isGrounded: true, isFallen: false);
            yield return WaitPhysicsFrames(1);

            _balance.SetGroundStateForTest(isGrounded: true, isFallen: false);
            _movement.SetMoveInputForTest(Vector2.up);
            _hipsRb.angularVelocity = Vector3.zero;
            yield return WaitPhysicsFrames(2);
            Vector3 groundedAngularVelocity = _hipsRb.angularVelocity;
            float groundedPitchRollMagnitude = Mathf.Sqrt(
                groundedAngularVelocity.x * groundedAngularVelocity.x +
                groundedAngularVelocity.z * groundedAngularVelocity.z);

            yield return SpawnTurningCharacter(Quaternion.AngleAxis(30f, Vector3.forward));

            _balance.SetGroundStateForTest(isGrounded: true, isFallen: false);
            yield return WaitPhysicsFrames(1);

            _balance.SetGroundStateForTest(isGrounded: false, isFallen: false);
            _movement.SetMoveInputForTest(Vector2.up);
            _hipsRb.angularVelocity = Vector3.zero;
            yield return WaitPhysicsFrames(2);
            Vector3 airborneAngularVelocity = _hipsRb.angularVelocity;
            float airbornePitchRollMagnitude = Mathf.Sqrt(
                airborneAngularVelocity.x * airborneAngularVelocity.x +
                airborneAngularVelocity.z * airborneAngularVelocity.z);

            Assert.That(groundedPitchRollMagnitude, Is.GreaterThan(0.001f));
            Assert.That(airbornePitchRollMagnitude, Is.LessThan(groundedPitchRollMagnitude * 0.9f),
                $"Upright correction should be measurably reduced while airborne on the real prefab. grounded={groundedPitchRollMagnitude:F5}, airborne={airbornePitchRollMagnitude:F5}");
        }

        [UnityTest]
        public IEnumerator SetGroundStateForTest_StillOverridesGroundAndFallenState()
        {
            yield return SpawnTurningCharacter(Quaternion.identity);

            _balance.SetGroundStateForTest(isGrounded: true, isFallen: false);
            yield return WaitPhysicsFrames(1);
            Assert.That(_balance.IsGrounded, Is.True);
            Assert.That(_balance.IsFallen, Is.False);

            _balance.SetGroundStateForTest(isGrounded: false, isFallen: true);
            yield return WaitPhysicsFrames(1);
            Assert.That(_balance.IsGrounded, Is.False);
            Assert.That(_balance.IsFallen, Is.True);
        }
        [UnityTest]
        public IEnumerator WhenDirectorDisabled_RapidInputTurn_DoesNotEnterSnapRecovery()
        {
            yield return SpawnTurningCharacter(Quaternion.identity);

            Assert.That(_director, Is.Not.Null,
                "PlayerRagdoll prefab should include LocomotionDirector for the Chapter 1 ownership path.");

            _director.enabled = false;
            _balance.SetGroundStateForTest(isGrounded: true, isFallen: false);

            _movement.SetMoveInputForTest(Vector2.up);
            yield return WaitPhysicsFrames(2);

            _movement.SetMoveInputForTest(Vector2.right);
            yield return WaitPhysicsFrames(2);

            Assert.That(_balance.IsInSnapRecovery, Is.False,
                "With LocomotionDirector disabled, BalanceController should not infer snap-recovery from raw movement input. " +
                "Support recovery must come from the director-owned support command.");
        }

        [UnityTest]
        public IEnumerator Fallen_UprightTorqueStillApplied_AidsRecovery()
        {
            yield return SpawnTurningCharacter(Quaternion.AngleAxis(80f, Vector3.forward));

            _balance.SetGroundStateForTest(isGrounded: true, isFallen: true);
            _movement.SetMoveInputForTest(Vector2.up);
            _hipsRb.angularVelocity = Vector3.zero;

            yield return WaitPhysicsFrames(2);

            Vector3 angularVelocity = _hipsRb.angularVelocity;
            float pitchRollMagnitude = Mathf.Sqrt(angularVelocity.x * angularVelocity.x + angularVelocity.z * angularVelocity.z);
            Assert.That(pitchRollMagnitude, Is.GreaterThan(0.001f),
                $"Fallen recovery should still receive upright torque on the real prefab. angularVelocity={angularVelocity}");
        }

        [UnityTest]
        public IEnumerator YawCorrection_WhenCollapseWatchdogConfirmsButStateRemainsStanding_StillAppliesTurnDrive()
        {
            yield return SpawnTurningCharacter(Quaternion.identity);

            LocomotionCollapseDetector collapseDetector = _instance.GetComponent<LocomotionCollapseDetector>();
            Assert.That(collapseDetector, Is.Not.Null,
                "PlayerRagdoll prefab must provide LocomotionCollapseDetector for watchdog-boundary coverage.");

            _balance.SetGroundStateForTest(isGrounded: true, isFallen: false);
            _characterState.SetStateForTest(CharacterStateType.Standing);
            _characterState.enabled = false;
            collapseDetector.enabled = false;
            LocomotionCollapseDetectorTestSeams.SetCollapseConfirmed(collapseDetector, true);
            _movement.SetMoveInputForTest(Vector2.right);
            _hipsRb.angularVelocity = Vector3.zero;

            yield return WaitPhysicsFrames(2);

            float yawMagnitude = Mathf.Abs(_hipsRb.angularVelocity.y);
            Assert.That(yawMagnitude, Is.GreaterThan(0.001f),
                $"Raw collapse confirmation should not suppress turn drive while CharacterState still labels the character Standing. yawAngularVelocity={yawMagnitude:F5}");
        }

        [UnityTest]
        public IEnumerator SetFacingDirection_ZeroVector_IgnoredNoNaN_AfterTorqueSplit()
        {
            yield return SpawnTurningCharacter(Quaternion.identity);

            _balance.SetGroundStateForTest(isGrounded: true, isFallen: false);
            Assert.DoesNotThrow(() => _balance.SetFacingDirection(Vector3.zero));

            _hipsRb.angularVelocity = Vector3.zero;
            yield return WaitPhysicsFrames(1);

            Vector3 angularVelocity = _hipsRb.angularVelocity;
            Assert.That(float.IsNaN(angularVelocity.x) || float.IsNaN(angularVelocity.y) || float.IsNaN(angularVelocity.z), Is.False,
                $"Zero facing input should not produce NaN angular velocity. angularVelocity={angularVelocity}");
        }

        [Test]
        public void BalanceController_HasYawDeadZoneDegField()
        {
            FieldInfo fieldInfo = typeof(BalanceController).GetField(
                "_yawDeadZoneDeg",
                BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.That(fieldInfo, Is.Not.Null,
                "_yawDeadZoneDeg must exist as a private instance field on BalanceController.");
            Assert.That(fieldInfo.FieldType, Is.EqualTo(typeof(float)),
                "_yawDeadZoneDeg must be of type float.");
        }

        [UnityTest]
        public IEnumerator YawTorque_WithinDeadZone_IsNotApplied()
        {
            yield return SpawnTurningCharacter(Quaternion.identity);

            _balance.SetGroundStateForTest(isGrounded: true, isFallen: false);
            float tinyAngle = 0.5f;
            Vector3 nearlyForward = new Vector3(
                Mathf.Sin(tinyAngle * Mathf.Deg2Rad),
                0f,
                Mathf.Cos(tinyAngle * Mathf.Deg2Rad));
            _balance.SetFacingDirection(nearlyForward);
            _hipsRb.angularVelocity = Vector3.zero;

            yield return WaitPhysicsFrames(2);

            float yawAngularVelocity = Mathf.Abs(_hipsRb.angularVelocity.y);
            Assert.That(yawAngularVelocity, Is.LessThan(0.35f),
                $"A very small yaw error should stay inside the dead zone. yawAngularVelocity={yawAngularVelocity:F5}");
        }

        [UnityTest]
        public IEnumerator YawTorque_OutsideDeadZone_IsStillApplied()
        {
            yield return SpawnTurningCharacter(Quaternion.identity);

            _balance.SetGroundStateForTest(isGrounded: true, isFallen: false);
            _movement.SetMoveInputForTest(Vector2.right);
            _hipsRb.angularVelocity = Vector3.zero;

            yield return WaitPhysicsFrames(2);

            float yawAngularVelocity = Mathf.Abs(_hipsRb.angularVelocity.y);
            Assert.That(yawAngularVelocity, Is.GreaterThan(0.001f),
                $"Large yaw errors should still apply yaw torque. yawAngularVelocity={yawAngularVelocity:F5}");
        }

        [UnityTest]
        public IEnumerator SetFacingDirection_ZeroInput_RetainsLastValidFacing()
        {
            yield return SpawnTurningCharacter(Quaternion.identity);

            _balance.SetGroundStateForTest(isGrounded: true, isFallen: false);
            _movement.SetMoveInputForTest(Vector2.right);
            yield return WaitPhysicsFrames(1);

            _balance.SetFacingDirection(Vector3.zero);
            _hipsRb.angularVelocity = Vector3.zero;
            yield return WaitPhysicsFrames(2);

            float yawAngularVelocity = Mathf.Abs(_hipsRb.angularVelocity.y);
            Assert.That(yawAngularVelocity, Is.GreaterThan(0.001f),
                $"Zero facing input should retain the last valid target direction. yawAngularVelocity={yawAngularVelocity:F5}");
        }

        [UnityTest]
        public IEnumerator SetFacingDirection_NearDegenerateVector_ProducesNoNaN()
        {
            yield return SpawnTurningCharacter(Quaternion.identity);

            _balance.SetGroundStateForTest(isGrounded: true, isFallen: false);
            Vector3 tinyButValid = new Vector3(0.04f, 0f, 0f);
            _hipsRb.angularVelocity = Vector3.zero;

            for (int i = 0; i < 3; i++)
            {
                Assert.DoesNotThrow(() => _balance.SetFacingDirection(tinyButValid));
                yield return new WaitForFixedUpdate();
            }

            Vector3 angularVelocity = _hipsRb.angularVelocity;
            Assert.That(float.IsNaN(angularVelocity.x) || float.IsNaN(angularVelocity.y) || float.IsNaN(angularVelocity.z), Is.False,
                $"Near-degenerate facing vectors should not produce NaN angular velocity. angularVelocity={angularVelocity}");
            Assert.That(float.IsInfinity(angularVelocity.x) || float.IsInfinity(angularVelocity.y) || float.IsInfinity(angularVelocity.z), Is.False,
                $"Near-degenerate facing vectors should not produce infinite angular velocity. angularVelocity={angularVelocity}");
        }

        [UnityTest]
        public IEnumerator SetFacingDirection_RapidAlternating_DoesNotCauseRunawaySpin()
        {
            yield return SpawnTurningCharacter(Quaternion.identity);

            _balance.SetGroundStateForTest(isGrounded: true, isFallen: false);
            _hipsRb.angularVelocity = Vector3.zero;

            for (int i = 0; i < 20; i++)
            {
                _movement.SetMoveInputForTest((i % 2 == 0) ? Vector2.right : Vector2.left);
                yield return new WaitForFixedUpdate();
            }

            float absYawAngularVelocity = Mathf.Abs(_hipsRb.angularVelocity.y);
            Assert.That(absYawAngularVelocity, Is.LessThan(20f),
                $"Rapid alternating turn input should remain bounded. absYawAngularVelocity={absYawAngularVelocity:F3}");
        }

        [UnityTest]
        public IEnumerator YawTorque_WhenForwardProjectsNearZeroOnXZ_ProducesNoNaN()
        {
            yield return SpawnTurningCharacter(Quaternion.AngleAxis(89f, Vector3.right));

            _balance.SetGroundStateForTest(isGrounded: true, isFallen: false);
            _movement.SetMoveInputForTest(Vector2.right);
            _hipsRb.angularVelocity = Vector3.zero;

            yield return WaitPhysicsFrames(2);

            Vector3 angularVelocity = _hipsRb.angularVelocity;
            Assert.That(float.IsNaN(angularVelocity.x) || float.IsNaN(angularVelocity.y) || float.IsNaN(angularVelocity.z), Is.False,
                $"Near-vertical forward projection should not produce NaN angular velocity. angularVelocity={angularVelocity}");
            Assert.That(float.IsInfinity(angularVelocity.x) || float.IsInfinity(angularVelocity.y) || float.IsInfinity(angularVelocity.z), Is.False,
                $"Near-vertical forward projection should not produce infinite angular velocity. angularVelocity={angularVelocity}");
        }

        private IEnumerator SpawnTurningCharacter(Quaternion rotation)
        {
            DestroyCharacter();

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerRagdollPrefabPath);
            Assert.That(prefab, Is.Not.Null,
                $"PlayerRagdoll prefab was not found at '{PlayerRagdollPrefabPath}'.");

            _instance = Object.Instantiate(prefab, TestOrigin + new Vector3(0f, 6f, 0f), rotation);
            Assert.That(_instance, Is.Not.Null, "Failed to instantiate PlayerRagdoll prefab.");

            _balance = _instance.GetComponent<BalanceController>();
            _movement = _instance.GetComponent<PlayerMovement>();
            _characterState = _instance.GetComponent<CharacterState>();
            _legAnimator = _instance.GetComponent<LegAnimator>();
            _director = _instance.GetComponent<LocomotionDirector>();
            _armAnimator = _instance.GetComponent<ArmAnimator>();
            _ragdollSetup = _instance.GetComponent<RagdollSetup>();
            _hipsRb = _instance.GetComponent<Rigidbody>();

            Assert.That(_balance, Is.Not.Null, "PlayerRagdoll prefab is missing BalanceController.");
            Assert.That(_movement, Is.Not.Null, "PlayerRagdoll prefab is missing PlayerMovement.");
            Assert.That(_characterState, Is.Not.Null, "PlayerRagdoll prefab is missing CharacterState.");
            Assert.That(_legAnimator, Is.Not.Null, "PlayerRagdoll prefab is missing LegAnimator.");
            Assert.That(_director, Is.Not.Null, "PlayerRagdoll prefab is missing LocomotionDirector.");
            Assert.That(_armAnimator, Is.Not.Null, "PlayerRagdoll prefab is missing ArmAnimator.");
            Assert.That(_ragdollSetup, Is.Not.Null, "PlayerRagdoll prefab is missing RagdollSetup.");
            Assert.That(_hipsRb, Is.Not.Null, "PlayerRagdoll prefab is missing the hips Rigidbody.");

            SetPrivateFloat(_movement, "_moveForce", 0f);
            SetPrivateFloat(_movement, "_maxSpeed", 0f);
            _movement.SetMoveInputForTest(Vector2.zero);

            yield return WaitPhysicsFrames(2);
        }

        private void DestroyCharacter()
        {
            if (_instance != null)
            {
                Object.Destroy(_instance);
                _instance = null;
            }
        }

        private static void SetPrivateFloat(object target, string fieldName, float value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Expected private float field '{fieldName}' on {target.GetType().Name}.");
            field.SetValue(target, value);
        }
        private static IEnumerator WaitPhysicsFrames(int count)
        {
            for (int i = 0; i < count; i++)
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
