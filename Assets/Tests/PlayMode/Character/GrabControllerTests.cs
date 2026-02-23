using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using PhysicsDrivenMovement.Character;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// PlayMode tests for <see cref="GrabController"/>.
    /// Verifies zone discovery, input-to-grab-state mapping, no-target behaviour,
    /// and release clearing state.
    /// </summary>
    public class GrabControllerTests
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
        /// Builds a minimal ragdoll hierarchy with Hips, two arms down to Hand_L/Hand_R,
        /// each Hand having a HandGrabZone. GrabController is attached to Hips.
        /// </summary>
        private GrabController CreateMinimalGrabRig(out GameObject hipsGo,
            out HandGrabZone zoneL, out HandGrabZone zoneR)
        {
            hipsGo = Track(new GameObject("Hips"));
            hipsGo.transform.position = new Vector3(0f, 50f, 0f);
            Rigidbody hipsRb = hipsGo.AddComponent<Rigidbody>();
            hipsRb.isKinematic = true;
            hipsGo.AddComponent<BoxCollider>();

            // Left arm chain: UpperArm_L → LowerArm_L → Hand_L
            GameObject upperArmL = CreateArmSegment("UpperArm_L", hipsGo);
            GameObject lowerArmL = CreateArmSegment("LowerArm_L", upperArmL);
            GameObject handL = CreateHandSegment("Hand_L", lowerArmL);

            // Right arm chain: UpperArm_R → LowerArm_R → Hand_R
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

            // Add ConfigurableJoint connected to parent.
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

            // ConfigurableJoint
            ConfigurableJoint joint = go.AddComponent<ConfigurableJoint>();
            joint.connectedBody = parent.GetComponent<Rigidbody>();
            joint.rotationDriveMode = RotationDriveMode.Slerp;
            joint.slerpDrive = new JointDrive
            {
                positionSpring = 50f,
                positionDamper = 5f,
                maximumForce = 200f
            };

            // Trigger sphere for grab detection.
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

        // ─── Tests ──────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator FindsZones_InAwake()
        {
            GrabController gc = CreateMinimalGrabRig(out _, out _, out _);
            yield return new WaitForFixedUpdate();

            Assert.That(gc.ZoneL, Is.Not.Null, "GrabController should find Hand_L zone.");
            Assert.That(gc.ZoneR, Is.Not.Null, "GrabController should find Hand_R zone.");
        }

        [UnityTest]
        public IEnumerator GrabInput_TriggersGrabState_WhenTargetPresent()
        {
            GrabController gc = CreateMinimalGrabRig(out GameObject hipsGo, out HandGrabZone zoneL, out _);
            // Place a target near Hand_L.
            GameObject handLGo = zoneL.gameObject;
            CreateExternalTarget(handLGo.transform.position + new Vector3(0.05f, 0f, 0f));

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            // Press grab input.
            gc.SetGrabInputForTest(true);
            yield return new WaitForFixedUpdate();

            Assert.That(gc.IsGrabbingLeft, Is.True,
                "IsGrabbingLeft should be true when grab input is held and a target is in range.");
        }

        [UnityTest]
        public IEnumerator NoTarget_NoGrab()
        {
            GrabController gc = CreateMinimalGrabRig(out _, out _, out _);
            yield return new WaitForFixedUpdate();

            // Press grab with no target in range.
            gc.SetGrabInputForTest(true);
            yield return new WaitForFixedUpdate();

            Assert.That(gc.IsGrabbingLeft, Is.False,
                "IsGrabbingLeft should remain false when no target is in range.");
            Assert.That(gc.IsGrabbingRight, Is.False,
                "IsGrabbingRight should remain false when no target is in range.");
        }

        [UnityTest]
        public IEnumerator Release_ClearsGrabState()
        {
            GrabController gc = CreateMinimalGrabRig(out _, out HandGrabZone zoneL, out _);
            GameObject handLGo = zoneL.gameObject;
            CreateExternalTarget(handLGo.transform.position + new Vector3(0.05f, 0f, 0f));

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            // Grab.
            gc.SetGrabInputForTest(true);
            yield return new WaitForFixedUpdate();

            Assert.That(gc.IsGrabbingLeft, Is.True, "Should be grabbing.");

            // Release.
            gc.SetGrabInputForTest(false);
            yield return new WaitForFixedUpdate();

            Assert.That(gc.IsGrabbingLeft, Is.False,
                "IsGrabbingLeft should be false after releasing grab input.");
        }
    }
}
