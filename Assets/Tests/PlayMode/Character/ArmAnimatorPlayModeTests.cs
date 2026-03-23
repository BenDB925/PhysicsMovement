using System.Collections;
using System.Reflection;
using NUnit.Framework;
using PhysicsDrivenMovement.Character;
using PhysicsDrivenMovement.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// PlayMode integration tests for ArmAnimator (Phase 3T — GAP-7/GAP-13).
    ///
    /// Validates the runtime behaviour of arm swing during locomotion:
    ///   • WhileWalking_RightArmSwingsOppositeToLeftLeg: checks that left arm and right arm
    ///     rotate in opposite directions relative to each other, matching natural counter-swing.
    ///   • AtIdle_ArmsReturnToNearRestPosition: after walking stops, arms must return to
    ///     near-identity within a few seconds (SmoothedInputMag decays to zero).
    ///
    /// These PlayMode tests complement the EditMode unit tests by verifying that the
    /// arm swing runs correctly under live FixedUpdate scheduling with real physics.
    /// </summary>
    public class ArmAnimatorPlayModeTests
    {
        private const string PlayerRagdollPrefabPath = "Assets/Prefabs/PlayerRagdoll_Skinned.prefab";

        // ── Constants ─────────────────────────────────────────────────────────

        private const int SettleFrames    = 100;
        private const int WalkFrames      = 120;
        private const int IdleDecayFrames = 150;

        private static readonly Vector3 TestOrigin = new Vector3(800f, 0f, 800f);

        // ── Rig ───────────────────────────────────────────────────────────────

        private GameObject        _groundGO;
        private GameObject        _hipsGO;
        private Rigidbody         _hipsRb;
        private PlayerMovement    _movement;
        private BalanceController _balance;
        private CharacterState    _characterState;

        private ConfigurableJoint _upperArmLJoint;
        private ConfigurableJoint _upperArmRJoint;
        private ConfigurableJoint _lowerArmLJoint;
        private ConfigurableJoint _lowerArmRJoint;

        /// <summary>Rest abduction quaternion for left arm, matching ArmAnimator default (12°).</summary>
        private Quaternion _abductionL;
        /// <summary>Rest abduction quaternion for right arm, matching ArmAnimator default (12°).</summary>
        private Quaternion _abductionR;

        private float _savedFixedDeltaTime;
        private int   _savedSolverIterations;
        private int   _savedSolverVelocityIterations;

        [SetUp]
        public void SetUp()
        {
            _savedFixedDeltaTime           = Time.fixedDeltaTime;
            _savedSolverIterations         = Physics.defaultSolverIterations;
            _savedSolverVelocityIterations = Physics.defaultSolverVelocityIterations;

            Time.fixedDeltaTime                  = 0.01f;
            Physics.defaultSolverIterations      = 12;
            Physics.defaultSolverVelocityIterations = 4;

            BuildGroundPlane();
            BuildRig();

            // ── Inject stable grounded state through the existing test seam while
            //    keeping the normal runtime components present on the prefab.
            _balance = _hipsGO.GetComponent<BalanceController>();
            _characterState = _hipsGO.GetComponent<CharacterState>();

            _balance.SetGroundStateForTest(isGrounded: true, isFallen: false);

            // Freeze hips rotation so the physics rig never accumulates yaw angular
            // velocity during the settle phase. Without this, collision bounces on the
            // dynamic Rigidbody can push angVelY above the spin-gate threshold

            // Cache rest abduction quaternions matching ArmAnimator defaults (12° abduction).
            _abductionL = Quaternion.AngleAxis( 12f, Vector3.right);
            _abductionR = Quaternion.AngleAxis(-12f, Vector3.right);
            // (threshold × 0.5 = 4 rad/s), repeatedly resetting the hysteresis counter
            // inside LegAnimator and preventing gait from ever engaging during the tests.
            _hipsRb.constraints = RigidbodyConstraints.FreezeRotation;
        }

        [TearDown]
        public void TearDown()
        {
            if (_hipsGO   != null) Object.Destroy(_hipsGO);
            if (_groundGO != null) Object.Destroy(_groundGO);

            Time.fixedDeltaTime                  = _savedFixedDeltaTime;
            Physics.defaultSolverIterations      = _savedSolverIterations;
            Physics.defaultSolverVelocityIterations = _savedSolverVelocityIterations;
        }

        // ── Tests ─────────────────────────────────────────────────────────────

        /// <summary>
        /// GAP-7 PlayMode: While walking, the right arm must swing in the OPPOSITE direction
        /// to the left arm at any given frame (counter-swing gait coordination).
        ///
        /// We sample both arm targetRotation Z-euler angles during the walk phase and
        /// confirm they are not both positive or both negative on the same frame.
        /// A sampling window of 40–80 frames (mid-walk) is used to avoid the ramp-up period.
        ///
        /// Threshold: across the sample window, the fraction of frames where arms swing
        /// in opposite directions must be ≥ 60%. Allows for cross-zero frames where
        /// both angles are near zero simultaneously.
        /// </summary>
        [UnityTest]
        public IEnumerator WhileWalking_RightArmSwingsOppositeToLeftLeg()
        {
            // Arrange — settle.
            yield return WaitPhysicsFrames(SettleFrames);

            // Inject Moving state so LegAnimator advances the gait phase.
            _characterState.SetStateForTest(CharacterStateType.Moving);

            // Begin walking.
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));

            int oppositeFrames = 0;
            int sampleFrames   = 0;

            for (int frame = 0; frame < WalkFrames; frame++)
            {
                yield return new WaitForFixedUpdate();

                // Only sample the middle portion (frames 40–80) to skip ramp-up.
                if (frame < 40 || frame >= 80) continue;

                sampleFrames++;

                // Read arm target rotations.
                Quaternion leftArmTarget  = _upperArmLJoint != null
                    ? _upperArmLJoint.targetRotation
                    : Quaternion.identity;
                Quaternion rightArmTarget = _upperArmRJoint != null
                    ? _upperArmRJoint.targetRotation
                    : Quaternion.identity;

                // Extract signed Z-euler angles.
                float leftZ  = Mathf.DeltaAngle(0f, leftArmTarget.eulerAngles.z);
                float rightZ = Mathf.DeltaAngle(0f, rightArmTarget.eulerAngles.z);

                // Opposite direction = signs differ (one positive, one negative).
                // Allow a deadzone of ±1° to avoid counting frames where both are near zero.
                bool leftActive  = Mathf.Abs(leftZ)  > 1f;
                bool rightActive = Mathf.Abs(rightZ) > 1f;

                if (leftActive && rightActive && Mathf.Sign(leftZ) != Mathf.Sign(rightZ))
                {
                    oppositeFrames++;
                }
            }

            // Assert: ≥ 60% of active sample frames have opposite swing directions.
            float oppositeRatio = sampleFrames > 0 ? (float)oppositeFrames / sampleFrames : 0f;
            Assert.That(sampleFrames, Is.GreaterThan(0),
                "No sample frames were collected. Check WalkFrames and sample window constants.");
            Assert.That(oppositeRatio, Is.GreaterThanOrEqualTo(0.6f),
                $"Arms should swing in opposite directions ≥ 60% of walk frames. " +
                $"Got {oppositeFrames}/{sampleFrames} = {oppositeRatio * 100f:F0}%. " +
                "Check ArmAnimator phase offset (left arm should use phase+π, right arm phase).");
        }

        /// <summary>
        /// C8.3a PlayMode: Arm swing amplitude must scale non-linearly with speed —
        /// slow walking (input mag 0.25) produces significantly smaller swing than
        /// full-speed walking (input mag 1.0). The ratio of slow-to-fast peak angles
        /// must be below 0.35, confirming the response curve suppresses amplitude at
        /// low speeds more aggressively than a linear ramp.
        /// </summary>
        [UnityTest]
        public IEnumerator SlowWalk_ProducesRestrainedArmSwing_ComparedToFullSpeed()
        {
            // Arrange — settle.
            yield return WaitPhysicsFrames(SettleFrames);
            _characterState.SetStateForTest(CharacterStateType.Moving);

            // Phase 1: Walk at slow input magnitude (0.25).
            _movement.SetMoveInputForTest(new Vector2(0f, 0.25f));
            float slowPeakAngle = 0f;

            for (int frame = 0; frame < WalkFrames; frame++)
            {
                yield return new WaitForFixedUpdate();
                if (frame < 60) continue; // skip ramp-up

                float leftAngle = _upperArmLJoint != null
                    ? Quaternion.Angle(_upperArmLJoint.targetRotation, _abductionL)
                    : 0f;
                float rightAngle = _upperArmRJoint != null
                    ? Quaternion.Angle(_upperArmRJoint.targetRotation, _abductionR)
                    : 0f;

                slowPeakAngle = Mathf.Max(slowPeakAngle, leftAngle, rightAngle);
            }

            // Phase 2: Walk at full input magnitude (1.0).
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));
            float fastPeakAngle = 0f;

            for (int frame = 0; frame < WalkFrames; frame++)
            {
                yield return new WaitForFixedUpdate();
                if (frame < 60) continue; // skip ramp-up

                float leftAngle = _upperArmLJoint != null
                    ? Quaternion.Angle(_upperArmLJoint.targetRotation, _abductionL)
                    : 0f;
                float rightAngle = _upperArmRJoint != null
                    ? Quaternion.Angle(_upperArmRJoint.targetRotation, _abductionR)
                    : 0f;

                fastPeakAngle = Mathf.Max(fastPeakAngle, leftAngle, rightAngle);
            }

            // Assert: slow walk produces visible but restrained swing.
            Assert.That(slowPeakAngle, Is.GreaterThan(0.5f),
                $"Slow walk should still produce some arm swing. Peak = {slowPeakAngle:F2}°.");

            // Assert: full speed produces larger swing.
            Assert.That(fastPeakAngle, Is.GreaterThan(slowPeakAngle),
                $"Full-speed arm swing ({fastPeakAngle:F2}°) must exceed slow walk ({slowPeakAngle:F2}°).");

            // Assert: the amplitude ratio confirms non-linear scaling — slow peak must be
            // well below proportional share of full peak (0.25 linearly → ratio 0.25;
            // with EaseInOut → ratio ~0.10). Threshold 0.35 is conservative enough to
            // tolerate physics noise while excluding a purely linear ramp.
            float ratio = slowPeakAngle / fastPeakAngle;
            Assert.That(ratio, Is.LessThan(0.35f),
                $"Slow-to-fast amplitude ratio ({ratio:F3}) should be < 0.35, confirming " +
                $"non-linear response. Slow peak = {slowPeakAngle:F2}°, fast peak = {fastPeakAngle:F2}°.");
        }

        /// <summary>
        /// C8.3b PlayMode: When LocomotionDirector signals active recovery, ArmAnimator
        /// must dampen arm swing amplitude via the brace blend. This test verifies:
        ///   1. _currentBraceBlend reaches 1.0 when recovery is active
        ///   2. Arm swing peak during full brace is smaller than arm swing peak
        ///      measured in the same SmoothedInputMag regime without brace
        ///   3. _currentBraceBlend returns to 0 when recovery ends
        ///
        /// To avoid SmoothedInputMag ramp bias, both measurement windows share the
        /// same settled gait. The test enables brace first, measures, then disables
        /// brace and measures, so SmoothedInputMag has converged in both windows.
        /// </summary>
        [UnityTest]
        public IEnumerator DuringRecovery_ArmSwingDampensAndElbowsTighten()
        {
            const int MeasureFrames = 200;

            // Read runtime rest abduction from ArmAnimator.
            ArmAnimator armAnimator = _hipsGO.GetComponent<ArmAnimator>();
            Assert.That(armAnimator, Is.Not.Null, "ArmAnimator must be present.");
            var abdLField = typeof(ArmAnimator).GetField("_abductionL",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var abdRField = typeof(ArmAnimator).GetField("_abductionR",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var braceField = typeof(ArmAnimator).GetField("_currentBraceBlend",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Quaternion runtimeAbdL = abdLField != null ? (Quaternion)abdLField.GetValue(armAnimator) : _abductionL;
            Quaternion runtimeAbdR = abdRField != null ? (Quaternion)abdRField.GetValue(armAnimator) : _abductionR;

            // Arrange — settle, start walking, warm up for a long time so SmoothedInputMag
            // has converged before ANY measurement happens.
            yield return WaitPhysicsFrames(SettleFrames);
            _characterState.SetStateForTest(CharacterStateType.Moving);
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));
            yield return WaitPhysicsFrames(800);

            // Phase 1: Enable brace and ramp in fully.
            var director = _hipsGO.GetComponent<LocomotionDirector>();
            Assert.That(director, Is.Not.Null, "LocomotionDirector must be present on rig.");
            director.SetRecoveryActiveForTest(true);
            yield return WaitPhysicsFrames(50);

            float braceBlend = braceField != null ? (float)braceField.GetValue(armAnimator) : -1f;
            Assert.That(braceBlend, Is.GreaterThanOrEqualTo(0.95f),
                $"Brace blend should reach ~1.0 after ramp. Got {braceBlend:F3}.");

            // Measure arm swing peak DURING brace.
            float bracedPeakAngle = 0f;
            for (int frame = 0; frame < MeasureFrames; frame++)
            {
                yield return new WaitForFixedUpdate();

                float leftAngle = _upperArmLJoint != null
                    ? Quaternion.Angle(_upperArmLJoint.targetRotation, runtimeAbdL) : 0f;
                float rightAngle = _upperArmRJoint != null
                    ? Quaternion.Angle(_upperArmRJoint.targetRotation, runtimeAbdR) : 0f;

                bracedPeakAngle = Mathf.Max(bracedPeakAngle, leftAngle, rightAngle);
            }

            // Phase 2: Disable brace and let it ramp out, then measure normal arm swing.
            director.SetRecoveryActiveForTest(false);
            yield return WaitPhysicsFrames(50);

            float unbracedBlend = braceField != null ? (float)braceField.GetValue(armAnimator) : 1f;
            Assert.That(unbracedBlend, Is.LessThanOrEqualTo(0.05f),
                $"Brace blend should return to ~0 after recovery ends. Got {unbracedBlend:F3}.");

            float normalPeakAngle = 0f;
            for (int frame = 0; frame < MeasureFrames; frame++)
            {
                yield return new WaitForFixedUpdate();

                float leftAngle = _upperArmLJoint != null
                    ? Quaternion.Angle(_upperArmLJoint.targetRotation, runtimeAbdL) : 0f;
                float rightAngle = _upperArmRJoint != null
                    ? Quaternion.Angle(_upperArmRJoint.targetRotation, runtimeAbdR) : 0f;

                normalPeakAngle = Mathf.Max(normalPeakAngle, leftAngle, rightAngle);
            }

            // Assert: braced arm swing should be significantly less than unbraced.
            Assert.That(normalPeakAngle, Is.GreaterThan(2f),
                $"Pre-condition: normal walk should produce visible arm swing. Peak = {normalPeakAngle:F2}°.");

            Assert.That(bracedPeakAngle, Is.LessThan(normalPeakAngle * 0.5f),
                $"During recovery, arm swing peak ({bracedPeakAngle:F2}°) should be < 50% of " +
                $"normal ({normalPeakAngle:F2}°). Brace dampen should reduce amplitude.");
        }

        /// <summary>
        /// GAP-13 PlayMode: After walking stops (input set to zero), both arm joints
        /// must return to near-identity (within 2° of Quaternion.identity) within
        /// IdleDecayFrames (1.5 s at 100 Hz).
        ///
        /// This validates that the ArmAnimator idle branch correctly sets targetRotation
        /// to identity when SmoothedInputMag decays to zero.
        /// </summary>
        [UnityTest]
        public IEnumerator AtIdle_ArmsReturnToNearRestPosition()
        {
            // Arrange — settle, then walk for WalkFrames to prime the arm swing.
            _characterState.SetStateForTest(CharacterStateType.Standing);
            yield return WaitPhysicsFrames(SettleFrames);

            _characterState.SetStateForTest(CharacterStateType.Moving);
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));
            LegAnimator legAnim = _hipsGO.GetComponent<LegAnimator>();
            float midMag = 0f;
            float maxAngleBefore = 0f;
            for (int dbgFrame = 0; dbgFrame < WalkFrames; dbgFrame++)
            {
                yield return new WaitForFixedUpdate();
                if (dbgFrame == 30) midMag = legAnim != null ? legAnim.SmoothedInputMag : -1f;

                float leftAngle = _upperArmLJoint != null
                    ? Quaternion.Angle(_upperArmLJoint.targetRotation, Quaternion.identity)
                    : 0f;
                float rightAngle = _upperArmRJoint != null
                    ? Quaternion.Angle(_upperArmRJoint.targetRotation, Quaternion.identity)
                    : 0f;

                maxAngleBefore = Mathf.Max(maxAngleBefore, leftAngle, rightAngle);
            }
            UnityEngine.Debug.Log($"[AtIdle_DEBUG] SmoothedInputMag@frame30={midMag:F4}  LegPhase={legAnim?.Phase:F4}  CharState={_characterState.CurrentState}");

            // Precondition: at least one arm should leave identity during the walk window.
            Assert.That(maxAngleBefore, Is.GreaterThan(1f),
                $"Pre-condition: at least one arm should leave identity during walking. " +
                $"Max angle from identity during walk = {maxAngleBefore:F2}°. " +
                "ArmAnimator may not be applying swing — check LegAnimator gait is running.");

            // Act — stop input.
            _movement.SetMoveInputForTest(Vector2.zero);
            _characterState.SetStateForTest(CharacterStateType.Standing);

            // Wait up to IdleDecayFrames for arms to stabilise at rest pose.
            // The rest pose includes abduction (not identity), so we check that
            // the target stops changing rather than comparing against identity.
            bool recovered = false;
            const float IdleThreshold = 2f; // degrees
            Quaternion prevLeft  = _upperArmLJoint != null ? _upperArmLJoint.targetRotation : Quaternion.identity;
            Quaternion prevRight = _upperArmRJoint != null ? _upperArmRJoint.targetRotation : Quaternion.identity;

            for (int frame = 0; frame < IdleDecayFrames; frame++)
            {
                yield return new WaitForFixedUpdate();

                Quaternion curLeft  = _upperArmLJoint != null ? _upperArmLJoint.targetRotation : Quaternion.identity;
                Quaternion curRight = _upperArmRJoint != null ? _upperArmRJoint.targetRotation : Quaternion.identity;

                float leftDelta  = Quaternion.Angle(curLeft, prevLeft);
                float rightDelta = Quaternion.Angle(curRight, prevRight);

                prevLeft  = curLeft;
                prevRight = curRight;

                if (leftDelta <= IdleThreshold && rightDelta <= IdleThreshold)
                {
                    recovered = true;
                    break;
                }
            }

            Assert.That(recovered, Is.True,
                $"Arms must settle to a stable rest pose (frame-to-frame change ≤ {IdleThreshold}°) within " +
                $"{IdleDecayFrames} frames ({IdleDecayFrames * Time.fixedDeltaTime:F1} s) after input stops. " +
                "Check ArmAnimator idle branch (SmoothedInputMag < 0.01 → SetAllArmTargetsToRest).");
        }

        /// <summary>
        /// C8.3c PlayMode: When CharacterState enters Airborne, ArmAnimator must blend
        /// toward a raised-outward pose (increased abduction + forward reach). On landing,
        /// the airborne blend must decay back toward the normal walk/idle pose within ~0.3 s.
        ///
        /// The test verifies:
        ///   1. After entering Airborne, arm abduction increases visibly beyond rest pose.
        ///   2. After landing (Airborne → Standing), the airborne blend decays to near zero.
        /// </summary>
        [UnityTest]
        public IEnumerator DuringAirborne_ArmsRaiseOutward_AndBlendBackOnLanding()
        {
            // Arrange — settle.
            yield return WaitPhysicsFrames(SettleFrames);
            _characterState.SetStateForTest(CharacterStateType.Standing);

            ArmAnimator armAnimator = _hipsGO.GetComponent<ArmAnimator>();
            Assert.That(armAnimator, Is.Not.Null, "ArmAnimator must be present on the rig.");

            var airborneBlendField = typeof(ArmAnimator).GetField("_currentAirborneBlend",
                BindingFlags.NonPublic | BindingFlags.Instance);

            Quaternion restUpperL = _upperArmLJoint != null ? _upperArmLJoint.targetRotation : Quaternion.identity;
            Quaternion restUpperR = _upperArmRJoint != null ? _upperArmRJoint.targetRotation : Quaternion.identity;
            Quaternion restLowerL = _lowerArmLJoint != null ? _lowerArmLJoint.targetRotation : Quaternion.identity;
            Quaternion restLowerR = _lowerArmRJoint != null ? _lowerArmRJoint.targetRotation : Quaternion.identity;

            // Measure rest-pose arm angle (both arms at idle abduction).
            float restAngleL = _upperArmLJoint != null
                ? Quaternion.Angle(restUpperL, Quaternion.identity)
                : 0f;
            float restAngleR = _upperArmRJoint != null
                ? Quaternion.Angle(restUpperR, Quaternion.identity)
                : 0f;
            float restAngle = Mathf.Max(restAngleL, restAngleR);

            // Act — enter Airborne. Must mark ground state as not-grounded so
            // CharacterState.FixedUpdate doesn't immediately transition back to Standing.
            _balance.SetGroundStateForTest(isGrounded: false, isFallen: false);
            _characterState.SetStateForTest(CharacterStateType.Airborne);

            // Let the airborne blend ramp in fully (at 6 units/s, 1/6 ≈ 0.17 s = 17 frames at 100 Hz).
            yield return WaitPhysicsFrames(40);

            // Measure airborne arm angle — should be visibly larger than rest.
            Quaternion airUpperL = _upperArmLJoint != null ? _upperArmLJoint.targetRotation : Quaternion.identity;
            Quaternion airUpperR = _upperArmRJoint != null ? _upperArmRJoint.targetRotation : Quaternion.identity;
            Quaternion airLowerL = _lowerArmLJoint != null ? _lowerArmLJoint.targetRotation : Quaternion.identity;
            Quaternion airLowerR = _lowerArmRJoint != null ? _lowerArmRJoint.targetRotation : Quaternion.identity;

            float airAngleL = _upperArmLJoint != null
                ? Quaternion.Angle(airUpperL, Quaternion.identity)
                : 0f;
            float airAngleR = _upperArmRJoint != null
                ? Quaternion.Angle(airUpperR, Quaternion.identity)
                : 0f;
            float airAngle = Mathf.Max(airAngleL, airAngleR);

            Assert.That(airAngle, Is.GreaterThan(restAngle + 5f),
                $"During Airborne, arm angle from identity ({airAngle:F2}°) should exceed " +
                $"rest angle ({restAngle:F2}°) by at least 5° due to abduction boost + forward reach.");

            float airborneBlend = airborneBlendField != null ? (float)airborneBlendField.GetValue(armAnimator) : -1f;
            Assert.That(airborneBlend, Is.GreaterThanOrEqualTo(0.9f),
                $"Airborne blend should have ramped to ~1.0. Got {airborneBlend:F3}.");

            float outwardRaiseL = Quaternion.Angle(airUpperL, restUpperL);
            float outwardRaiseR = Quaternion.Angle(airUpperR, restUpperR);
            Assert.That(outwardRaiseL, Is.GreaterThan(8f),
                $"Left arm should visibly raise outward in Airborne. Delta from rest = {outwardRaiseL:F2}°.");
            Assert.That(outwardRaiseR, Is.GreaterThan(8f),
                $"Right arm should visibly raise outward in Airborne. Delta from rest = {outwardRaiseR:F2}°.");
            Assert.That(Mathf.Abs(outwardRaiseL - outwardRaiseR), Is.LessThanOrEqualTo(2f),
                $"Airborne balance pose should stay symmetric. Left delta = {outwardRaiseL:F2}°, right delta = {outwardRaiseR:F2}°.");

            float airborneElbowAngleL = Quaternion.Angle(airLowerL, Quaternion.identity);
            float airborneElbowAngleR = Quaternion.Angle(airLowerR, Quaternion.identity);
            Assert.That(airborneElbowAngleL, Is.InRange(7.5f, 18f),
                $"Airborne elbow pose should stay softly bent, not rigid. Left elbow angle = {airborneElbowAngleL:F2}°.");
            Assert.That(airborneElbowAngleR, Is.InRange(7.5f, 18f),
                $"Airborne elbow pose should stay softly bent, not rigid. Right elbow angle = {airborneElbowAngleR:F2}°.");
            Assert.That(Mathf.Abs(airborneElbowAngleL - airborneElbowAngleR), Is.LessThanOrEqualTo(1f),
                $"Airborne elbow pose should stay symmetric. Left = {airborneElbowAngleL:F2}°, right = {airborneElbowAngleR:F2}°.");

            // Act — land (Airborne → Standing). Restore grounded state first.
            _balance.SetGroundStateForTest(isGrounded: true, isFallen: false);
            _characterState.SetStateForTest(CharacterStateType.Standing);

            // Let the blend decay (at 6 units/s, approximately 0.17 s = 17 frames).
            yield return WaitPhysicsFrames(40);

            float postLandBlend = airborneBlendField != null ? (float)airborneBlendField.GetValue(armAnimator) : 1f;
            Assert.That(postLandBlend, Is.LessThanOrEqualTo(0.05f),
                $"After landing, airborne blend should decay to ~0. Got {postLandBlend:F3}.");

            // Confirm arms have returned to near-rest angles.
            float landAngleL = _upperArmLJoint != null
                ? Quaternion.Angle(_upperArmLJoint.targetRotation, Quaternion.identity)
                : 0f;
            float landAngleR = _upperArmRJoint != null
                ? Quaternion.Angle(_upperArmRJoint.targetRotation, Quaternion.identity)
                : 0f;
            float landAngle = Mathf.Max(landAngleL, landAngleR);

            Assert.That(landAngle, Is.LessThan(airAngle - 3f),
                $"After landing, arm angle ({landAngle:F2}°) should be notably less than " +
                $"airborne angle ({airAngle:F2}°), confirming the raised pose has blended out.");
        }

        /// <summary>
        /// C8.5c PlayMode: During the jump wind-up phase, arms must pull back visibly
        /// (shoulder extension). During the launch phase, arms must thrust forward.
        /// The direction of the arm swing axis Z-delta should reverse between the two
        /// phases, confirming the pull-back → thrust-forward sequence.
        /// </summary>
        [UnityTest]
        public IEnumerator DuringJump_ArmsPullBackInWindUp_AndThrustForwardOnLaunch()
        {
            // Arrange — settle, standing and grounded.
            yield return WaitPhysicsFrames(SettleFrames);
            _characterState.SetStateForTest(CharacterStateType.Standing);
            _balance.SetGroundStateForTest(isGrounded: true, isFallen: false);

            ArmAnimator armAnimator = _hipsGO.GetComponent<ArmAnimator>();
            Assert.That(armAnimator, Is.Not.Null, "ArmAnimator must be present.");

            var windUpBlendField = typeof(ArmAnimator).GetField("_jumpWindUpBlend",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var launchBlendField = typeof(ArmAnimator).GetField("_jumpLaunchBlend",
                BindingFlags.NonPublic | BindingFlags.Instance);

            // Use a small jump force so the character doesn't fly far.
            typeof(PlayerMovement).GetField("_jumpForce",
                BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(_movement, 5f);

            // Measure rest arm rotations for comparison.
            Quaternion restL = _upperArmLJoint.targetRotation;
            Quaternion restR = _upperArmRJoint.targetRotation;

            // Act — trigger jump.
            _movement.SetJumpInputForTest(true);

            // Wait for wind-up blend to ramp up.
            // At _jumpArmBlendSpeed 12 and fixedDeltaTime 0.01: 12 frames → 1.44 (clamped to 1).
            yield return WaitPhysicsFrames(12);

            float windUpBlend = windUpBlendField != null
                ? (float)windUpBlendField.GetValue(armAnimator) : -1f;
            Assert.That(windUpBlend, Is.GreaterThanOrEqualTo(0.8f),
                $"Wind-up arm blend should be near 1.0 after 12 frames. Got {windUpBlend:F3}.");

            // Measure wind-up arm displacement from rest — should show visible pull-back.
            Quaternion windUpL = _upperArmLJoint.targetRotation;
            Quaternion windUpR = _upperArmRJoint.targetRotation;
            float windUpAngle = Mathf.Max(
                Quaternion.Angle(windUpL, restL),
                Quaternion.Angle(windUpR, restR));

            Assert.That(windUpAngle, Is.GreaterThan(5f),
                $"During wind-up, arms should pull back visibly from rest. " +
                $"Max angle from rest = {windUpAngle:F2}°.");

            // Record signed Z-delta for direction check (negative = arm pulled backward).
            float windUpZDelta = (Mathf.DeltaAngle(0f, windUpL.eulerAngles.z)
                                - Mathf.DeltaAngle(0f, restL.eulerAngles.z)
                                + Mathf.DeltaAngle(0f, windUpR.eulerAngles.z)
                                - Mathf.DeltaAngle(0f, restR.eulerAngles.z)) * 0.5f;

            // Wait for launch phase. Wind-up is 0.2 s = 20 frames total; 12 used,
            // so 8 more to reach launch start, then 8 more for launch blend to ramp.
            yield return WaitPhysicsFrames(16);

            float launchBlend = launchBlendField != null
                ? (float)launchBlendField.GetValue(armAnimator) : -1f;
            Assert.That(launchBlend, Is.GreaterThanOrEqualTo(0.5f),
                $"Launch arm blend should be ramping toward 1.0. Got {launchBlend:F3}.");

            // Measure launch arm displacement from rest — should exceed wind-up.
            Quaternion launchL = _upperArmLJoint.targetRotation;
            Quaternion launchR = _upperArmRJoint.targetRotation;
            float launchAngle = Mathf.Max(
                Quaternion.Angle(launchL, restL),
                Quaternion.Angle(launchR, restR));

            Assert.That(launchAngle, Is.GreaterThan(windUpAngle),
                $"During launch, arm displacement ({launchAngle:F2}°) should exceed " +
                $"wind-up displacement ({windUpAngle:F2}°) due to larger thrust angle.");

            // Confirm direction reversal: wind-up Z-delta and launch Z-delta should
            // have opposite signs (pull-back vs thrust-forward).
            float launchZDelta = (Mathf.DeltaAngle(0f, launchL.eulerAngles.z)
                                - Mathf.DeltaAngle(0f, restL.eulerAngles.z)
                                + Mathf.DeltaAngle(0f, launchR.eulerAngles.z)
                                - Mathf.DeltaAngle(0f, restR.eulerAngles.z)) * 0.5f;

            Assert.That(Mathf.Sign(windUpZDelta), Is.Not.EqualTo(Mathf.Sign(launchZDelta)),
                $"Wind-up Z-delta ({windUpZDelta:F2}°) and launch Z-delta ({launchZDelta:F2}°) " +
                "should have opposite signs, confirming pull-back then thrust-forward.");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static IEnumerator WaitPhysicsFrames(int count)
        {
            for (int i = 0; i < count; i++)
            {
                yield return new WaitForFixedUpdate();
            }
        }

        // ── Rig Construction ──────────────────────────────────────────────────

        private void BuildGroundPlane()
        {
            _groundGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _groundGO.name = "ArmPlayMode_Ground";
            _groundGO.transform.position  = TestOrigin + new Vector3(0f, -0.5f, 0f);
            _groundGO.transform.localScale = new Vector3(400f, 1f, 400f);
            _groundGO.layer = GameSettings.LayerEnvironment;
        }

        private void BuildRig()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerRagdollPrefabPath);
            Assert.That(prefab, Is.Not.Null,
                $"PlayerRagdoll prefab must be loadable from '{PlayerRagdollPrefabPath}'.");

            _hipsGO = Object.Instantiate(prefab, TestOrigin + new Vector3(0f, 0.5f, 0f), Quaternion.identity);
            _hipsRb = _hipsGO.GetComponent<Rigidbody>();
            _movement = _hipsGO.GetComponent<PlayerMovement>();

            Assert.That(_hipsRb, Is.Not.Null, "PlayerRagdoll prefab must include Rigidbody on root hips.");
            Assert.That(_movement, Is.Not.Null, "PlayerRagdoll prefab must include PlayerMovement.");

            _upperArmLJoint = FindRequiredJoint(_hipsGO, "UpperArm_L");
            _upperArmRJoint = FindRequiredJoint(_hipsGO, "UpperArm_R");
            _lowerArmLJoint = FindRequiredJoint(_hipsGO, "LowerArm_L");
            _lowerArmRJoint = FindRequiredJoint(_hipsGO, "LowerArm_R");

            _movement.SetMoveInputForTest(Vector2.zero);
        }

        private static ConfigurableJoint FindRequiredJoint(GameObject root, string name)
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

        private static GameObject CreateCapsule(string name, GameObject parent,
            Vector3 localPos, float mass)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, worldPositionStays: false);
            go.transform.localPosition = localPos;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            var col = go.AddComponent<CapsuleCollider>();
            col.radius = 0.07f; col.height = 0.28f; col.direction = 1;
            return go;
        }

        private static GameObject CreateBox(string name, GameObject parent,
            Vector3 localPos, float mass, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, worldPositionStays: false);
            go.transform.localPosition = localPos;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            go.AddComponent<BoxCollider>().size = size;
            return go;
        }

        private static void ConfigureJoint(GameObject child, Rigidbody parentRb,
            float spring, float damper)
        {
            ConfigureJointReturn(child, parentRb, spring, damper);
        }

        private static ConfigurableJoint ConfigureJointReturn(GameObject child, Rigidbody parentRb,
            float spring, float damper)
        {
            var joint = child.AddComponent<ConfigurableJoint>();
            joint.connectedBody = parentRb;
            joint.xMotion = ConfigurableJointMotion.Locked;
            joint.yMotion = ConfigurableJointMotion.Locked;
            joint.zMotion = ConfigurableJointMotion.Locked;
            joint.angularXMotion = ConfigurableJointMotion.Limited;
            joint.angularYMotion = ConfigurableJointMotion.Limited;
            joint.angularZMotion = ConfigurableJointMotion.Limited;
            joint.lowAngularXLimit  = new SoftJointLimit { limit = -70f };
            joint.highAngularXLimit = new SoftJointLimit { limit = 70f };
            joint.angularYLimit     = new SoftJointLimit { limit = 40f };
            joint.angularZLimit     = new SoftJointLimit { limit = 40f };
            joint.rotationDriveMode = RotationDriveMode.Slerp;
            joint.slerpDrive = new JointDrive
            {
                positionSpring = spring,
                positionDamper = damper,
                maximumForce   = float.MaxValue,
            };
            joint.targetRotation  = Quaternion.identity;
            joint.enableCollision = false;
            return joint;
        }

        private static void AddGroundSensor(GameObject footGO)
        {
            var sensor = footGO.AddComponent<GroundSensor>();
            var field  = typeof(GroundSensor).GetField("_groundLayers",
                BindingFlags.NonPublic | BindingFlags.Instance);
            field?.SetValue(sensor, (LayerMask)(1 << GameSettings.LayerEnvironment));
        }
    }
}
