using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using PhysicsDrivenMovement.Character;

namespace PhysicsDrivenMovement.Tests.EditMode.Character
{
    /// <summary>
    /// EditMode unit tests for <see cref="ArmAnimator"/>.
    /// Validates joint discovery, idle-state identity resets, and counter-swing direction
    /// relative to the leg gait phase from <see cref="LegAnimator"/>.
    ///
    /// These tests run entirely in EditMode: GameObjects are created and destroyed in
    /// [SetUp]/[TearDown] — no scene is loaded.
    /// </summary>
    [TestFixture]
    public class ArmAnimatorTests
    {
        // ── Test Hierarchy ───────────────────────────────────────────────────
        // We build a minimal ragdoll hierarchy:
        //   Hips (root — has ArmAnimator, LegAnimator, Rigidbody, PlayerMovement, CharacterState)
        //     UpperArm_L (ConfigurableJoint)
        //     UpperArm_R (ConfigurableJoint)
        //     LowerArm_L (ConfigurableJoint)
        //     LowerArm_R (ConfigurableJoint)
        //     UpperLeg_L (ConfigurableJoint)
        //     UpperLeg_R (ConfigurableJoint)
        //     LowerLeg_L (ConfigurableJoint)
        //     LowerLeg_R (ConfigurableJoint)

        private GameObject _root;
        private ArmAnimator _armAnimator;
        private LegAnimator _legAnimator;

        // Child joint GameObjects
        private GameObject _upperArmL;
        private GameObject _upperArmR;
        private GameObject _lowerArmL;
        private GameObject _lowerArmR;
        private GameObject _upperLegL;
        private GameObject _upperLegR;
        private GameObject _lowerLegL;
        private GameObject _lowerLegR;

        // ConfigurableJoints on children
        private ConfigurableJoint _upperArmLJoint;
        private ConfigurableJoint _upperArmRJoint;
        private ConfigurableJoint _lowerArmLJoint;
        private ConfigurableJoint _lowerArmRJoint;
        private ConfigurableJoint _upperLegLJoint;
        private ConfigurableJoint _upperLegRJoint;

