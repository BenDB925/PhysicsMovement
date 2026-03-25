using System.Collections;
using NUnit.Framework;
using PhysicsDrivenMovement.Character;
using UnityEngine;
using UnityEngine.TestTools;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// PlayMode full-stack sanity tests (Phase 3T — GAP-10).
    ///
    /// Validates that no Rigidbody in the character hierarchy develops NaN or Inf
    /// values in position, linearVelocity, or angularVelocity during a varied
    /// input sequence that exercises all locomotion subsystems simultaneously.
    ///
    /// The NaN-propagation risk:
    ///   - Camera pitch near vertical → ProjectOnPlane(camera.forward) ≈ zero →
    ///     Normalize() on near-zero vector → NaN velocity injected into PhysX.
    ///   - A NaN in angularVelocity propagates silently through all subsequent
    ///     physics steps, permanently locking the character in place.
    ///   - This test catches any such regression before it reaches the player.
    ///
    /// Design: builds a full character rig (all components), applies a mixed
    /// input cycle (forward → strafe → jump → stop → spin → forward) over
    /// 300 fixed frames, and asserts all Rigidbody physics values are finite.
    /// The test origin is placed far from (0,0,0) to avoid interaction with any
    /// geometry that might exist at the default scene origin.
    /// </summary>
    public class FullStackSanityTests
    {
        // ── Constants ────────────────────────────────────────────────────────

        private const int SettleFrames   = 100;
        private const int TestFrameCount = 300;

        /// <summary>Spawn origin — far from (0,0,0) to avoid scene geometry collisions.</summary>
        private static readonly Vector3 TestOrigin = new Vector3(500f, 0f, 500f);

        // ── Input cycle (segment lengths in frames) ───────────────────────────

        // 0–49:  forward
        // 50–99: strafe right
        // 100:   jump
        // 101–149: stop
        // 150–199: spin (cycle directions)
        // 200–299: forward

        // ── Shared Rig ────────────────────────────────────────────────────────

        private PlayerPrefabTestRig _rig;

                private float _savedFixedDeltaTime;
        private int _savedSolverIterations;
        private int _savedSolverVelocityIterations;
[SetUp]
        public void SetUp()
        {
            _savedFixedDeltaTime = Time.fixedDeltaTime;
            _savedSolverIterations = Physics.defaultSolverIterations;
            _savedSolverVelocityIterations = Physics.defaultSolverVelocityIterations;
            Time.fixedDeltaTime = 0.01f;
            Physics.defaultSolverIterations = 12;
            Physics.defaultSolverVelocityIterations = 4;
            _rig = PlayerPrefabTestRig.Create(new PlayerPrefabTestRig.Options
            {
                TestOrigin = TestOrigin,
                GroundName = "FullStack_Ground",
            });
        }

        [TearDown]
        public void TearDown()
        {
            Time.fixedDeltaTime = _savedFixedDeltaTime;
            Physics.defaultSolverIterations = _savedSolverIterations;
            Physics.defaultSolverVelocityIterations = _savedSolverVelocityIterations;
            _rig?.Dispose();
            _rig = null;
        }

        // ── Tests ─────────────────────────────────────────────────────────────

        /// <summary>
        /// GAP-10: After 300 frames of mixed input (forward, strafe, jump, stop, spin),
        /// no Rigidbody in the character hierarchy should contain NaN or Inf in its
        /// position, linearVelocity, or angularVelocity fields.
        /// </summary>
        [UnityTest]
        public IEnumerator FullStack_MixedInputLocomotion_NoNaNOrInfInAnyRigidbody()
        {
            // Arrange — let the rig settle before starting the test sequence.
            yield return _rig.WarmUp(SettleFrames);
            PlayerMovement movement = _rig.PlayerMovement;

            // Act — run the mixed input sequence.
            Vector2[] spinDirs =
            {
                new Vector2(1f, 0f),
                new Vector2(0f, -1f),
                new Vector2(-1f, 0f),
                new Vector2(0f, 1f),
            };

            for (int frame = 0; frame < TestFrameCount; frame++)
            {
                // Set input based on which segment we're in.
                if (frame < 50)
                {
                    movement.SetMoveInputForTest(Vector2.up);           // forward
                }
                else if (frame < 100)
                {
                    movement.SetMoveInputForTest(new Vector2(1f, 0f));  // strafe right
                }
                else if (frame == 100)
                {
                    movement.SetMoveInputForTest(Vector2.zero);
                    movement.SetJumpInputForTest(true);                  // single jump
                }
                else if (frame < 150)
                {
                    movement.SetMoveInputForTest(Vector2.zero);         // stop
                }
                else if (frame < 200)
                {
                    // Spin: cycle directions every 12 frames.
                    movement.SetMoveInputForTest(spinDirs[(frame / 12) % spinDirs.Length]);
                }
                else
                {
                    movement.SetMoveInputForTest(Vector2.up);           // forward again
                }

                yield return new WaitForFixedUpdate();

                // Assert: check all rigidbodies for NaN/Inf every frame.
                AssertNoNaNOrInf(frame);
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void AssertNoNaNOrInf(int frame)
        {
            foreach (Rigidbody rb in _rig.AllBodies)
            {
                if (rb == null) continue;

                string tag = $"[{rb.gameObject.name}] frame {frame}";

                Assert.That(float.IsNaN(rb.position.x) || float.IsInfinity(rb.position.x), Is.False,
                    $"{tag}: position.x is NaN/Inf ({rb.position.x:F6}). " +
                    "Likely cause: near-zero ProjectOnPlane result was Normalized, producing NaN.");

                Assert.That(float.IsNaN(rb.position.y) || float.IsInfinity(rb.position.y), Is.False,
                    $"{tag}: position.y is NaN/Inf ({rb.position.y:F6}).");

                Assert.That(float.IsNaN(rb.position.z) || float.IsInfinity(rb.position.z), Is.False,
                    $"{tag}: position.z is NaN/Inf ({rb.position.z:F6}).");

                Assert.That(float.IsNaN(rb.linearVelocity.x) || float.IsInfinity(rb.linearVelocity.x), Is.False,
                    $"{tag}: linearVelocity.x is NaN/Inf ({rb.linearVelocity.x:F6}).");

                Assert.That(float.IsNaN(rb.linearVelocity.y) || float.IsInfinity(rb.linearVelocity.y), Is.False,
                    $"{tag}: linearVelocity.y is NaN/Inf ({rb.linearVelocity.y:F6}).");

                Assert.That(float.IsNaN(rb.linearVelocity.z) || float.IsInfinity(rb.linearVelocity.z), Is.False,
                    $"{tag}: linearVelocity.z is NaN/Inf ({rb.linearVelocity.z:F6}).");

                Assert.That(float.IsNaN(rb.angularVelocity.x) || float.IsInfinity(rb.angularVelocity.x), Is.False,
                    $"{tag}: angularVelocity.x is NaN/Inf ({rb.angularVelocity.x:F6}).");

                Assert.That(float.IsNaN(rb.angularVelocity.y) || float.IsInfinity(rb.angularVelocity.y), Is.False,
                    $"{tag}: angularVelocity.y is NaN/Inf ({rb.angularVelocity.y:F6}).");

                Assert.That(float.IsNaN(rb.angularVelocity.z) || float.IsInfinity(rb.angularVelocity.z), Is.False,
                    $"{tag}: angularVelocity.z is NaN/Inf ({rb.angularVelocity.z:F6}).");
            }
        }

        private static IEnumerator WaitPhysicsFrames(int count)
        {
            for (int i = 0; i < count; i++)
            {
                yield return new WaitForFixedUpdate();
            }
        }
    }
}
