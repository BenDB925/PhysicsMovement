using NUnit.Framework;
using PhysicsDrivenMovement.Character;
using UnityEditor;
using UnityEngine;

namespace PhysicsDrivenMovement.Tests.EditMode.Character
{
    /// <summary>
    /// EditMode coverage for the prefab-side LocomotionDirector seam introduced by
    /// Chapter 1 task C1.3 of the unified locomotion roadmap.
    /// </summary>
    [TestFixture]
    public class LocomotionDirectorEditModeTests
    {
        private const string PlayerRagdollPrefabPath = "Assets/Prefabs/PlayerRagdoll.prefab";

        [Test]
        public void PlayerRagdollPrefab_LocomotionDirector_IsPresentAndPassThroughByDefault()
        {
            // Arrange
            GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerRagdollPrefabPath);

            // Act
            LocomotionDirector director = prefabRoot.GetComponent<LocomotionDirector>();

            // Assert
            Assert.That(prefabRoot, Is.Not.Null, "PlayerRagdoll prefab must exist at the expected path.");
            Assert.That(director, Is.Not.Null,
                "PlayerRagdoll prefab should include LocomotionDirector so the director exists on the Hips runtime path.");
            Assert.That(director.IsPassThroughMode, Is.True,
                "LocomotionDirector should default to pass-through mode until downstream executors are rewired.");
        }
    }
}