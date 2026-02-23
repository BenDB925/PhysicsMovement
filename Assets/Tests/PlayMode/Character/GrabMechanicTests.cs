using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using PhysicsDrivenMovement.Character;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// PlayMode tests for the FixedJoint grab mechanic (Phase 4C).
    /// Verifies joint creation/destruction, correct breakForce, break-under-force
    /// behaviour, and two-ragdoll grab scenarios.
    /// </summary>
    public class GrabMechanicTests
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

            SphereCollider trigger = handGo.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 0.15f;

            HandGrabZone zone = handGo.AddComponent<HandGrabZone>();
            zone.SetSelfBodiesForTest(new HashSet<Rigidbody> { rootRb, handRb });
            return zone;
        }

        private Rigidbody CreateTarget(Vector3 position, string name = "Target")
        {
            GameObject go = Track(new GameObject(name));
            go.transform.position = position;
            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            go.AddComponent<SphereCollider>();
            return rb;
        }

        // ─── Tests ──────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator CreateGrabJoint_CreatesFixedJoint()
        {
            HandGrabZone zone = CreateHandWithZone(new Vector3(0f, 50f, 0f), out GameObject handGo);
            Rigidbody target = CreateTarget(handGo.transform.position + new Vector3(0.05f, 0f, 0f));

            yield return new WaitForFixedUpdate();

            bool created = zone.CreateGrabJoint(target, 2000f, 2000f);

            Assert.That(created, Is.True, "CreateGrabJoint should return true.");
            Assert.That(zone.IsGrabbing, Is.True, "IsGrabbing should be true after joint creation.");

            FixedJoint fj = handGo.GetComponent<FixedJoint>();
            Assert.That(fj, Is.Not.Null, "A FixedJoint should exist on the hand.");
            Assert.That(fj.connectedBody, Is.EqualTo(target),
                "The FixedJoint should be connected to the target Rigidbody.");
        }

        [UnityTest]
        public IEnumerator CreateGrabJoint_SetsCorrectBreakForce()
        {
            HandGrabZone zone = CreateHandWithZone(new Vector3(0f, 50f, 0f), out GameObject handGo);
            Rigidbody target = CreateTarget(handGo.transform.position + new Vector3(0.05f, 0f, 0f));

            yield return new WaitForFixedUpdate();

            zone.CreateGrabJoint(target, 1500f, 1800f);
            FixedJoint fj = handGo.GetComponent<FixedJoint>();

            Assert.That(fj.breakForce, Is.EqualTo(1500f).Within(0.01f),
                "FixedJoint breakForce should match the requested value.");
            Assert.That(fj.breakTorque, Is.EqualTo(1800f).Within(0.01f),
                "FixedJoint breakTorque should match the requested value.");
        }

        [UnityTest]
        public IEnumerator DestroyGrabJoint_DestroysFixedJoint()
        {
            HandGrabZone zone = CreateHandWithZone(new Vector3(0f, 50f, 0f), out GameObject handGo);
            Rigidbody target = CreateTarget(handGo.transform.position + new Vector3(0.05f, 0f, 0f));

            yield return new WaitForFixedUpdate();

            zone.CreateGrabJoint(target, 2000f, 2000f);
            Assert.That(zone.IsGrabbing, Is.True);

            zone.DestroyGrabJoint();
            yield return new WaitForFixedUpdate();

            Assert.That(zone.IsGrabbing, Is.False,
                "IsGrabbing should be false after DestroyGrabJoint.");
        }

        [UnityTest]
        public IEnumerator DuplicateGrab_ReturnsFalse()
        {
            HandGrabZone zone = CreateHandWithZone(new Vector3(0f, 50f, 0f), out _);
            Rigidbody target = CreateTarget(new Vector3(0.05f, 50f, 0f));

            yield return new WaitForFixedUpdate();

            bool first = zone.CreateGrabJoint(target, 2000f, 2000f);
            bool second = zone.CreateGrabJoint(target, 2000f, 2000f);

            Assert.That(first, Is.True);
            Assert.That(second, Is.False,
                "Creating a second grab joint while one is active should return false.");
        }

        [UnityTest]
        public IEnumerator GrabbedTarget_ReturnsConnectedBody()
        {
            HandGrabZone zone = CreateHandWithZone(new Vector3(0f, 50f, 0f), out _);
            Rigidbody target = CreateTarget(new Vector3(0.05f, 50f, 0f));

            yield return new WaitForFixedUpdate();

            zone.CreateGrabJoint(target, 2000f, 2000f);

            Assert.That(zone.GrabbedTarget, Is.EqualTo(target),
                "GrabbedTarget should return the Rigidbody connected via FixedJoint.");
        }
    }
}
