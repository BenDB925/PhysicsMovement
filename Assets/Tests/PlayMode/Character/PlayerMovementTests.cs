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
    /// PlayMode tests for <see cref="PlayerMovement"/> covering locomotion force gating,
    /// camera-relative direction, and horizontal speed limiting.
    /// </summary>
    public class PlayerMovementTests
    {
        private const float TestEpsilon = 0.001f;

        private GameObject _root;
        private Rigidbody _rb;
        private BalanceController _balance;
        private PlayerMovement _movement;
        private Camera _camera;
        private MethodInfo _applyMovementMethod;

        [SetUp]
        public void SetUp()
        {
            // Arrange
            _root = new GameObject("TestHips");
            _rb = _root.AddComponent<Rigidbody>();
            _rb.useGravity = false;
            _rb.linearDamping = 0f;
            _rb.angularDamping = 0f;

            _balance = _root.AddComponent<BalanceController>();
            _movement = _root.AddComponent<PlayerMovement>();

            GameObject cameraGo = new GameObject("TestCamera");
            _camera = cameraGo.AddComponent<Camera>();

            SetPrivateField(_movement, "_camera", _camera);

            _applyMovementMethod = typeof(PlayerMovement).GetMethod(
                "ApplyMovementForces",
                BindingFlags.Instance | BindingFlags.NonPublic);

            if (_applyMovementMethod == null)
            {
                throw new InvalidOperationException("PlayerMovement.ApplyMovementForces must exist for tests.");
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (_camera != null)
            {
                UnityEngine.Object.Destroy(_camera.gameObject);
            }

            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
            }
        }

        [UnityTest]
        public IEnumerator ApplyMovementForces_WhenFallen_DoesNotApplyHorizontalForce()
        {
            // Arrange
            yield return null;
            SetAutoPropertyBackingField(_balance, "IsFallen", true);
            Vector3 velocityBefore = _rb.linearVelocity;

            // Act
            _applyMovementMethod.Invoke(_movement, new object[] { new Vector2(0f, 1f) });
            yield return new WaitForFixedUpdate();

            // Assert
            Vector3 horizontalBefore = new Vector3(velocityBefore.x, 0f, velocityBefore.z);
            Vector3 horizontalAfter = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z);
            Assert.That((horizontalAfter - horizontalBefore).sqrMagnitude, Is.LessThanOrEqualTo(TestEpsilon),
                $"Fallen locomotion must be blocked. Horizontal delta sqrMagnitude={((horizontalAfter - horizontalBefore).sqrMagnitude):F6}.");
        }

        [UnityTest]
        public IEnumerator ApplyMovementForces_WithForwardInput_UsesCameraRelativeDirection()
        {
            // Arrange
            yield return null;
            SetAutoPropertyBackingField(_balance, "IsFallen", false);
            _camera.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

            // Act
            _applyMovementMethod.Invoke(_movement, new object[] { new Vector2(0f, 1f) });
            yield return new WaitForFixedUpdate();

            // Assert
            Vector3 horizontalVelocity = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z);
            Assert.That(horizontalVelocity.magnitude, Is.GreaterThan(0.01f),
                "Movement input should produce measurable horizontal velocity.");

            float xContribution = Mathf.Abs(horizontalVelocity.normalized.x);
            float zContribution = Mathf.Abs(horizontalVelocity.normalized.z);
            Assert.That(xContribution, Is.GreaterThan(zContribution),
                $"With camera yaw=90°, forward input should resolve mostly to world +X. x={xContribution:F3}, z={zContribution:F3}.");
        }

        [UnityTest]
        public IEnumerator ApplyMovementForces_WhenAtOrAboveSpeedCap_DoesNotIncreaseHorizontalSpeed()
        {
            // Arrange
            yield return null;
            SetAutoPropertyBackingField(_balance, "IsFallen", false);
            SetPrivateField(_movement, "_maxSpeed", 5f);
            _rb.linearVelocity = new Vector3(8f, 0f, 0f);
            float speedBefore = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z).magnitude;

            // Act
            _applyMovementMethod.Invoke(_movement, new object[] { new Vector2(1f, 0f) });
            yield return new WaitForFixedUpdate();

            // Assert
            float speedAfter = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z).magnitude;
            Assert.That(speedAfter, Is.LessThanOrEqualTo(speedBefore + 0.05f),
                $"Speed cap should block additional acceleration above max speed. before={speedBefore:F3}, after={speedAfter:F3}.");
        }

        // ─── GAP-1: Camera-relative movement at steep pitch (−60°) ─────────

        /// <summary>
        /// GAP-1a: With the camera pitched -60° (looking sharply downward at the character),
        /// forward input must still produce measurable horizontal displacement.
        ///
        /// Bug guarded against: PlayerMovement previously used ProjectOnPlane(camera.forward, up)
        /// and then Normalize(). At steep downward pitch the projected horizontal component of
        /// camera.forward approaches zero → Normalize() of a near-zero vector → NaN velocity.
        ///
        /// The current implementation extracts yaw only (eulerAngles.y) so pitch has no
        /// effect on movement direction. This test confirms that protection is in place.
        /// </summary>
        [UnityTest]
        public IEnumerator CameraRelativeMovement_AtSteepPitchMinus60_CharacterStillMovesForward()
        {
            // Arrange
            yield return null;

            SetAutoPropertyBackingField(_balance, "IsFallen", false);
            SetPrivateField(_movement, "_maxSpeed", 20f); // generous cap so displacement can build

            // Camera pitched 60° downward (common isometric-style follow angle), yaw = 0.
            _camera.transform.rotation = Quaternion.Euler(60f, 0f, 0f);

            Vector3 startPos = _root.transform.position;

            // Act — apply forward input for 200 fixed frames.
            _movement.SetMoveInputForTest(Vector2.up);

            for (int i = 0; i < 200; i++)
            {
                _applyMovementMethod.Invoke(_movement, new object[] { _movement.CurrentMoveInput });
                yield return new WaitForFixedUpdate();
            }

            // Assert 1: horizontal displacement ≥ 1.5 m.
            Vector3 displacement = _root.transform.position - startPos;
            float horizontalDisplacement = new Vector3(displacement.x, 0f, displacement.z).magnitude;
            Assert.That(horizontalDisplacement, Is.GreaterThanOrEqualTo(1.5f),
                $"Camera at pitch=-60° must not freeze movement. Horizontal displacement = {horizontalDisplacement:F3} m " +
                "(expected ≥ 1.5 m). Likely cause: yaw extraction from camera eulerAngles failed.");

            // Assert 2: no NaN in position or velocity.
            Assert.That(float.IsNaN(_rb.position.x), Is.False,
                $"Hips position.x is NaN after steep-pitch camera movement. " +
                "Likely cause: Normalize() on near-zero ProjectOnPlane result.");
            Assert.That(float.IsNaN(_rb.linearVelocity.x), Is.False,
                "Hips linearVelocity.x is NaN after steep-pitch camera movement.");
        }

        // ─── GAP-1: Camera-relative movement at all cardinal yaw angles ──────

        private static readonly int[] CardinalYaws = { 0, 90, 180, 270 };

        /// <summary>
        /// GAP-1b: For each cardinal camera yaw (0°, 90°, 180°, 270°), pressing
        /// forward (Vector2.up) must produce displacement aligned with the camera's
        /// yaw direction, not world +Z. This confirms the full camera-relative
        /// direction logic for all orientations.
        ///
        /// Parameterised: runs once per yaw value via TestCaseSource.
        /// </summary>
        [UnityTest]
        public IEnumerator CameraRelativeMovement_AtAllCardinalYawAngles_CharacterMovesInCorrectDirection(
            [ValueSource(nameof(CardinalYaws))] int yawDeg)
        {
            // Arrange
            yield return null;

            SetAutoPropertyBackingField(_balance, "IsFallen", false);
            SetPrivateField(_movement, "_maxSpeed", 20f);

            // Set camera yaw, zero pitch.
            _camera.transform.rotation = Quaternion.Euler(0f, yawDeg, 0f);

            // Camera forward (yaw only) in world XZ.
            Vector3 expectedForward = Quaternion.Euler(0f, yawDeg, 0f) * Vector3.forward;
            expectedForward.y = 0f;
            expectedForward.Normalize();

            Vector3 startPos = _root.transform.position;

            // Act — 200 frames of forward input.
            _movement.SetMoveInputForTest(Vector2.up);

            for (int i = 0; i < 200; i++)
            {
                _applyMovementMethod.Invoke(_movement, new object[] { _movement.CurrentMoveInput });
                yield return new WaitForFixedUpdate();
            }

            // Assert 1: measurable displacement.
            Vector3 displacement = _root.transform.position - startPos;
            Vector3 horizontalDisp = new Vector3(displacement.x, 0f, displacement.z);
            Assert.That(horizontalDisp.magnitude, Is.GreaterThanOrEqualTo(1.0f),
                $"yaw={yawDeg}°: forward input must produce ≥ 1.0 m displacement. " +
                $"Got {horizontalDisp.magnitude:F3} m.");

            // Assert 2: displacement direction aligns with expected camera-relative forward
            // (dot product ≥ 0.7 ≈ within 45° of expected direction).
            float dot = Vector3.Dot(horizontalDisp.normalized, expectedForward);
            Assert.That(dot, Is.GreaterThanOrEqualTo(0.7f),
                $"yaw={yawDeg}°: displacement direction {horizontalDisp.normalized:F2} should align with " +
                $"camera forward {expectedForward:F2} (dot ≥ 0.7, got {dot:F3}).");

            // Assert 3: no NaN.
            Assert.That(float.IsNaN(_rb.position.x), Is.False,
                $"yaw={yawDeg}°: position.x must not be NaN after camera-relative movement.");
        }

        private static void SetPrivateField(object instance, string fieldName, object value)
        {
            FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new InvalidOperationException($"Missing private field '{fieldName}' on {instance.GetType().Name}.");
            }

            field.SetValue(instance, value);
        }

        private static void SetAutoPropertyBackingField(object instance, string propertyName, object value)
        {
            string backingFieldName = $"<{propertyName}>k__BackingField";
            FieldInfo field = instance.GetType().GetField(backingFieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new InvalidOperationException(
                    $"Could not locate auto-property backing field '{backingFieldName}' on {instance.GetType().Name}.");
            }

            field.SetValue(instance, value);
        }

        // ─── GAP-4: Speed cap — horizontal magnitude check ───────────────────

        /// <summary>
        /// GAP-4: Under 600 frames of sustained forward input, the character's horizontal
        /// speed (XZ magnitude only) must not exceed _maxSpeed × 1.2 (20% physics overshoot
        /// tolerance). Specifically guards against: speed cap check using 3D velocity
        /// magnitude (including vertical) instead of horizontal-only magnitude, which would
        /// allow horizontal speed to exceed the intended cap.
        /// </summary>
        [UnityTest]
        public IEnumerator UnderSustainedInput_HorizontalSpeedDoesNotExceedMaxSpeed()
        {
            // Arrange
            yield return null;

            SetAutoPropertyBackingField(_balance, "IsFallen", false);
            const float maxSpeed = 5f;
            SetPrivateField(_movement, "_maxSpeed", maxSpeed);

            _movement.SetMoveInputForTest(Vector2.up);

            float maxObservedHorizontalSpeed = 0f;

            // Act — 600 frames of sustained forward input; sample speed every 5 frames.
            for (int frame = 0; frame < 600; frame++)
            {
                _applyMovementMethod.Invoke(_movement, new object[] { _movement.CurrentMoveInput });
                yield return new WaitForFixedUpdate();

                if (frame % 5 == 0)
                {
                    float hSpeed = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z).magnitude;
                    if (hSpeed > maxObservedHorizontalSpeed)
                    {
                        maxObservedHorizontalSpeed = hSpeed;
                    }
                }
            }

            // Assert 1: max observed horizontal speed within 20% tolerance.
            Assert.That(maxObservedHorizontalSpeed, Is.LessThanOrEqualTo(maxSpeed * 1.2f),
                $"Horizontal speed must not exceed _maxSpeed × 1.2 = {maxSpeed * 1.2f:F2} m/s. " +
                $"Observed max: {maxObservedHorizontalSpeed:F3} m/s. " +
                "Likely cause: speed gate using 3D magnitude (incl. vertical) instead of horizontal.");

            // Assert 2: no sample exceeds 2× max speed (catastrophic failure check).
            Assert.That(maxObservedHorizontalSpeed, Is.LessThanOrEqualTo(maxSpeed * 2f),
                $"Horizontal speed exceeded 2× _maxSpeed = {maxSpeed * 2f:F2} m/s. " +
                $"Got {maxObservedHorizontalSpeed:F3} m/s. Speed cap is completely broken.");
        }
    }
}