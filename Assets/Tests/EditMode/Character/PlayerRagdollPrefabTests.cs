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
        private const string SourcePlayerRagdollPrefabPath = "Assets/Prefabs/PlayerRagdoll.prefab";
        private const string SkinnedPlayerRagdollPrefabPath = "Assets/Prefabs/PlayerRagdoll_Skinned.prefab";

        private static readonly string[] RequiredImpactReceiverBones =
        {
            "Torso",
            "UpperArm_L",
            "UpperArm_R",
            "UpperLeg_L",
            "UpperLeg_R",
        };

        private static readonly string[] ExcludedImpactReceiverBones =
        {
            "LowerLeg_L",
            "LowerLeg_R",
        };

        [Test]
        [Description("All Rigidbody body-part GameObjects in PlayerRagdoll prefab should be assigned to Player1Parts layer, not Default.")]
        public void PlayerRagdollPrefab_RigidbodyParts_AreAssignedToPlayerPartsLayer()
        {
            // Arrange
            GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(SkinnedPlayerRagdollPrefabPath);

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
            GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(SkinnedPlayerRagdollPrefabPath);
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

        [Test]
        [Description("Source and skinned ragdoll prefabs should route collision-impact yield through the same six receiver-bearing body parts.")]
        public void PlayerRagdollPrefabs_CollisionImpactReceivers_AreWiredToImpactBones()
        {
            AssertCollisionImpactReceiverWiring(SourcePlayerRagdollPrefabPath, "PlayerRagdoll.prefab");
            AssertCollisionImpactReceiverWiring(SkinnedPlayerRagdollPrefabPath, "PlayerRagdoll_Skinned.prefab");
        }

        private static void AssertCollisionImpactReceiverWiring(string prefabPath, string prefabLabel)
        {
            GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefabRoot, Is.Not.Null, $"{prefabLabel} must exist at the expected path.");

            CollisionImpactReceiver[] receivers = prefabRoot.GetComponentsInChildren<CollisionImpactReceiver>(includeInactive: true);
            Assert.That(receivers.Length, Is.EqualTo(6),
                $"{prefabLabel} should have exactly six CollisionImpactReceiver components (root + torso + two upper arms + two upper legs).");

            Assert.That(prefabRoot.GetComponent<CollisionImpactReceiver>(), Is.Not.Null,
                $"{prefabLabel} root should carry CollisionImpactReceiver for direct hits to the hips body.");

            foreach (string boneName in RequiredImpactReceiverBones)
            {
                Transform bone = FindRequiredChild(prefabRoot, boneName, prefabLabel);
                Assert.That(bone.GetComponent<CollisionImpactReceiver>(), Is.Not.Null,
                    $"{prefabLabel} should attach CollisionImpactReceiver to '{boneName}'.");
            }

            foreach (string boneName in ExcludedImpactReceiverBones)
            {
                Transform bone = FindRequiredChild(prefabRoot, boneName, prefabLabel);
                Assert.That(bone.GetComponent<CollisionImpactReceiver>(), Is.Null,
                    $"{prefabLabel} should not attach CollisionImpactReceiver to '{boneName}' because ground contacts would spam impacts.");
            }
        }

        private static Transform FindRequiredChild(GameObject prefabRoot, string childName, string prefabLabel)
        {
            foreach (Transform transform in prefabRoot.GetComponentsInChildren<Transform>(includeInactive: true))
            {
                if (transform.name == childName)
                {
                    return transform;
                }
            }

            Assert.Fail($"{prefabLabel} is missing child transform '{childName}'.");
            return null;
        }

    }
}
