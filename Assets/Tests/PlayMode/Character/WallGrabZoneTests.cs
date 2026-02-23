using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using PhysicsDrivenMovement.Character;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// PlayMode tests for <see cref="HandGrabZone"/> static collider (wall) support.
    /// Verifies static detection, trigger exclusion, exit cleanup,
    /// world grab joint creation, and world grab joint destruction.
    /// </summary>
    public class WallGrabZoneTests
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
        /// Creates a minimal hand GameObject with HandGrabZone, plus a root with Rigidbody
        /// to serve as the ragdoll hierarchy. Returns the HandGrabZone.
        /// </summary>
        private HandGrabZone CreateHandWithZone(Vector3 position, out GameObject handGo)
        {
            GameObject root = Track(new GameObject("TestRoot"));
            root.transform.position = position;
            Rigidbody rootRb = root.AddComponent<Rigidbody>();
            rootRb.isKinematic = true;
            root.AddComponent<BoxCollider>();

            handGo = new GameObject("Hand_L");
            handGo.transform.SetParent(root.transform);
            handGo.transform.localPosition = Vector3.zero;
            Rigidbody handRb = handGo.AddComponent<Rigidbody>();
            handRb.isKinematic = true;
            handGo.AddComponent<BoxCollider>();

            // Add trigger sphere.
            SphereCollider trigger = handGo.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 0.15f;

            HandGrabZone zone = handGo.AddComponent<HandGrabZone>();

            // Inject self-bodies set.
            var selfBodies = new HashSet<Rigidbody> { rootRb, handRb };
            zone.SetSelfBodiesForTest(selfBodies);

            return zone;
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
        public IEnumerator StaticCollider_Detected_WhenInTriggerRange()
        {
            HandGrabZone zone = CreateHandWithZone(new Vector3(0f, 50f, 0f), out GameObject handGo);
            // Place wall inside the trigger radius.
            CreateStaticWall(handGo.transform.position + new Vector3(0.05f, 0f, 0f),
                new Vector3(0.1f, 0.5f, 0.5f));

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Assert.That(zone.NearestStaticCollider, Is.Not.Null,
                "HandGrabZone should detect a static BoxCollider (wall) overlapping its trigger.");
        }

        [UnityTest]
        public IEnumerator StaticTrigger_Excluded_FromOverlap()
        {
            HandGrabZone zone = CreateHandWithZone(new Vector3(0f, 50f, 0f), out GameObject handGo);

            // Create a static trigger collider (no RB, isTrigger = true) — should NOT be tracked.
            GameObject triggerGo = Track(new GameObject("StaticTrigger"));
            triggerGo.transform.position = handGo.transform.position + new Vector3(0.05f, 0f, 0f);
            BoxCollider col = triggerGo.AddComponent<BoxCollider>();
            col.isTrigger = true;

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Assert.That(zone.NearestStaticCollider, Is.Null,
                "Static trigger colliders (isTrigger=true, no RB) should NOT appear in NearestStaticCollider.");
        }

        [UnityTest]
        public IEnumerator StaticCollider_Removed_OnTriggerExit()
        {
            HandGrabZone zone = CreateHandWithZone(new Vector3(0f, 50f, 0f), out GameObject handGo);
            GameObject wall = CreateStaticWall(
                handGo.transform.position + new Vector3(0.05f, 0f, 0f),
                new Vector3(0.1f, 0.5f, 0.5f));

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Assert.That(zone.NearestStaticCollider, Is.Not.Null,
                "Precondition: wall should be detected initially.");

            // Move wall far away so it exits the trigger.
            wall.transform.position = new Vector3(100f, 100f, 100f);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Assert.That(zone.NearestStaticCollider, Is.Null,
                "After the wall exits the trigger, NearestStaticCollider should be null.");
        }

        [UnityTest]
        public IEnumerator CreateWorldGrabJoint_SetsIsWorldGrab()
        {
            HandGrabZone zone = CreateHandWithZone(new Vector3(0f, 50f, 0f), out GameObject handGo);
            yield return new WaitForFixedUpdate();

            Vector3 anchor = handGo.transform.position;
            bool created = zone.CreateWorldGrabJoint(anchor, 2000f, 2000f);

            Assert.That(created, Is.True,
                "CreateWorldGrabJoint should return true when no joint exists.");
            Assert.That(zone.IsGrabbing, Is.True,
                "IsGrabbing should be true after creating a world grab joint.");
            Assert.That(zone.IsWorldGrab, Is.True,
                "IsWorldGrab should be true after creating a world grab joint.");
            Assert.That(zone.GrabbedTarget, Is.Null,
                "GrabbedTarget should be null for a world grab (connectedBody is null).");
        }

        [UnityTest]
        public IEnumerator DestroyGrabJoint_ClearsIsWorldGrab()
        {
            HandGrabZone zone = CreateHandWithZone(new Vector3(0f, 50f, 0f), out GameObject handGo);
            yield return new WaitForFixedUpdate();

            Vector3 anchor = handGo.transform.position;
            zone.CreateWorldGrabJoint(anchor, 2000f, 2000f);

            Assert.That(zone.IsWorldGrab, Is.True, "Precondition: IsWorldGrab should be true.");

            zone.DestroyGrabJoint();

            Assert.That(zone.IsGrabbing, Is.False,
                "IsGrabbing should be false after destroying the grab joint.");
            Assert.That(zone.IsWorldGrab, Is.False,
                "IsWorldGrab should be false after destroying the grab joint.");
        }
    }
}
