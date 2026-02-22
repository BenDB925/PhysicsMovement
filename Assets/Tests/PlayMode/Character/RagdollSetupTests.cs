using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using PhysicsDrivenMovement.Character;
using PhysicsDrivenMovement.Core;

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

        /// <summary>
        /// Builds a minimal ragdoll that includes LowerLeg_L and LowerLeg_R child
        /// GameObjects. RagdollSetup will reassign these to the LowerLegParts layer (13)
        /// and disable that layer's collision with ground layers.
        /// </summary>
        private static RagdollSetup CreateRagdollWithLowerLegs()
        {
            // ── Hips (root) ──
            GameObject hips   = new GameObject("TestHips_LL");
            Rigidbody  hipsRb = hips.AddComponent<Rigidbody>();
            hips.AddComponent<BoxCollider>();

            // ── UpperLeg_L ──
            GameObject upperLegL  = new GameObject("UpperLeg_L");
            upperLegL.transform.SetParent(hips.transform);
            upperLegL.transform.localPosition = new Vector3(-0.1f, -0.22f, 0f);
            Rigidbody upperLegLRb = upperLegL.AddComponent<Rigidbody>();
            upperLegL.AddComponent<CapsuleCollider>();
            ConfigurableJoint ulJointL = upperLegL.AddComponent<ConfigurableJoint>();
            ulJointL.connectedBody = hipsRb;
            ulJointL.xMotion = ConfigurableJointMotion.Locked;
            ulJointL.yMotion = ConfigurableJointMotion.Locked;
            ulJointL.zMotion = ConfigurableJointMotion.Locked;

            // ── LowerLeg_L — start on Player1Parts (8), RagdollSetup should move to 13 ──
            GameObject lowerLegL  = new GameObject("LowerLeg_L");
            lowerLegL.transform.SetParent(upperLegL.transform);
            lowerLegL.transform.localPosition = new Vector3(0f, -0.38f, 0f);
            lowerLegL.layer = GameSettings.LayerPlayer1Parts;
            lowerLegL.AddComponent<Rigidbody>();
            lowerLegL.AddComponent<CapsuleCollider>();
            ConfigurableJoint llJointL = lowerLegL.AddComponent<ConfigurableJoint>();
            llJointL.connectedBody = upperLegLRb;
            llJointL.xMotion = ConfigurableJointMotion.Locked;
            llJointL.yMotion = ConfigurableJointMotion.Locked;
            llJointL.zMotion = ConfigurableJointMotion.Locked;

            // ── UpperLeg_R ──
            GameObject upperLegR  = new GameObject("UpperLeg_R");
            upperLegR.transform.SetParent(hips.transform);
            upperLegR.transform.localPosition = new Vector3(0.1f, -0.22f, 0f);
            Rigidbody upperLegRRb = upperLegR.AddComponent<Rigidbody>();
            upperLegR.AddComponent<CapsuleCollider>();
            ConfigurableJoint ulJointR = upperLegR.AddComponent<ConfigurableJoint>();
            ulJointR.connectedBody = hipsRb;
            ulJointR.xMotion = ConfigurableJointMotion.Locked;
            ulJointR.yMotion = ConfigurableJointMotion.Locked;
            ulJointR.zMotion = ConfigurableJointMotion.Locked;

            // ── LowerLeg_R — start on Player1Parts (8), RagdollSetup should move to 13 ──
            GameObject lowerLegR  = new GameObject("LowerLeg_R");
            lowerLegR.transform.SetParent(upperLegR.transform);
            lowerLegR.transform.localPosition = new Vector3(0f, -0.38f, 0f);
            lowerLegR.layer = GameSettings.LayerPlayer1Parts;
            lowerLegR.AddComponent<Rigidbody>();
            lowerLegR.AddComponent<CapsuleCollider>();
            ConfigurableJoint llJointR = lowerLegR.AddComponent<ConfigurableJoint>();
            llJointR.connectedBody = upperLegRRb;
            llJointR.xMotion = ConfigurableJointMotion.Locked;
            llJointR.yMotion = ConfigurableJointMotion.Locked;
            llJointR.zMotion = ConfigurableJointMotion.Locked;

            // DESIGN: RagdollSetup added LAST so Awake runs against the completed hierarchy.
            RagdollSetup setup = hips.AddComponent<RagdollSetup>();
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
            Assert.That(setup.AllBodies.Count, Is.EqualTo(2),
                "A two-body ragdoll should report exactly 2 Rigidbodies.");

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
    }
}
