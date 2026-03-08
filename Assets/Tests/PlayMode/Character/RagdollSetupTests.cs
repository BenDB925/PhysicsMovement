using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using PhysicsDrivenMovement.Character;
using PhysicsDrivenMovement.Core;
using UnityEditor;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// PlayMode integration tests for <see cref="RagdollSetup"/>.
    /// These tests instantiate a minimal two-body ragdoll at runtime and verify that
    /// RagdollSetup correctly ignores collisions between neighbouring segments and
    /// applies lower-leg ground-collision suppression.
    /// Requires the full MonoBehaviour Awake lifecycle, hence PlayMode.
    /// </summary>
    public class RagdollSetupTests
    {
        private const string PlayerRagdollPrefabPath = "Assets/Prefabs/PlayerRagdoll.prefab";

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
        }

        [TearDown]
        public void TearDown()
        {
            Time.fixedDeltaTime = _originalFixedDeltaTime;
            Physics.defaultSolverIterations = _originalSolverIterations;
            Physics.defaultSolverVelocityIterations = _originalSolverVelocityIterations;
            RestoreLayerCollisionMatrix(_originalLayerCollisionMatrix);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────

        /// <summary>
        /// Builds a minimal two-body ragdoll:
        ///   Parent (Hips) → Child (Torso) connected by a ConfigurableJoint.
        /// Returns the RagdollSetup component on the parent.
        /// </summary>
        private static RagdollSetup CreateMinimalRagdoll(out Collider hipCollider, out Collider torsoCollider)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerRagdollPrefabPath);
            Assert.That(prefab, Is.Not.Null,
                $"PlayerRagdoll prefab must be loadable from '{PlayerRagdollPrefabPath}'.");

            GameObject hips = Object.Instantiate(prefab, new Vector3(1600f, 1.1f, 1600f), Quaternion.identity);
            hipCollider = hips.GetComponent<Collider>();
            torsoCollider = FindRequiredChild(hips.transform, "Torso").GetComponent<Collider>();

            Assert.That(hipCollider, Is.Not.Null, "PlayerRagdoll root hips must include a collider.");
            Assert.That(torsoCollider, Is.Not.Null, "PlayerRagdoll torso must include a collider.");

            RagdollSetup setup = hips.GetComponent<RagdollSetup>();
            Assert.That(setup, Is.Not.Null, "PlayerRagdoll prefab must include RagdollSetup.");
            return setup;
        }

        /// <summary>
        /// Builds a minimal ragdoll that includes LowerLeg_L and LowerLeg_R child
        /// GameObjects. RagdollSetup will reassign these to the LowerLegParts layer (13)
        /// and disable that layer's collision with ground layers.
        /// </summary>
        private static RagdollSetup CreateRagdollWithLowerLegs()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerRagdollPrefabPath);
            Assert.That(prefab, Is.Not.Null,
                $"PlayerRagdoll prefab must be loadable from '{PlayerRagdollPrefabPath}'.");

            GameObject hips = Object.Instantiate(prefab, new Vector3(1610f, 1.1f, 1610f), Quaternion.identity);
            RagdollSetup setup = hips.GetComponent<RagdollSetup>();
            Assert.That(setup, Is.Not.Null, "PlayerRagdoll prefab must include RagdollSetup.");
            return setup;
        }

        // ─── Tests: AllBodies ─────────────────────────────────────────────────

        /// <summary>
        /// After Awake, AllBodies must contain all Rigidbodies in the hierarchy (root + children).
        /// </summary>
        [UnityTest]
        public IEnumerator AllBodies_AfterAwake_ContainsHipsAndTorso()
        {
            // Arrange
            RagdollSetup setup = CreateMinimalRagdoll(
                out Collider _, out Collider _);

            // Act — wait one frame so Awake has run.
            yield return null;

            // Assert
            Assert.That(setup.AllBodies, Is.Not.Null,
                "AllBodies should be populated after Awake.");
            Assert.That(setup.AllBodies.Count, Is.GreaterThan(2),
                "The real PlayerRagdoll should report multiple rigidbody segments in AllBodies.");
            Assert.That(ContainsBodyNamed(setup, setup.gameObject.name), Is.True,
                "AllBodies should include the root hips rigidbody from the real PlayerRagdoll.");
            Assert.That(ContainsBodyNamed(setup, "Torso"), Is.True,
                "AllBodies should include the torso rigidbody from the real PlayerRagdoll.");

            // Cleanup
            Object.Destroy(setup.gameObject);
        }

        // ─── Tests: Neighbor collision ────────────────────────────────────────

        /// <summary>
        /// After Awake, direct neighbour colliders (connected by ConfigurableJoint) must
        /// be set to ignore each other's collisions.
        /// </summary>
        [UnityTest]
        public IEnumerator NeighboringColliders_AfterAwake_CollisionIsIgnored()
        {
            // Arrange
            RagdollSetup setup = CreateMinimalRagdoll(
                out Collider hipCollider, out Collider torsoCollider);

            // Act — wait one frame so Awake has run.
            yield return null;

            // Assert — Physics.GetIgnoreCollision returns true if the pair is ignored.
            bool ignored = Physics.GetIgnoreCollision(hipCollider, torsoCollider);
            Assert.That(ignored, Is.True,
                "Hips and Torso are direct neighbours (connected by joint); " +
                "their colliders must ignore each other.");

            // Cleanup
            Object.Destroy(setup.gameObject);
        }

        /// <summary>
        /// A RagdollSetup on a lone Rigidbody (no joints) should still initialise without
        /// throwing. AllBodies should contain exactly the root Rigidbody.
        /// </summary>
        [UnityTest]
        public IEnumerator RagdollWithNoJoints_AfterAwake_DoesNotThrowAndReturnsOneBody()
        {
            // Arrange
            GameObject go    = new GameObject("IsolatedHips");
            go.AddComponent<Rigidbody>();
            go.AddComponent<BoxCollider>();
            RagdollSetup setup = go.AddComponent<RagdollSetup>();

            // Act
            yield return null;

            // Assert
            Assert.That(setup.AllBodies, Is.Not.Null);
            Assert.That(setup.AllBodies.Count, Is.EqualTo(1));

            // Cleanup
            Object.Destroy(go);
        }

        /// <summary>
        /// Colliders that are NOT connected by a joint must NOT have their collision ignored.
        /// </summary>
        [UnityTest]
        public IEnumerator UnconnectedColliders_AfterAwake_CollisionIsNotIgnored()
        {
            // Arrange — a minimal ragdoll plus an unrelated object.
            RagdollSetup setup = CreateMinimalRagdoll(out Collider _, out Collider _);

            GameObject unrelated    = new GameObject("UnrelatedObject");
            Collider   unrelatedCol = unrelated.AddComponent<BoxCollider>();

            // Act
            yield return null;

            // Assert — RagdollSetup should not have touched the unrelated collider.
            Collider hipCol = setup.GetComponent<Collider>();
            bool ignored    = Physics.GetIgnoreCollision(hipCol, unrelatedCol);

            Assert.That(ignored, Is.False,
                "RagdollSetup must only ignore collisions between jointed neighbours, " +
                "not arbitrary unrelated colliders.");

            // Cleanup
            Object.Destroy(setup.gameObject);
            Object.Destroy(unrelated);
        }

        // ─── FIX 2: Lower leg ground collision suppression tests ─────────────

        /// <summary>
        /// After Awake, RagdollSetup must disable physics collision between the
        /// LowerLegParts layer (13) and the Environment layer (12) — the arena geometry
        /// layer used by this project. This prevents the ground contact impulse from
        /// wrenching the lower legs off their joints during gait.
        ///
        /// RagdollSetup reassigns LowerLeg_L and LowerLeg_R to layer 13 (LowerLegParts)
        /// and then calls Physics.IgnoreLayerCollision(13, 12, true).
        ///
        /// Verifies: <c>Physics.GetIgnoreLayerCollision(13, 12)</c> is true.
        ///
        /// DESIGN: Only the Environment layer (12) is suppressed, not Default (0).
        /// The arena floor is on Environment (12). Leaving Default (0) enabled means
        /// internal ragdoll bodies that share Default (0) in test rigs (e.g. hips) still
        /// physically interact with the lower legs, preserving correct joint behaviour.
        /// </summary>
        [UnityTest]
        public IEnumerator LowerLegLayer_AfterAwake_DoesNotCollideWithEnvironmentLayer()
        {
            // Arrange — build a ragdoll with LowerLeg_L and LowerLeg_R children.
            RagdollSetup setup = CreateRagdollWithLowerLegs();

            // Act — wait one frame so Awake (and DisableLowerLegGroundCollisions) has run.
            yield return null;

            // Assert — LowerLegParts layer (13) must ignore Environment layer (12).
            int lowerLegLayer = GameSettings.LayerLowerLegParts;
            bool ignoredEnv = Physics.GetIgnoreLayerCollision(lowerLegLayer, GameSettings.LayerEnvironment);
            Assert.That(ignoredEnv, Is.True,
                $"LowerLegParts layer ({lowerLegLayer}) must ignore Environment layer " +
                $"({GameSettings.LayerEnvironment}) after RagdollSetup.Awake(). " +
                "This prevents arena floor contacts from wrenching the lower legs.");

            // Cleanup
            Object.Destroy(setup.gameObject);
        }

        /// <summary>
        /// After Awake, RagdollSetup must reassign LowerLeg_L and LowerLeg_R GameObjects
        /// to the LowerLegParts layer (13). Verifies the layer assignment is in effect.
        /// </summary>
        [UnityTest]
        public IEnumerator LowerLegGameObjects_AfterAwake_AreOnLowerLegPartsLayer()
        {
            // Arrange
            RagdollSetup setup = CreateRagdollWithLowerLegs();

            // Act
            yield return null;

            // Assert — both lower leg GameObjects must now be on layer 13.
            int expectedLayer = GameSettings.LayerLowerLegParts;
            GameObject lowerLegL = setup.transform.Find("UpperLeg_L/LowerLeg_L")?.gameObject;
            GameObject lowerLegR = setup.transform.Find("UpperLeg_R/LowerLeg_R")?.gameObject;

            Assert.That(lowerLegL, Is.Not.Null, "LowerLeg_L must exist in hierarchy.");
            Assert.That(lowerLegR, Is.Not.Null, "LowerLeg_R must exist in hierarchy.");
            Assert.That(lowerLegL.layer, Is.EqualTo(expectedLayer),
                $"LowerLeg_L must be on LowerLegParts layer ({expectedLayer}) after RagdollSetup.Awake(). " +
                $"Found layer {lowerLegL.layer}.");
            Assert.That(lowerLegR.layer, Is.EqualTo(expectedLayer),
                $"LowerLeg_R must be on LowerLegParts layer ({expectedLayer}) after RagdollSetup.Awake(). " +
                $"Found layer {lowerLegR.layer}.");

            // Cleanup
            Object.Destroy(setup.gameObject);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────

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

        private static Transform FindRequiredChild(Transform root, string name)
        {
            Transform[] children = root.GetComponentsInChildren<Transform>(includeInactive: true);
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].name == name)
                {
                    return children[i];
                }
            }

            throw new System.InvalidOperationException($"Required child '{name}' not found under '{root.name}'.");
        }

        private static bool ContainsBodyNamed(RagdollSetup setup, string bodyName)
        {
            for (int i = 0; i < setup.AllBodies.Count; i++)
            {
                if (setup.AllBodies[i] != null && setup.AllBodies[i].gameObject.name == bodyName)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
