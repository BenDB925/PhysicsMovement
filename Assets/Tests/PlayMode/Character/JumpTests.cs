using System.Collections;
using System.Reflection;
using NUnit.Framework;
using PhysicsDrivenMovement.Character;
using UnityEngine;
using UnityEngine.TestTools;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// PlayMode tests for Phase 3F1 — jump impulse, grounded gate, and one-frame consume.
    /// All tests are outcome-based: they assert that the Hips Rigidbody gains upward
    /// velocity after a valid jump input, rather than just checking that a method was called.
    /// </summary>
    public class JumpTests
    {
        private GameObject _root;
        private Rigidbody _rb;
        private BalanceController _balance;
        private PlayerMovement _movement;
        private CharacterState _characterState;

        [SetUp]
        public void SetUp()
        {
            // Build a minimal test rig: one GameObject with all required components.
            _root = new GameObject("Jump_TestRoot");
            _rb = _root.AddComponent<Rigidbody>();
            _balance = _root.AddComponent<BalanceController>();
            _movement = _root.AddComponent<PlayerMovement>();
            _characterState = _root.AddComponent<CharacterState>();

            // Inject grounded/not-fallen state BEFORE the first physics tick so
            // FixedUpdate does not overwrite these values during Awake.
            _balance.SetGroundStateForTest(isGrounded: true, isFallen: false);
            _movement.SetMoveInputForTest(Vector2.zero);

            // Disable BalanceController and PlayerMovement so they do not apply
            // forces or update their own state during the test.  CharacterState is
            // kept enabled so it can drive the FSM transitions we want to test.
            _balance.enabled = false;
            _movement.enabled = false;
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
            }
        }

        // ─── Test 1: Jump fires from Standing ───────────────────────────────

        [UnityTest]
        public IEnumerator Jump_WhenStandingAndGrounded_AppliesUpwardImpulse()
        {
            // Arrange
            yield return null; // Let Awake complete.

            SetBalanceSignals(isGrounded: true, isFallen: false);
            SetPrivateField(_movement, "_jumpForce", 15f);

            // Ensure state is Standing.
            ForceState(CharacterStateType.Standing);

            _rb.linearVelocity = Vector3.zero;
            _movement.SetJumpInputForTest(true);

            // Act — trigger one FixedUpdate with movement enabled.
            _movement.enabled = true;
            yield return new WaitForFixedUpdate();
            _movement.enabled = false;

            // Assert — velocity must have gained upward component from impulse.
            float velocityY = _rb.linearVelocity.y;
            Assert.That(velocityY, Is.GreaterThan(0.1f),
                $"Expected upward velocity after Standing jump, but got velocityY={velocityY:F4}. " +
                "Jump impulse must have been applied to the Hips Rigidbody.");
        }

        // ─── Test 2: Jump fires from Moving ─────────────────────────────────

        [UnityTest]
        public IEnumerator Jump_WhenMovingAndGrounded_AppliesUpwardImpulse()
        {
            // Arrange
            yield return null;

            SetBalanceSignals(isGrounded: true, isFallen: false);
            SetPrivateField(_movement, "_jumpForce", 15f);

            // Set state to Moving by forcing the FSM.
            ForceState(CharacterStateType.Moving);

            _rb.linearVelocity = Vector3.zero;
            _movement.SetJumpInputForTest(true);

            // Act
            _movement.enabled = true;
            yield return new WaitForFixedUpdate();
            _movement.enabled = false;

            // Assert
            float velocityY = _rb.linearVelocity.y;
            Assert.That(velocityY, Is.GreaterThan(0.1f),
                $"Expected upward velocity after Moving jump, but got velocityY={velocityY:F4}. " +
                "Jump impulse must fire from Moving state when grounded.");
        }

        // ─── Test 3: Jump does NOT fire when Fallen ──────────────────────────

        [UnityTest]
        public IEnumerator Jump_WhenFallen_DoesNotApplyImpulse()
        {
            // Arrange
            yield return null;

            SetBalanceSignals(isGrounded: true, isFallen: true);
            SetPrivateField(_movement, "_jumpForce", 15f);
            ForceState(CharacterStateType.Fallen);

            _rb.linearVelocity = Vector3.zero;
            _movement.SetJumpInputForTest(true);

            // Act
            _movement.enabled = true;
            yield return new WaitForFixedUpdate();
            _movement.enabled = false;

            // Assert — velocity should remain at or near zero (gravity aside).
            float velocityY = _rb.linearVelocity.y;
            Assert.That(velocityY, Is.LessThan(0.5f),
                $"Jump must be blocked when state is Fallen. Got velocityY={velocityY:F4}.");
        }

        // ─── Test 4: Jump does NOT fire when Airborne ────────────────────────

        [UnityTest]
        public IEnumerator Jump_WhenAirborne_DoesNotApplyImpulse()
        {
            // Arrange
            yield return null;

            // Airborne = not grounded, not fallen.
            SetBalanceSignals(isGrounded: false, isFallen: false);
            SetPrivateField(_movement, "_jumpForce", 15f);
            ForceState(CharacterStateType.Airborne);

            _rb.linearVelocity = Vector3.zero;
            _movement.SetJumpInputForTest(true);

            // Act
            _movement.enabled = true;
            yield return new WaitForFixedUpdate();
            _movement.enabled = false;

            // Assert — velocity should remain at or near zero (gravity aside).
            float velocityY = _rb.linearVelocity.y;
            Assert.That(velocityY, Is.LessThan(0.5f),
                $"Jump must be blocked when Airborne (not grounded). Got velocityY={velocityY:F4}.");
        }

        // ─── Test 5: One-frame consume (no repeat while held) ───────────────

        [UnityTest]
        public IEnumerator Jump_WhenHeldForSecondFrame_DoesNotFireAgain()
        {
            // Arrange
            yield return null;

            SetBalanceSignals(isGrounded: true, isFallen: false);
            SetPrivateField(_movement, "_jumpForce", 15f);
            ForceState(CharacterStateType.Standing);

            _rb.linearVelocity = Vector3.zero;

            // Frame 1: inject jump-pressed and tick physics.
            _movement.SetJumpInputForTest(true);
            _movement.enabled = true;
            yield return new WaitForFixedUpdate();
            float velocityAfterFrame1 = _rb.linearVelocity.y;
            _movement.enabled = false;

            // Verify the first frame produced an upward impulse.
            Assert.That(velocityAfterFrame1, Is.GreaterThan(0.1f),
                $"Precondition: expected upward velocity after frame 1 (got {velocityAfterFrame1:F4}).");

            // Zero velocity to isolate the second frame's contribution.
            _rb.linearVelocity = Vector3.zero;

            // Frame 2: jump is still injected as held (no call to SetJumpInputForTest(false)),
            // but the one-frame consume must prevent a second impulse.
            _movement.enabled = true;
            yield return new WaitForFixedUpdate();
            _movement.enabled = false;

            float velocityAfterFrame2 = _rb.linearVelocity.y;

            // Assert — no additional upward impulse on the second frame.
            Assert.That(velocityAfterFrame2, Is.LessThan(0.5f),
                $"Jump must not fire again on the second frame while held. " +
                $"velocityAfterFrame2={velocityAfterFrame2:F4}. One-frame consume is broken.");
        }

        // ─── Helpers ─────────────────────────────────────────────────────────

        private void SetBalanceSignals(bool isGrounded, bool isFallen)
        {
            _balance.SetGroundStateForTest(isGrounded, isFallen);
        }

        /// <summary>
        /// Forces the CharacterState FSM into a specific state via the auto-property
        /// backing field, bypassing normal transition logic.  Mirrors the pattern used in
        /// CharacterStateTests.cs.
        /// </summary>
        private void ForceState(CharacterStateType state)
        {
            string backingFieldName = $"<{nameof(CharacterState.CurrentState)}>k__BackingField";
            FieldInfo field = typeof(CharacterState).GetField(
                backingFieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null,
                $"Could not find backing field for CharacterState.CurrentState. " +
                "The test seam relies on the auto-property backing field naming convention.");

            field.SetValue(_characterState, state);
        }

        private static void SetPrivateField(object instance, string fieldName, object value)
        {
            FieldInfo field = instance.GetType().GetField(
                fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null,
                $"Could not find private field '{fieldName}' on {instance.GetType().Name}.");

            field.SetValue(instance, value);
        }
    }
}
