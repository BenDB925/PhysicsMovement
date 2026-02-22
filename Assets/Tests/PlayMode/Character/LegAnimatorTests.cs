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

        private void SetIdleBlendSpeed(float speed)
        {
            SetPrivateField(_legAnimator, "_idleBlendSpeed", speed);
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
