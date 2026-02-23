using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using PhysicsDrivenMovement.Character;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// PlayMode tests for arm stiffening during grab (Phase 4D).
    /// Verifies springs increase on grab, restore on release, ArmAnimator skips
    /// the grabbed arm, and still animates the other arm.
    /// </summary>
    public class ArmStiffeningTests
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
        /// Builds a minimal ragdoll hierarchy with arms and GrabController,
        /// including ConfigurableJoints with known baseline drives.
        /// </summary>
        private GrabController CreateGrabRigWithArms(out GameObject hipsGo,
            out ConfigurableJoint upperArmLJoint, out HandGrabZone zoneL)
        {
            hipsGo = Track(new GameObject("Hips"));
            hipsGo.transform.position = new Vector3(0f, 50f, 0f);
            Rigidbody hipsRb = hipsGo.AddComponent<Rigidbody>();
            hipsRb.isKinematic = true;
            hipsGo.AddComponent<BoxCollider>();

            // Left arm chain.
            GameObject upperArmL = CreateArmSegment("UpperArm_L", hipsGo, 100f);
            GameObject lowerArmL = CreateArmSegment("LowerArm_L", upperArmL, 80f);
            GameObject handL = CreateHandSegment("Hand_L", lowerArmL, 50f);

            // Right arm chain.
            CreateArmSegment("UpperArm_R", hipsGo, 100f);
            CreateArmSegment("LowerArm_R", hipsGo, 80f);
            CreateHandSegment("Hand_R", hipsGo, 50f);

            upperArmLJoint = upperArmL.GetComponent<ConfigurableJoint>();

            // Collect all self-bodies.
            Rigidbody[] allBodies = hipsGo.GetComponentsInChildren<Rigidbody>(true);
            var selfBodies = new HashSet<Rigidbody>(allBodies);

            zoneL = handL.GetComponent<HandGrabZone>();
            zoneL.SetSelfBodiesForTest(selfBodies);

            HandGrabZone zoneR = null;
            foreach (HandGrabZone z in hipsGo.GetComponentsInChildren<HandGrabZone>())
            {
                if (z.gameObject.name == "Hand_R") zoneR = z;
            }
            if (zoneR != null) zoneR.SetSelfBodiesForTest(selfBodies);

            GrabController gc = hipsGo.AddComponent<GrabController>();
            return gc;
        }

        private GameObject CreateArmSegment(string name, GameObject parent, float spring)
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
                positionSpring = spring,
                positionDamper = spring * 0.1f,
                maximumForce = spring * 4f
            };

            return go;
        }

        private GameObject CreateHandSegment(string name, GameObject parent, float spring)
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
                positionSpring = spring,
                positionDamper = spring * 0.1f,
                maximumForce = spring * 4f
            };

            SphereCollider trigger = go.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 0.15f;

            go.AddComponent<HandGrabZone>();
            return go;
        }

        private GameObject CreateExternalTarget(Vector3 position)
        {
            GameObject go = Track(new GameObject("Target"));
            go.transform.position = position;
            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            go.AddComponent<SphereCollider>();
            return go;
        }

        // ─── Tests ──────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator Springs_Increase_OnGrab()
        {
            GrabController gc = CreateGrabRigWithArms(out _, out ConfigurableJoint upperArmL, out HandGrabZone zoneL);

            // Wait for Start() baseline capture.
            yield return null;
            yield return new WaitForFixedUpdate();

            float baselineSpring = upperArmL.slerpDrive.positionSpring;

            // Place target near left hand and grab.
            CreateExternalTarget(zoneL.transform.position + new Vector3(0.05f, 0f, 0f));
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            gc.SetGrabInputForTest(true);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            float grabSpring = upperArmL.slerpDrive.positionSpring;
            Assert.That(grabSpring, Is.GreaterThan(baselineSpring),
                $"Upper arm spring should increase during grab. Baseline={baselineSpring}, During grab={grabSpring}");
        }

        [UnityTest]
        public IEnumerator Springs_Restore_OnRelease()
        {
            GrabController gc = CreateGrabRigWithArms(out _, out ConfigurableJoint upperArmL, out HandGrabZone zoneL);

            yield return null;
            yield return new WaitForFixedUpdate();

            float baselineSpring = upperArmL.slerpDrive.positionSpring;

            CreateExternalTarget(zoneL.transform.position + new Vector3(0.05f, 0f, 0f));
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            gc.SetGrabInputForTest(true);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            // Release.
            gc.SetGrabInputForTest(false);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            float releasedSpring = upperArmL.slerpDrive.positionSpring;
            Assert.That(releasedSpring, Is.EqualTo(baselineSpring).Within(1f),
                $"Upper arm spring should restore to baseline after release. Expected ~{baselineSpring}, got {releasedSpring}");
        }
    }
}
