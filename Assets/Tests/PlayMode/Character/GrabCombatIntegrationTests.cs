using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using PhysicsDrivenMovement.Character;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// Integration tests for Phase 4 grab + combat systems.
    /// Tests multi-component interactions: grab-and-hold, punch-to-knockout,
    /// grab-and-throw, and knockout-recovery cycle.
    /// </summary>
    public class GrabCombatIntegrationTests
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
        /// Creates a minimal ragdoll with Hips, arm chain (both sides), Head,
        /// and all Phase 4 components.
        /// </summary>
        private GameObject CreateFullRagdoll(Vector3 position, string prefix = "P1")
        {
            GameObject hips = Track(new GameObject($"{prefix}_Hips"));
            hips.transform.position = position;
            Rigidbody hipsRb = hips.AddComponent<Rigidbody>();
            hipsRb.isKinematic = true;
            hips.AddComponent<BoxCollider>();

            // Feet
            for (int i = 0; i < 2; i++)
            {
                GameObject foot = new GameObject(i == 0 ? "Foot_L" : "Foot_R");
                foot.transform.SetParent(hips.transform);
                foot.transform.localPosition = new Vector3(i == 0 ? -0.1f : 0.1f, -0.4f, 0f);
                foot.AddComponent<Rigidbody>().isKinematic = true;
                foot.AddComponent<BoxCollider>();
                foot.AddComponent<GroundSensor>();
            }

            // Torso
            GameObject torso = CreateBodySegment("Torso", hips, 300f);

            // Head
            GameObject head = CreateBodySegment("Head", torso, 150f);
            head.GetComponent<BoxCollider>(); // Already added by helper
            head.AddComponent<HitReceiver>();

            // Left arm chain
            GameObject upperArmL = CreateBodySegment("UpperArm_L", torso, 100f);
            GameObject lowerArmL = CreateBodySegment("LowerArm_L", upperArmL, 80f);
            GameObject handL = CreateHandSegment("Hand_L", lowerArmL, 50f);

            // Right arm chain
            GameObject upperArmR = CreateBodySegment("UpperArm_R", torso, 100f);
            GameObject lowerArmR = CreateBodySegment("LowerArm_R", upperArmR, 80f);
            GameObject handR = CreateHandSegment("Hand_R", lowerArmR, 50f);

            // Self-body exclusion for hand zones.
            Rigidbody[] allBodies = hips.GetComponentsInChildren<Rigidbody>(true);
            var selfBodies = new HashSet<Rigidbody>(allBodies);
            foreach (HandGrabZone z in hips.GetComponentsInChildren<HandGrabZone>())
            {
                z.SetSelfBodiesForTest(selfBodies);
            }

            // Add Hips-level components.
            hips.AddComponent<RagdollSetup>();
            hips.AddComponent<BalanceController>();
            hips.AddComponent<PlayerMovement>();
            hips.AddComponent<CharacterState>();
            hips.AddComponent<GrabController>();
            hips.AddComponent<PunchController>();

            return hips;
        }

        private GameObject CreateBodySegment(string name, GameObject parent, float spring)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent.transform);
            go.transform.localPosition = new Vector3(0f, 0.2f, 0f);
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

        // ─── Tests ──────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator GrabAndHold_MaintainsGrabState()
        {
            GameObject p1 = CreateFullRagdoll(new Vector3(0f, 50f, 0f), "P1");
            GrabController gc = p1.GetComponent<GrabController>();

            // Create an external prop to grab.
            GameObject prop = Track(new GameObject("Prop"));
            prop.transform.position = gc.ZoneL.transform.position + new Vector3(0.05f, 0f, 0f);
            prop.AddComponent<Rigidbody>().isKinematic = true;
            prop.AddComponent<SphereCollider>();

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            gc.SetGrabInputForTest(true);
            yield return new WaitForFixedUpdate();

            Assert.That(gc.IsGrabbingLeft || gc.IsGrabbingRight, Is.True,
                "At least one hand should be grabbing the prop.");

            // Hold for several frames.
            for (int i = 0; i < 10; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            Assert.That(gc.IsGrabbingLeft || gc.IsGrabbingRight, Is.True,
                "Grab should persist while input is held.");
        }

        [UnityTest]
        public IEnumerator KnockoutRecoveryCycle()
        {
            GameObject p1 = CreateFullRagdoll(new Vector3(0f, 50f, 0f), "P1");
            HitReceiver hr = p1.GetComponentInChildren<HitReceiver>();
            Assert.That(hr, Is.Not.Null, "HitReceiver should exist on Head.");

            // Wait for baselines.
            yield return null;
            yield return new WaitForFixedUpdate();

            // Trigger knockout via test seam.
            hr.TriggerKnockoutForTest();
            yield return new WaitForFixedUpdate();

            Assert.That(hr.IsKnockedOut, Is.True, "Character should be knocked out.");

            // Wait for knockout to expire.
            System.Reflection.FieldInfo durationField = typeof(HitReceiver)
                .GetField("_knockoutDuration",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            float duration = (float)durationField.GetValue(hr);

            float waited = 0f;
            while (hr.IsKnockedOut && waited < duration + 2f)
            {
                yield return new WaitForFixedUpdate();
                waited += Time.fixedDeltaTime;
            }

            Assert.That(hr.IsKnockedOut, Is.False,
                "Character should have recovered from knockout after the duration.");
        }

        [UnityTest]
        public IEnumerator PunchController_Exists_AfterFullSetup()
        {
            GameObject p1 = CreateFullRagdoll(new Vector3(0f, 50f, 0f), "P1");
            PunchController pc = p1.GetComponent<PunchController>();

            yield return new WaitForFixedUpdate();

            Assert.That(pc, Is.Not.Null, "PunchController should be present on the ragdoll.");
        }
    }
}
