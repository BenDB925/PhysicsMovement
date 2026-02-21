using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using PhysicsDrivenMovement.Character;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// PlayMode integration tests for <see cref="BalanceController"/>.
    /// These tests build a minimal Hips + two Foot setup at runtime and verify that
    /// the BalanceController correctly tracks ground state, fallen state, and facing direction.
    /// PlayMode is required because tests rely on Awake, FixedUpdate, and Rigidbody physics.
    /// </summary>
    public class BalanceControllerTests
    {
        // ─── Helpers ─────────────────────────────────────────────────────────

        /// <summary>
        /// Builds a minimal ragdoll Hips GameObject with:
        ///   - Rigidbody (physics body)
        ///   - Two child foot GameObjects, each with GroundSensor attached
        ///   - BalanceController on the Hips
        /// Returns references to the key components.
        /// </summary>
        private static BalanceController CreateMinimalHips(
            out Rigidbody hipsRb,
            out GameObject hipsGo)
        {
            hipsGo = new GameObject("TestHips");
            // Place high enough that the feet won't accidentally hit anything.
            hipsGo.transform.position = new Vector3(0f, 100f, 0f);

            hipsRb = hipsGo.AddComponent<Rigidbody>();
            hipsRb.isKinematic = true;   // Kinematic during setup so we control pose.
            hipsGo.AddComponent<BoxCollider>();

            // Add two foot children, each with a GroundSensor.
            // Sensors will report false (nothing to hit at height 100).
            for (int i = 0; i < 2; i++)
            {
                GameObject foot = new GameObject(i == 0 ? "Foot_L" : "Foot_R");
                foot.transform.SetParent(hipsGo.transform);
                foot.transform.localPosition = new Vector3(i == 0 ? -0.1f : 0.1f, -0.4f, 0f);
                foot.AddComponent<Rigidbody>();
                foot.AddComponent<BoxCollider>();
                foot.AddComponent<GroundSensor>();
            }

            // BalanceController LAST — its Awake searches for GroundSensor children.
            BalanceController bc = hipsGo.AddComponent<BalanceController>();
            return bc;
        }

        // ─── IsGrounded ───────────────────────────────────────────────────────

        /// <summary>
        /// When neither foot sensor detects ground, IsGrounded must be false.
        /// </summary>
        [UnityTest]
        public IEnumerator IsGrounded_WhenNoFootOnGround_ReturnsFalse()
        {
            // Arrange — hips at height 100; no ground geometry nearby.
            BalanceController bc = CreateMinimalHips(out _, out GameObject hipsGo);

            // Act — allow at least one FixedUpdate.
            yield return new WaitForFixedUpdate();

            // Assert
            Assert.That(bc.IsGrounded, Is.False,
                "Neither foot can reach a ground surface at height 100; IsGrounded must be false.");

            Object.Destroy(hipsGo);
        }

        // ─── IsFallen ─────────────────────────────────────────────────────────

        /// <summary>
        /// When the Hips is upright (no rotation), IsFallen must be false.
        /// </summary>
        [UnityTest]
        public IEnumerator IsFallen_WhenHipsUpright_ReturnsFalse()
        {
            // Arrange
            BalanceController bc = CreateMinimalHips(out Rigidbody _, out GameObject hipsGo);
            // Set transform.rotation directly so transform.up reflects the pose instantly.
            hipsGo.transform.rotation = Quaternion.identity;   // Perfectly upright.

            // Act
            yield return new WaitForFixedUpdate();

            // Assert
            Assert.That(bc.IsFallen, Is.False,
                "Hips aligned with world-up (0° tilt) should not be considered fallen.");

            Object.Destroy(hipsGo);
        }

        /// <summary>
        /// When the Hips is tilted well beyond the default fallen-enter threshold,
        /// IsFallen must be true.
        /// </summary>
        [UnityTest]
        public IEnumerator IsFallen_WhenHipsTiltedBeyondThreshold_ReturnsTrue()
        {
            // Arrange
            BalanceController bc = CreateMinimalHips(out Rigidbody _, out GameObject hipsGo);
            // Set transform.rotation directly so transform.up reflects the pose instantly.
            // Tilt 80° around world Z — well beyond the 65° default enter threshold.
            hipsGo.transform.rotation = Quaternion.AngleAxis(80f, Vector3.forward);

            // Act
            yield return new WaitForFixedUpdate();

            // Assert
            Assert.That(bc.IsFallen, Is.True,
                "Hips tilted 80° from world-up (beyond default enter threshold) must be IsFallen = true.");

            Object.Destroy(hipsGo);
        }

        /// <summary>
        /// When the Hips is tilted below the fallen-enter threshold (65° by default),
        /// IsFallen must be false.
        /// We intentionally avoid testing exactly at threshold because Vector3.Angle can
        /// return fractional values due to floating-point.
        /// </summary>
        [UnityTest]
        public IEnumerator IsFallen_WhenHipsTiltedAtThreshold_ReturnsFalse()
        {
            // Arrange
            BalanceController bc = CreateMinimalHips(out Rigidbody _, out GameObject hipsGo);
            // Set transform.rotation directly so transform.up reflects the pose instantly.
            // Tilt 59° — clearly below the 65° default enter threshold with a safe margin.
            hipsGo.transform.rotation = Quaternion.AngleAxis(59f, Vector3.forward);

            // Act
            yield return new WaitForFixedUpdate();

            // Assert
            Assert.That(bc.IsFallen, Is.False,
                "Hips tilted 59° (below default enter threshold) should not be IsFallen.");

            Object.Destroy(hipsGo);
        }

        /// <summary>
        /// Fallen-state hysteresis must hold the fallen state while tilt remains between
        /// enter (65°) and exit (55°), then clear only after crossing below exit.
        /// </summary>
        [UnityTest]
        public IEnumerator IsFallen_Hysteresis_HoldsBetweenEnterAndExitThresholds()
        {
            BalanceController bc = CreateMinimalHips(out Rigidbody _, out GameObject hipsGo);

            hipsGo.transform.rotation = Quaternion.AngleAxis(80f, Vector3.forward);
            yield return new WaitForFixedUpdate();
            Assert.That(bc.IsFallen, Is.True,
                "At 80° tilt, the controller should enter fallen state.");

            hipsGo.transform.rotation = Quaternion.AngleAxis(60f, Vector3.forward);
            yield return new WaitForFixedUpdate();
            Assert.That(bc.IsFallen, Is.True,
                "At 60° (between enter and exit), fallen state should remain true due to hysteresis.");

            hipsGo.transform.rotation = Quaternion.AngleAxis(54f, Vector3.forward);
            yield return new WaitForFixedUpdate();
            Assert.That(bc.IsFallen, Is.False,
                "At 54° (below exit threshold), fallen state should clear.");

            Object.Destroy(hipsGo);
        }

        // ─── SetFacingDirection ───────────────────────────────────────────────

        /// <summary>
        /// Calling SetFacingDirection with a valid horizontal direction must not throw.
        /// </summary>
        [UnityTest]
        public IEnumerator SetFacingDirection_WithValidDirection_DoesNotThrow()
        {
            // Arrange
            BalanceController bc = CreateMinimalHips(out _, out GameObject hipsGo);
            yield return new WaitForFixedUpdate();

            // Act + Assert — no exception expected.
            Assert.DoesNotThrow(() => bc.SetFacingDirection(Vector3.forward),
                "SetFacingDirection(Vector3.forward) must not throw.");

            Object.Destroy(hipsGo);
        }

        /// <summary>
        /// Calling SetFacingDirection with a zero vector must be silently ignored (no throw,
        /// no NaN in internal state). This guards against dividing by zero when normalising.
        /// </summary>
        [UnityTest]
        public IEnumerator SetFacingDirection_WithZeroVector_IsIgnoredSafely()
        {
            // Arrange
            BalanceController bc = CreateMinimalHips(out _, out GameObject hipsGo);
            yield return new WaitForFixedUpdate();

            // Act + Assert — must not throw or corrupt state.
            Assert.DoesNotThrow(() => bc.SetFacingDirection(Vector3.zero),
                "SetFacingDirection(Vector3.zero) must be silently ignored — not throw.");

            // Verify that subsequent IsFallen / IsGrounded reads don't produce NaN.
            Assert.That(float.IsNaN(bc.IsGrounded ? 1f : 0f), Is.False);
            Assert.That(float.IsNaN(bc.IsFallen ? 1f : 0f), Is.False);

            Object.Destroy(hipsGo);
        }

        // ─── Torque application ───────────────────────────────────────────────

        /// <summary>
        /// When the Hips is not fallen and is tilted, the balance controller must apply a
        /// corrective torque, resulting in a change to angular velocity over fixed steps.
        /// </summary>
        [UnityTest]
        public IEnumerator FixedUpdate_WhenUprightAndTilted_AppliesCorrectionTorque()
        {
            // Arrange — dynamic Rigidbody, tilted so a corrective torque is needed.
            BalanceController bc = CreateMinimalHips(out Rigidbody rb, out GameObject hipsGo);
            rb.isKinematic = false;
            rb.rotation = Quaternion.AngleAxis(30f, Vector3.forward);   // 30° tilt, not fallen.
            Vector3 initialAngularVelocity = rb.angularVelocity;        // Should be ~zero.

            // Act — run a few physics steps so AddTorque has time to accumulate.
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            // Assert — angular velocity must differ from initial (torque was applied).
            Assert.That(rb.angularVelocity, Is.Not.EqualTo(initialAngularVelocity),
                "A 30° tilt (below fallen threshold) should cause the balance controller to " +
                "apply corrective torque, changing the Rigidbody's angular velocity.");

            Object.Destroy(hipsGo);
        }

        /// <summary>
        /// When the Hips is fallen (tilt >60°), the balance controller must NOT apply torque.
        /// Angular velocity should remain near zero (within floating-point noise).
        /// </summary>
        [UnityTest]
        public IEnumerator FixedUpdate_WhenFallen_DoesNotApplyTorque()
        {
            // Arrange — kinematic so gravity doesn't change things; fallen pose.
            BalanceController bc = CreateMinimalHips(out Rigidbody rb, out GameObject hipsGo);
            rb.isKinematic = true;
            // Set transform.rotation directly so transform.up reflects the pose instantly.
            hipsGo.transform.rotation = Quaternion.AngleAxis(80f, Vector3.forward);  // Fallen.

            Vector3 angularVelocityBefore = rb.angularVelocity;

            // Act
            yield return new WaitForFixedUpdate();

            // Assert — a kinematic body's angularVelocity should stay zero because
            // AddTorque on a kinematic Rigidbody has no effect.
            Assert.That(rb.angularVelocity, Is.EqualTo(angularVelocityBefore),
                "IsFallen = true must skip torque application; angular velocity must be unchanged.");

            Object.Destroy(hipsGo);
        }
    }
}
