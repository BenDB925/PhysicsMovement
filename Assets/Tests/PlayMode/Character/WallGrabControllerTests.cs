using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using PhysicsDrivenMovement.Character;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// PlayMode tests for <see cref="GrabController"/> wall-grab integration.
    /// Verifies static fallback, dynamic preference, per-hand wall-grab state,
    /// no-throw on wall release, and world-grab joint survival across FixedUpdate.
    /// </summary>
    public class WallGrabControllerTests
    {
        private readonly List<GameObject> _cleanup = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _cleanup)
            {
                if (go != null) Object.Destroy(go);
            }
            _cleanup.Clear();
        }

        private GameObject Track(GameObject go)
        {
            _cleanup.Add(go);
            return go;
        }

        /// <summary>
        /// Builds a minimal ragdoll hierarchy with Hips and Hand_L/Hand_R.
        /// Each Hand has HandGrabZone + trigger sphere. GrabController on Hips.
        /// </summary>
        private GrabController CreateMinimalGrabRig(out GameObject hipsGo,
            out HandGrabZone zoneL, out HandGrabZone zoneR)
        {
            hipsGo = Track(new GameObject("Hips"));
            hipsGo.transform.position = new Vector3(0f, 50f, 0f);
            Rigidbody hipsRb = hipsGo.AddComponent<Rigidbody>();
            hipsRb.isKinematic = true;
            hipsGo.AddComponent<BoxCollider>();

            // Left arm chain: UpperArm_L -> LowerArm_L -> Hand_L
            GameObject upperArmL = CreateArmSegment("UpperArm_L", hipsGo);
            GameObject lowerArmL = CreateArmSegment("LowerArm_L", upperArmL);
            GameObject handL = CreateHandSegment("Hand_L", lowerArmL);

            // Right arm chain: UpperArm_R -> LowerArm_R -> Hand_R
            GameObject upperArmR = CreateArmSegment("UpperArm_R", hipsGo);
            GameObject lowerArmR = CreateArmSegment("LowerArm_R", upperArmR);
            GameObject handR = CreateHandSegment("Hand_R", lowerArmR);

            // Collect all self-bodies for zone exclusion.
            Rigidbody[] allBodies = hipsGo.GetComponentsInChildren<Rigidbody>(true);
            var selfBodies = new HashSet<Rigidbody>(allBodies);

            zoneL = handL.GetComponent<HandGrabZone>();
            zoneR = handR.GetComponent<HandGrabZone>();
            zoneL.SetSelfBodiesForTest(selfBodies);
            zoneR.SetSelfBodiesForTest(selfBodies);

            // Add GrabController LAST so Awake finds the zones.
            GrabController gc = hipsGo.AddComponent<GrabController>();
            return gc;
        }

        private GameObject CreateArmSegment(string name, GameObject parent)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent.transform);
            go.transform.localPosition = new Vector3(0f, -0.2f, 0f);
            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            go.AddComponent<CapsuleCollider>();

            ConfigurableJoint joint = go.AddComponent<ConfigurableJoint>();
            joint.connectedBody = parent.GetComponent<Rigidbody>();
            joint.rotationDriveMode = RotationDriveMode.Slerp;
            joint.slerpDrive = new JointDrive
            {
                positionSpring = 100f,
                positionDamper = 10f,
                maximumForce = 400f
            };

            return go;
        }

        private GameObject CreateHandSegment(string name, GameObject parent)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent.transform);
            go.transform.localPosition = new Vector3(0f, -0.2f, 0f);
            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            go.AddComponent<BoxCollider>();

            ConfigurableJoint joint = go.AddComponent<ConfigurableJoint>();
            joint.connectedBody = parent.GetComponent<Rigidbody>();
            joint.rotationDriveMode = RotationDriveMode.Slerp;
            joint.slerpDrive = new JointDrive
            {
                positionSpring = 50f,
                positionDamper = 5f,
                maximumForce = 200f
            };

            SphereCollider trigger = go.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 0.15f;

            go.AddComponent<HandGrabZone>();
            return go;
        }

        private GameObject CreateExternalTarget(Vector3 position, string name = "Target")
        {
            GameObject go = Track(new GameObject(name));
            go.transform.position = position;
            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            go.AddComponent<SphereCollider>();
            return go;
        }

        /// <summary>
        /// Creates a static wall (BoxCollider, no Rigidbody) at the given position.
        /// </summary>
        private GameObject CreateStaticWall(Vector3 position, Vector3 scale)
        {
            GameObject wall = Track(new GameObject("StaticWall"));
            wall.transform.position = position;
            wall.transform.localScale = scale;
            wall.AddComponent<BoxCollider>();
            return wall;
        }

        // ─── Tests ──────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator GrabInput_FallsBackToStatic_WhenNoDynamicTarget()
        {
            GrabController gc = CreateMinimalGrabRig(out _, out HandGrabZone zoneL, out _);
            // Place a wall near Hand_L (no dynamic target).
            CreateStaticWall(
                zoneL.transform.position + new Vector3(0.05f, 0f, 0f),
                new Vector3(0.1f, 0.5f, 0.5f));

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            // Press grab.
            gc.SetGrabInputForTest(true);
            yield return new WaitForFixedUpdate();

            Assert.That(gc.IsGrabbingLeft, Is.True,
                "Should be grabbing when only a static wall is in range.");
            Assert.That(gc.IsWallGrabbing, Is.True,
                "IsWallGrabbing should be true when grabbing a static wall.");
        }

        [UnityTest]
        public IEnumerator GrabInput_PrefersDynamic_OverStatic()
        {
            GrabController gc = CreateMinimalGrabRig(out _, out HandGrabZone zoneL, out _);
            Vector3 handPos = zoneL.transform.position;

            // Place both a dynamic target and a static wall near Hand_L.
            CreateExternalTarget(handPos + new Vector3(0.05f, 0f, 0f), "DynamicTarget");
            CreateStaticWall(handPos + new Vector3(0.08f, 0f, 0f),
                new Vector3(0.1f, 0.5f, 0.5f));

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            gc.SetGrabInputForTest(true);
            yield return new WaitForFixedUpdate();

            Assert.That(gc.IsGrabbingLeft, Is.True,
                "Should be grabbing when a dynamic target is in range.");
            Assert.That(gc.IsWallGrabbing, Is.False,
                "IsWallGrabbing should be false when a dynamic target was preferred over the wall.");
            Assert.That(gc.GrabbedTargetLeft, Is.Not.Null,
                "GrabbedTargetLeft should reference the dynamic Rigidbody.");
        }

        [UnityTest]
        public IEnumerator IsWallGrabbingLeft_True_WhenLeftHandGrabsWall()
        {
            GrabController gc = CreateMinimalGrabRig(out _, out HandGrabZone zoneL, out _);
            // Place wall near Hand_L only.
            CreateStaticWall(
                zoneL.transform.position + new Vector3(0.05f, 0f, 0f),
                new Vector3(0.1f, 0.5f, 0.5f));

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            // Only grab with left hand.
            gc.SetGrabInputForTest(leftHeld: true, rightHeld: false);
            yield return new WaitForFixedUpdate();

            Assert.That(gc.IsWallGrabbingLeft, Is.True,
                "IsWallGrabbingLeft should be true when the left hand grabs a wall.");
            Assert.That(gc.IsWallGrabbingRight, Is.False,
                "IsWallGrabbingRight should be false when only the left hand grabbed.");
        }

        [UnityTest]
        public IEnumerator Release_FromWallGrab_NoThrowImpulse()
        {
            GrabController gc = CreateMinimalGrabRig(out GameObject hipsGo, out HandGrabZone zoneL, out _);
            Rigidbody hipsRb = hipsGo.GetComponent<Rigidbody>();
            hipsRb.isKinematic = false;
            hipsRb.useGravity = false;

            CreateStaticWall(
                zoneL.transform.position + new Vector3(0.05f, 0f, 0f),
                new Vector3(0.1f, 0.5f, 0.5f));

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            // Grab the wall.
            gc.SetGrabInputForTest(true);
            yield return new WaitForFixedUpdate();

            Assert.That(gc.IsWallGrabbing, Is.True, "Precondition: should be wall-grabbing.");

            // Give hips velocity to test that throw does NOT fire for wall grabs.
            hipsRb.linearVelocity = new Vector3(5f, 0f, 0f);

            // Release.
            gc.SetGrabInputForTest(false);
            yield return new WaitForFixedUpdate();

            // Wall-grab releases call TryApplyThrowImpulse with null target (GrabbedTarget
            // is null for world grabs), so no impulse should be applied to anything.
            // The test simply verifies the release completes without error and grab state clears.
            Assert.That(gc.IsGrabbingLeft, Is.False,
                "IsGrabbingLeft should be false after releasing wall grab.");
            Assert.That(gc.IsWallGrabbing, Is.False,
                "IsWallGrabbing should be false after releasing wall grab.");
        }

        [UnityTest]
        public IEnumerator WorldGrab_SurvivesFixedUpdate_WithoutBeingDestroyed()
        {
            GrabController gc = CreateMinimalGrabRig(out _, out HandGrabZone zoneL, out _);
            CreateStaticWall(
                zoneL.transform.position + new Vector3(0.05f, 0f, 0f),
                new Vector3(0.1f, 0.5f, 0.5f));

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            // Grab the wall.
            gc.SetGrabInputForTest(true);
            yield return new WaitForFixedUpdate();

            Assert.That(gc.IsWallGrabbing, Is.True, "Precondition: should be wall-grabbing.");

            // Run several FixedUpdates — the world-grab joint (connectedBody=null)
            // should NOT be destroyed by the null-body prune in HandGrabZone.FixedUpdate.
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Assert.That(gc.IsGrabbingLeft, Is.True,
                "World-grab joint should survive multiple FixedUpdates without being pruned.");
            Assert.That(gc.IsWallGrabbing, Is.True,
                "IsWallGrabbing should remain true across FixedUpdates.");
        }
    }
}
