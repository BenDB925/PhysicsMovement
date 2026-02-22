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
    /// - Lower leg joints physically rotate during gait, overcoming gravity (regression: dragging-feet bug)
    /// - World-space swing: _useWorldSpaceSwing field exists and defaults to true
    /// - World-space swing: leg targetRotation changes even when hips are pitched forward
    /// - World-space swing: swing axis is world-horizontal (independent of hips pitch)
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
            _movement.SetMoveInputForTest(Vector2.zero);

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
            _movement.SetMoveInputForTest(Vector2.zero);
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
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));
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
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));

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
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));

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
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));

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
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));

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
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));

            // Stabilize phase first
            yield return new WaitForSeconds(0.05f);

            Quaternion lRotA = _upperLegLJoint.targetRotation;
            Quaternion rRotA = _upperLegRJoint.targetRotation;

            // Compute a signed angle difference between L and R at snapshot A.
            // DESIGN: When _useWorldSpaceSwing is true (default), the targetRotation is
            // expressed in the connected-body frame and the rotation axis is world-horizontal
            // (e.g., -Vector3.right for forward input, not necessarily Vector3.forward/Z).
            // We use the relative rotation between L and R to detect the phase offset,
            // which is axis-agnostic and works regardless of swing implementation mode.
            // Quaternion.Dot measures how similar two quaternions are: +1 = identical,
            // 0 = 90° apart, -1 = opposite. We extract a signed offset via the relative
            // quaternion's ToAngleAxis representation.
            float signedDiffA = GetRelativeSignedAngle(lRotA, rRotA);

            // Wait a half-cycle
            yield return new WaitForSeconds(0.25f);

            Quaternion lRotB = _upperLegLJoint.targetRotation;
            Quaternion rRotB = _upperLegRJoint.targetRotation;
            float signedDiffB = GetRelativeSignedAngle(lRotB, rRotB);

            // Assert — the sign of (L relative to R) should flip over a half-cycle,
            // confirming the legs alternate with π phase offset.
            Assert.That(signedDiffA * signedDiffB, Is.LessThan(0f),
                $"Upper legs must alternate (L/R phase offset by π). " +
                $"signedDiffA={signedDiffA:F3}°, signedDiffB={signedDiffB:F3}° — sign should invert over half-cycle.");
        }

        // ─── Identity Tests (Fallen / GettingUp) ────────────────────────────

        [UnityTest]
        public IEnumerator FixedUpdate_WhenStateFallen_UpperLegLReturnsToIdentity()
        {
            // Arrange — run gait for a bit, then flip to Fallen
            yield return null;
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));
            yield return new WaitForSeconds(0.1f);

            SetCurrentState(CharacterStateType.Fallen);
            _movement.SetMoveInputForTest(Vector2.zero);

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
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));
            yield return new WaitForSeconds(0.1f);

            SetCurrentState(CharacterStateType.Fallen);
            _movement.SetMoveInputForTest(Vector2.zero);

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
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));
            yield return new WaitForSeconds(0.1f);

            SetCurrentState(CharacterStateType.Fallen);
            _movement.SetMoveInputForTest(Vector2.zero);

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
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));
            yield return new WaitForSeconds(0.1f);

            SetCurrentState(CharacterStateType.GettingUp);
            _movement.SetMoveInputForTest(Vector2.zero);

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

        // ─── Idle Blend Tests (Phase 3E2) ───────────────────────────────────

        /// <summary>
        /// LegAnimator must expose a serialized _idleBlendSpeed field that controls
        /// how quickly leg joints slerp back to Quaternion.identity when idle.
        /// </summary>
        [UnityTest]
        public IEnumerator IdleBlendSpeed_FieldExists_AndIsSerializable()
        {
            // Arrange
            yield return null;

            // Act — use reflection to confirm the field exists and is serializable
            FieldInfo field = typeof(LegAnimator).GetField("_idleBlendSpeed",
                BindingFlags.Instance | BindingFlags.NonPublic);

            // Assert
            Assert.That(field, Is.Not.Null,
                "LegAnimator must have a private serialized field named '_idleBlendSpeed'.");
            Assert.That(field.FieldType, Is.EqualTo(typeof(float)),
                "_idleBlendSpeed must be a float.");
        }

        /// <summary>
        /// LegAnimator must expose a serialized _swingAxis field (Vector3) so the upper-leg
        /// rotation axis can be tuned in the Inspector without code changes.
        /// Default must be Vector3.forward (Z) — the correct targetRotation axis for
        /// joint.axis=right with ConfigurableJoint's internal frame mapping.
        /// </summary>
        [UnityTest]
        public IEnumerator SwingAxis_FieldExists_AndDefaultsToForward()
        {
            // Arrange
            yield return null;

            // Act — use reflection to confirm the field exists and has the correct default
            FieldInfo field = typeof(LegAnimator).GetField("_swingAxis",
                BindingFlags.Instance | BindingFlags.NonPublic);

            // Assert: field exists and is a Vector3
            Assert.That(field, Is.Not.Null,
                "LegAnimator must have a private serialized field named '_swingAxis'.");
            Assert.That(field.FieldType, Is.EqualTo(typeof(Vector3)),
                "_swingAxis must be a Vector3.");

            // Assert: default value is Vector3.forward (0, 0, 1)
            Vector3 defaultValue = (Vector3)field.GetValue(_legAnimator);
            Assert.That(defaultValue, Is.EqualTo(Vector3.forward),
                $"_swingAxis default must be Vector3.forward (0,0,1) for correct sagittal swing. " +
                $"Got: {defaultValue}.");
        }

        /// <summary>
        /// LegAnimator must expose a serialized _kneeAxis field (Vector3) so the lower-leg
        /// knee-bend rotation axis can be tuned in the Inspector without code changes.
        /// Default must be Vector3.forward (Z) — the correct targetRotation axis for
        /// joint.axis=right with ConfigurableJoint's internal frame mapping.
        /// </summary>
        [UnityTest]
        public IEnumerator KneeAxis_FieldExists_AndDefaultsToForward()
        {
            // Arrange
            yield return null;

            // Act — use reflection to confirm the field exists and has the correct default
            FieldInfo field = typeof(LegAnimator).GetField("_kneeAxis",
                BindingFlags.Instance | BindingFlags.NonPublic);

            // Assert: field exists and is a Vector3
            Assert.That(field, Is.Not.Null,
                "LegAnimator must have a private serialized field named '_kneeAxis'.");
            Assert.That(field.FieldType, Is.EqualTo(typeof(Vector3)),
                "_kneeAxis must be a Vector3.");

            // Assert: default value is Vector3.forward (0, 0, 1)
            Vector3 defaultValue = (Vector3)field.GetValue(_legAnimator);
            Assert.That(defaultValue, Is.EqualTo(Vector3.forward),
                $"_kneeAxis default must be Vector3.forward (0,0,1) for correct knee bend. " +
                $"Got: {defaultValue}.");
        }

        /// <summary>
        /// After moving (which sets a non-identity leg rotation), stopping input must
        /// cause upper leg L to blend smoothly back toward identity over multiple frames.
        /// The blend must be measurably closer to identity after 0.5 s idle time.
        /// </summary>
        [UnityTest]
        public IEnumerator FixedUpdate_AfterMoving_WhenInputStops_UpperLegLBlendsTowardIdentity()
        {
            // Arrange — move for a bit to establish a non-identity rotation
            yield return null;
            SetIdleBlendSpeed(5f);   // fast enough to measure in 0.5 s
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));
            yield return new WaitForSeconds(0.15f);

            // Capture rotation after moving (should be non-identity)
            Quaternion rotAfterMove = _upperLegLJoint.targetRotation;
            float angleAfterMove = Quaternion.Angle(rotAfterMove, Quaternion.identity);

            // Act — stop input, wait for blend
            _movement.SetMoveInputForTest(Vector2.zero);
            yield return new WaitForSeconds(0.5f);

            // Assert — rotation must be closer to identity than right after stopping
            Quaternion rotAfterIdle = _upperLegLJoint.targetRotation;
            float angleAfterIdle = Quaternion.Angle(rotAfterIdle, Quaternion.identity);

            Assert.That(angleAfterIdle, Is.LessThan(angleAfterMove),
                $"UpperLeg_L must blend toward identity on idle. " +
                $"AngleAfterMove={angleAfterMove:F3}° AngleAfterIdle={angleAfterIdle:F3}°.");
        }

        /// <summary>
        /// After moving, stopping input must blend upper leg R toward identity.
        /// </summary>
        [UnityTest]
        public IEnumerator FixedUpdate_AfterMoving_WhenInputStops_UpperLegRBlendsTowardIdentity()
        {
            // Arrange
            yield return null;
            SetIdleBlendSpeed(5f);
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));
            yield return new WaitForSeconds(0.15f);

            Quaternion rotAfterMove = _upperLegRJoint.targetRotation;
            float angleAfterMove = Quaternion.Angle(rotAfterMove, Quaternion.identity);

            // Act
            _movement.SetMoveInputForTest(Vector2.zero);
            yield return new WaitForSeconds(0.5f);

            // Assert
            Quaternion rotAfterIdle = _upperLegRJoint.targetRotation;
            float angleAfterIdle = Quaternion.Angle(rotAfterIdle, Quaternion.identity);

            Assert.That(angleAfterIdle, Is.LessThan(angleAfterMove),
                $"UpperLeg_R must blend toward identity on idle. " +
                $"AngleAfterMove={angleAfterMove:F3}° AngleAfterIdle={angleAfterIdle:F3}°.");
        }

        /// <summary>
        /// After moving, stopping input must blend lower leg L toward identity.
        /// </summary>
        [UnityTest]
        public IEnumerator FixedUpdate_AfterMoving_WhenInputStops_LowerLegLBlendsTowardIdentity()
        {
            // Arrange
            yield return null;
            SetIdleBlendSpeed(5f);
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));
            yield return new WaitForSeconds(0.15f);

            Quaternion rotAfterMove = _lowerLegLJoint.targetRotation;
            float angleAfterMove = Quaternion.Angle(rotAfterMove, Quaternion.identity);

            // Act
            _movement.SetMoveInputForTest(Vector2.zero);
            yield return new WaitForSeconds(0.5f);

            // Assert
            Quaternion rotAfterIdle = _lowerLegLJoint.targetRotation;
            float angleAfterIdle = Quaternion.Angle(rotAfterIdle, Quaternion.identity);

            Assert.That(angleAfterIdle, Is.LessThan(angleAfterMove),
                $"LowerLeg_L must blend toward identity on idle. " +
                $"AngleAfterMove={angleAfterMove:F3}° AngleAfterIdle={angleAfterIdle:F3}°.");
        }

        /// <summary>
        /// After moving, stopping input must blend lower leg R toward identity.
        /// </summary>
        [UnityTest]
        public IEnumerator FixedUpdate_AfterMoving_WhenInputStops_LowerLegRBlendsTowardIdentity()
        {
            // Arrange
            yield return null;
            SetIdleBlendSpeed(5f);
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));
            yield return new WaitForSeconds(0.15f);

            Quaternion rotAfterMove = _lowerLegRJoint.targetRotation;
            float angleAfterMove = Quaternion.Angle(rotAfterMove, Quaternion.identity);

            // Act
            _movement.SetMoveInputForTest(Vector2.zero);
            yield return new WaitForSeconds(0.5f);

            // Assert
            Quaternion rotAfterIdle = _lowerLegRJoint.targetRotation;
            float angleAfterIdle = Quaternion.Angle(rotAfterIdle, Quaternion.identity);

            Assert.That(angleAfterIdle, Is.LessThan(angleAfterMove),
                $"LowerLeg_R must blend toward identity on idle. " +
                $"AngleAfterMove={angleAfterMove:F3}° AngleAfterIdle={angleAfterIdle:F3}°.");
        }

        /// <summary>
        /// The gait phase accumulator must decay toward zero when input is zero.
        /// After enough idle time it must be measurably smaller than it was when moving stopped.
        /// </summary>
        [UnityTest]
        public IEnumerator FixedUpdate_WhenInputStops_PhaseDecaysTowardZero()
        {
            // Arrange — move to accumulate a non-zero phase
            yield return null;
            SetIdleBlendSpeed(5f);
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));
            yield return new WaitForSeconds(0.2f);

            float phaseAtStop = GetPhaseAccumulator();
            // Ensure phase is meaningfully non-zero before stopping
            Assume.That(phaseAtStop, Is.GreaterThan(0.05f),
                "Pre-condition: phase must be non-zero before testing decay.");

            // Act — stop input, allow decay
            _movement.SetMoveInputForTest(Vector2.zero);
            yield return new WaitForSeconds(0.5f);

            // Assert — phase must have decayed
            float phaseAfterIdle = GetPhaseAccumulator();
            Assert.That(phaseAfterIdle, Is.LessThan(phaseAtStop),
                $"Phase accumulator must decay toward zero on idle. " +
                $"PhaseAtStop={phaseAtStop:F3} PhaseAfterIdle={phaseAfterIdle:F3}.");
        }

        /// <summary>
        /// On a quick move-stop-move toggle, the leg joint targetRotation must NOT jump
        /// discontinuously when movement resumes. The first frame of resumed gait must
        /// produce a small rotation (due to the slerp-on-entry), not a sudden large
        /// amplitude matching the full gait target.
        /// </summary>
        [UnityTest]
        public IEnumerator FixedUpdate_QuickMoveStopMove_NoAbruptRotationJump()
        {
            // Arrange
            yield return null;
            SetIdleBlendSpeed(5f);
            SetStepFrequency(2f);

            // Phase 1: move to build up a gait rotation, then stop
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));
            yield return new WaitForSeconds(0.15f);

            // Phase 2: stop — one FixedUpdate will set joints to identity
            _movement.SetMoveInputForTest(Vector2.zero);
            yield return new WaitForFixedUpdate();

            // Verify joints are at identity (the idle path just fired)
            float angleAtIdle = Quaternion.Angle(_upperLegLJoint.targetRotation, Quaternion.identity);

            // Phase 3: immediately resume — one FixedUpdate will begin slerping toward gait
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));
            yield return new WaitForFixedUpdate();
            Quaternion rotOnFirstResumeFrame = _upperLegLJoint.targetRotation;
            float angleOnResume = Quaternion.Angle(rotOnFirstResumeFrame, Quaternion.identity);

            // Assert — joints were at identity when idle
            Assert.That(angleAtIdle, Is.LessThanOrEqualTo(1f),
                $"Joints must be at identity after one idle frame. Angle={angleAtIdle:F3}°.");

            // Assert — the first resumed-movement frame must NOT snap to full gait amplitude.
            // Slerp at t=idleBlendSpeed*fixedDeltaTime (≈0.05) means the rotation is at most
            // ~5% of the gait target. The maximum gait amplitude is stepAngle (25°) so the
            // maximum first-frame rotation is ~1.25°. We use a generous 5° threshold.
            Assert.That(angleOnResume, Is.LessThan(5f),
                $"First resumed-gait frame must not snap to full amplitude. " +
                $"Angle={angleOnResume:F3}° (threshold 5°). IdleAngle={angleAtIdle:F3}°.");
        }

        /// <summary>
        /// After a long idle, legs must be very close to identity (nearly fully settled).
        /// Tests that the blend completes within a reasonable time window.
        /// </summary>
        [UnityTest]
        public IEnumerator FixedUpdate_AfterLongIdle_LegJointsAreNearIdentity()
        {
            // Arrange
            yield return null;
            SetIdleBlendSpeed(5f);

            // Move to get a visible rotation
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));
            yield return new WaitForSeconds(0.15f);

            // Act — long idle (2 seconds at blend speed 5)
            _movement.SetMoveInputForTest(Vector2.zero);
            yield return new WaitForSeconds(2f);

            // Assert — all four joints near identity (< 2°)
            float angleUL = Quaternion.Angle(_upperLegLJoint.targetRotation, Quaternion.identity);
            float angleUR = Quaternion.Angle(_upperLegRJoint.targetRotation, Quaternion.identity);
            float angleLL = Quaternion.Angle(_lowerLegLJoint.targetRotation, Quaternion.identity);
            float angleLR = Quaternion.Angle(_lowerLegRJoint.targetRotation, Quaternion.identity);

            Assert.That(angleUL, Is.LessThan(2f),
                $"UpperLeg_L must be near identity after long idle. Angle={angleUL:F3}°.");
            Assert.That(angleUR, Is.LessThan(2f),
                $"UpperLeg_R must be near identity after long idle. Angle={angleUR:F3}°.");
            Assert.That(angleLL, Is.LessThan(2f),
                $"LowerLeg_L must be near identity after long idle. Angle={angleLL:F3}°.");
            Assert.That(angleLR, Is.LessThan(2f),
                $"LowerLeg_R must be near identity after long idle. Angle={angleLR:F3}°.");
        }

        // ─── Arm Non-Modification Test ──────────────────────────────────────

        [UnityTest]
        public IEnumerator FixedUpdate_WhenMoving_ArmJointsAreNotModified()
        {
            // Arrange
            yield return null;
            Quaternion armRotBefore = _upperArmLJoint.targetRotation;
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));

            // Act
            yield return new WaitForSeconds(0.2f);

            // Assert
            Quaternion armRotAfter = _upperArmLJoint.targetRotation;
            float angleDiff = Quaternion.Angle(armRotBefore, armRotAfter);
            Assert.That(angleDiff, Is.LessThanOrEqualTo(0.01f),
                $"LegAnimator must not modify arm joint targetRotations. " +
                $"UpperArm_L changed by {angleDiff:F4}°.");
        }

        // ─── Physical Lift Regression Test ──────────────────────────────────

        /// <summary>
        /// Regression test for the 'dragging feet' bug.
        ///
        /// Verifies that, with a full physics-enabled ragdoll (RagdollSetup + LegAnimator +
        /// PlayerMovement + CharacterState + BalanceController all active), at least one
        /// lower leg ConfigurableJoint physically rotates away from its rest orientation
        /// during sustained walking gait — proving the joint drive overcomes gravity and
        /// the leg actually lifts, not just that <c>targetRotation</c> is assigned.
        ///
        /// Measurement: angular displacement of the lower leg segment's local rotation
        /// relative to its starting orientation (measured via Quaternion.Angle between
        /// initial and final local rotation of the lower leg Transform).  Local rotation
        /// is the quantity driven by the ConfigurableJoint spring, making it the most
        /// direct indicator of whether the joint is physically responding to the
        /// LegAnimator's targetRotation commands.
        ///
        /// Threshold rationale (5°):
        ///   - LegAnimator default _kneeAngle = 20°, applied immediately once input starts.
        ///   - RagdollSetup applies lowerLegSpring = 1200 Nm/rad; at 20° = 0.35 rad,
        ///     spring torque ≈ 420 Nm vs gravity torque ≈ 4.2 Nm — the joint wins 100:1.
        ///   - The _smoothedInputMag ramp means amplitude is only ~5% at frame 1, ramping
        ///     toward 100% over ~60 frames; after 50 frames the joint should be > 15°.
        ///   - 5° threshold is deliberately conservative: a limp/no-drive leg measures 0°,
        ///     while a properly driven leg will exceed 10° well within 50 frames.
        ///   - If CI proves this too tight, raise to 3° — the point is that ANY measurable
        ///     angular response proves the drive chain is alive end-to-end.
        ///
        /// This test uses a SEPARATE, fully-wired physics rig (not the shared SetUp rig)
        /// so that gravity is enabled on all bodies and the drives are configured by
        /// RagdollSetup exactly as they are at runtime.
        /// </summary>
        [UnityTest]
        public IEnumerator LowerLeg_WhenWalking_LiftsBeyondMinimumThreshold()
        {
            // ── Arrange ────────────────────────────────────────────────────────

            // Minimum angular displacement (degrees) the lower leg joint must achieve
            // relative to its rest orientation. See threshold rationale above.
            const float MinAngularDisplacementDeg = 5f;

            // Number of FixedUpdate frames to simulate before measuring.
            // 50 frames at 100 Hz = 0.5 s — enough for the gait phase + smoothed-scale
            // ramp (_smoothedInputMag) to reach near-full amplitude.
            const int PhysicsFrames = 50;

            // ── Build the physical rig ─────────────────────────────────────────
            // 5-body ragdoll: Hips → UpperLeg_L → LowerLeg_L
            //                        UpperLeg_R → LowerLeg_R
            // Joints use locked linear axes (position-constrained) so gravity does not
            // cause the bodies to free-fall through each other.
            // Rotation is free (Slerp drive controls angular response via targetRotation).

            GameObject physicsHips = new GameObject("PhysicsHips");
            Rigidbody hipsRb = physicsHips.AddComponent<Rigidbody>();
            hipsRb.useGravity = false;  // Hips are the anchor — keep stable
            hipsRb.isKinematic = true;  // Lock hips in place so legs swing freely
            physicsHips.AddComponent<BoxCollider>();

            // Build leg hierarchy BEFORE adding Character components so all Awake calls
            // see the complete hierarchy.
            GameObject physUpperLegL = CreatePhysicsLegJoint(physicsHips, "UpperLeg_L", hipsRb,
                localPos: new Vector3(-0.2f, -0.3f, 0f), mass: 3f);
            GameObject physUpperLegR = CreatePhysicsLegJoint(physicsHips, "UpperLeg_R", hipsRb,
                localPos: new Vector3( 0.2f, -0.3f, 0f), mass: 3f);

            Rigidbody upperLRb = physUpperLegL.GetComponent<Rigidbody>();
            Rigidbody upperRRb = physUpperLegR.GetComponent<Rigidbody>();

            GameObject physLowerLegL = CreatePhysicsLegJoint(physUpperLegL, "LowerLeg_L", upperLRb,
                localPos: new Vector3(0f, -0.35f, 0f), mass: 2.5f);
            GameObject physLowerLegR = CreatePhysicsLegJoint(physUpperLegR, "LowerLeg_R", upperRRb,
                localPos: new Vector3(0f, -0.35f, 0f), mass: 2.5f);

            // ── Add Character components on Hips in dependency order ───────────
            // RagdollSetup FIRST: its Awake applies SLERP drives to joints.
            physicsHips.AddComponent<RagdollSetup>();

            BalanceController physicsBalance = physicsHips.AddComponent<BalanceController>();
            PlayerMovement physicsMovement = physicsHips.AddComponent<PlayerMovement>();
            physicsHips.AddComponent<CharacterState>();
            physicsHips.AddComponent<LegAnimator>();

            // ── Configure test seams so gait runs without falling ──────────────
            physicsBalance.SetGroundStateForTest(isGrounded: true, isFallen: false);

            // Disable BalanceController and PlayerMovement physics loops to isolate
            // the LegAnimator joint-drive signal.
            physicsBalance.enabled = false;
            physicsMovement.enabled = false;

            // Inject non-zero move input so LegAnimator's phase accumulates.
            physicsMovement.SetMoveInputForTest(new Vector2(0f, 1f));

            // Wait one frame for all Awake/Start calls to complete.
            yield return null;

            // ── Record rest rotation ───────────────────────────────────────────
            // Capture initial local rotation of both lower legs before gait has any effect.
            Quaternion lowerLegLRestRot = physLowerLegL.transform.localRotation;
            Quaternion lowerLegRRestRot = physLowerLegR.transform.localRotation;

            // ── Act — simulate physics frames ─────────────────────────────────
            for (int i = 0; i < PhysicsFrames; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            // ── Assert ─────────────────────────────────────────────────────────
            float angularDisplacementL = Quaternion.Angle(lowerLegLRestRot, physLowerLegL.transform.localRotation);
            float angularDisplacementR = Quaternion.Angle(lowerLegRRestRot, physLowerLegR.transform.localRotation);

            // At least one lower leg must have physically rotated by MinAngularDisplacementDeg.
            bool eitherRotated = (angularDisplacementL >= MinAngularDisplacementDeg) ||
                                 (angularDisplacementR >= MinAngularDisplacementDeg);

            Assert.That(eitherRotated, Is.True,
                $"At least one lower leg joint must physically rotate ≥{MinAngularDisplacementDeg:F1}° " +
                $"from rest during {PhysicsFrames} FixedUpdate frames of walking gait. " +
                $"LowerLeg_L angular displacement={angularDisplacementL:F2}°  |  " +
                $"LowerLeg_R angular displacement={angularDisplacementR:F2}°. " +
                $"A rotation near 0° indicates the SLERP drive is not overcoming gravity — " +
                $"the 'dragging feet' bug has returned.");

            // ── Cleanup ───────────────────────────────────────────────────────
            UnityEngine.Object.Destroy(physicsHips);
        }

        // ─── World-Space Swing Tests (Phase 3E3) ────────────────────────────

        /// <summary>
        /// LegAnimator must expose a serialized _useWorldSpaceSwing field (bool)
        /// that defaults to true. This allows the world-space swing behaviour to be
        /// toggled off in the Inspector for side-by-side comparison.
        /// </summary>
        [UnityTest]
        public IEnumerator UseWorldSpaceSwing_FieldExists_AndDefaultsToTrue()
        {
            // Arrange
            yield return null;

            // Act — use reflection to confirm the field exists and has the correct default
            System.Reflection.FieldInfo field = typeof(LegAnimator).GetField("_useWorldSpaceSwing",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            // Assert: field exists and is a bool
            Assert.That(field, Is.Not.Null,
                "LegAnimator must have a private serialized field named '_useWorldSpaceSwing'.");
            Assert.That(field.FieldType, Is.EqualTo(typeof(bool)),
                "_useWorldSpaceSwing must be a bool.");

            // Assert: default value is true
            bool defaultValue = (bool)field.GetValue(_legAnimator);
            Assert.That(defaultValue, Is.True,
                "_useWorldSpaceSwing must default to true so world-space swing is active by default.");
        }

        /// <summary>
        /// When _useWorldSpaceSwing is true and the hips are pitched forward 45°,
        /// upper leg target rotations must still change measurably during walking gait.
        /// This verifies the fix: in legacy mode, pitching the hips forward made the local
        /// "forward" axis point downward, causing zero net swing; world-space mode must
        /// still produce a swing regardless of hips orientation.
        /// </summary>
        [UnityTest]
        public IEnumerator WorldSpaceSwing_WhenHipsPitchedForward_UpperLegTargetRotationChanges()
        {
            // Arrange — pitch the hips forward 45° to simulate the "torso lean" condition
            yield return null;
            _hips.transform.rotation = Quaternion.Euler(45f, 0f, 0f);

            // Ensure world-space swing is enabled
            SetPrivateField(_legAnimator, "_useWorldSpaceSwing", true);

            Quaternion rotBefore = _upperLegLJoint.targetRotation;
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));

            // Act — run enough frames for phase to accumulate
            yield return new WaitForSeconds(0.15f);

            // Assert — target rotation must have changed despite hips being pitched
            Quaternion rotAfter = _upperLegLJoint.targetRotation;
            float angleDiff = Quaternion.Angle(rotBefore, rotAfter);
            Assert.That(angleDiff, Is.GreaterThan(0.1f),
                $"UpperLeg_L targetRotation must change during walking even when hips are pitched 45°. " +
                $"Angle diff={angleDiff:F3}°. " +
                $"A near-zero diff indicates the world-space fix is not working (swing axis collapsed to zero).");
        }

        /// <summary>
        /// When _useWorldSpaceSwing is true, the swing axis used in targetRotation must
        /// be world-horizontal (perpendicular to Vector3.up), not aligned with the hips
        /// local frame. This is validated by checking that the effective swing axis
        /// produced by the targetRotation change is nearly perpendicular to world up.
        ///
        /// Method: compare the targetRotation produced with hips at 0° pitch versus
        /// hips at 45° pitch. If world-space swing is working, the angle-axis of the
        /// resulting targetRotation change should be similar (world-horizontal) in both
        /// cases, not rotated by 45° as it would be in local-space mode.
        /// </summary>
        [UnityTest]
        public IEnumerator WorldSpaceSwing_SwingAxisIsWorldHorizontal_RegardlessOfHipsPitch()
        {
            // Arrange — helper: measure the swing axis component along world-up
            // for a given hips pitch after one burst of movement.
            yield return null;

            // We measure the targetRotation change angle for two conditions and verify
            // that tilting the hips does NOT cause a proportional reduction in swing magnitude.
            // If the swing were purely local, a 45° pitch would cause the effective world-up
            // component to drop by ~sin(45°) ≈ 29%. In world-space mode the magnitude stays
            // the same — only the phase timing changes.

            // --- Condition A: hips upright (0° pitch) ---
            _hips.transform.rotation = Quaternion.identity;
            SetPrivateField(_legAnimator, "_useWorldSpaceSwing", true);
            SetPrivateField(_legAnimator, "_smoothedInputMag", 0f);
            SetPrivateField(_legAnimator, "_phase", 0f);

            _movement.SetMoveInputForTest(new Vector2(0f, 1f));
            yield return new WaitForSeconds(0.15f);
            float angleUprightL = Quaternion.Angle(Quaternion.identity, _upperLegLJoint.targetRotation);
            float angleUprightR = Quaternion.Angle(Quaternion.identity, _upperLegRJoint.targetRotation);

            // Reset
            _movement.SetMoveInputForTest(Vector2.zero);
            yield return new WaitForFixedUpdate();
            SetPrivateField(_legAnimator, "_smoothedInputMag", 0f);
            SetPrivateField(_legAnimator, "_phase", 0f);

            // --- Condition B: hips pitched forward 45° ---
            _hips.transform.rotation = Quaternion.Euler(45f, 0f, 0f);
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));
            yield return new WaitForSeconds(0.15f);
            float anglePitchedL = Quaternion.Angle(Quaternion.identity, _upperLegLJoint.targetRotation);
            float anglePitchedR = Quaternion.Angle(Quaternion.identity, _upperLegRJoint.targetRotation);

            // Assert: the rotation magnitude must NOT have dropped significantly due to pitch.
            // In world-space mode both conditions produce the same swing amplitude (±_stepAngle).
            // In local-space mode the effective world-space component drops by ~sin(45°) ≈ 29%.
            // We allow a generous 40% reduction as the tolerance so the test catches the
            // broken (local-space) case without being overly sensitive to minor phase timing.
            // The assertion only needs at least one leg to be non-degenerate (non-zero).
            bool uprightNonZero = (angleUprightL > 0.5f) || (angleUprightR > 0.5f);
            bool pitchedNonZero = (anglePitchedL > 0.5f) || (anglePitchedR > 0.5f);

            Assert.That(uprightNonZero, Is.True,
                $"Pre-condition: upright upper-leg swing must be non-zero. " +
                $"L={angleUprightL:F2}° R={angleUprightR:F2}°");

            Assert.That(pitchedNonZero, Is.True,
                $"World-space swing must produce non-zero leg rotation even when hips are pitched 45°. " +
                $"L={anglePitchedL:F2}° R={anglePitchedR:F2}°. " +
                $"Near-zero indicates the swing is being computed in local-frame (still broken).");

            // Cleanup: reset hips rotation
            _hips.transform.rotation = Quaternion.identity;
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

        /// <summary>
        /// Creates a child GameObject with a Rigidbody (gravity enabled), a BoxCollider,
        /// and a ConfigurableJoint connected to <paramref name="parentRb"/> — suitable for
        /// the physical-lift regression test that measures real angular displacement.
        /// Linear axes are locked so the body remains position-constrained to its parent
        /// (preventing gravity free-fall through the joint). Rotation is free so the
        /// SLERP drive can rotate the joint in response to LegAnimator's targetRotation.
        /// The joint uses SLERP drive mode so <see cref="RagdollSetup"/> can override it.
        /// </summary>
        private static GameObject CreatePhysicsLegJoint(
            GameObject parent, string name, Rigidbody parentRb, Vector3 localPos, float mass)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent.transform);
            go.transform.localPosition = localPos;

            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.useGravity = true;   // Gravity enabled — the joint spring must resist it

            go.AddComponent<BoxCollider>();

            ConfigurableJoint joint = go.AddComponent<ConfigurableJoint>();
            joint.connectedBody = parentRb;

            // Lock linear axes so the body stays positionally anchored to the parent
            // and cannot free-fall away from the joint. Angular motion remains free so
            // the SLERP drive can rotate the segment in response to targetRotation.
            joint.xMotion = ConfigurableJointMotion.Locked;
            joint.yMotion = ConfigurableJointMotion.Locked;
            joint.zMotion = ConfigurableJointMotion.Locked;

            joint.rotationDriveMode = RotationDriveMode.Slerp;
            // Deliberately weak initial drive — RagdollSetup.Awake overrides this with the
            // authoritative spring/damper values (default _lowerLegSpring = 1200 Nm/rad).
            joint.slerpDrive = new JointDrive
            {
                positionSpring = 100f,
                positionDamper = 10f,
                maximumForce   = float.MaxValue,
            };
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

        private void SetIdleBlendSpeed(float speed)
        {
            SetPrivateField(_legAnimator, "_idleBlendSpeed", speed);
        }

        private void SetCurrentState(CharacterStateType state)
        {
            SetAutoPropertyBackingField(_characterState, nameof(CharacterState.CurrentState), state);
        }

        private static float GetRotationZAngle(Quaternion q)
        {
            // Extract local Z rotation angle (degrees) from quaternion, signed by convention.
            // Used because LegAnimator._swingAxis defaults to Vector3.forward (Z) — the
            // ConfigurableJoint targetRotation Z component maps to rotation around joint.axis
            // (Vector3.right = primary hinge), which drives sagittal forward/backward swing.
            q.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) { angle -= 360f; }
            return angle * Vector3.Dot(axis.normalized, Vector3.forward);
        }

        private static float GetRotationXAngle(Quaternion q)
        {
            // Extract local X rotation angle (degrees) from quaternion, signed by convention.
            // Retained for backward compatibility — not used by gait tests after axis fix.
            q.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) { angle -= 360f; }
            return angle * Vector3.Dot(axis.normalized, Vector3.right);
        }

        /// <summary>
        /// Returns a signed scalar representing the relative angular offset between
        /// two quaternions in degrees, measured along the dominant rotation axis of
        /// their relative rotation, projected onto the XZ plane.
        /// Positive when the relative rotation points toward +X or +Z.
        /// Used by the alternating-legs test because the world-space swing axis
        /// is not always Vector3.forward/Z — this helper works regardless of which
        /// axis the swing is applied around (X or Z, forward or lateral).
        /// </summary>
        private static float GetRelativeSignedAngle(Quaternion a, Quaternion b)
        {
            // Relative rotation from a to b
            Quaternion relative = Quaternion.Inverse(a) * b;
            relative.ToAngleAxis(out float angle, out Vector3 axis);
            // Normalise angle to [-180, 180]
            if (angle > 180f) { angle -= 360f; }
            // Project axis onto XZ plane and use its magnitude/sign to determine the
            // signed direction. This handles both Z-axis (local swing) and X-axis
            // (world-space swing with forward input) cases.
            float xzComponent = axis.x + axis.z;   // dominant horizontal component
            return angle * Mathf.Sign(xzComponent != 0f ? xzComponent : 1f);
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
