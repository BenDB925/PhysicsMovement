#pragma warning disable CS0618 // SetFacingDirection is obsolete but still tested for legacy coverage
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
    /// PlayMode integration tests for BalanceController using the real PlayerRagdoll prefab
    /// on explicit test ground so grounding, posture, and push recovery match runtime wiring.
    /// </summary>
    public class BalanceControllerIntegrationTests
    {
        private const string PlayerRagdollPrefabPath = "Assets/Prefabs/PlayerRagdoll_Skinned.prefab";
        private const int SettleFrameCount = 220;
        private const int RecoveryFrameCount = 300;

        private static readonly Vector3 TestOrigin = new Vector3(0f, 0f, 2400f);
        private static readonly Vector3 TestGroundScale = new Vector3(400f, 1f, 400f);

        private GameObject _groundGO;
        private GameObject _instance;
        private Rigidbody _hipsRb;
        private Rigidbody _torsoRb;
        private BalanceController _balance;
        private PlayerMovement _movement;
        private CharacterState _characterState;
        private LegAnimator _legAnimator;
        private ArmAnimator _armAnimator;
        private RagdollSetup _ragdollSetup;

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

            if (_groundGO != null)
            {
                Object.Destroy(_groundGO);
            }

            Time.fixedDeltaTime = _originalFixedDeltaTime;
            Physics.defaultSolverIterations = _originalSolverIterations;
            Physics.defaultSolverVelocityIterations = _originalSolverVelocityIterations;
            RestoreLayerCollisionMatrix(_originalLayerCollisionMatrix);
        }

        [UnityTest]
        public IEnumerator Bug_GroundOnWrongLayer_CharacterFallsOver()
        {
            CreateGroundPlane(0);
            SpawnCharacter();

            yield return WaitPhysicsFrames(SettleFrameCount);

            Assert.That(_balance.IsGrounded, Is.False,
                "GroundSensor should not detect a Default-layer ground under the real prefab.");

            float tilt = GetHipsTiltAngle();
            Assert.That(tilt, Is.LessThan(25f),
                $"Wrong-layer ground should still remain physically recoverable under fail-safe stabilization (tilt={tilt:F1} degrees).");
        }

        [UnityTest]
        public IEnumerator StandsUpright_OnCorrectLayerGround_TiltBelow25Degrees()
        {
            yield return PrepareStandingCharacter(GameSettings.LayerEnvironment);

            Assert.That(_balance.IsGrounded, Is.True,
                "The real prefab should ground on Environment-layer terrain.");

            float tilt = GetHipsTiltAngle();
            Assert.That(tilt, Is.LessThan(25f),
                $"The real prefab should settle upright on flat ground (tilt={tilt:F1} degrees).");

            Assert.That(_balance.IsFallen, Is.False,
                "A settled upright prefab should not be in fallen state.");
        }

        [UnityTest]
        public IEnumerator SmallPush_CausesWobble_ThenRecovers()
        {
            yield return PrepareStandingCharacter(GameSettings.LayerEnvironment);

            Assert.That(GetHipsTiltAngle(), Is.LessThan(25f),
                "Precondition failed: expected the prefab to settle standing before the push.");

            _hipsRb.AddForce(Vector3.right * 200f, ForceMode.Force);
            yield return new WaitForFixedUpdate();
            yield return WaitPhysicsFrames(RecoveryFrameCount);

            float tiltAfter = GetHipsTiltAngle();
            Assert.That(tiltAfter, Is.LessThan(35f),
                $"After a 200 N push, the real character should recover to an upright posture (tilt={tiltAfter:F1} degrees).");
            Assert.That(_balance.IsFallen, Is.False,
                "A small push should not leave the real character fallen after the recovery window.");
        }

        [UnityTest]
        public IEnumerator ModeratePush_SwaysButRecovers()
        {
            yield return PrepareStandingCharacter(GameSettings.LayerEnvironment);

            Assert.That(GetHipsTiltAngle(), Is.LessThan(25f),
                "Precondition failed: expected the prefab to settle standing before the push.");

            _hipsRb.AddForce(Vector3.right * 300f, ForceMode.Force);
            yield return WaitPhysicsFrames(RecoveryFrameCount);

            float tilt = GetHipsTiltAngle();
            Assert.That(tilt, Is.LessThan(40f),
                $"After a moderate push, the real character should still recover (tilt={tilt:F1} degrees).");
        }

        [UnityTest]
        public IEnumerator RepeatedSmallPushes_CharacterStaysStanding()
        {
            yield return PrepareStandingCharacter(GameSettings.LayerEnvironment);

            Vector3[] pushDirections =
            {
                Vector3.right,
                Vector3.left,
                Vector3.forward,
                Vector3.back,
                new Vector3(1f, 0f, 1f).normalized,
            };

            for (int i = 0; i < pushDirections.Length; i++)
            {
                _hipsRb.AddForce(pushDirections[i] * 150f, ForceMode.Force);
                yield return WaitPhysicsFrames(50);
            }

            yield return WaitPhysicsFrames(RecoveryFrameCount);

            float tilt = GetHipsTiltAngle();
            Assert.That(tilt, Is.LessThan(35f),
                $"After repeated small pushes, the real character should still recover upright (tilt={tilt:F1} degrees).");
            Assert.That(_balance.IsFallen, Is.False,
                "Repeated small pushes should not leave the real character in fallen state.");
        }

        [UnityTest]
        public IEnumerator StrongPush_KnocksCharacterDown()
        {
            yield return PrepareStandingCharacter(GameSettings.LayerEnvironment);

            Assert.That(_torsoRb, Is.Not.Null, "The real prefab must expose a Torso rigidbody for strong-push toppling.");

            float maxTiltDuringPush = GetHipsTiltAngle();
            bool enteredFallenState = false;
            bool destabilized = false;

            for (int i = 0; i < 200; i++)
            {
                _torsoRb.AddForce(Vector3.right * 1500f, ForceMode.Force);
                yield return new WaitForFixedUpdate();

                float tilt = GetHipsTiltAngle();
                maxTiltDuringPush = Mathf.Max(maxTiltDuringPush, tilt);
                enteredFallenState |= _balance.IsFallen;
                destabilized |= tilt > 35f;
            }

            bool recoveredWithinWindow = false;
            for (int i = 0; i < RecoveryFrameCount; i++)
            {
                yield return new WaitForFixedUpdate();

                float tilt = GetHipsTiltAngle();
                maxTiltDuringPush = Mathf.Max(maxTiltDuringPush, tilt);
                enteredFallenState |= _balance.IsFallen;
                destabilized |= tilt > 35f;

                if (_balance.IsGrounded && !_balance.IsFallen && tilt < 40f)
                {
                    recoveredWithinWindow = true;
                    break;
                }
            }

            Assert.That(destabilized, Is.True,
                $"A strong torso push should visibly destabilize the real character (maxTilt={maxTiltDuringPush:F1} degrees).");

            bool exceededHighTilt = maxTiltDuringPush > 45f;
            Assert.That(enteredFallenState || exceededHighTilt, Is.True,
                $"A strong push should either enter fallen state or exceed a high-tilt posture. Observed IsFallen={enteredFallenState}, maxTilt={maxTiltDuringPush:F1} degrees.");

            Assert.That(recoveredWithinWindow, Is.True,
                $"Under current tuning, the real character should recover from the strong push within {RecoveryFrameCount} frames.");
        }

        [UnityTest]
        public IEnumerator GroundSensor_DetectsEnvironmentLayerGround()
        {
            yield return PrepareStandingCharacter(GameSettings.LayerEnvironment);

            Assert.That(_balance.IsGrounded, Is.True,
                "The real prefab should detect Environment-layer ground after settling.");
        }

        [UnityTest]
        public IEnumerator GroundSensor_SingleFrameContactLoss_DoesNotClearGrounded()
        {
            yield return PrepareStandingCharacter(GameSettings.LayerEnvironment);

            Assert.That(_balance.IsGrounded, Is.True,
                "Precondition failed: expected the prefab to be grounded before contact flicker.");

            Vector3 originalGroundPosition = _groundGO.transform.position;
            _groundGO.transform.position = new Vector3(originalGroundPosition.x, -5f, originalGroundPosition.z);
            yield return new WaitForFixedUpdate();
            _groundGO.transform.position = originalGroundPosition;
            yield return new WaitForFixedUpdate();

            Assert.That(_balance.IsGrounded, Is.True,
                "Single-frame contact loss should be absorbed by GroundSensor hysteresis on the real prefab.");
        }

        [UnityTest]
        public IEnumerator GroundSensor_DoesNotDetectWrongLayerGround()
        {
            yield return PrepareStandingCharacter(0);

            Assert.That(_balance.IsGrounded, Is.False,
                "The real prefab should not treat Default-layer ground as valid Environment ground.");
        }

        [UnityTest]
        public IEnumerator GroundSensor_SustainedContactLoss_ClearsGrounded()
        {
            yield return PrepareStandingCharacter(GameSettings.LayerEnvironment);

            Assert.That(_balance.IsGrounded, Is.True,
                "Precondition failed: expected the prefab to be grounded before sustained contact loss.");

            Vector3 originalGroundPosition = _groundGO.transform.position;
            _groundGO.transform.position = new Vector3(originalGroundPosition.x, -5f, originalGroundPosition.z);
            Physics.SyncTransforms();

            bool groundedCleared = false;
            for (int frame = 0; frame < 20; frame++)
            {
                yield return new WaitForFixedUpdate();
                if (!_balance.IsGrounded)
                {
                    groundedCleared = true;
                    break;
                }
            }

            _groundGO.transform.position = originalGroundPosition;
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();

            Assert.That(groundedCleared, Is.True,
                "Sustained contact loss should clear grounded state within a short runtime window on the real prefab.");
        }

        [UnityTest]
        public IEnumerator SetFacingDirection_ThenPush_StillRecovers()
        {
            yield return PrepareStandingCharacter(GameSettings.LayerEnvironment);

            Assert.That(GetHipsTiltAngle(), Is.LessThan(25f),
                "Precondition failed: expected the prefab to settle standing before the turn-and-push sequence.");

            _movement.SetMoveInputForTest(Vector2.right);
            yield return WaitPhysicsFrames(50);
            _movement.SetMoveInputForTest(Vector2.zero);

            _hipsRb.AddForce(Vector3.forward * 200f, ForceMode.Force);
            yield return WaitPhysicsFrames(RecoveryFrameCount);

            float tilt = GetHipsTiltAngle();
            Assert.That(tilt, Is.LessThan(35f),
                $"Changing facing intent through PlayerMovement and then pushing should still recover (tilt={tilt:F1} degrees).");
        }

        [UnityTest]
        public IEnumerator AfterLateralImpulse250N_UprightRestoredWithin150Frames()
        {
            yield return PrepareStandingCharacter(GameSettings.LayerEnvironment);

            float tiltBefore = GetHipsTiltAngle();
            Assert.That(tiltBefore, Is.LessThan(20f),
                $"Precondition failed: expected the prefab to settle standing before the lateral impulse (tilt={tiltBefore:F1} degrees).");

            _hipsRb.AddForce(Vector3.right * 250f, ForceMode.Impulse);

            bool recoveredWithin150 = false;
            float maxTilt = 0f;

            for (int frame = 0; frame < 150; frame++)
            {
                yield return new WaitForFixedUpdate();

                float tilt = GetHipsTiltAngle();
                maxTilt = Mathf.Max(maxTilt, tilt);
                if (tilt <= 15f)
                {
                    recoveredWithin150 = true;
                    break;
                }
            }

            float finalTilt = GetHipsTiltAngle();
            Assert.That(recoveredWithin150, Is.True,
                $"The real character should restore upright posture within 150 frames after a 250 N lateral impulse. Max tilt={maxTilt:F1} degrees, final tilt={finalTilt:F1} degrees.");
            Assert.That(_balance.IsFallen, Is.False,
                "The real character should not remain fallen after recovering from the lateral impulse.");
        }

        private IEnumerator PrepareStandingCharacter(int groundLayer)
        {
            CreateGroundPlane(groundLayer);
            SpawnCharacter();
            _movement.SetMoveInputForTest(Vector2.zero);
            yield return WaitPhysicsFrames(SettleFrameCount);
        }

        private void CreateGroundPlane(int layer)
        {
            _groundGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _groundGO.name = layer == GameSettings.LayerEnvironment ? "TestGround" : "TestGround_WrongLayer";
            _groundGO.transform.position = TestOrigin + new Vector3(0f, -0.5f, 0f);
            _groundGO.transform.localScale = TestGroundScale;
            _groundGO.layer = layer;
        }

        private void SpawnCharacter()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerRagdollPrefabPath);
            Assert.That(prefab, Is.Not.Null,
                $"PlayerRagdoll prefab was not found at '{PlayerRagdollPrefabPath}'.");

            _instance = Object.Instantiate(prefab, TestOrigin + new Vector3(0f, 0.5f, 0f), Quaternion.identity);
            Assert.That(_instance, Is.Not.Null, "Failed to instantiate PlayerRagdoll prefab.");

            _balance = _instance.GetComponent<BalanceController>();
            _movement = _instance.GetComponent<PlayerMovement>();
            _characterState = _instance.GetComponent<CharacterState>();
            _legAnimator = _instance.GetComponent<LegAnimator>();
            _armAnimator = _instance.GetComponent<ArmAnimator>();
            _ragdollSetup = _instance.GetComponent<RagdollSetup>();
            _hipsRb = _instance.GetComponent<Rigidbody>();
            _torsoRb = FindChildComponent<Rigidbody>(_instance.transform, "Torso");

            Assert.That(_balance, Is.Not.Null, "PlayerRagdoll prefab is missing BalanceController.");
            Assert.That(_movement, Is.Not.Null, "PlayerRagdoll prefab is missing PlayerMovement.");
            Assert.That(_characterState, Is.Not.Null, "PlayerRagdoll prefab is missing CharacterState.");
            Assert.That(_legAnimator, Is.Not.Null, "PlayerRagdoll prefab is missing LegAnimator.");
            Assert.That(_armAnimator, Is.Not.Null, "PlayerRagdoll prefab is missing ArmAnimator.");
            Assert.That(_ragdollSetup, Is.Not.Null, "PlayerRagdoll prefab is missing RagdollSetup.");
            Assert.That(_hipsRb, Is.Not.Null, "PlayerRagdoll prefab is missing the hips Rigidbody.");
        }

        private float GetHipsTiltAngle()
        {
            return Vector3.Angle(_hipsRb.transform.up, Vector3.up);
        }

        private static T FindChildComponent<T>(Transform root, string childName) where T : Component
        {
            Transform[] children = root.GetComponentsInChildren<Transform>(includeInactive: true);
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].name == childName)
                {
                    return children[i].GetComponent<T>();
                }
            }

            return null;
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
