using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using PhysicsDrivenMovement.Character;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// PlayMode tests for <see cref="PunchController"/> (Phase 4E).
    /// Verifies impulse applied, gated when fallen, duration restores drives,
    /// and cooldown enforced.
    /// </summary>
    public class PunchControllerTests
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
        /// Creates a minimal Hips with right arm chain, BalanceController, PlayerMovement,
        /// CharacterState, and PunchController.
        /// </summary>
        private PunchController CreatePunchRig(out GameObject hipsGo, out Rigidbody handRbR)
        {
            hipsGo = Track(new GameObject("Hips"));
            hipsGo.transform.position = new Vector3(0f, 50f, 0f);
            Rigidbody hipsRb = hipsGo.AddComponent<Rigidbody>();
            hipsRb.isKinematic = true;
            hipsGo.AddComponent<BoxCollider>();

            // Add feet for GroundSensor / BalanceController.
            for (int i = 0; i < 2; i++)
            {
                GameObject foot = new GameObject(i == 0 ? "Foot_L" : "Foot_R");
                foot.transform.SetParent(hipsGo.transform);
                foot.transform.localPosition = new Vector3(i == 0 ? -0.1f : 0.1f, -0.4f, 0f);
                foot.AddComponent<Rigidbody>().isKinematic = true;
                foot.AddComponent<BoxCollider>();
                foot.AddComponent<GroundSensor>();
            }

            // Right arm chain.
            GameObject upperArmR = CreateSegmentWithJoint("UpperArm_R", hipsGo, 100f);
            GameObject lowerArmR = CreateSegmentWithJoint("LowerArm_R", upperArmR, 80f);
            GameObject handR = CreateSegmentWithJoint("Hand_R", lowerArmR, 50f);

            handRbR = handR.GetComponent<Rigidbody>();

            // Add dependencies in order.
            hipsGo.AddComponent<BalanceController>();
            hipsGo.AddComponent<PlayerMovement>();
            hipsGo.AddComponent<CharacterState>();
            PunchController pc = hipsGo.AddComponent<PunchController>();

            return pc;
        }

        private GameObject CreateSegmentWithJoint(string name, GameObject parent, float spring)
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

        // ─── Tests ──────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator PunchInput_StartsPunch_WhenStanding()
        {
            PunchController pc = CreatePunchRig(out GameObject hipsGo, out Rigidbody handRb);

            // Let everything initialize.
            yield return null;
            yield return new WaitForFixedUpdate();

            // Override BalanceController to report grounded + not fallen (Standing).
            BalanceController bc = hipsGo.GetComponent<BalanceController>();
            bc.SetGroundStateForTest(true, false);
            yield return new WaitForFixedUpdate();

            pc.SetPunchInputForTest(true);
            yield return new WaitForFixedUpdate();

            Assert.That(pc.IsPunching, Is.True,
                "PunchController should enter punching state when punch input fires and character is Standing.");
        }

        [UnityTest]
        public IEnumerator Punch_GatedWhenFallen()
        {
            PunchController pc = CreatePunchRig(out GameObject hipsGo, out _);

            yield return null;
            yield return new WaitForFixedUpdate();

            // Set fallen state.
            BalanceController bc = hipsGo.GetComponent<BalanceController>();
            bc.SetGroundStateForTest(true, true);
            yield return new WaitForFixedUpdate();

            pc.SetPunchInputForTest(true);
            yield return new WaitForFixedUpdate();

            Assert.That(pc.IsPunching, Is.False,
                "Punch should be blocked when the character is fallen.");
        }

        [UnityTest]
        public IEnumerator Punch_Duration_RestoresDrives()
        {
            PunchController pc = CreatePunchRig(out GameObject hipsGo, out _);

            yield return null;
            yield return new WaitForFixedUpdate();

            BalanceController bc = hipsGo.GetComponent<BalanceController>();
            bc.SetGroundStateForTest(true, false);
            yield return new WaitForFixedUpdate();

            pc.SetPunchInputForTest(true);
            yield return new WaitForFixedUpdate();

            Assert.That(pc.IsPunching, Is.True);

            // Wait for punch duration to expire (default 0.3s + some margin).
            float waitTime = 0f;
            while (pc.IsPunching && waitTime < 1f)
            {
                yield return new WaitForFixedUpdate();
                waitTime += Time.fixedDeltaTime;
            }

            Assert.That(pc.IsPunching, Is.False,
                "Punch should end after the duration timer expires.");
        }

        [UnityTest]
        public IEnumerator Punch_Cooldown_Enforced()
        {
            PunchController pc = CreatePunchRig(out GameObject hipsGo, out _);

            yield return null;
            yield return new WaitForFixedUpdate();

            BalanceController bc = hipsGo.GetComponent<BalanceController>();
            bc.SetGroundStateForTest(true, false);
            yield return new WaitForFixedUpdate();

            // First punch.
            pc.SetPunchInputForTest(true);
            yield return new WaitForFixedUpdate();
            Assert.That(pc.IsPunching, Is.True, "First punch should start.");

            // Wait for punch to end.
            float waitTime = 0f;
            while (pc.IsPunching && waitTime < 1f)
            {
                yield return new WaitForFixedUpdate();
                waitTime += Time.fixedDeltaTime;
            }

            // Immediately try another punch — should be blocked by cooldown.
            pc.SetPunchInputForTest(true);
            yield return new WaitForFixedUpdate();
            Assert.That(pc.IsPunching, Is.False,
                "Second punch should be blocked by cooldown.");
        }
    }
}
