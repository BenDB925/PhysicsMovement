using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using PhysicsDrivenMovement.Character;
using UnityEngine;
using UnityEngine.TestTools;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// PlayMode tests verifying that <see cref="RagdollSetup"/> configures leg
    /// ConfigurableJoints with SLERP drive mode and spring values strong enough to
    /// actively resist gravity during gait animation.
    ///
    /// Minimum spring threshold rationale:
    ///   LowerLeg mass = 2.5 kg, half-length ≈ 0.17 m, gravity = 9.81 m/s²
    ///   Gravity torque at full horizontal extension ≈ 2.5 × 9.81 × 0.17 ≈ 4.2 Nm
    ///   At 100 Hz, spring must generate >> 4 Nm. At spring=800, a 1-rad (57°) error
    ///   produces ~800 Nm — vastly exceeding gravity. Spring ≥ 800 is a safe minimum
    ///   that ensures the joint wins over gravity at any reasonable gait pose.
    ///   The threshold is kept below the tunable default (1200) so tests pass even if
    ///   the Inspector value is reduced slightly during tuning.
    /// </summary>
    public class LegJointSpringTests
    {
        // ─── Minimum Thresholds ───────────────────────────────────────────────

        /// <summary>
        /// Minimum SLERP spring value required on UpperLeg joints to resist gravity
        /// during gait. Below this, the joint cannot maintain a target rotation against
        /// the weight of the limb.
        /// </summary>
        private const float MinUpperLegSpring = 800f;

        /// <summary>
        /// Minimum SLERP spring value required on LowerLeg joints to resist gravity.
        /// LowerLeg is lighter than UpperLeg but experiences compound inertia from the
        /// foot; a minimum of 800 provides adequate margin.
        /// </summary>
        private const float MinLowerLegSpring = 800f;

        // ─── Test Rig ─────────────────────────────────────────────────────────

        private GameObject _hips;
        private ConfigurableJoint _upperLegLJoint;
        private ConfigurableJoint _upperLegRJoint;
        private ConfigurableJoint _lowerLegLJoint;
        private ConfigurableJoint _lowerLegRJoint;

        [SetUp]
        public void SetUp()
        {
            // Build a minimal 5-body ragdoll:
            // Hips → UpperLeg_L → LowerLeg_L
            //      → UpperLeg_R → LowerLeg_R
            // This mirrors the structure RagdollSetup.ApplyLegJointDrives() searches.

            _hips = new GameObject("Hips");
            Rigidbody hipsRb = _hips.AddComponent<Rigidbody>();
            hipsRb.useGravity = false;

            // Build the leg hierarchy BEFORE adding RagdollSetup so Awake sees it.
            _upperLegLJoint = CreateLegJoint(_hips, "UpperLeg_L", hipsRb);
            _upperLegRJoint = CreateLegJoint(_hips, "UpperLeg_R", hipsRb);

            Rigidbody upperLRb = _upperLegLJoint.GetComponent<Rigidbody>();
            Rigidbody upperRRb = _upperLegRJoint.GetComponent<Rigidbody>();

            _lowerLegLJoint = CreateLegJoint(_upperLegLJoint.gameObject, "LowerLeg_L", upperLRb);
            _lowerLegRJoint = CreateLegJoint(_upperLegRJoint.gameObject, "LowerLeg_R", upperRRb);

            // Add RagdollSetup last so its Awake() fires with the full hierarchy present.
            _hips.AddComponent<RagdollSetup>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_hips != null)
            {
                Object.Destroy(_hips);
            }
        }

        // ─── RotationDriveMode Tests ──────────────────────────────────────────

        [UnityTest]
        public IEnumerator UpperLegL_AfterAwake_UsesSlerpDriveMode()
        {
            // Arrange
            yield return null;

            // Assert
            Assert.That(_upperLegLJoint.rotationDriveMode, Is.EqualTo(RotationDriveMode.Slerp),
                "UpperLeg_L ConfigurableJoint must use RotationDriveMode.Slerp so that " +
                "LegAnimator.targetRotation is honoured by the SLERP drive.");
        }

        [UnityTest]
        public IEnumerator UpperLegR_AfterAwake_UsesSlerpDriveMode()
        {
            // Arrange
            yield return null;

            // Assert
            Assert.That(_upperLegRJoint.rotationDriveMode, Is.EqualTo(RotationDriveMode.Slerp),
                "UpperLeg_R ConfigurableJoint must use RotationDriveMode.Slerp so that " +
                "LegAnimator.targetRotation is honoured by the SLERP drive.");
        }

        [UnityTest]
        public IEnumerator LowerLegL_AfterAwake_UsesSlerpDriveMode()
        {
            // Arrange
            yield return null;

            // Assert
            Assert.That(_lowerLegLJoint.rotationDriveMode, Is.EqualTo(RotationDriveMode.Slerp),
                "LowerLeg_L ConfigurableJoint must use RotationDriveMode.Slerp so that " +
                "LegAnimator.targetRotation is honoured by the SLERP drive.");
        }

        [UnityTest]
        public IEnumerator LowerLegR_AfterAwake_UsesSlerpDriveMode()
        {
            // Arrange
            yield return null;

            // Assert
            Assert.That(_lowerLegRJoint.rotationDriveMode, Is.EqualTo(RotationDriveMode.Slerp),
                "LowerLeg_R ConfigurableJoint must use RotationDriveMode.Slerp so that " +
                "LegAnimator.targetRotation is honoured by the SLERP drive.");
        }

        // ─── Spring Minimum Tests ─────────────────────────────────────────────

        [UnityTest]
        public IEnumerator UpperLegL_AfterAwake_SlerpSpringMeetsMinimumThreshold()
        {
            // Arrange
            yield return null;

            // Act
            float spring = _upperLegLJoint.slerpDrive.positionSpring;

            // Assert
            Assert.That(spring, Is.GreaterThanOrEqualTo(MinUpperLegSpring),
                $"UpperLeg_L SLERP spring ({spring}) must be >= {MinUpperLegSpring} to resist gravity during gait.");
        }

        [UnityTest]
        public IEnumerator UpperLegR_AfterAwake_SlerpSpringMeetsMinimumThreshold()
        {
            // Arrange
            yield return null;

            // Act
            float spring = _upperLegRJoint.slerpDrive.positionSpring;

            // Assert
            Assert.That(spring, Is.GreaterThanOrEqualTo(MinUpperLegSpring),
                $"UpperLeg_R SLERP spring ({spring}) must be >= {MinUpperLegSpring} to resist gravity during gait.");
        }

        [UnityTest]
        public IEnumerator LowerLegL_AfterAwake_SlerpSpringMeetsMinimumThreshold()
        {
            // Arrange
            yield return null;

            // Act
            float spring = _lowerLegLJoint.slerpDrive.positionSpring;

            // Assert
            Assert.That(spring, Is.GreaterThanOrEqualTo(MinLowerLegSpring),
                $"LowerLeg_L SLERP spring ({spring}) must be >= {MinLowerLegSpring} to resist gravity during gait. " +
                $"Lower legs were hanging limp — spring too weak to overcome gravity torque.");
        }

        [UnityTest]
        public IEnumerator LowerLegR_AfterAwake_SlerpSpringMeetsMinimumThreshold()
        {
            // Arrange
            yield return null;

            // Act
            float spring = _lowerLegRJoint.slerpDrive.positionSpring;

            // Assert
            Assert.That(spring, Is.GreaterThanOrEqualTo(MinLowerLegSpring),
                $"LowerLeg_R SLERP spring ({spring}) must be >= {MinLowerLegSpring} to resist gravity during gait. " +
                $"Lower legs were hanging limp — spring too weak to overcome gravity torque.");
        }

        // ─── Serialized Field Tests ───────────────────────────────────────────

        [UnityTest]
        public IEnumerator RagdollSetup_HasSerializedUpperLegSpringField()
        {
            // Arrange
            yield return null;

            // Act — verify the field is exposed as [SerializeField] (visible to SerializedObject)
            using var so = new UnityEditor.SerializedObject(_hips.GetComponent<RagdollSetup>());
            UnityEditor.SerializedProperty prop = so.FindProperty("_upperLegSpring");

            // Assert
            Assert.That(prop, Is.Not.Null,
                "RagdollSetup must expose a [SerializeField] field named '_upperLegSpring' " +
                "so the upper leg joint spring can be tuned in the Inspector.");
        }

        [UnityTest]
        public IEnumerator RagdollSetup_HasSerializedLowerLegSpringField()
        {
            // Arrange
            yield return null;

            // Act
            using var so = new UnityEditor.SerializedObject(_hips.GetComponent<RagdollSetup>());
            UnityEditor.SerializedProperty prop = so.FindProperty("_lowerLegSpring");

            // Assert
            Assert.That(prop, Is.Not.Null,
                "RagdollSetup must expose a [SerializeField] field named '_lowerLegSpring' " +
                "so the lower leg joint spring can be tuned in the Inspector.");
        }

        [UnityTest]
        public IEnumerator RagdollSetup_HasSerializedUpperLegDamperField()
        {
            // Arrange
            yield return null;

            // Act
            using var so = new UnityEditor.SerializedObject(_hips.GetComponent<RagdollSetup>());
            UnityEditor.SerializedProperty prop = so.FindProperty("_upperLegDamper");

            // Assert
            Assert.That(prop, Is.Not.Null,
                "RagdollSetup must expose a [SerializeField] field named '_upperLegDamper'.");
        }

        [UnityTest]
        public IEnumerator RagdollSetup_HasSerializedLowerLegDamperField()
        {
            // Arrange
            yield return null;

            // Act
            using var so = new UnityEditor.SerializedObject(_hips.GetComponent<RagdollSetup>());
            UnityEditor.SerializedProperty prop = so.FindProperty("_lowerLegDamper");

            // Assert
            Assert.That(prop, Is.Not.Null,
                "RagdollSetup must expose a [SerializeField] field named '_lowerLegDamper'.");
        }

        // ─── Drive Applied to Joint Tests ────────────────────────────────────

        /// <summary>
        /// Verifies that RagdollSetup.ApplyLegJointDrives() respects the serialized
        /// spring value: changing _lowerLegSpring before Awake fires should result
        /// in the joint using that custom spring value.
        /// This test uses SerializedObject to set the field (mirrors Inspector usage).
        /// </summary>
        [UnityTest]
        public IEnumerator RagdollSetup_LowerLegSpringIsAppliedToJoint_AfterAwake()
        {
            // Arrange — build a NEW rig (the SetUp rig already fired Awake)
            // We need to set the serialized field BEFORE Awake runs.
            // Build the hierarchy without RagdollSetup, set the field, then add RagdollSetup.

            GameObject hips2 = new GameObject("Hips2");
            Rigidbody hipsRb2 = hips2.AddComponent<Rigidbody>();
            hipsRb2.useGravity = false;

            ConfigurableJoint upperLL2 = CreateLegJoint(hips2, "UpperLeg_L", hipsRb2);
            ConfigurableJoint upperRR2 = CreateLegJoint(hips2, "UpperLeg_R", hipsRb2);
            ConfigurableJoint lowerLL2 = CreateLegJoint(upperLL2.gameObject, "LowerLeg_L",
                upperLL2.GetComponent<Rigidbody>());
            ConfigurableJoint lowerLR2 = CreateLegJoint(upperRR2.gameObject, "LowerLeg_R",
                upperRR2.GetComponent<Rigidbody>());

            // Add RagdollSetup and then use SerializedObject to set spring before Awake
            // NOTE: In PlayMode tests AddComponent fires Awake immediately on an active GO.
            // To set the field before Awake, we add to an inactive GO.
            hips2.SetActive(false);
            RagdollSetup setup2 = hips2.AddComponent<RagdollSetup>();

            const float customSpring = 999f;
            using (var so = new UnityEditor.SerializedObject(setup2))
            {
                so.FindProperty("_lowerLegSpring").floatValue = customSpring;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            // Activate to trigger Awake
            hips2.SetActive(true);

            // Act
            yield return null;

            // Assert
            float actualSpring = lowerLL2.slerpDrive.positionSpring;
            Assert.That(actualSpring, Is.EqualTo(customSpring).Within(0.01f),
                $"LowerLeg_L slerpDrive.positionSpring must match the serialized _lowerLegSpring value. " +
                $"Expected {customSpring}, got {actualSpring}.");

            Object.Destroy(hips2);
        }

        // ─── Helper ───────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a child GameObject with Rigidbody and ConfigurableJoint (Slerp mode)
        /// connected to <paramref name="parentRb"/>, with minimal valid configuration.
        /// </summary>
        private static ConfigurableJoint CreateLegJoint(
            GameObject parent, string name, Rigidbody parentRb)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent.transform);
            go.transform.localPosition = new Vector3(0f, -0.4f, 0f);
            go.AddComponent<BoxCollider>();
            go.AddComponent<Rigidbody>();

            ConfigurableJoint joint = go.AddComponent<ConfigurableJoint>();
            joint.connectedBody     = parentRb;
            joint.xMotion           = ConfigurableJointMotion.Locked;
            joint.yMotion           = ConfigurableJointMotion.Locked;
            joint.zMotion           = ConfigurableJointMotion.Locked;
            joint.rotationDriveMode = RotationDriveMode.Slerp;
            joint.slerpDrive        = new JointDrive
            {
                positionSpring = 100f, // deliberately weak — Awake must override this
                positionDamper = 10f,
                maximumForce   = float.MaxValue,
            };
            return joint;
        }
    }
}
