using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using PhysicsDrivenMovement.Character;
using UnityEngine;
using UnityEngine.TestTools;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// PlayMode integration tests for <see cref="LegAnimator"/> covering:
    /// - Component caching (leg joints, PlayerMovement, CharacterState)
    /// - Phase accumulator advances when move input is non-zero
    /// - Upper-leg target rotations change while moving (L/R alternating)
    /// - Lower-leg knee-bend rotations change while moving
    /// - Legs return to Quaternion.identity when state is Fallen or GettingUp
    /// - Arm joints are not modified by LegAnimator
    /// </summary>
    public class LegAnimatorTests
    {
        // ─── Test Rig ────────────────────────────────────────────────────────

        private GameObject _hips;
        private Rigidbody _hipsRb;
        private BalanceController _balance;
        private PlayerMovement _movement;
        private CharacterState _characterState;
        private LegAnimator _legAnimator;

        // Leg joint GameObjects and their ConfigurableJoints
        private GameObject _upperLegL;
        private GameObject _upperLegR;
        private GameObject _lowerLegL;
        private GameObject _lowerLegR;
        private ConfigurableJoint _upperLegLJoint;
        private ConfigurableJoint _upperLegRJoint;
        private ConfigurableJoint _lowerLegLJoint;
        private ConfigurableJoint _lowerLegRJoint;

        // Arm joint GameObject for regression (must not be modified by LegAnimator)
        private GameObject _upperArmL;
        private ConfigurableJoint _upperArmLJoint;

        [SetUp]
        public void SetUp()
        {
            // ── Hips root ──────────────────────────────────────────────
            _hips = new GameObject("Hips");
            _hipsRb = _hips.AddComponent<Rigidbody>();
            _hipsRb.useGravity = false;

            // ── Leg GameObjects as children of Hips ─────────────────────
            _upperLegL = CreateLegJoint(_hips, "UpperLeg_L");
            _upperLegR = CreateLegJoint(_hips, "UpperLeg_R");

            _lowerLegL = CreateLegJoint(_upperLegL, "LowerLeg_L");
            _lowerLegR = CreateLegJoint(_upperLegR, "LowerLeg_R");

            // Store joint references
            _upperLegLJoint = _upperLegL.GetComponent<ConfigurableJoint>();
            _upperLegRJoint = _upperLegR.GetComponent<ConfigurableJoint>();
            _lowerLegLJoint = _lowerLegL.GetComponent<ConfigurableJoint>();
            _lowerLegRJoint = _lowerLegR.GetComponent<ConfigurableJoint>();

            // ── Arm joint (should NOT be touched by LegAnimator) ────────
            _upperArmL = CreateArmJoint(_hips, "UpperArm_L");
            _upperArmLJoint = _upperArmL.GetComponent<ConfigurableJoint>();

            // ── Components on Hips ───────────────────────────────────────
            _balance = _hips.AddComponent<BalanceController>();
            _movement = _hips.AddComponent<PlayerMovement>();
            _characterState = _hips.AddComponent<CharacterState>();
            _legAnimator = _hips.AddComponent<LegAnimator>();

            // Provide deterministic test state via seams
            _balance.SetGroundStateForTest(isGrounded: true, isFallen: false);
            _movement.SetMoveInputOverride(Vector2.zero);

            // Disable non-deterministic components to avoid interference
            _balance.enabled = false;
            _movement.enabled = false;
            // CharacterState and LegAnimator intentionally left enabled for testing
        }

        [TearDown]
        public void TearDown()
        {
            if (_hips != null)
            {
                UnityEngine.Object.Destroy(_hips);
            }
        }

        // ─── Caching Tests ──────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator Awake_CachesUpperLegLJoint()
        {
            // Arrange
            yield return null;

            // Act
            object cached = GetPrivateField(_legAnimator, "_upperLegL");

            // Assert
            Assert.That(cached, Is.Not.Null,
                "LegAnimator must cache the UpperLeg_L ConfigurableJoint in Awake.");
        }

        [UnityTest]
        public IEnumerator Awake_CachesUpperLegRJoint()
        {
            // Arrange
            yield return null;

            // Act
            object cached = GetPrivateField(_legAnimator, "_upperLegR");

            // Assert
            Assert.That(cached, Is.Not.Null,
                "LegAnimator must cache the UpperLeg_R ConfigurableJoint in Awake.");
        }

        [UnityTest]
        public IEnumerator Awake_CachesLowerLegLJoint()
        {
            // Arrange
            yield return null;

            // Act
            object cached = GetPrivateField(_legAnimator, "_lowerLegL");

            // Assert
            Assert.That(cached, Is.Not.Null,
                "LegAnimator must cache the LowerLeg_L ConfigurableJoint in Awake.");
        }

        [UnityTest]
        public IEnumerator Awake_CachesLowerLegRJoint()
        {
            // Arrange
            yield return null;

            // Act
            object cached = GetPrivateField(_legAnimator, "_lowerLegR");

            // Assert
            Assert.That(cached, Is.Not.Null,
                "LegAnimator must cache the LowerLeg_R ConfigurableJoint in Awake.");
        }

        [UnityTest]
        public IEnumerator Awake_CachesPlayerMovementReference()
        {
            // Arrange
            yield return null;

            // Act
            object cached = GetPrivateField(_legAnimator, "_playerMovement");

            // Assert
            Assert.That(cached, Is.Not.Null,
                "LegAnimator must cache a PlayerMovement reference in Awake.");
        }

        [UnityTest]
        public IEnumerator Awake_CachesCharacterStateReference()
        {
            // Arrange
            yield return null;

            // Act
            object cached = GetPrivateField(_legAnimator, "_characterState");

            // Assert
            Assert.That(cached, Is.Not.Null,
                "LegAnimator must cache a CharacterState reference in Awake.");
        }

        // ─── Phase Accumulator Tests ────────────────────────────────────────

        [UnityTest]
        public IEnumerator FixedUpdate_WhenMoveInputIsZero_PhaseAccumulatorDoesNotAdvance()
        {
            // Arrange
            yield return null;
            _movement.SetMoveInputOverride(Vector2.zero);
            float phaseBefore = GetPhaseAccumulator();

            // Act
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            // Assert
            float phaseAfter = GetPhaseAccumulator();
            Assert.That(phaseAfter, Is.EqualTo(phaseBefore).Within(0.001f),
                "Phase accumulator must not advance when move input is zero.");
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WhenMoveInputIsNonZero_PhaseAccumulatorAdvances()
        {
            // Arrange
            yield return null;
            _movement.SetMoveInputOverride(new Vector2(0f, 1f));
            float phaseBefore = GetPhaseAccumulator();

            // Act
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            // Assert
            float phaseAfter = GetPhaseAccumulator();
            Assert.That(phaseAfter, Is.GreaterThan(phaseBefore),
                "Phase accumulator must advance when move input is non-zero.");
        }

        // ─── Target Rotation Tests (Moving State) ───────────────────────────

        [UnityTest]
        public IEnumerator FixedUpdate_WhenMoving_UpperLegLTargetRotationChanges()
        {
            // Arrange
            yield return null;
            Quaternion rotBefore = _upperLegLJoint.targetRotation;
            _movement.SetMoveInputOverride(new Vector2(0f, 1f));

            // Act — run enough frames for phase to accumulate meaningfully
            yield return new WaitForSeconds(0.1f);

            // Assert
            Quaternion rotAfter = _upperLegLJoint.targetRotation;
            float angleDiff = Quaternion.Angle(rotBefore, rotAfter);
            Assert.That(angleDiff, Is.GreaterThan(0.1f),
                $"UpperLeg_L targetRotation must change while moving. Angle diff={angleDiff:F3}°.");
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WhenMoving_UpperLegRTargetRotationChanges()
        {
            // Arrange
            yield return null;
            Quaternion rotBefore = _upperLegRJoint.targetRotation;
            _movement.SetMoveInputOverride(new Vector2(0f, 1f));

            // Act
            yield return new WaitForSeconds(0.1f);

            // Assert
            Quaternion rotAfter = _upperLegRJoint.targetRotation;
            float angleDiff = Quaternion.Angle(rotBefore, rotAfter);
            Assert.That(angleDiff, Is.GreaterThan(0.1f),
                $"UpperLeg_R targetRotation must change while moving. Angle diff={angleDiff:F3}°.");
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WhenMoving_LowerLegLTargetRotationChanges()
        {
            // Arrange
            yield return null;
            Quaternion rotBefore = _lowerLegLJoint.targetRotation;
            _movement.SetMoveInputOverride(new Vector2(0f, 1f));

            // Act
            yield return new WaitForSeconds(0.1f);

            // Assert
            Quaternion rotAfter = _lowerLegLJoint.targetRotation;
            float angleDiff = Quaternion.Angle(rotBefore, rotAfter);
            Assert.That(angleDiff, Is.GreaterThan(0.1f),
                $"LowerLeg_L targetRotation must change while moving. Angle diff={angleDiff:F3}°.");
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WhenMoving_LowerLegRTargetRotationChanges()
        {
            // Arrange
            yield return null;
            Quaternion rotBefore = _lowerLegRJoint.targetRotation;
            _movement.SetMoveInputOverride(new Vector2(0f, 1f));

            // Act
            yield return new WaitForSeconds(0.1f);

            // Assert
            Quaternion rotAfter = _lowerLegRJoint.targetRotation;
            float angleDiff = Quaternion.Angle(rotBefore, rotAfter);
            Assert.That(angleDiff, Is.GreaterThan(0.1f),
                $"LowerLeg_R targetRotation must change while moving. Angle diff={angleDiff:F3}°.");
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WhenMoving_UpperLegsAreAlternating()
        {
            // Arrange — capture snapshot at one time then a half-cycle later
            yield return null;
            SetStepFrequency(2f);   // 2 Hz → half-cycle = 0.25 s
            _movement.SetMoveInputOverride(new Vector2(0f, 1f));

            // Stabilize phase first
            yield return new WaitForSeconds(0.05f);

            Quaternion lRotA = _upperLegLJoint.targetRotation;
            Quaternion rRotA = _upperLegRJoint.targetRotation;

            // Capture the sign of the X-component difference now
            float diffA = GetRotationXAngle(lRotA) - GetRotationXAngle(rRotA);

            // Wait a half-cycle
            yield return new WaitForSeconds(0.25f);

            Quaternion lRotB = _upperLegLJoint.targetRotation;
            Quaternion rRotB = _upperLegRJoint.targetRotation;
            float diffB = GetRotationXAngle(lRotB) - GetRotationXAngle(rRotB);

            // Assert — the sign of (L - R) should flip over a half-cycle
            Assert.That(diffA * diffB, Is.LessThan(0f),
                $"Upper legs must alternate (L/R phase offset by π). " +
                $"diffA={diffA:F3}, diffB={diffB:F3} — sign should invert over half-cycle.");
        }

        // ─── Identity Tests (Fallen / GettingUp) ────────────────────────────

        [UnityTest]
        public IEnumerator FixedUpdate_WhenStateFallen_UpperLegLReturnsToIdentity()
        {
            // Arrange — run gait for a bit, then flip to Fallen
            yield return null;
            _movement.SetMoveInputOverride(new Vector2(0f, 1f));
            yield return new WaitForSeconds(0.1f);

            SetCurrentState(CharacterStateType.Fallen);
            _movement.SetMoveInputOverride(Vector2.zero);

            // Act
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            // Assert
            float angleFromIdentity = Quaternion.Angle(_upperLegLJoint.targetRotation, Quaternion.identity);
            Assert.That(angleFromIdentity, Is.LessThanOrEqualTo(1f),
                $"UpperLeg_L targetRotation must return to identity when Fallen. " +
                $"Angle from identity={angleFromIdentity:F3}°.");
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WhenStateFallen_UpperLegRReturnsToIdentity()
        {
            // Arrange
            yield return null;
            _movement.SetMoveInputOverride(new Vector2(0f, 1f));
            yield return new WaitForSeconds(0.1f);

            SetCurrentState(CharacterStateType.Fallen);
            _movement.SetMoveInputOverride(Vector2.zero);

            // Act
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            // Assert
            float angleFromIdentity = Quaternion.Angle(_upperLegRJoint.targetRotation, Quaternion.identity);
            Assert.That(angleFromIdentity, Is.LessThanOrEqualTo(1f),
                $"UpperLeg_R targetRotation must return to identity when Fallen. " +
                $"Angle from identity={angleFromIdentity:F3}°.");
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WhenStateFallen_LowerLegsReturnToIdentity()
        {
            // Arrange
            yield return null;
            _movement.SetMoveInputOverride(new Vector2(0f, 1f));
            yield return new WaitForSeconds(0.1f);

            SetCurrentState(CharacterStateType.Fallen);
            _movement.SetMoveInputOverride(Vector2.zero);

            // Act
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            // Assert
            float angleLFromId = Quaternion.Angle(_lowerLegLJoint.targetRotation, Quaternion.identity);
            float angleRFromId = Quaternion.Angle(_lowerLegRJoint.targetRotation, Quaternion.identity);
            Assert.That(angleLFromId, Is.LessThanOrEqualTo(1f),
                $"LowerLeg_L targetRotation must return to identity when Fallen. Angle={angleLFromId:F3}°.");
            Assert.That(angleRFromId, Is.LessThanOrEqualTo(1f),
                $"LowerLeg_R targetRotation must return to identity when Fallen. Angle={angleRFromId:F3}°.");
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WhenStateGettingUp_AllLegJointsReturnToIdentity()
        {
            // Arrange
            yield return null;
            _movement.SetMoveInputOverride(new Vector2(0f, 1f));
            yield return new WaitForSeconds(0.1f);

            SetCurrentState(CharacterStateType.GettingUp);
            _movement.SetMoveInputOverride(Vector2.zero);

            // Act
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            // Assert
            float angleUL = Quaternion.Angle(_upperLegLJoint.targetRotation, Quaternion.identity);
            float angleUR = Quaternion.Angle(_upperLegRJoint.targetRotation, Quaternion.identity);
            float angleLL = Quaternion.Angle(_lowerLegLJoint.targetRotation, Quaternion.identity);
            float angleLR = Quaternion.Angle(_lowerLegRJoint.targetRotation, Quaternion.identity);

            Assert.That(angleUL, Is.LessThanOrEqualTo(1f),
                $"UpperLeg_L must return to identity when GettingUp. Angle={angleUL:F3}°.");
            Assert.That(angleUR, Is.LessThanOrEqualTo(1f),
                $"UpperLeg_R must return to identity when GettingUp. Angle={angleUR:F3}°.");
            Assert.That(angleLL, Is.LessThanOrEqualTo(1f),
                $"LowerLeg_L must return to identity when GettingUp. Angle={angleLL:F3}°.");
            Assert.That(angleLR, Is.LessThanOrEqualTo(1f),
                $"LowerLeg_R must return to identity when GettingUp. Angle={angleLR:F3}°.");
        }

        // ─── Arm Non-Modification Test ──────────────────────────────────────

        [UnityTest]
        public IEnumerator FixedUpdate_WhenMoving_ArmJointsAreNotModified()
        {
            // Arrange
            yield return null;
            Quaternion armRotBefore = _upperArmLJoint.targetRotation;
            _movement.SetMoveInputOverride(new Vector2(0f, 1f));

            // Act
            yield return new WaitForSeconds(0.2f);

            // Assert
            Quaternion armRotAfter = _upperArmLJoint.targetRotation;
            float angleDiff = Quaternion.Angle(armRotBefore, armRotAfter);
            Assert.That(angleDiff, Is.LessThanOrEqualTo(0.01f),
                $"LegAnimator must not modify arm joint targetRotations. " +
                $"UpperArm_L changed by {angleDiff:F4}°.");
        }

        // ─── Helper: create test-rig leg/arm joints ─────────────────────────

        private static GameObject CreateLegJoint(GameObject parent, string name)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent.transform);
            go.transform.localPosition = Vector3.zero;
            go.AddComponent<Rigidbody>();
            ConfigurableJoint joint = go.AddComponent<ConfigurableJoint>();
            joint.targetRotation = Quaternion.identity;
            return go;
        }

        private static GameObject CreateArmJoint(GameObject parent, string name)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent.transform);
            go.transform.localPosition = Vector3.zero;
            go.AddComponent<Rigidbody>();
            ConfigurableJoint joint = go.AddComponent<ConfigurableJoint>();
            joint.targetRotation = Quaternion.identity;
            return go;
        }

        // ─── Reflection helpers ──────────────────────────────────────────────

        private float GetPhaseAccumulator()
        {
            object val = GetPrivateField(_legAnimator, "_phase");
            if (val == null)
            {
                throw new InvalidOperationException(
                    "LegAnimator must have a private float field named '_phase' for the gait phase accumulator.");
            }

            return (float)val;
        }

        private void SetStepFrequency(float freq)
        {
            SetPrivateField(_legAnimator, "_stepFrequency", freq);
        }

        private void SetCurrentState(CharacterStateType state)
        {
            SetAutoPropertyBackingField(_characterState, nameof(CharacterState.CurrentState), state);
        }

        private static float GetRotationXAngle(Quaternion q)
        {
            // Extract local X rotation angle (degrees) from quaternion, signed by convention.
            q.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) { angle -= 360f; }
            return angle * Vector3.Dot(axis.normalized, Vector3.right);
        }

        private static object GetPrivateField(object instance, string fieldName)
        {
            FieldInfo field = instance.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new InvalidOperationException(
                    $"Missing expected private field '{fieldName}' on {instance.GetType().Name}.");
            }

            return field.GetValue(instance);
        }

        private static void SetPrivateField(object instance, string fieldName, object value)
        {
            FieldInfo field = instance.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new InvalidOperationException(
                    $"Missing expected private field '{fieldName}' on {instance.GetType().Name}.");
            }

            field.SetValue(instance, value);
        }

        private static void SetAutoPropertyBackingField(object instance, string propertyName, object value)
        {
            string backingFieldName = $"<{propertyName}>k__BackingField";
            FieldInfo field = instance.GetType().GetField(backingFieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new InvalidOperationException(
                    $"Missing expected auto-property backing field '{backingFieldName}' on {instance.GetType().Name}.");
            }

            field.SetValue(instance, value);
        }
    }
}
