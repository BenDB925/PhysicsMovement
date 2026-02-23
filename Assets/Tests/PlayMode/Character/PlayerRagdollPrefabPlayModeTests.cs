using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using PhysicsDrivenMovement.Character;
using PhysicsDrivenMovement.Core;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// PlayMode integration tests that instantiate the real PlayerRagdoll prefab
    /// to validate production wiring and startup stability.
    /// </summary>
    public class PlayerRagdollPrefabPlayModeTests
    {
        private const string PlayerRagdollPrefabPath = "Assets/Prefabs/PlayerRagdoll.prefab";
        private const int SettleFrames = 220;
        private const int RecoveryFrames = 420;
        private const int LongStabilityFrames = 2000;

        private GameObject _ground;
        private GameObject _instance;
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

            if (_ground != null)
            {
                Object.Destroy(_ground);
            }

            Time.fixedDeltaTime = _originalFixedDeltaTime;
            Physics.defaultSolverIterations = _originalSolverIterations;
            Physics.defaultSolverVelocityIterations = _originalSolverVelocityIterations;
            RestoreLayerCollisionMatrix(_originalLayerCollisionMatrix);
        }

        [UnityTest]
        public IEnumerator PlayerRagdollPrefab_OnEnvironmentGround_SettlesWithoutImmediateTopple()
        {
            // Arrange
            _ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ground.name = "TestGround";
            _ground.transform.position = new Vector3(0f, -0.5f, 0f);
            _ground.transform.localScale = new Vector3(20f, 1f, 20f);
            _ground.layer = GameSettings.LayerEnvironment;

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerRagdollPrefabPath);
            Assert.That(prefab, Is.Not.Null, "PlayerRagdoll prefab must be loadable from Assets/Prefabs.");

            _instance = Object.Instantiate(prefab, new Vector3(0f, 1.1f, 0f), Quaternion.identity);
            BalanceController balance = _instance.GetComponent<BalanceController>();
            Assert.That(balance, Is.Not.Null, "PlayerRagdoll prefab should include BalanceController on root Hips.");

            // Act
            for (int i = 0; i < SettleFrames; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            // Assert
            float tilt = Vector3.Angle(balance.transform.up, Vector3.up);
            float hipsHeight = balance.transform.position.y;
            Assert.That(balance.IsGrounded, Is.True,
                "At least one foot should report grounded on Environment-layer ground.");
            Assert.That(hipsHeight, Is.GreaterThan(0.65f),
                $"PlayerRagdoll hips should remain elevated (height={hipsHeight:F2}m), not settle into a seated pose.");
            Assert.That(tilt, Is.LessThan(45f),
                $"PlayerRagdoll should not immediately topple on spawn (tilt={tilt:F1}°).");
        }

        [Test]
        public void PlayerRagdollPrefab_HipsHasSinglePlayerMovementWithRequiredComponents()
        {
            // Arrange
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerRagdollPrefabPath);
            Assert.That(prefab, Is.Not.Null, "PlayerRagdoll prefab must be loadable from Assets/Prefabs.");

            // Act
            PlayerMovement[] movementComponents = prefab.GetComponents<PlayerMovement>();
            Rigidbody hipsRigidbody = prefab.GetComponent<Rigidbody>();
            BalanceController balanceController = prefab.GetComponent<BalanceController>();

            // Assert
            Assert.That(movementComponents, Has.Length.EqualTo(1),
                $"PlayerRagdoll Hips must have exactly one PlayerMovement component, found {movementComponents.Length}.");
            Assert.That(hipsRigidbody, Is.Not.Null,
                "PlayerRagdoll Hips must include a Rigidbody on the same object as PlayerMovement.");
            Assert.That(balanceController, Is.Not.Null,
                "PlayerRagdoll Hips must include a BalanceController on the same object as PlayerMovement.");
        }

        [UnityTest]
        public IEnumerator PlayerRagdollPrefab_FromBackFall_RecoversToStanding()
        {
            _ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ground.name = "TestGround";
            _ground.transform.position = new Vector3(0f, -0.5f, 0f);
            _ground.transform.localScale = new Vector3(20f, 1f, 20f);
            _ground.layer = GameSettings.LayerEnvironment;

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerRagdollPrefabPath);
            Assert.That(prefab, Is.Not.Null, "PlayerRagdoll prefab must be loadable from Assets/Prefabs.");

            Quaternion backFallRotation = Quaternion.Euler(95f, 0f, 0f);
            _instance = Object.Instantiate(prefab, new Vector3(0f, 1.15f, 0f), backFallRotation);
            BalanceController balance = _instance.GetComponent<BalanceController>();
            Assert.That(balance, Is.Not.Null, "PlayerRagdoll prefab should include BalanceController on root Hips.");

            for (int i = 0; i < RecoveryFrames; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            float tilt = Vector3.Angle(balance.transform.up, Vector3.up);
            float hipsHeight = balance.transform.position.y;
            Assert.That(balance.IsGrounded, Is.True,
                "At least one foot should report grounded during recovery.");
            Assert.That(balance.IsFallen, Is.False,
                "Controller should recover out of fallen state after seated/back startup.");
            Assert.That(hipsHeight, Is.GreaterThan(0.72f),
                $"Recovery should lift hips off seated posture (height={hipsHeight:F2}m).");
            Assert.That(tilt, Is.LessThan(35f),
                $"Recovered posture should be mostly upright (tilt={tilt:F1}°).");
        }

        [UnityTest]
        public IEnumerator PlayerRagdollPrefab_LongRun_RemainsRecoverablyStable()
        {
            _ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ground.name = "TestGround";
            _ground.transform.position = new Vector3(0f, -0.5f, 0f);
            _ground.transform.localScale = new Vector3(20f, 1f, 20f);
            _ground.layer = GameSettings.LayerEnvironment;

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerRagdollPrefabPath);
            Assert.That(prefab, Is.Not.Null, "PlayerRagdoll prefab must be loadable from Assets/Prefabs.");

            _instance = Object.Instantiate(prefab, new Vector3(0f, 1.1f, 0f), Quaternion.identity);
            BalanceController balance = _instance.GetComponent<BalanceController>();
            Assert.That(balance, Is.Not.Null, "PlayerRagdoll prefab should include BalanceController on root Hips.");

            float minHipsHeight = float.MaxValue;
            float maxTilt = 0f;
            int fallenFrames = 0;
            // Track grounded across the last window to avoid a false-negative caused by a
            // single physics frame where both feet are momentarily mid-step (transient jitter).
            const int GroundedWindowFrames = 30;
            int groundedInLastWindow = 0;

            for (int i = 0; i < LongStabilityFrames; i++)
            {
                yield return new WaitForFixedUpdate();

                float tilt = Vector3.Angle(balance.transform.up, Vector3.up);
                float hipsHeight = balance.transform.position.y;
                minHipsHeight = Mathf.Min(minHipsHeight, hipsHeight);
                maxTilt = Mathf.Max(maxTilt, tilt);

                if (balance.IsFallen)
                {
                    fallenFrames++;
                }

                // Count frames within the final window where grounded was observed.
                if (i >= LongStabilityFrames - GroundedWindowFrames && balance.IsGrounded)
                {
                    groundedInLastWindow++;
                }
            }

            // Require grounded for at least half the final window — a stable character should
            // almost always have at least one foot in contact with the ground. A single-frame
            // jitter (both feet mid-step at the exact last sample) should not fail the test.
            Assert.That(groundedInLastWindow, Is.GreaterThan(GroundedWindowFrames / 2),
                $"At least one foot should remain grounded during the final {GroundedWindowFrames} frames " +
                $"of long-run simulation (grounded {groundedInLastWindow}/{GroundedWindowFrames} frames). " +
                "Character may have toppled or left the ground permanently.");
            Assert.That(fallenFrames, Is.LessThan(150),
                $"Long-run stability should not remain fallen for extended periods (fallenFrames={fallenFrames}).");
            Assert.That(minHipsHeight, Is.GreaterThan(0.62f),
                $"Long-run behavior should avoid sustained seated collapse (minHipsHeight={minHipsHeight:F2}m).");
            Assert.That(maxTilt, Is.LessThan(75f),
                $"Long-run behavior should avoid catastrophic topple (maxTilt={maxTilt:F1}°).");
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
