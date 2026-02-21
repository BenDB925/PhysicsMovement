using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using PhysicsDrivenMovement.Character;
using PhysicsDrivenMovement.Core;

namespace PhysicsDrivenMovement.Tests.EditMode.Character
{
    /// <summary>
    /// EditMode validation tests for the built PlayerRagdoll prefab wiring.
    /// Focuses on authoring-time setup that can silently break runtime stability,
    /// such as incorrect physics layers or missing GroundSensor masks.
    /// </summary>
    public class PlayerRagdollPrefabTests
    {
        private const string PlayerRagdollPrefabPath = "Assets/Prefabs/PlayerRagdoll.prefab";

        [Test]
        [Description("All Rigidbody body-part GameObjects in PlayerRagdoll prefab should be assigned to Player1Parts layer, not Default.")]
        public void PlayerRagdollPrefab_RigidbodyParts_AreAssignedToPlayerPartsLayer()
        {
            // Arrange
            GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerRagdollPrefabPath);

            // Act
            Rigidbody[] bodies = prefabRoot.GetComponentsInChildren<Rigidbody>(includeInactive: true);

            // Assert
            Assert.That(prefabRoot, Is.Not.Null, "PlayerRagdoll prefab must exist at the expected path.");
            Assert.That(bodies.Length, Is.GreaterThan(0), "PlayerRagdoll prefab should contain rigidbody segments.");

            foreach (Rigidbody body in bodies)
            {
                Assert.That(body.gameObject.layer, Is.EqualTo(GameSettings.LayerPlayer1Parts),
                    $"'{body.gameObject.name}' is on layer {body.gameObject.layer}. " +
                    $"Expected Player1Parts ({GameSettings.LayerPlayer1Parts}) to enable self-collision filtering.");
            }
        }

        [Test]
        [Description("Both GroundSensor components should include Environment layer in their ground mask.")]
        public void PlayerRagdollPrefab_GroundSensors_UseEnvironmentLayerMask()
        {
            // Arrange
            GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerRagdollPrefabPath);
            GroundSensor[] sensors = prefabRoot.GetComponentsInChildren<GroundSensor>(includeInactive: true);
            FieldInfo maskField = typeof(GroundSensor).GetField("_groundLayers", BindingFlags.NonPublic | BindingFlags.Instance);

            // Act / Assert
            Assert.That(prefabRoot, Is.Not.Null, "PlayerRagdoll prefab must exist at the expected path.");
            Assert.That(sensors.Length, Is.EqualTo(2), "PlayerRagdoll should have exactly two GroundSensor components (Foot_L + Foot_R).");
            Assert.That(maskField, Is.Not.Null, "GroundSensor should expose a private serialized _groundLayers field.");

            int environmentBit = 1 << GameSettings.LayerEnvironment;
            foreach (GroundSensor sensor in sensors)
            {
                LayerMask mask = (LayerMask)maskField.GetValue(sensor);
                bool includesEnvironment = (mask.value & environmentBit) != 0;
                Assert.That(includesEnvironment, Is.True,
                    $"GroundSensor on '{sensor.gameObject.name}' must include Environment layer ({GameSettings.LayerEnvironment}) in _groundLayers.");
            }
        }
    }
}
