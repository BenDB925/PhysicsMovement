using System.Collections;
using NUnit.Framework;
using PhysicsDrivenMovement.Character;
using UnityEngine;
using UnityEngine.TestTools;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// Regression tests for the Fallen→Moving→Fallen stutter loop described in
    /// Logs/stumble-investigation.md. The collapse detector was false-triggering
    /// during normal forward acceleration and turns, causing repeated 1-frame Fallen
    /// blips that killed the gait and prevented natural recovery.
    ///
    /// These tests assert outcome-level body metrics:
    ///   - Zero or near-zero Moving→Fallen transitions during sustained movement
    ///   - Upright angle stays within recoverable bounds
    ///   - Hips height remains reasonable (no near-collapse)
    ///   - Gait phase continuity (no mid-stride resets)
    /// </summary>
    public class StumbleStutterRegressionTests
    {
        private const int SettleFrames = 150;
        private const int WalkFrames = 500;
        private const int TurnWindupFrames = 200;
        private const int PostTurnFrames = 300;

        private static readonly Vector3 TestOriginOffset = new Vector3(0f, 0f, 12000f);

        private PlayerPrefabTestRig _rig;

        [SetUp]
        public void SetUp()
        {
            _rig = PlayerPrefabTestRig.Create(new PlayerPrefabTestRig.Options
            {
                TestOrigin = TestOriginOffset,
                SpawnOffset = new Vector3(0f, 1.1f, 0f),
                GroundName = "StumbleTestGround",
                GroundScale = new Vector3(600f, 1f, 600f),
            });
        }

        [TearDown]
        public void TearDown()
        {
            _rig?.Dispose();
            _rig = null;
        }

        /// <summary>
        /// From standstill, apply full forward input for 5 seconds.
        /// The character must not enter the Fallen state at all — the acceleration lean
        /// should be handled by the gait without the collapse detector intervening.
        /// Regression target: Fall 1 from the investigation (acceleration faceplant).
        /// </summary>
        [UnityTest]
        public IEnumerator ForwardAcceleration_NoFallenStutter()
        {
            yield return _rig.WarmUp(SettleFrames);

            int fallenTransitions = 0;
            CharacterStateType previousState = _rig.CharacterState.CurrentState;
            float peakUprightAngle = 0f;

            _rig.PlayerMovement.SetMoveInputForTest(Vector2.up);
            for (int i = 0; i < WalkFrames; i++)
            {
                yield return new WaitForFixedUpdate();

                CharacterStateType currentState = _rig.CharacterState.CurrentState;
                if (currentState == CharacterStateType.Fallen && previousState != CharacterStateType.Fallen)
                {
                    fallenTransitions++;
                }
                previousState = currentState;

                float uprightAngle = Vector3.Angle(_rig.Hips.up, Vector3.up);
                if (uprightAngle > peakUprightAngle)
                {
                    peakUprightAngle = uprightAngle;
                }
            }
            _rig.PlayerMovement.SetMoveInputForTest(Vector2.zero);

            Assert.That(fallenTransitions, Is.EqualTo(0),
                $"Forward acceleration caused {fallenTransitions} Moving→Fallen transition(s) " +
                $"during 5s of sustained input. Expected zero — the collapse detector should " +
                $"not fire during normal acceleration lean. Peak upright angle: {peakUprightAngle:F1}°");

            Assert.That(peakUprightAngle, Is.LessThan(55f),
                $"Peak upright angle during forward acceleration was {peakUprightAngle:F1}° " +
                $"(max allowed: 55°). The character is leaning excessively.");
        }

        /// <summary>
        /// Walk forward for 2 seconds, then snap input 90° right.
        /// The character must not enter Fallen during the turn — the velocity gate
        /// should suppress the collapse detector while the character is mid-turn.
        /// Regression target: Falls 3 and 4 from the investigation (turn-related).
        /// </summary>
        [UnityTest]
        public IEnumerator SharpTurn90_NoFallenTransition()
        {
            yield return _rig.WarmUp(SettleFrames);

            // Build forward speed
            _rig.PlayerMovement.SetMoveInputForTest(Vector2.up);
            for (int i = 0; i < TurnWindupFrames; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            // Snap 90° right and track state transitions
            int fallenTransitions = 0;
            CharacterStateType previousState = _rig.CharacterState.CurrentState;
            float peakUprightAngle = 0f;

            _rig.PlayerMovement.SetMoveInputForTest(Vector2.right);
            for (int i = 0; i < PostTurnFrames; i++)
            {
                yield return new WaitForFixedUpdate();

                CharacterStateType currentState = _rig.CharacterState.CurrentState;
                if (currentState == CharacterStateType.Fallen && previousState != CharacterStateType.Fallen)
                {
                    fallenTransitions++;
                }
                previousState = currentState;

                float uprightAngle = Vector3.Angle(_rig.Hips.up, Vector3.up);
                if (uprightAngle > peakUprightAngle)
                {
                    peakUprightAngle = uprightAngle;
                }
            }
            _rig.PlayerMovement.SetMoveInputForTest(Vector2.zero);

            Assert.That(fallenTransitions, Is.EqualTo(0),
                $"90° turn caused {fallenTransitions} Moving→Fallen transition(s). " +
                $"Expected zero — the velocity gate should suppress false collapse triggers " +
                $"during turns. Peak upright angle: {peakUprightAngle:F1}°");

            Assert.That(peakUprightAngle, Is.LessThan(55f),
                $"Peak upright angle during 90° turn was {peakUprightAngle:F1}° " +
                $"(max allowed: 55°). Excessive lean during turn.");
        }

        /// <summary>
        /// Walk forward for 2 seconds, then reverse input 180°.
        /// The character must not enter Fallen — the hardest turn case.
        /// Regression target: the velocity-gate fix must handle full reversals.
        /// </summary>
        [UnityTest]
        public IEnumerator Reversal180_NoCollapseDetection()
        {
            yield return _rig.WarmUp(SettleFrames);

            // Build forward speed
            _rig.PlayerMovement.SetMoveInputForTest(Vector2.up);
            for (int i = 0; i < TurnWindupFrames; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            // Full reverse
            int fallenTransitions = 0;
            CharacterStateType previousState = _rig.CharacterState.CurrentState;
            float peakUprightAngle = 0f;

            _rig.PlayerMovement.SetMoveInputForTest(Vector2.down);
            for (int i = 0; i < PostTurnFrames; i++)
            {
                yield return new WaitForFixedUpdate();

                CharacterStateType currentState = _rig.CharacterState.CurrentState;
                if (currentState == CharacterStateType.Fallen && previousState != CharacterStateType.Fallen)
                {
                    fallenTransitions++;
                }
                previousState = currentState;

                float uprightAngle = Vector3.Angle(_rig.Hips.up, Vector3.up);
                if (uprightAngle > peakUprightAngle)
                {
                    peakUprightAngle = uprightAngle;
                }
            }
            _rig.PlayerMovement.SetMoveInputForTest(Vector2.zero);

            Assert.That(fallenTransitions, Is.EqualTo(0),
                $"180° reversal caused {fallenTransitions} Moving→Fallen transition(s). " +
                $"Expected zero. Peak upright angle: {peakUprightAngle:F1}°");

            Assert.That(peakUprightAngle, Is.LessThan(55f),
                $"Peak upright angle during 180° reversal was {peakUprightAngle:F1}° " +
                $"(max allowed: 55°).");
        }

        /// <summary>
        /// Walk forward for 5 seconds and track the mean hips height.
        /// The character should maintain stable upright posture with hips above 0.75m.
        /// Regression target: hips height stability (investigation Metric 6).
        /// </summary>
        [UnityTest]
        public IEnumerator SteadyForwardWalk_HipsHeightStable()
        {
            yield return _rig.WarmUp(SettleFrames);

            float totalHeight = 0f;
            float minHeight = float.MaxValue;
            int sampleCount = 0;

            _rig.PlayerMovement.SetMoveInputForTest(Vector2.up);
            for (int i = 0; i < WalkFrames; i++)
            {
                yield return new WaitForFixedUpdate();

                // Start sampling after the first second of acceleration
                if (i >= 100)
                {
                    float height = _rig.Hips.position.y;
                    totalHeight += height;
                    sampleCount++;
                    if (height < minHeight)
                    {
                        minHeight = height;
                    }
                }
            }
            _rig.PlayerMovement.SetMoveInputForTest(Vector2.zero);

            float meanHeight = totalHeight / sampleCount;

            Assert.That(meanHeight, Is.GreaterThanOrEqualTo(0.75f),
                $"Mean hips height during steady walk was {meanHeight:F3}m " +
                $"(minimum expected: 0.75m). Character may be in a crouch or stumble loop.");

            Assert.That(minHeight, Is.GreaterThanOrEqualTo(0.55f),
                $"Minimum hips height during steady walk was {minHeight:F3}m " +
                $"(minimum expected: 0.55m). Character nearly collapsed.");
        }

        /// <summary>
        /// Track the number of Moving→Fallen transitions over a 5-second window with
        /// sustained forward input. Any stutter loop would produce ≥3 transitions.
        /// </summary>
        [UnityTest]
        public IEnumerator SteadyForwardWalk_NoStutterLoop()
        {
            yield return _rig.WarmUp(SettleFrames);

            int fallenTransitions = 0;
            CharacterStateType previousState = _rig.CharacterState.CurrentState;

            _rig.PlayerMovement.SetMoveInputForTest(Vector2.up);
            for (int i = 0; i < WalkFrames; i++)
            {
                yield return new WaitForFixedUpdate();

                CharacterStateType currentState = _rig.CharacterState.CurrentState;
                if (currentState == CharacterStateType.Fallen && previousState != CharacterStateType.Fallen)
                {
                    fallenTransitions++;
                }
                previousState = currentState;
            }
            _rig.PlayerMovement.SetMoveInputForTest(Vector2.zero);

            Assert.That(fallenTransitions, Is.LessThanOrEqualTo(1),
                $"Detected {fallenTransitions} Moving→Fallen transitions during 5s of " +
                $"steady forward input. ≥2 transitions indicates the stutter loop is still active.");
        }
    }
}
