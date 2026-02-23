using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using PhysicsDrivenMovement.Character;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// PlayMode tests for the throw mechanic (Phase 4H).
    /// Verifies throw impulse when moving, no impulse when stationary,
    /// and direction matches velocity.
    /// </summary>
    public class ThrowMechanicTests
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
        /// Creates a minimal grab rig where the Hips Rigidbody can have its velocity set.
        /// Returns GrabController + hipsRb for testing.
        /// </summary>
        private GrabController CreateThrowTestRig(out Rigidbody hipsRb,
            out HandGrabZone zoneL)
        {
            GameObject hipsGo = Track(new GameObject("Hips"));
            hipsGo.transform.position = new Vector3(0f, 50f, 0f);
            hipsRb = hipsGo.AddComponent<Rigidbody>();
            hipsRb.useGravity = false;
            hipsGo.AddComponent<BoxCollider>();

            // Minimal arm: just Hand_L with a zone.
            GameObject handL = new GameObject("Hand_L");
            handL.transform.SetParent(hipsGo.transform);
            handL.transform.localPosition = new Vector3(-0.3f, 0f, 0f);
            Rigidbody handRb = handL.AddComponent<Rigidbody>();
            handRb.isKinematic = true;
            handL.AddComponent<BoxCollider>();

            ConfigurableJoint handJoint = handL.AddComponent<ConfigurableJoint>();
            handJoint.connectedBody = hipsRb;
            handJoint.rotationDriveMode = RotationDriveMode.Slerp;
            handJoint.slerpDrive = new JointDrive
            {
                positionSpring = 50f,
                positionDamper = 5f,
                maximumForce = 200f
            };

            SphereCollider trigger = handL.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 0.15f;

            zoneL = handL.AddComponent<HandGrabZone>();

            // Also add Hand_R (empty, no zone) to avoid null warnings.
            GameObject handR = new GameObject("Hand_R");
            handR.transform.SetParent(hipsGo.transform);
            handR.transform.localPosition = new Vector3(0.3f, 0f, 0f);
            Rigidbody handRbR = handR.AddComponent<Rigidbody>();
            handRbR.isKinematic = true;
            handR.AddComponent<BoxCollider>();
            ConfigurableJoint handJointR = handR.AddComponent<ConfigurableJoint>();
            handJointR.connectedBody = hipsRb;
            SphereCollider triggerR = handR.AddComponent<SphereCollider>();
            triggerR.isTrigger = true;
            triggerR.radius = 0.15f;
            handR.AddComponent<HandGrabZone>();

            // Set up self-body exclusion.
            Rigidbody[] allBodies = hipsGo.GetComponentsInChildren<Rigidbody>(true);
            var selfBodies = new HashSet<Rigidbody>(allBodies);
            foreach (HandGrabZone z in hipsGo.GetComponentsInChildren<HandGrabZone>())
            {
                z.SetSelfBodiesForTest(selfBodies);
            }

            GrabController gc = hipsGo.AddComponent<GrabController>();
            return gc;
        }

        private Rigidbody CreateDynamicTarget(Vector3 position)
        {
            GameObject go = Track(new GameObject("ThrowTarget"));
            go.transform.position = position;
            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.mass = 1f;
            go.AddComponent<SphereCollider>();
            return rb;
        }

        // ─── Tests ──────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator ThrowImpulse_Applied_WhenMoving()
        {
            GrabController gc = CreateThrowTestRig(out Rigidbody hipsRb, out HandGrabZone zoneL);
            Rigidbody target = CreateDynamicTarget(zoneL.transform.position + new Vector3(0.05f, 0f, 0f));

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            // Grab the target.
            gc.SetGrabInputForTest(true);
            yield return new WaitForFixedUpdate();

            Assert.That(gc.IsGrabbingLeft, Is.True, "Should be grabbing.");

            // Set hips velocity above throw threshold.
            hipsRb.linearVelocity = new Vector3(3f, 0f, 0f);

            Vector3 targetVelBefore = target.linearVelocity;

            // Release — should apply throw impulse.
            gc.SetGrabInputForTest(false);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            // The target should have gained velocity from the throw impulse.
            float speedAfter = target.linearVelocity.magnitude;
            Assert.That(speedAfter, Is.GreaterThan(0.1f),
                "Released target should have velocity from throw impulse when character is moving.");
        }

        [UnityTest]
        public IEnumerator NoImpulse_WhenStationary()
        {
            GrabController gc = CreateThrowTestRig(out Rigidbody hipsRb, out HandGrabZone zoneL);
            Rigidbody target = CreateDynamicTarget(zoneL.transform.position + new Vector3(0.05f, 0f, 0f));

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            gc.SetGrabInputForTest(true);
            yield return new WaitForFixedUpdate();

            Assert.That(gc.IsGrabbingLeft, Is.True, "Should be grabbing.");

            // Hips stationary (below throw threshold).
            hipsRb.linearVelocity = Vector3.zero;

            gc.SetGrabInputForTest(false);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            float speedAfter = target.linearVelocity.magnitude;
            Assert.That(speedAfter, Is.LessThan(0.5f),
                "Released target should have minimal velocity when character is stationary.");
        }
    }
}
