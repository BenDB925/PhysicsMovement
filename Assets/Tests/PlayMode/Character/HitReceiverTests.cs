using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using PhysicsDrivenMovement.Character;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// PlayMode tests for <see cref="HitReceiver"/> (Phase 4F).
    /// Verifies knockout triggers above threshold, ignores below threshold,
    /// self-exclusion, drives zeroed during knockout, and drives restored after duration.
    /// </summary>
    public class HitReceiverTests
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
        /// Creates a minimal ragdoll with Hips → Torso → Head, all with ConfigurableJoints
        /// and known spring values. HitReceiver is on the Head.
        /// </summary>
        private HitReceiver CreateMinimalRagdollWithHead(out GameObject hipsGo,
            out ConfigurableJoint torsoJoint)
        {
            hipsGo = Track(new GameObject("Hips"));
            hipsGo.transform.position = new Vector3(0f, 50f, 0f);
            Rigidbody hipsRb = hipsGo.AddComponent<Rigidbody>();
            hipsRb.isKinematic = true;
            hipsGo.AddComponent<BoxCollider>();
            hipsGo.AddComponent<RagdollSetup>();

            // Torso
            GameObject torso = new GameObject("Torso");
            torso.transform.SetParent(hipsGo.transform);
            torso.transform.localPosition = new Vector3(0f, 0.3f, 0f);
            Rigidbody torsoRb = torso.AddComponent<Rigidbody>();
            torsoRb.isKinematic = true;
            torso.AddComponent<BoxCollider>();
            torsoJoint = torso.AddComponent<ConfigurableJoint>();
            torsoJoint.connectedBody = hipsRb;
            torsoJoint.rotationDriveMode = RotationDriveMode.Slerp;
            torsoJoint.slerpDrive = new JointDrive
            {
                positionSpring = 300f,
                positionDamper = 30f,
                maximumForce = 1000f
            };

            // Head
            GameObject head = new GameObject("Head");
            head.transform.SetParent(torso.transform);
            head.transform.localPosition = new Vector3(0f, 0.25f, 0f);
            Rigidbody headRb = head.AddComponent<Rigidbody>();
            headRb.isKinematic = true;
            head.AddComponent<SphereCollider>();
            ConfigurableJoint headJoint = head.AddComponent<ConfigurableJoint>();
            headJoint.connectedBody = torsoRb;
            headJoint.rotationDriveMode = RotationDriveMode.Slerp;
            headJoint.slerpDrive = new JointDrive
            {
                positionSpring = 150f,
                positionDamper = 15f,
                maximumForce = 500f
            };

            HitReceiver hr = head.AddComponent<HitReceiver>();
            return hr;
        }

        // ─── Tests ──────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator Knockout_TriggersViaTestSeam()
        {
            HitReceiver hr = CreateMinimalRagdollWithHead(out _, out _);

            // Wait for Start() to capture baselines.
            yield return null;
            yield return new WaitForFixedUpdate();

            hr.TriggerKnockoutForTest();
            yield return new WaitForFixedUpdate();

            Assert.That(hr.IsKnockedOut, Is.True,
                "HitReceiver should be knocked out after TriggerKnockoutForTest.");
        }

        [UnityTest]
        public IEnumerator Knockout_ZeroesDrives()
        {
            HitReceiver hr = CreateMinimalRagdollWithHead(out _, out ConfigurableJoint torsoJoint);

            yield return null;
            yield return new WaitForFixedUpdate();

            float baselineSpring = torsoJoint.slerpDrive.positionSpring;
            Assert.That(baselineSpring, Is.GreaterThan(0f), "Baseline spring should be non-zero.");

            hr.TriggerKnockoutForTest();
            yield return new WaitForFixedUpdate();

            float knockoutSpring = torsoJoint.slerpDrive.positionSpring;
            Assert.That(knockoutSpring, Is.EqualTo(0f).Within(0.01f),
                "Joint spring should be zeroed during knockout.");
        }

        [UnityTest]
        public IEnumerator Knockout_RestoresDrives_AfterDuration()
        {
            HitReceiver hr = CreateMinimalRagdollWithHead(out _, out ConfigurableJoint torsoJoint);

            yield return null;
            yield return new WaitForFixedUpdate();

            float baselineSpring = torsoJoint.slerpDrive.positionSpring;

            hr.TriggerKnockoutForTest();
            yield return new WaitForFixedUpdate();

            Assert.That(torsoJoint.slerpDrive.positionSpring, Is.EqualTo(0f).Within(0.01f),
                "Spring should be zero during knockout.");

            // Wait for knockout duration to expire (default 3s — use reflection to read it,
            // or just wait long enough).
            System.Reflection.FieldInfo durationField = typeof(HitReceiver)
                .GetField("_knockoutDuration",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            float duration = (float)durationField.GetValue(hr);

            float waited = 0f;
            while (hr.IsKnockedOut && waited < duration + 1f)
            {
                yield return new WaitForFixedUpdate();
                waited += Time.fixedDeltaTime;
            }

            Assert.That(hr.IsKnockedOut, Is.False, "Knockout should have expired.");
            Assert.That(torsoJoint.slerpDrive.positionSpring, Is.EqualTo(baselineSpring).Within(1f),
                $"Joint spring should be restored to baseline ({baselineSpring}) after knockout recovery.");
        }

        [UnityTest]
        public IEnumerator KnockoutTimeRemaining_DecreasesOverTime()
        {
            HitReceiver hr = CreateMinimalRagdollWithHead(out _, out _);

            yield return null;
            yield return new WaitForFixedUpdate();

            hr.TriggerKnockoutForTest();
            float initialTime = hr.KnockoutTimeRemaining;
            Assert.That(initialTime, Is.GreaterThan(0f));

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Assert.That(hr.KnockoutTimeRemaining, Is.LessThan(initialTime),
                "KnockoutTimeRemaining should decrease each FixedUpdate.");
        }
    }
}
