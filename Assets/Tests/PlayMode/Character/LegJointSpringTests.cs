using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PhysicsDrivenMovement.Character;
using UnityEditor;
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
        private const string PlayerRagdollPrefabPath = "Assets/Prefabs/PlayerRagdoll_Skinned.prefab";

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
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerRagdollPrefabPath);
            Assert.That(prefab, Is.Not.Null,
                $"PlayerRagdoll prefab must be loadable from '{PlayerRagdollPrefabPath}'.");

            _hips = Object.Instantiate(prefab, new Vector3(1400f, 1.1f, 1400f), Quaternion.identity);
            _upperLegLJoint = FindJointByName(_hips, "UpperLeg_L");
            _upperLegRJoint = FindJointByName(_hips, "UpperLeg_R");
            _lowerLegLJoint = FindJointByName(_hips, "LowerLeg_L");
            _lowerLegRJoint = FindJointByName(_hips, "LowerLeg_R");

            Assert.That(_hips.GetComponent<RagdollSetup>(), Is.Not.Null,
                "PlayerRagdoll prefab must include RagdollSetup.");

            LegAnimator legAnimator = _hips.GetComponent<LegAnimator>();
            if (legAnimator != null)
            {
                legAnimator.enabled = false;
            }
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
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerRagdollPrefabPath);
            Assert.That(prefab, Is.Not.Null,
                $"PlayerRagdoll prefab must be loadable from '{PlayerRagdollPrefabPath}'.");

            GameObject hips2 = Object.Instantiate(prefab, new Vector3(1410f, 1.1f, 1410f), Quaternion.identity);
            RagdollSetup setup2 = hips2.GetComponent<RagdollSetup>();
            Assert.That(setup2, Is.Not.Null, "PlayerRagdoll prefab must include RagdollSetup.");

            LegAnimator legAnimator2 = hips2.GetComponent<LegAnimator>();
            if (legAnimator2 != null)
            {
                legAnimator2.enabled = false;
            }

            ConfigurableJoint lowerLL2 = FindJointByName(hips2, "LowerLeg_L");

            const float customSpring = 999f;
            using (var so = new UnityEditor.SerializedObject(setup2))
            {
                so.FindProperty("_lowerLegSpring").floatValue = customSpring;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            InvokeApplyLegJointDrives(setup2);

            // Act
            yield return null;

            // Assert
            float actualSpring = lowerLL2.slerpDrive.positionSpring;
            Assert.That(actualSpring, Is.EqualTo(customSpring).Within(0.01f),
                $"LowerLeg_L slerpDrive.positionSpring must match the serialized _lowerLegSpring value. " +
                $"Expected {customSpring}, got {actualSpring}.");

            Object.Destroy(hips2);
        }

        private static ConfigurableJoint FindJointByName(GameObject root, string name)
        {
            ConfigurableJoint[] joints = root.GetComponentsInChildren<ConfigurableJoint>(includeInactive: true);
            for (int i = 0; i < joints.Length; i++)
            {
                if (joints[i].gameObject.name == name)
                {
                    return joints[i];
                }
            }

            throw new System.InvalidOperationException($"Required joint '{name}' not found under '{root.name}'.");
        }

        private static void InvokeApplyLegJointDrives(RagdollSetup setup)
        {
            MethodInfo method = typeof(RagdollSetup).GetMethod("ApplyLegJointDrives",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null,
                "RagdollSetup must have a private ApplyLegJointDrives method so runtime joint drives can be refreshed from serialized values.");

            method.Invoke(setup, null);
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
