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

            // Disable all three components so their FixedUpdate loops do not
            // override the state we inject via ForceState / SetGroundStateForTest.
            // Individual tests re-enable only the components they need.
            _balance.enabled = false;
            _movement.enabled = false;
            _characterState.enabled = false;
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

        // ─── GAP-6: Jump guard — GettingUp state ────────────────────────────

        /// <summary>
        /// GAP-6a: A jump input received while the character is in GettingUp state
        /// must be fully discarded and must NOT produce any upward velocity.
        ///
        /// Risk: if the one-frame consume only guards against Fallen but not GettingUp,
        /// a held jump button could fire as soon as the character stands up, which
        /// creates an unexpected bounce immediately after recovery.
        /// </summary>
        [UnityTest]
        public IEnumerator Jump_WhileGettingUp_IsNotApplied()
        {
            // Arrange
            yield return null;

            SetBalanceSignals(isGrounded: true, isFallen: false);
            SetPrivateField(_movement, "_jumpForce", 15f);

            // Put the FSM in GettingUp state.
            ForceState(CharacterStateType.GettingUp);

            _rb.linearVelocity = Vector3.zero;
            _movement.SetJumpInputForTest(true);

            // Act — one FixedUpdate with movement enabled.
            _movement.enabled = true;
            yield return new WaitForFixedUpdate();
            _movement.enabled = false;

            // Assert — no upward velocity should be applied while GettingUp.
            float velocityY = _rb.linearVelocity.y;
            Assert.That(velocityY, Is.LessThan(0.5f),
                $"Jump must be blocked when state is GettingUp. Got velocityY={velocityY:F4}. " +
                "PlayerMovement.ApplyJump must guard against GettingUp state in addition to Fallen.");
        }

        /// <summary>
        /// GAP-6b: A jump button held through a Fallen→GettingUp→Standing transition
        /// must not fire on the frame the character finally stands up.
        ///
        /// Risk: the one-frame consume resets _jumpConsumed when the character lands/stands
        /// but does NOT re-consume the held input — allowing a jump to fire immediately
        /// after recovery without the player intentionally pressing jump.
        ///
        /// Method:
        ///   1. Start in Fallen state with jump held.
        ///   2. Tick several frames (all while still Fallen).
        ///   3. Transition to GettingUp, tick several frames.
        ///   4. Transition to Standing.
        ///   5. Assert: no upward velocity fired on the Standing-entry frame.
        ///
        /// The implementation should require the button to be released and re-pressed
        /// after recovery, or maintain a "button was held at fall-time" latch that
        /// persists through the GettingUp recovery.
        /// </summary>
        [UnityTest]
        public IEnumerator Jump_HeldDuringFallen_DoesNotFireOnGetUp()
        {
            // Arrange
            yield return null;

            SetPrivateField(_movement, "_jumpForce", 15f);

            // Phase 1: Fallen with jump held — tick 5 frames.
            SetBalanceSignals(isGrounded: true, isFallen: true);
            ForceState(CharacterStateType.Fallen);
            _rb.linearVelocity = Vector3.zero;
            _movement.SetJumpInputForTest(true); // hold jump throughout

            _movement.enabled = true;
            for (int i = 0; i < 5; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            // Phase 2: GettingUp with jump still held — tick 5 frames.
            ForceState(CharacterStateType.GettingUp);
            for (int i = 0; i < 5; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            // Phase 3: Transition to Standing — zero velocity, then tick 1 frame.
            SetBalanceSignals(isGrounded: true, isFallen: false);
            ForceState(CharacterStateType.Standing);
            _rb.linearVelocity = Vector3.zero;

            yield return new WaitForFixedUpdate();
            _movement.enabled = false;

            // Assert: held jump must NOT have fired on the Standing-entry frame.
            float velocityY = _rb.linearVelocity.y;
            Assert.That(velocityY, Is.LessThan(0.5f),
                $"Jump must NOT fire when transitioning from GettingUp→Standing with button held. " +
                $"Got velocityY={velocityY:F4}. " +
                "PlayerMovement must track that jump was 'live' during a non-jump state and " +
                "require a fresh press after recovery (button-held latch).");
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