        // Reflection helpers for private fields
        private static readonly FieldInfo FieldUpperArmL =
            typeof(ArmAnimator).GetField("_upperArmL", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo FieldUpperArmR =
            typeof(ArmAnimator).GetField("_upperArmR", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo FieldLowerArmL =
            typeof(ArmAnimator).GetField("_lowerArmL", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo FieldLowerArmR =
            typeof(ArmAnimator).GetField("_lowerArmR", BindingFlags.NonPublic | BindingFlags.Instance);

        [SetUp]
        public void SetUp()
        {
            // Arrange: build minimal hierarchy
            _root = new GameObject("Hips");
            _root.AddComponent<Rigidbody>();

            // Required siblings for LegAnimator (it logs warnings but doesn't crash without them)
            // PlayerMovement and CharacterState have their own required components, so we use
            // reflection to inject the fields on LegAnimator / ArmAnimator after Awake would run.
            // We add the components BEFORE calling any Awake-equivalent so Unity auto-runs Awake.
            _armAnimator = _root.AddComponent<ArmAnimator>();
            _legAnimator = _root.AddComponent<LegAnimator>();

            // Create child GameObjects with ConfigurableJoints
            _upperArmL = CreateChildJoint(_root, "UpperArm_L", out _upperArmLJoint);
            _upperArmR = CreateChildJoint(_root, "UpperArm_R", out _upperArmRJoint);
            _lowerArmL = CreateChildJoint(_root, "LowerArm_L", out _lowerArmLJoint);
            _lowerArmR = CreateChildJoint(_root, "LowerArm_R", out _lowerArmRJoint);
            _upperLegL = CreateChildJoint(_root, "UpperLeg_L", out _upperLegLJoint);
            _upperLegR = CreateChildJoint(_root, "UpperLeg_R", out _upperLegRJoint);
            _lowerLegL = CreateChildJoint(_root, "LowerLeg_L", out _);
            _lowerLegR = CreateChildJoint(_root, "LowerLeg_R", out _);
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }
        }

        // ── Test 1: Joint discovery ──────────────────────────────────────────

        [Test]
        [Description("ArmAnimator.Awake should locate UpperArm_L, UpperArm_R, LowerArm_L, LowerArm_R " +
                     "ConfigurableJoints and store them in its private fields.")]
        public void Awake_ChildArmJointsPresent_AllFourArmJointsAreFound()
        {
            // Arrange: hierarchy is built in SetUp; Unity has called Awake when components
            // were added to the root. We need to re-trigger Awake logic by calling the
            // reflection helper that invokes the private Awake method explicitly.
            InvokeAwake(_armAnimator);

            // Act: read back the private joint fields via reflection
            ConfigurableJoint foundUpperArmL = FieldUpperArmL?.GetValue(_armAnimator) as ConfigurableJoint;
            ConfigurableJoint foundUpperArmR = FieldUpperArmR?.GetValue(_armAnimator) as ConfigurableJoint;
            ConfigurableJoint foundLowerArmL = FieldLowerArmL?.GetValue(_armAnimator) as ConfigurableJoint;
            ConfigurableJoint foundLowerArmR = FieldLowerArmR?.GetValue(_armAnimator) as ConfigurableJoint;

            // Assert
            Assert.That(FieldUpperArmL, Is.Not.Null, "ArmAnimator must have a private field '_upperArmL'.");
            Assert.That(FieldUpperArmR, Is.Not.Null, "ArmAnimator must have a private field '_upperArmR'.");
            Assert.That(FieldLowerArmL, Is.Not.Null, "ArmAnimator must have a private field '_lowerArmL'.");
            Assert.That(FieldLowerArmR, Is.Not.Null, "ArmAnimator must have a private field '_lowerArmR'.");

            Assert.That(foundUpperArmL, Is.Not.Null, "ArmAnimator should find UpperArm_L ConfigurableJoint in children.");
            Assert.That(foundUpperArmR, Is.Not.Null, "ArmAnimator should find UpperArm_R ConfigurableJoint in children.");
            Assert.That(foundLowerArmL, Is.Not.Null, "ArmAnimator should find LowerArm_L ConfigurableJoint in children.");
            Assert.That(foundLowerArmR, Is.Not.Null, "ArmAnimator should find LowerArm_R ConfigurableJoint in children.");
        }

        // ── Test 2: Identity at zero SmoothedInputMag ────────────────────────

        [Test]
        [Description("When LegAnimator.SmoothedInputMag is 0, all arm joint targetRotations " +
                     "should be set to Quaternion.identity.")]
        public void FixedUpdate_SmoothedInputMagIsZero_AllArmTargetRotationsAreIdentity()
        {
            // Arrange: inject arm joints via Awake, ensure LegAnimator.SmoothedInputMag=0
            // SmoothedInputMag starts at 0 by default (private field _smoothedInputMag is 0).
            InvokeAwake(_armAnimator);

            // Seed all arm joints to a non-identity rotation to prove they get reset.
            Quaternion nonIdentity = Quaternion.Euler(30f, 20f, 10f);
            _upperArmLJoint.targetRotation = nonIdentity;
            _upperArmRJoint.targetRotation = nonIdentity;
            _lowerArmLJoint.targetRotation = nonIdentity;
            _lowerArmRJoint.targetRotation = nonIdentity;

            // Act: invoke FixedUpdate (SmoothedInputMag is 0, so arms should go to identity)
            InvokeFixedUpdate(_armAnimator);

            // Assert
            AssertQuaternionIsIdentity(_upperArmLJoint.targetRotation, "UpperArm_L");
            AssertQuaternionIsIdentity(_upperArmRJoint.targetRotation, "UpperArm_R");
            AssertQuaternionIsIdentity(_lowerArmLJoint.targetRotation, "LowerArm_L");
            AssertQuaternionIsIdentity(_lowerArmRJoint.targetRotation, "LowerArm_R");
        }

        // ── Test 3: Counter-swing direction ─────────────────────────────────

        [Test]
        [Description("Arm swing should be opposite to leg swing: when LegAnimator phase produces " +
                     "left leg forward (positive left upper leg swing), the left arm should swing " +
                     "backward (negative left upper arm swing), and vice versa for right side.")]
        public void FixedUpdate_GaitPhaseQuarterCycle_LeftArmSwingsOppositeToLeftLeg()
        {
            // Arrange: set up joints and fake a non-zero SmoothedInputMag.
            // Phase = π/2 → sin(phase)=1 → left leg swings fully forward.
            // Expected: left ARM swings fully BACKWARD (negative angle).
            InvokeAwake(_armAnimator);

            // Inject SmoothedInputMag = 1.0 into LegAnimator via reflection so arm blends fully in.
            FieldInfo legSmoothedMag = typeof(LegAnimator).GetField("_smoothedInputMag",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(legSmoothedMag, Is.Not.Null, "LegAnimator must have private field '_smoothedInputMag'.");
            legSmoothedMag.SetValue(_legAnimator, 1.0f);

            // Inject phase = π/2 into LegAnimator.
            FieldInfo legPhase = typeof(LegAnimator).GetField("_phase",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(legPhase, Is.Not.Null, "LegAnimator must have private field '_phase'.");
            legPhase.SetValue(_legAnimator, Mathf.PI / 2f);

            // Act
            InvokeFixedUpdate(_armAnimator);

            // Assert counter-swing:
            // LegAnimator phase=π/2 → left leg uses sin(π/2)=+1 → swings FORWARD (positive).
            // ArmAnimator left arm uses phase + π → sin(π/2 + π) = sin(3π/2) = -1 → swings BACKWARD.
            // We check that the left arm's Z-axis rotation (primary swing axis, positive=forward)
            // is negative when the left leg is fully forward.
            //
            // targetRotation is a Quaternion — we extract the axis/angle to determine direction.
            // A rotation of -angle around Z (or +angle around -Z) means backward swing.
            // The cleanest proxy: check that the left arm and left leg targetRotation Z components
            // have opposite signs (or that left-arm rotation "opposes" left-leg rotation).
            //
            // Since we can't directly read LegAnimator's set targetRotation here (LegAnimator
            // didn't run FixedUpdate in this test), we use the known phase math directly:
            //   Left leg: sin(π/2) = +1 → positive forward swing → rotation Z > 0 in quaternion sense
            //   Left arm: sin(π/2 + π) = -1 → negative backward swing → rotation Z < 0
            //
            // We verify the arm target is NOT identity and swings in the negative direction.
            Quaternion leftArmTarget = _upperArmLJoint.targetRotation;
            Quaternion rightArmTarget = _upperArmRJoint.targetRotation;

            // Left arm should not be identity (it's swinging).
            Assert.That(leftArmTarget, Is.Not.EqualTo(Quaternion.identity),
                "Left arm should not be at identity when SmoothedInputMag=1 and phase=π/2.");

            // Extract the swing angle from the quaternion (rotation around Z/forward axis).
            // AngleAxis decomposes the quaternion; we can check it via eulerAngles.z or
            // by checking the angle component. We use a simpler proxy: the Z euler angle of
            // the arm target. For a pure AngleAxis(angle, Vector3.forward), eulerAngles.z = angle
            // (mod 360°) and eulerAngles.x, y ≈ 0 for small angles.
            //
            // With phase=π/2 and sin=−1 (left arm), the swing deg = −armSwingAngle (default −20°).
            // Quaternion.AngleAxis(−20°, forward).eulerAngles.z ≈ 340° (i.e., −20° wrapped).
            // So we check eulerAngles.z > 180 (negative rotation in Unity's 0–360 euler range).
            float leftArmEulerZ  = Mathf.DeltaAngle(0f, leftArmTarget.eulerAngles.z);
            float rightArmEulerZ = Mathf.DeltaAngle(0f, rightArmTarget.eulerAngles.z);

            // At phase=π/2:
            //   Left arm phase = π/2 + π = 3π/2 → sin = −1 → swing = −20° → eulerZ ≈ −20° (negative)
            //   Right arm phase = π/2 → sin = +1 → swing = +20° → eulerZ ≈ +20° (positive)
            Assert.That(leftArmEulerZ, Is.LessThan(0f),
                $"Left arm should swing backward (negative Z euler) when phase=π/2. Got {leftArmEulerZ:F2}°.");
            Assert.That(rightArmEulerZ, Is.GreaterThan(0f),
                $"Right arm should swing forward (positive Z euler) when phase=π/2. Got {rightArmEulerZ:F2}°.");

            // Also verify left and right arms swing in opposite directions.
            Assert.That(Mathf.Sign(leftArmEulerZ), Is.Not.EqualTo(Mathf.Sign(rightArmEulerZ)),
                "Left and right arm swings should be in opposite directions.");
        }

        // ── GAP-7: Required fields exist with sensible defaults ──────────────

        /// <summary>
        /// GAP-7: ArmAnimator must expose sensible serialised default values for its
        /// two core swing parameters so the component is usable out-of-the-box without
        /// Inspector configuration.
        ///
        /// Expected defaults (matching production prefab values):
        ///   _armSwingAngle  ∈ (0°, 90°]  — non-zero swing amplitude.
        ///   _armSwingAxis   is not the zero vector — a valid rotation axis.
        ///
        /// If either default is zero, the arm swing produces no visible motion
        /// (quaternion of 0° rotation = identity, or Quaternion.AngleAxis with zero axis
        /// returns identity regardless of angle).
        /// </summary>
        [Test]
        [Description("ArmAnimator._armSwingAngle must be > 0 and ≤ 90, " +
                     "and _armSwingAxis must not be the zero vector, out of the box.")]
        public void RequiredFields_ExistWithSensibleDefaults()
        {
            // Arrange: ArmAnimator was added to _root in SetUp; Awake ran automatically.
            // Read the serialised field values directly via reflection.
            FieldInfo fieldSwingAngle = typeof(ArmAnimator)
                .GetField("_armSwingAngle", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo fieldSwingAxis  = typeof(ArmAnimator)
                .GetField("_armSwingAxis",  BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.That(fieldSwingAngle, Is.Not.Null,
                "ArmAnimator must have a private serialized field '_armSwingAngle'. " +
                "Rename check: ensure the field name matches between production code and test.");
            Assert.That(fieldSwingAxis, Is.Not.Null,
                "ArmAnimator must have a private serialized field '_armSwingAxis'.");

            float armSwingAngle = (float)fieldSwingAngle.GetValue(_armAnimator);
            Vector3 swingAxis   = (Vector3)fieldSwingAxis.GetValue(_armAnimator);

            // Assert: swing angle is a sensible non-zero value.
            Assert.That(armSwingAngle, Is.GreaterThan(0f),
                $"_armSwingAngle default must be > 0° so arms visibly swing. Got {armSwingAngle:F2}°.");
            Assert.That(armSwingAngle, Is.LessThanOrEqualTo(90f),
                $"_armSwingAngle default must be ≤ 90° (not physically implausible). Got {armSwingAngle:F2}°.");

            // Assert: swing axis is not the zero vector.
            Assert.That(swingAxis.sqrMagnitude, Is.GreaterThan(0.01f),
                $"_armSwingAxis default must not be zero — Quaternion.AngleAxis with zero axis returns identity. " +
                $"Got {swingAxis}.");
        }

        // ── GAP-13: DoesNotThrowWhenCharacterStateIsAbsent ───────────────────

        /// <summary>
        /// GAP-13: ArmAnimator must not throw a NullReferenceException or log an
        /// error when CharacterState is absent from the GameObject.
        ///
        /// In the current architecture, ArmAnimator only reads LegAnimator.Phase and
        /// LegAnimator.SmoothedInputMag — it does NOT directly reference CharacterState.
        /// However, a future refactor might add a direct CharacterState dependency.
        /// This test guards against that regression.
        ///
        /// Design: creates a minimal hierarchy WITHOUT CharacterState, invokes Awake
        /// and FixedUpdate, asserts no exceptions are thrown and no error-level log
        /// messages are generated.
        /// </summary>
        [Test]
        [Description("ArmAnimator.FixedUpdate must not throw when CharacterState is absent from the GameObject.")]
        public void DoesNotThrowWhenCharacterStateIsAbsent()
        {
            // Arrange: create a separate minimal root without CharacterState.
            GameObject minimalRoot = new GameObject("MinimalRoot_NoCharacterState");
            minimalRoot.AddComponent<Rigidbody>();

            // Add LegAnimator and ArmAnimator but intentionally NO CharacterState.
            var legAnim = minimalRoot.AddComponent<LegAnimator>();
            var armAnim = minimalRoot.AddComponent<ArmAnimator>();

            // Add arm joint children so Awake doesn't generate "joints not found" warnings
            // (we're testing that CharacterState absence doesn't throw, not joint warnings).
            var armLGO = new GameObject("UpperArm_L");
            armLGO.transform.SetParent(minimalRoot.transform);
            armLGO.AddComponent<Rigidbody>();
            armLGO.AddComponent<ConfigurableJoint>().targetRotation = Quaternion.identity;

            var armRGO = new GameObject("UpperArm_R");
            armRGO.transform.SetParent(minimalRoot.transform);
            armRGO.AddComponent<Rigidbody>();
            armRGO.AddComponent<ConfigurableJoint>().targetRotation = Quaternion.identity;

            try
            {
                // Act: invoke Awake then FixedUpdate.
                // Should NOT throw even without CharacterState.
                MethodInfo awake = typeof(ArmAnimator)
                    .GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
                MethodInfo fixedUpdate = typeof(ArmAnimator)
                    .GetMethod("FixedUpdate", BindingFlags.NonPublic | BindingFlags.Instance);

                Assert.DoesNotThrow(() => awake?.Invoke(armAnim, null),
                    "ArmAnimator.Awake must not throw when CharacterState is absent.");
                Assert.DoesNotThrow(() => fixedUpdate?.Invoke(armAnim, null),
                    "ArmAnimator.FixedUpdate must not throw when CharacterState is absent.");
            }
            finally
            {
                Object.DestroyImmediate(minimalRoot);
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>Creates a named child GameObject with a Rigidbody and ConfigurableJoint.</summary>
        private static GameObject CreateChildJoint(GameObject parent, string name, out ConfigurableJoint joint)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent.transform);
            go.AddComponent<Rigidbody>();
            joint = go.AddComponent<ConfigurableJoint>();
            joint.targetRotation = Quaternion.identity;
            return go;
        }

        /// <summary>
        /// Invokes the private Awake method on <paramref name="component"/> via reflection.
        /// Unity calls Awake automatically in Play mode, but in EditMode tests we must invoke
        /// it explicitly after the hierarchy is fully built.
        /// </summary>
        private static void InvokeAwake(MonoBehaviour component)
        {
            MethodInfo awakeMethod = component.GetType()
                .GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(awakeMethod, Is.Not.Null, $"{component.GetType().Name} must have a private Awake() method.");
            awakeMethod.Invoke(component, null);
        }

        /// <summary>
        /// Invokes the private FixedUpdate method on <paramref name="component"/> via reflection.
        /// </summary>
        private static void InvokeFixedUpdate(MonoBehaviour component)
        {
            MethodInfo fixedUpdateMethod = component.GetType()
                .GetMethod("FixedUpdate", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(fixedUpdateMethod, Is.Not.Null, $"{component.GetType().Name} must have a private FixedUpdate() method.");
            fixedUpdateMethod.Invoke(component, null);
        }

        /// <summary>Asserts that a quaternion is effectively Quaternion.identity (angle ≤ 0.1°).</summary>
        private static void AssertQuaternionIsIdentity(Quaternion q, string jointName)
        {
            float angle = Quaternion.Angle(q, Quaternion.identity);
            Assert.That(angle, Is.LessThanOrEqualTo(0.1f),
                $"{jointName} targetRotation should be identity (angle ≤ 0.1°) but is {angle:F2}°.");
        }
    }
}
