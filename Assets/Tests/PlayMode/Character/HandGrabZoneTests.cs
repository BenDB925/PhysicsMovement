using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using PhysicsDrivenMovement.Character;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// PlayMode tests for <see cref="HandGrabZone"/>.
    /// Verifies trigger creation, external overlap detection, self-exclusion,
    /// exit cleanup, and nearest-of-multiple selection.
    /// </summary>
    public class HandGrabZoneTests
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

        private GameObject CreateExternalTarget(Vector3 position, string name = "Target")
        {
            GameObject go = Track(new GameObject(name));
            go.transform.position = position;
            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            go.AddComponent<SphereCollider>();
            return go;
        }

        // ─── Tests ──────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator TriggerSphere_IsCreated_OnAwake()
        {
            HandGrabZone zone = CreateHandWithZone(new Vector3(0f, 50f, 0f), out _);
            yield return new WaitForFixedUpdate();

            Assert.That(zone.TriggerCollider, Is.Not.Null,
                "HandGrabZone should have a trigger SphereCollider after Awake.");
            Assert.That(zone.TriggerCollider.isTrigger, Is.True,
                "The SphereCollider should be marked as trigger.");
        }

        [UnityTest]
        public IEnumerator ExternalOverlap_Detected()
        {
            HandGrabZone zone = CreateHandWithZone(new Vector3(0f, 50f, 0f), out GameObject handGo);
            // Place target inside the trigger radius.
            CreateExternalTarget(handGo.transform.position + new Vector3(0.05f, 0f, 0f));

            // Wait for physics to process trigger events.
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Assert.That(zone.NearestTarget, Is.Not.Null,
                "HandGrabZone should detect an external Rigidbody overlapping its trigger.");
        }

        [UnityTest]
        public IEnumerator SelfBody_Excluded()
        {
            HandGrabZone zone = CreateHandWithZone(new Vector3(0f, 50f, 0f), out GameObject handGo);
            // The root's Rigidbody is in _selfBodies — it should not appear as NearestTarget.
            // Place a self-body collider overlapping the zone.
            // (The root already has a collider at the same position as the hand.)

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Assert.That(zone.NearestTarget, Is.Null,
                "HandGrabZone should exclude self-ragdoll bodies from the overlap list.");
        }

        [UnityTest]
        public IEnumerator ExitCleanup_RemovesFromList()
        {
            HandGrabZone zone = CreateHandWithZone(new Vector3(0f, 50f, 0f), out GameObject handGo);
            GameObject target = CreateExternalTarget(handGo.transform.position + new Vector3(0.05f, 0f, 0f));

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Assert.That(zone.NearestTarget, Is.Not.Null, "Target should be detected initially.");

            // Move target far away so it exits the trigger.
            target.transform.position = new Vector3(100f, 100f, 100f);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Assert.That(zone.NearestTarget, Is.Null,
                "After the target exits the trigger, NearestTarget should be null.");
        }

        [UnityTest]
        public IEnumerator NearestOfMultiple_ReturnsClosest()
        {
            HandGrabZone zone = CreateHandWithZone(new Vector3(0f, 50f, 0f), out GameObject handGo);
            Vector3 handPos = handGo.transform.position;

            // Place two targets at different distances.
            GameObject near = CreateExternalTarget(handPos + new Vector3(0.05f, 0f, 0f), "NearTarget");
            GameObject far = CreateExternalTarget(handPos + new Vector3(0.12f, 0f, 0f), "FarTarget");

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Rigidbody nearRb = near.GetComponent<Rigidbody>();
            Rigidbody nearest = zone.NearestTarget;

            Assert.That(nearest, Is.Not.Null, "At least one target should be detected.");
            Assert.That(nearest, Is.EqualTo(nearRb),
                "NearestTarget should return the closest overlapping Rigidbody.");
        }
    }
}
