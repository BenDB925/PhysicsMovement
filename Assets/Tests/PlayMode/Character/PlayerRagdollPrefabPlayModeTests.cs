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

        private GameObject _ground;
        private GameObject _instance;

        [SetUp]
        public void SetUp()
        {
            Time.fixedDeltaTime = 0.01f;
            Physics.defaultSolverIterations = 12;
            Physics.defaultSolverVelocityIterations = 4;
            Physics.IgnoreLayerCollision(GameSettings.LayerPlayer1Parts, GameSettings.LayerPlayer1Parts, true);
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
    }
}
