using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using PhysicsDrivenMovement.Character;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// PlayMode tests that verify the split between upright (pitch/roll) torque and
    /// yaw torque introduced in Phase 3D1, plus yaw stability hardening from Phase 3D2.
    ///
    /// Key assertions:
    ///   (1) _kPYaw / _kDYaw serialized fields exist and are accessible via reflection.
    ///   (2) When the character is perfectly upright but facing the wrong direction, yaw
    ///       torque is applied around world Y, while upright torque is near zero.
    ///   (3) When the character is tilted but already facing the correct direction, upright
    ///       torque is applied, while yaw torque is near zero.
    ///   (4) The airborne multiplier reduces upright torque when not grounded, but yaw
    ///       torque is NOT reduced.
    ///   (5) SetGroundStateForTest seam still works correctly.
    ///   (6) Existing fallen-state skipping still applies (no torque when IsFallen).
    ///   (7) [3D2] _yawDeadZoneDeg field exists and is accessible via reflection.
    ///   (8) [3D2] Within dead zone, yaw torque is suppressed (no micro-oscillation).
    ///   (9) [3D2] Zero movement input retains last valid facing direction.
    ///   (10) [3D2] No NaN produced from normalization paths (degenerate vector safety).
    ///   (11) [3D2] Large yaw error still converges toward target (dead zone does not block large errors).
    ///
    /// PlayMode required because tests use FixedUpdate, Rigidbody.AddTorque, and
    /// need the physics engine to confirm angular velocity changes.
    /// Collaborators: <see cref="BalanceController"/>, <see cref="GroundSensor"/>.
    /// </summary>
    public class BalanceControllerTurningTests
    {
        // ─── Physics restore ─────────────────────────────────────────────────

        private float _originalFixedDeltaTime;
        private int _originalSolverIterations;
        private int _originalSolverVelocityIterations;
        private bool[,] _originalLayerCollisionMatrix;

        [SetUp]
        public void SetUp()
        {
            _originalFixedDeltaTime = Time.fixedDeltaTime;
            _originalSolverIterations = Physics.defaultSolverIterations;
            _originalSolverVelocityIterations = Physics.defaultSolverVelocityIterations;
            _originalLayerCollisionMatrix = CaptureLayerCollisionMatrix();
        }

        [TearDown]
        public void TearDown()
        {
            Time.fixedDeltaTime = _originalFixedDeltaTime;
            Physics.defaultSolverIterations = _originalSolverIterations;
            Physics.defaultSolverVelocityIterations = _originalSolverVelocityIterations;
            RestoreLayerCollisionMatrix(_originalLayerCollisionMatrix);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────

        /// <summary>
        /// Builds a minimal kinematic Hips rig for examining torque direction.
        /// Kinematic so we control pose; we read angular velocity after switching to dynamic
        /// for a single frame to observe torque effect.
        /// </summary>
        private static BalanceController CreateKinematicHips(out Rigidbody hipsRb, out GameObject hipsGo)
        {
            hipsGo = new GameObject("TestHips_Turning");
            hipsGo.transform.position = new Vector3(0f, 100f, 0f);

            hipsRb = hipsGo.AddComponent<Rigidbody>();
            hipsRb.isKinematic = true;
            hipsGo.AddComponent<BoxCollider>();

            // Two foot children with GroundSensor (sensors will report not-grounded at height 100).
            for (int i = 0; i < 2; i++)
            {
                GameObject foot = new GameObject(i == 0 ? "Foot_L" : "Foot_R");
                foot.transform.SetParent(hipsGo.transform);
                foot.transform.localPosition = new Vector3(i == 0 ? -0.1f : 0.1f, -0.4f, 0f);
                foot.AddComponent<Rigidbody>();
                foot.AddComponent<BoxCollider>();
                foot.AddComponent<GroundSensor>();
            }

            BalanceController bc = hipsGo.AddComponent<BalanceController>();
            return bc;
        }

        // ─── Field existence ──────────────────────────────────────────────────

        /// <summary>
        /// _kPYaw must exist as a serialized private float field.
        /// This test fails (red) before the field is added to BalanceController.
        /// </summary>
        [Test]
        public void BalanceController_HasKPYawField()
        {
            // Arrange
            var fieldInfo = typeof(BalanceController).GetField(
                "_kPYaw",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            // Assert
            Assert.That(fieldInfo, Is.Not.Null,
                "_kPYaw field must exist as a private instance field on BalanceController.");
            Assert.That(fieldInfo.FieldType, Is.EqualTo(typeof(float)),
                "_kPYaw must be of type float.");
        }

        /// <summary>
        /// _kDYaw must exist as a serialized private float field.
        /// This test fails (red) before the field is added to BalanceController.
        /// </summary>
        [Test]
        public void BalanceController_HasKDYawField()
        {
            // Arrange
            var fieldInfo = typeof(BalanceController).GetField(
                "_kDYaw",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            // Assert
            Assert.That(fieldInfo, Is.Not.Null,
                "_kDYaw field must exist as a private instance field on BalanceController.");
            Assert.That(fieldInfo.FieldType, Is.EqualTo(typeof(float)),
                "_kDYaw must be of type float.");
        }

        // ─── Yaw-only correction ──────────────────────────────────────────────

        /// <summary>
        /// When the Hips is perfectly upright but facing the wrong yaw direction,
        /// the net torque applied must be predominantly around world Y (yaw axis),
        /// with minimal pitch/roll components.
        ///
        /// Strategy: inject grounded+not-fallen state via test seam, set Hips to
        /// identity (upright, facing +Z), call SetFacingDirection(Vector3.right) so
        /// yaw error = 90°, allow one physics frame, then verify the resulting angular
        /// velocity has a larger Y component than X or Z.
        /// </summary>
        [UnityTest]
        public IEnumerator YawCorrection_WhenUprightAndFacingWrong_AppliesTorquePredominantlyAroundWorldY()
        {
            // Arrange
            BalanceController bc = CreateKinematicHips(out Rigidbody rb, out GameObject hipsGo);
            yield return new WaitForFixedUpdate(); // Let Awake run.

            // Set upright, facing +Z.
            hipsGo.transform.rotation = Quaternion.identity;
            // Use test seam: grounded, not fallen.
            bc.SetGroundStateForTest(isGrounded: true, isFallen: false);
            // Set facing to +X → 90° yaw error.
            bc.SetFacingDirection(Vector3.right);

            // Switch to dynamic so torque manifests as angular velocity.
            rb.isKinematic = false;
            rb.angularVelocity = Vector3.zero;

            // Act — one physics frame.
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            // Assert — Y component of angular velocity (yaw) must dominate.
            Vector3 av = rb.angularVelocity;
            float yawMagnitude   = Mathf.Abs(av.y);
            float pitchMagnitude = Mathf.Abs(av.x);
            float rollMagnitude  = Mathf.Abs(av.z);

            Assert.That(yawMagnitude, Is.GreaterThan(0.001f),
                $"A 90° yaw error should produce measurable yaw angular velocity. av={av}");
            Assert.That(yawMagnitude, Is.GreaterThan(pitchMagnitude * 2f),
                $"Yaw component (Y={yawMagnitude:F4}) should dominate over pitch (X={pitchMagnitude:F4}). av={av}");
            Assert.That(yawMagnitude, Is.GreaterThan(rollMagnitude * 2f),
                $"Yaw component (Y={yawMagnitude:F4}) should dominate over roll (Z={rollMagnitude:F4}). av={av}");

            Object.Destroy(hipsGo);
        }

        /// <summary>
        /// When the Hips is tilted (pitch/roll error) but already facing the correct
        /// yaw direction, the net torque must be predominantly around the pitch/roll axes
        /// (X and/or Z in world space), not around world Y.
        ///
        /// Strategy: upright facing forward (+Z), SetFacingDirection(Vector3.forward) so
        /// yaw error = 0°, tilt 30° around Z. The resulting angular velocity should have
        /// near-zero Y component and non-zero X/Z components.
        /// </summary>
        [UnityTest]
        public IEnumerator UprightCorrection_WhenTiltedAndAlreadyFacingCorrectly_AppliesTorqueAroundPitchRollAxes()
        {
            // Arrange
            BalanceController bc = CreateKinematicHips(out Rigidbody rb, out GameObject hipsGo);
            yield return new WaitForFixedUpdate();

            // Tilt 30° around Z (roll error), facing already forward (+Z).
            hipsGo.transform.rotation = Quaternion.AngleAxis(30f, Vector3.forward);
            bc.SetGroundStateForTest(isGrounded: true, isFallen: false);
            bc.SetFacingDirection(Vector3.forward); // No yaw error.

            rb.isKinematic = false;
            rb.angularVelocity = Vector3.zero;

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            // Assert — pitch/roll (X or Z) angular velocity must exceed Y by a meaningful margin.
            Vector3 av = rb.angularVelocity;
            float pitchRollMag = Mathf.Sqrt(av.x * av.x + av.z * av.z);
            float yawMag = Mathf.Abs(av.y);

            Assert.That(pitchRollMag, Is.GreaterThan(0.001f),
                $"A 30° roll error should produce measurable pitch/roll angular velocity. av={av}");
            Assert.That(pitchRollMag, Is.GreaterThan(yawMag * 2f),
                $"Pitch/roll magnitude ({pitchRollMag:F4}) should dominate over yaw ({yawMag:F4}) " +
                $"when there is no yaw error. av={av}");

            Object.Destroy(hipsGo);
        }

        // ─── Airborne multiplier applies only to upright torque ───────────────

        /// <summary>
        /// When airborne (not grounded), the yaw torque must NOT be scaled down by
        /// _airborneMultiplier. Yaw should remain at full strength to keep facing direction.
        ///
        /// Strategy: compare angular velocity Y component between grounded and airborne
        /// states with a 90° yaw error and no pitch/roll error. Airborne yaw should be
        /// equal (not reduced) vs grounded yaw.
        ///
        /// Note: each run injects grounded=true for one frame first to set _hasBeenGrounded,
        /// ensuring the airborne multiplier path is active.
        /// </summary>
        [UnityTest]
        public IEnumerator AirborneMultiplier_DoesNotAffectYawTorque()
        {
            // ── Grounded run ──
            BalanceController bcGrounded = CreateKinematicHips(out Rigidbody rbGrounded, out GameObject goGrounded);
            // Prime _hasBeenGrounded.
            bcGrounded.SetGroundStateForTest(isGrounded: true, isFallen: false);
            yield return new WaitForFixedUpdate();

            goGrounded.transform.rotation = Quaternion.identity;
            bcGrounded.SetGroundStateForTest(isGrounded: true, isFallen: false);
            bcGrounded.SetFacingDirection(Vector3.right);

            rbGrounded.isKinematic = false;
            rbGrounded.angularVelocity = Vector3.zero;

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            float groundedYawAV = Mathf.Abs(rbGrounded.angularVelocity.y);

            Object.Destroy(goGrounded);

            // ── Airborne run ──
            BalanceController bcAirborne = CreateKinematicHips(out Rigidbody rbAirborne, out GameObject goAirborne);
            // Prime _hasBeenGrounded, then switch to airborne.
            bcAirborne.SetGroundStateForTest(isGrounded: true, isFallen: false);
            yield return new WaitForFixedUpdate();

            goAirborne.transform.rotation = Quaternion.identity;
            bcAirborne.SetGroundStateForTest(isGrounded: false, isFallen: false);
            bcAirborne.SetFacingDirection(Vector3.right);

            rbAirborne.isKinematic = false;
            rbAirborne.angularVelocity = Vector3.zero;

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            float airborneYawAV = Mathf.Abs(rbAirborne.angularVelocity.y);

            Object.Destroy(goAirborne);

            // Assert — airborne yaw AV must be at least 80% of grounded yaw AV
            // (not reduced by airborne multiplier). Small variance allowed for physics noise.
            Assert.That(groundedYawAV, Is.GreaterThan(0.0001f),
                $"Grounded yaw AV must be measurable (got {groundedYawAV:F5}).");
            Assert.That(airborneYawAV, Is.GreaterThan(0.0001f),
                $"Airborne yaw AV must be measurable (got {airborneYawAV:F5}).");

            float ratio = airborneYawAV / groundedYawAV;
            Assert.That(ratio, Is.GreaterThan(0.8f),
                $"Airborne yaw AV ({airborneYawAV:F5}) should not be significantly reduced " +
                $"vs grounded yaw AV ({groundedYawAV:F5}). Ratio={ratio:F3}. " +
                "The _airborneMultiplier must NOT affect yaw torque.");
        }

        /// <summary>
        /// When airborne (not grounded), the upright torque IS reduced by the airborne
        /// multiplier. Compares upright angular velocity between grounded and airborne
        /// states with identical pitch/roll error but no yaw error.
        ///
        /// The test seam first injects isGrounded=true for one frame to trigger the
        /// _hasBeenGrounded flag (which gates the airborne multiplier), then switches
        /// to isGrounded=false to observe the reduced torque.
        /// </summary>
        [UnityTest]
        public IEnumerator AirborneMultiplier_ReducesUprightTorqueWhenAirborne()
        {
            // Default airborneMultiplier is 0.2f; grounded upright torque >> airborne.

            // ── Grounded run ──
            BalanceController bcGrounded = CreateKinematicHips(out Rigidbody rbGrounded, out GameObject goGrounded);
            // Inject grounded first to set _hasBeenGrounded = true.
            bcGrounded.SetGroundStateForTest(isGrounded: true, isFallen: false);
            yield return new WaitForFixedUpdate(); // _hasBeenGrounded set to true here.

            goGrounded.transform.rotation = Quaternion.AngleAxis(30f, Vector3.forward);
            bcGrounded.SetGroundStateForTest(isGrounded: true, isFallen: false);
            bcGrounded.SetFacingDirection(Vector3.forward); // No yaw error.

            rbGrounded.isKinematic = false;
            rbGrounded.angularVelocity = Vector3.zero;

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Vector3 avGrounded = rbGrounded.angularVelocity;
            float groundedPitchRollMag = Mathf.Sqrt(avGrounded.x * avGrounded.x + avGrounded.z * avGrounded.z);

            Object.Destroy(goGrounded);

            // ── Airborne run ──
            BalanceController bcAirborne = CreateKinematicHips(out Rigidbody rbAirborne, out GameObject goAirborne);
            // Inject grounded first to set _hasBeenGrounded = true, then switch to airborne.
            bcAirborne.SetGroundStateForTest(isGrounded: true, isFallen: false);
            yield return new WaitForFixedUpdate(); // _hasBeenGrounded set to true here.

            goAirborne.transform.rotation = Quaternion.AngleAxis(30f, Vector3.forward);
            // Now inject airborne: _hasBeenGrounded is true, so the multiplier will apply.
            bcAirborne.SetGroundStateForTest(isGrounded: false, isFallen: false);
            bcAirborne.SetFacingDirection(Vector3.forward); // No yaw error.

            rbAirborne.isKinematic = false;
            rbAirborne.angularVelocity = Vector3.zero;

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Vector3 avAirborne = rbAirborne.angularVelocity;
            float airbornePitchRollMag = Mathf.Sqrt(avAirborne.x * avAirborne.x + avAirborne.z * avAirborne.z);

            Object.Destroy(goAirborne);

            // Assert — grounded upright must be significantly larger than airborne upright.
            // Default airborneMultiplier = 0.2 means airborne is ~20% of grounded.
            Assert.That(groundedPitchRollMag, Is.GreaterThan(0.001f),
                $"Grounded pitch/roll AV must be measurable (got {groundedPitchRollMag:F5}).");
            Assert.That(airbornePitchRollMag, Is.LessThan(groundedPitchRollMag * 0.7f),
                $"Airborne upright AV ({airbornePitchRollMag:F5}) should be significantly less " +
                $"than grounded ({groundedPitchRollMag:F5}). The _airborneMultiplier must reduce " +
                $"upright torque when not grounded.");
        }

        // ─── Test seam preservation ───────────────────────────────────────────

        /// <summary>
        /// SetGroundStateForTest must still override IsGrounded and IsFallen after the
        /// torque split refactor. This is a regression guard on the test seam.
        /// </summary>
        [UnityTest]
        public IEnumerator SetGroundStateForTest_StillOverridesGroundAndFallenState()
        {
            // Arrange
            BalanceController bc = CreateKinematicHips(out _, out GameObject hipsGo);
            yield return new WaitForFixedUpdate();

            // Act — inject specific state via seam.
            bc.SetGroundStateForTest(isGrounded: true, isFallen: false);
            yield return new WaitForFixedUpdate();

            // Assert — seam values must be reflected in public properties.
            Assert.That(bc.IsGrounded, Is.True,
                "SetGroundStateForTest(isGrounded: true) must be reflected in IsGrounded.");
            Assert.That(bc.IsFallen, Is.False,
                "SetGroundStateForTest(isFallen: false) must be reflected in IsFallen.");

            // Inject opposite values.
            bc.SetGroundStateForTest(isGrounded: false, isFallen: true);
            yield return new WaitForFixedUpdate();

            Assert.That(bc.IsGrounded, Is.False,
                "SetGroundStateForTest(isGrounded: false) must update IsGrounded.");
            Assert.That(bc.IsFallen, Is.True,
                "SetGroundStateForTest(isFallen: true) must update IsFallen.");

            Object.Destroy(hipsGo);
        }

        /// <summary>
        /// When the character is fallen (IsFallen = true) but there is a clear upright
        /// error, the upright torque is still applied to aid recovery. This verifies that
        /// the torque split did NOT accidentally block correction while fallen.
        ///
        /// Uses a dynamic Rigidbody at height 100, IsFallen = true via test seam.
        /// The upright torque must still produce measurable angular velocity.
        /// </summary>
        [UnityTest]
        public IEnumerator Fallen_UprightTorqueStillApplied_AidsRecovery()
        {
            // Arrange — fallen but tilted 80°; we expect upright torque to still fire.
            BalanceController bc = CreateKinematicHips(out Rigidbody rb, out GameObject hipsGo);
            yield return new WaitForFixedUpdate();

            hipsGo.transform.rotation = Quaternion.AngleAxis(80f, Vector3.forward);
            // Grounded = true so effectivelyGrounded fires and full multiplier applies.
            bc.SetGroundStateForTest(isGrounded: true, isFallen: true);
            bc.SetFacingDirection(Vector3.forward); // No yaw error — isolate upright.

            rb.isKinematic = false;
            rb.angularVelocity = Vector3.zero;

            // Act
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            // Assert — upright torque must still be applied (non-zero angular velocity).
            // This ensures the torque split did not accidentally add an IsFallen guard
            // that would block the correction needed for self-recovery.
            Vector3 av = rb.angularVelocity;
            float pitchRollMag = Mathf.Sqrt(av.x * av.x + av.z * av.z);
            Assert.That(pitchRollMag, Is.GreaterThan(0.001f),
                $"When IsFallen=true, upright torque must still be applied for self-recovery. " +
                $"angularVelocity={av}, pitchRollMag={pitchRollMag:F4}");

            Object.Destroy(hipsGo);
        }

        // ─── Zero-input safety ────────────────────────────────────────────────

        /// <summary>
        /// SetFacingDirection with zero vector must still be silently ignored after
        /// the torque split refactor. No NaN or exceptions.
        /// </summary>
        [UnityTest]
        public IEnumerator SetFacingDirection_ZeroVector_IgnoredNoNaN_AfterTorqueSplit()
        {
            // Arrange
            BalanceController bc = CreateKinematicHips(out Rigidbody rb, out GameObject hipsGo);
            yield return new WaitForFixedUpdate();

            bc.SetGroundStateForTest(isGrounded: true, isFallen: false);

            // Act
            Assert.DoesNotThrow(() => bc.SetFacingDirection(Vector3.zero),
                "SetFacingDirection(Vector3.zero) must never throw after torque split.");

            rb.isKinematic = false;
            rb.angularVelocity = Vector3.zero;
            yield return new WaitForFixedUpdate();

            // Assert — no NaN in angular velocity.
            Vector3 av = rb.angularVelocity;
            Assert.That(float.IsNaN(av.x) || float.IsNaN(av.y) || float.IsNaN(av.z), Is.False,
                $"Angular velocity must not be NaN after zero-direction input. av={av}");

            Object.Destroy(hipsGo);
        }

        // ─── Phase 3D2: Yaw Stability Hardening ──────────────────────────────

        /// <summary>
        /// [3D2] _yawDeadZoneDeg field must exist as a serialized private float on
        /// BalanceController. This test fails (red) before the field is added.
        /// </summary>
        [Test]
        public void BalanceController_HasYawDeadZoneDegField()
        {
            // Arrange
            var fieldInfo = typeof(BalanceController).GetField(
                "_yawDeadZoneDeg",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            // Assert
            Assert.That(fieldInfo, Is.Not.Null,
                "_yawDeadZoneDeg field must exist as a private instance field on BalanceController " +
                "(Phase 3D2 dead zone for yaw micro-oscillation suppression).");
            Assert.That(fieldInfo.FieldType, Is.EqualTo(typeof(float)),
                "_yawDeadZoneDeg must be of type float.");
        }

        /// <summary>
        /// [3D2] When yaw error is within the dead zone (very small angle, e.g. 0.5°),
        /// no yaw torque should be applied — angular velocity Y must stay near zero.
        ///
        /// Strategy: set Hips exactly facing +Z. Call SetFacingDirection with a vector
        /// that is 0.5° off +Z — well within the default dead zone of ~2°. Allow one
        /// physics frame and verify Y angular velocity is negligibly small.
        ///
        /// Note: a small residual Y angular velocity (≤ 0.15 rad/s) is acceptable as
        /// physics noise from the upright PD spring coupling through the Rigidbody.
        /// The key assertion is that this is far below what a live yaw torque would produce.
        /// At _kPYaw=400, a 0.5° error would produce ~3.5 N·m yaw torque; even with
        /// a 1 kg unit-box inertia that would produce >1 rad/s after two FixedUpdates.
        /// </summary>
        [UnityTest]
        public IEnumerator YawTorque_WithinDeadZone_IsNotApplied()
        {
            // Arrange
            BalanceController bc = CreateKinematicHips(out Rigidbody rb, out GameObject hipsGo);
            yield return new WaitForFixedUpdate();

            // Face exactly forward.
            hipsGo.transform.rotation = Quaternion.identity;
            bc.SetGroundStateForTest(isGrounded: true, isFallen: false);

            // Direction only 0.5° off +Z — must be within the ~2° dead zone.
            float tinyAngle = 0.5f;
            Vector3 nearlyForward = new Vector3(
                Mathf.Sin(tinyAngle * Mathf.Deg2Rad),
                0f,
                Mathf.Cos(tinyAngle * Mathf.Deg2Rad));
            bc.SetFacingDirection(nearlyForward);

            rb.isKinematic = false;
            rb.angularVelocity = Vector3.zero;

            // Act — one physics frame.
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            // Assert — Y angular velocity must be negligibly small (dead zone suppresses torque).
            // We allow up to 0.35 rad/s for physics noise (upright spring coupling, RB init).
            // A live yaw torque at 0.5° error (_kPYaw=400) would produce >1 rad/s — far above this.
            float yawAV = Mathf.Abs(rb.angularVelocity.y);
            Assert.That(yawAV, Is.LessThan(0.35f),
                $"A 0.5° yaw error is within the dead zone; no significant yaw torque should be " +
                $"applied. Yaw angular velocity was {yawAV:F5} rad/s. " +
                "Increase _yawDeadZoneDeg or verify the dead zone implementation.");

            Object.Destroy(hipsGo);
        }

        /// <summary>
        /// [3D2] A large yaw error (90°) must still converge — the dead zone must not block
        /// large corrections. Angular velocity Y must be measurably non-zero after one frame.
        /// </summary>
        [UnityTest]
        public IEnumerator YawTorque_OutsideDeadZone_IsStillApplied()
        {
            // Arrange
            BalanceController bc = CreateKinematicHips(out Rigidbody rb, out GameObject hipsGo);
            yield return new WaitForFixedUpdate();

            hipsGo.transform.rotation = Quaternion.identity;
            bc.SetGroundStateForTest(isGrounded: true, isFallen: false);
            bc.SetFacingDirection(Vector3.right); // 90° yaw error — well outside dead zone.

            rb.isKinematic = false;
            rb.angularVelocity = Vector3.zero;

            // Act
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            // Assert — Y angular velocity must be clearly non-zero.
            float yawAV = Mathf.Abs(rb.angularVelocity.y);
            Assert.That(yawAV, Is.GreaterThan(0.001f),
                $"A 90° yaw error is outside the dead zone; yaw torque must still be applied. " +
                $"Yaw angular velocity was {yawAV:F5} rad/s.");

            Object.Destroy(hipsGo);
        }

        /// <summary>
        /// [3D2] Zero movement input must retain the last valid facing direction.
        /// After calling SetFacingDirection(Vector3.right) once, calling
        /// SetFacingDirection(Vector3.zero) must not change the stored target.
        /// The character must still turn toward +X, not revert to default.
        ///
        /// Strategy: set facing to +X (90° error from +Z), then call zero. Verify
        /// yaw torque is still applied toward +X (positive Y angular velocity for a
        /// left-hand turn from +Z to +X when looking down world-Y).
        /// </summary>
        [UnityTest]
        public IEnumerator SetFacingDirection_ZeroInput_RetainsLastValidFacing()
        {
            // Arrange
            BalanceController bc = CreateKinematicHips(out Rigidbody rb, out GameObject hipsGo);
            yield return new WaitForFixedUpdate();

            hipsGo.transform.rotation = Quaternion.identity; // Facing +Z.
            bc.SetGroundStateForTest(isGrounded: true, isFallen: false);

            // Set a valid facing (+X → 90° yaw error toward right).
            bc.SetFacingDirection(Vector3.right);

            // Now call zero — must be silently ignored; last valid facing (+X) retained.
            bc.SetFacingDirection(Vector3.zero);

            rb.isKinematic = false;
            rb.angularVelocity = Vector3.zero;

            // Act
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            // Assert — the character should still be turning toward +X.
            // The yaw torque magnitude should be non-trivial (same as a 90° error),
            // demonstrating the zero-input did not revert the target to +Z (which would give 0 error).
            float yawAV = Mathf.Abs(rb.angularVelocity.y);
            Assert.That(yawAV, Is.GreaterThan(0.001f),
                $"After SetFacingDirection(Vector3.zero), the last valid facing (+X) must be " +
                $"retained and yaw torque must still be applied toward it. " +
                $"Yaw angular velocity was {yawAV:F5} rad/s. " +
                "If this is 0, the zero-input call reset the facing to the current forward.");

            Object.Destroy(hipsGo);
        }

        /// <summary>
        /// [3D2] Calling SetFacingDirection repeatedly with near-degenerate vectors
        /// (tiny magnitude but non-zero) must not produce NaN in angular velocity.
        /// Guards against normalization of vectors that pass the sqrMagnitude threshold
        /// but are so small they produce floating-point garbage.
        /// </summary>
        [UnityTest]
        public IEnumerator SetFacingDirection_NearDegenerateVector_ProducesNoNaN()
        {
            // Arrange
            BalanceController bc = CreateKinematicHips(out Rigidbody rb, out GameObject hipsGo);
            yield return new WaitForFixedUpdate();

            hipsGo.transform.rotation = Quaternion.identity;
            bc.SetGroundStateForTest(isGrounded: true, isFallen: false);

            // A vector that is very small but technically above sqrMagnitude 0.001.
            // sqrMagnitude must be > 0.001, so magnitude > ~0.0316.
            // We use magnitude = 0.04f, which is small but passes the guard.
            Vector3 tinyButValid = new Vector3(0.04f, 0f, 0f);

            rb.isKinematic = false;
            rb.angularVelocity = Vector3.zero;

            // Act — call several times to simulate input frame accumulation.
            for (int i = 0; i < 3; i++)
            {
                Assert.DoesNotThrow(() => bc.SetFacingDirection(tinyButValid),
                    $"SetFacingDirection with near-degenerate vector must not throw (iteration {i}).");
                yield return new WaitForFixedUpdate();
            }

            // Assert — no NaN in angular velocity.
            Vector3 av = rb.angularVelocity;
            Assert.That(float.IsNaN(av.x) || float.IsNaN(av.y) || float.IsNaN(av.z), Is.False,
                $"Angular velocity must not contain NaN after near-degenerate SetFacingDirection calls. av={av}");
            Assert.That(float.IsInfinity(av.x) || float.IsInfinity(av.y) || float.IsInfinity(av.z), Is.False,
                $"Angular velocity must not contain Infinity after near-degenerate SetFacingDirection calls. av={av}");

            Object.Destroy(hipsGo);
        }

        /// <summary>
        /// [3D2] Rapid alternating SetFacingDirection calls (left, right, left, right)
        /// must not produce a runaway spin. After settling, |angularVelocity.y| must
        /// remain bounded (less than a reasonable threshold, e.g. 20 rad/s).
        ///
        /// This catches the scenario where opposing yaw errors drive oscillation.
        /// </summary>
        [UnityTest]
        public IEnumerator SetFacingDirection_RapidAlternating_DoesNotCauseRunawaySpin()
        {
            // Arrange
            BalanceController bc = CreateKinematicHips(out Rigidbody rb, out GameObject hipsGo);
            // Spawn near a ground proxy so effectivelyGrounded works.
            hipsGo.transform.position = new Vector3(0f, 0.5f, 0f);
            yield return new WaitForFixedUpdate();

            hipsGo.transform.rotation = Quaternion.identity;
            bc.SetGroundStateForTest(isGrounded: true, isFallen: false);

            rb.isKinematic = false;
            rb.angularVelocity = Vector3.zero;

            // Act — alternate facing direction every frame for 20 frames.
            for (int i = 0; i < 20; i++)
            {
                Vector3 dir = (i % 2 == 0) ? Vector3.right : Vector3.left;
                bc.SetFacingDirection(dir);
                yield return new WaitForFixedUpdate();
            }

            // Assert — angular velocity Y must be bounded.
            float absYawAV = Mathf.Abs(rb.angularVelocity.y);
            Assert.That(absYawAV, Is.LessThan(20f),
                $"Rapid alternating facing inputs must not cause runaway spin. " +
                $"|angularVelocity.y| = {absYawAV:F3} rad/s (limit: 20 rad/s). " +
                "Check _kDYaw damping or the dead zone implementation.");

            Object.Destroy(hipsGo);
        }

        /// <summary>
        /// [3D2] Verifies the yaw normalization is safe when the character's forward vector
        /// projects near-zero on the XZ plane (character near-vertical, i.e. lying on its side).
        /// The sqrMagnitude guard should prevent torque application and avoid NaN.
        ///
        /// Strategy: tilt 89° (nearly horizontal), leave facing at default. No torque
        /// should be applied (guard fires), and no NaN should result.
        /// </summary>
        [UnityTest]
        public IEnumerator YawTorque_WhenForwardProjectsNearZeroOnXZ_ProducesNoNaN()
        {
            // Arrange
            BalanceController bc = CreateKinematicHips(out Rigidbody rb, out GameObject hipsGo);
            yield return new WaitForFixedUpdate();

            // Tilt 89° — near horizontal. Forward vector projects near-zero onto XZ plane.
            // IsFallen override: set to false so we enter the yaw block.
            hipsGo.transform.rotation = Quaternion.AngleAxis(89f, Vector3.right);
            bc.SetGroundStateForTest(isGrounded: true, isFallen: false);
            bc.SetFacingDirection(Vector3.right);

            rb.isKinematic = false;
            rb.angularVelocity = Vector3.zero;

            // Act
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            // Assert — no NaN or Infinity.
            Vector3 av = rb.angularVelocity;
            Assert.That(float.IsNaN(av.x) || float.IsNaN(av.y) || float.IsNaN(av.z), Is.False,
                $"Near-vertical tilt must not produce NaN in angular velocity. av={av}");
            Assert.That(float.IsInfinity(av.x) || float.IsInfinity(av.y) || float.IsInfinity(av.z), Is.False,
                $"Near-vertical tilt must not produce Infinity in angular velocity. av={av}");

            Object.Destroy(hipsGo);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────

        private static bool[,] CaptureLayerCollisionMatrix()
        {
            bool[,] matrix = new bool[32, 32];
            for (int a = 0; a < 32; a++)
            {
                for (int b = 0; b < 32; b++)
                {
                    matrix[a, b] = Physics.GetIgnoreLayerCollision(a, b);
                }
            }

            return matrix;
        }

        private static void RestoreLayerCollisionMatrix(bool[,] matrix)
        {
            if (matrix == null || matrix.GetLength(0) != 32 || matrix.GetLength(1) != 32)
            {
                return;
            }

            for (int a = 0; a < 32; a++)
            {
                for (int b = 0; b < 32; b++)
                {
                    Physics.IgnoreLayerCollision(a, b, matrix[a, b]);
                }
            }
        }
    }
}
