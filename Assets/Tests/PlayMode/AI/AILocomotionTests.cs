using System.Collections;
using NUnit.Framework;
using PhysicsDrivenMovement.AI;
using PhysicsDrivenMovement.Character;
using UnityEngine;
using UnityEngine.TestTools;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// PlayMode tests for <see cref="AILocomotion"/> force application,
    /// arrival detection, speed capping, and IMovementInput interface compliance.
    /// </summary>
    public class AILocomotionTests
    {
        private GameObject _root;
        private Rigidbody _rb;
        private BalanceController _balance;
        private AILocomotion _locomotion;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("AILoco_TestRoot");
            _rb = _root.AddComponent<Rigidbody>();
            _rb.useGravity = false; // Simplify force tests.
            _balance = _root.AddComponent<BalanceController>();
            _locomotion = _root.AddComponent<AILocomotion>();

            // Set grounded and not fallen so movement forces are applied.
            _balance.SetGroundStateForTest(isGrounded: true, isFallen: false);
            _balance.enabled = false; // Prevent PD torque from interfering.
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.Destroy(_root);
            }
        }

        [UnityTest]
        public IEnumerator SetTarget_AppliesForceTowardTarget()
        {
            yield return null; // Allow Awake.

            _locomotion.SetTarget(new Vector3(10f, 0f, 0f));

            // Run a few physics frames.
            for (int i = 0; i < 5; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            // Hips should have moved toward positive X.
            Assert.That(_rb.linearVelocity.x, Is.GreaterThan(0f),
                "AILocomotion should apply force toward the target, resulting in positive X velocity.");
        }

        [UnityTest]
        public IEnumerator HasArrived_WhenWithinArrivalDistance_ReturnsTrue()
        {
            yield return null;

            // Place target very close to current position.
            _locomotion.SetTarget(_root.transform.position + new Vector3(0.5f, 0f, 0f));

            yield return new WaitForFixedUpdate();

            Assert.That(_locomotion.HasArrived, Is.True,
                "HasArrived should be true when target is within arrival distance.");
        }

        [UnityTest]
        public IEnumerator HasArrived_WhenFarFromTarget_ReturnsFalse()
        {
            yield return null;

            _locomotion.SetTarget(new Vector3(100f, 0f, 0f));

            yield return new WaitForFixedUpdate();

            Assert.That(_locomotion.HasArrived, Is.False,
                "HasArrived should be false when target is far away.");
        }

        [UnityTest]
        public IEnumerator ClearTarget_StopsMovement()
        {
            yield return null;

            _locomotion.SetTarget(new Vector3(10f, 0f, 0f));
            yield return new WaitForFixedUpdate();

            _locomotion.ClearTarget();
            yield return new WaitForFixedUpdate();

            Assert.That(_locomotion.CurrentMoveInput.magnitude, Is.LessThan(0.01f),
                "CurrentMoveInput should be zero after ClearTarget.");
        }

        [UnityTest]
        public IEnumerator CurrentMoveInput_WhileMoving_IsNonZero()
        {
            yield return null;

            _locomotion.SetTarget(new Vector3(50f, 0f, 0f));
            yield return new WaitForFixedUpdate();

            Assert.That(_locomotion.CurrentMoveInput.magnitude, Is.GreaterThan(0.1f),
                "CurrentMoveInput should be non-zero while actively moving toward a target.");
        }

        [UnityTest]
        public IEnumerator IMovementInput_CompatibleWithCharacterState()
        {
            yield return null;

            // Verify AILocomotion can be retrieved as IMovementInput.
            IMovementInput movementInput = _root.GetComponent<IMovementInput>();
            Assert.That(movementInput, Is.Not.Null,
                "AILocomotion should be retrievable via GetComponent<IMovementInput>().");
            Assert.That(movementInput, Is.InstanceOf<AILocomotion>());
        }

        [UnityTest]
        public IEnumerator SetFacingOnly_DoesNotApplyMovementForce()
        {
            yield return null;

            _rb.linearVelocity = Vector3.zero;
            _locomotion.SetFacingOnly(Vector3.forward);

            for (int i = 0; i < 5; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            float speed = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z).magnitude;
            Assert.That(speed, Is.LessThan(0.1f),
                "SetFacingOnly should not apply movement force.");
            Assert.That(_locomotion.CurrentMoveInput.magnitude, Is.LessThan(0.01f),
                "CurrentMoveInput should be zero during facing-only mode.");
        }
    }
}
