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
    }
}