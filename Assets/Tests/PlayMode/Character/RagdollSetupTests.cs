using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using PhysicsDrivenMovement.Character;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// PlayMode integration tests for <see cref="RagdollSetup"/>.
    /// These tests instantiate a minimal two-body ragdoll at runtime and verify that
    /// RagdollSetup correctly ignores collisions between neighbouring segments.
    /// Requires the full MonoBehaviour Awake lifecycle, hence PlayMode.
    /// </summary>
    public class RagdollSetupTests
    {
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
            // ── Hips (root) ──
            GameObject hips    = new GameObject("TestHips");
            Rigidbody  hipsRb  = hips.AddComponent<Rigidbody>();
            hipCollider        = hips.AddComponent<BoxCollider>();

            // ── Torso (child) — fully wired BEFORE adding RagdollSetup so that
            //    Awake() sees the complete hierarchy when AddComponent fires it. ──
            GameObject torso   = new GameObject("TestTorso");
            torso.transform.SetParent(hips.transform);
            torso.transform.localPosition = new Vector3(0f, 0.35f, 0f);

            Rigidbody torsoRb  = torso.AddComponent<Rigidbody>();
            torsoCollider      = torso.AddComponent<BoxCollider>();

            ConfigurableJoint joint = torso.AddComponent<ConfigurableJoint>();
            joint.connectedBody = hipsRb;
            joint.xMotion       = ConfigurableJointMotion.Locked;
            joint.yMotion       = ConfigurableJointMotion.Locked;
            joint.zMotion       = ConfigurableJointMotion.Locked;

            // DESIGN: Add RagdollSetup LAST so its Awake() runs against the complete
            //         hierarchy. AddComponent triggers Awake immediately on an active GO.
            RagdollSetup setup = hips.AddComponent<RagdollSetup>();
            return setup;
        }

        // ─── Tests ───────────────────────────────────────────────────────────

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
            Assert.That(setup.AllBodies.Count, Is.EqualTo(2),
                "A two-body ragdoll should report exactly 2 Rigidbodies.");

            // Cleanup
            Object.Destroy(setup.gameObject);
        }

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
