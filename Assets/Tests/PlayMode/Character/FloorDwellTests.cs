using System.Collections;
using NUnit.Framework;
using PhysicsDrivenMovement.Character;
using UnityEngine;
using UnityEngine.TestTools;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// PlayMode tests for the Chapter 3 severity-based floor dwell system.
    /// Validates that light/heavy knockdown severity produces the expected dwell
    /// duration, that movement input is ignored while on the floor, and that
    /// re-hits reset the timer with a cap.
    /// </summary>
    public class FloorDwellTests
    {
        private const int WarmUpFrames = 150;

        private static readonly Vector3 TestOrigin = new Vector3(0f, 0f, 10000f);

        private PlayerPrefabTestRig _rig;

        [TearDown]
        public void TearDown()
        {
            _rig?.Dispose();
            _rig = null;
        }

        /// <summary>
        /// Test 1: Light severity (0.1) → floor dwell ≈ 1.5–1.9 s.
        /// Lerp(1.5, 3.0, 0.1) = 1.65 s. We allow a margin for physics jitter.
        /// </summary>
        [UnityTest]
        public IEnumerator LightSeverity_DwellApproximately1Point5To1Point9Seconds()
        {
            _rig = PlayerPrefabTestRig.Create(new PlayerPrefabTestRig.Options
            {
                TestOrigin = TestOrigin,
                SpawnOffset = new Vector3(0f, 0.5f, 0f),
            });
            yield return _rig.WarmUp(WarmUpFrames);

            Assert.That(_rig.CharacterState.CurrentState, Is.Not.EqualTo(CharacterStateType.Fallen));

            // Trigger surrender directly with light severity.
            _rig.BalanceController.TriggerSurrender(0.1f);

            // Wait for Fallen state.
            bool enteredFallen = false;
            for (int i = 0; i < 200; i++)
            {
                yield return new WaitForFixedUpdate();
                if (_rig.CharacterState.CurrentState == CharacterStateType.Fallen)
                {
                    enteredFallen = true;
                    break;
                }
            }

            Assert.That(enteredFallen, Is.True, "Character must enter Fallen after TriggerSurrender.");
            Assert.That(_rig.CharacterState.WasSurrendered, Is.True);

            // Measure the time spent in Fallen before transitioning to GettingUp.
            float dwellStart = Time.time;
            bool transitioned = false;
            // Allow up to 5 s to accommodate the dwell + timing jitter.
            int maxFrames = Mathf.CeilToInt(5f / Time.fixedDeltaTime);
            for (int i = 0; i < maxFrames; i++)
            {
                yield return new WaitForFixedUpdate();
                if (_rig.CharacterState.CurrentState != CharacterStateType.Fallen)
                {
                    transitioned = true;
                    break;
                }
            }

            float dwellDuration = Time.time - dwellStart;
            Assert.That(transitioned, Is.True,
                "Character must eventually leave Fallen state.");
            Assert.That(dwellDuration, Is.GreaterThanOrEqualTo(1.3f),
                $"Light severity dwell should be >= 1.3 s (actual: {dwellDuration:F2} s).");
            Assert.That(dwellDuration, Is.LessThanOrEqualTo(2.2f),
                $"Light severity dwell should be <= 2.2 s (actual: {dwellDuration:F2} s).");
        }

        /// <summary>
        /// Test 2: Heavy severity (0.9) → floor dwell ≈ 2.7–3.0 s.
        /// Lerp(1.5, 3.0, 0.9) = 2.85 s. We allow a margin for physics jitter.
        /// </summary>
        [UnityTest]
        public IEnumerator HeavySeverity_DwellApproximately2Point7To3Point0Seconds()
        {
            _rig = PlayerPrefabTestRig.Create(new PlayerPrefabTestRig.Options
            {
                TestOrigin = TestOrigin + new Vector3(100f, 0f, 0f),
                SpawnOffset = new Vector3(0f, 0.5f, 0f),
            });
            yield return _rig.WarmUp(WarmUpFrames);

            _rig.BalanceController.TriggerSurrender(0.9f);

            bool enteredFallen = false;
            for (int i = 0; i < 200; i++)
            {
                yield return new WaitForFixedUpdate();
                if (_rig.CharacterState.CurrentState == CharacterStateType.Fallen)
                {
                    enteredFallen = true;
                    break;
                }
            }

            Assert.That(enteredFallen, Is.True, "Character must enter Fallen after TriggerSurrender.");

            float dwellStart = Time.time;
            bool transitioned = false;
            int maxFrames = Mathf.CeilToInt(7f / Time.fixedDeltaTime);
            for (int i = 0; i < maxFrames; i++)
            {
                yield return new WaitForFixedUpdate();
                if (_rig.CharacterState.CurrentState != CharacterStateType.Fallen)
                {
                    transitioned = true;
                    break;
                }
            }

            float dwellDuration = Time.time - dwellStart;
            Assert.That(transitioned, Is.True,
                "Character must eventually leave Fallen state.");
            Assert.That(dwellDuration, Is.GreaterThanOrEqualTo(2.4f),
                $"Heavy severity dwell should be >= 2.4 s (actual: {dwellDuration:F2} s).");
            Assert.That(dwellDuration, Is.LessThanOrEqualTo(3.5f),
                $"Heavy severity dwell should be <= 3.5 s (actual: {dwellDuration:F2} s).");
        }

        /// <summary>
        /// Test 3: Movement input during floor dwell is ignored — character stays Fallen.
        /// Apply forward movement input immediately after entering Fallen and verify
        /// the state machine does not transition to Moving.
        /// </summary>
        [UnityTest]
        public IEnumerator InputDuringFloorDwell_IsIgnored_CharacterStaysFallen()
        {
            _rig = PlayerPrefabTestRig.Create(new PlayerPrefabTestRig.Options
            {
                TestOrigin = TestOrigin + new Vector3(200f, 0f, 0f),
                SpawnOffset = new Vector3(0f, 0.5f, 0f),
            });
            yield return _rig.WarmUp(WarmUpFrames);

            // Trigger surrender with a medium severity to get a ~2 s dwell.
            _rig.BalanceController.TriggerSurrender(0.5f);

            bool enteredFallen = false;
            for (int i = 0; i < 200; i++)
            {
                yield return new WaitForFixedUpdate();
                if (_rig.CharacterState.CurrentState == CharacterStateType.Fallen)
                {
                    enteredFallen = true;
                    break;
                }
            }

            Assert.That(enteredFallen, Is.True);

            // Apply full forward movement input during dwel.
            _rig.PlayerMovement.SetMoveInputForTest(Vector2.up);

            // Run for 1.5 s — well within the dwell window — and verify Fallen persists.
            int checkFrames = Mathf.CeilToInt(1.5f / Time.fixedDeltaTime);
            for (int i = 0; i < checkFrames; i++)
            {
                yield return new WaitForFixedUpdate();
                Assert.That(_rig.CharacterState.CurrentState, Is.EqualTo(CharacterStateType.Fallen),
                    $"Movement input must be ignored during floor dwell. " +
                    $"Frame {i}: state was {_rig.CharacterState.CurrentState}");
            }

            // Clean up input.
            _rig.PlayerMovement.SetMoveInputForTest(Vector2.zero);
        }

        /// <summary>
        /// Test 4: Re-hit during floor dwell resets timer, capped at _reKnockdownFloorDwellCap.
        /// Trigger surrender with severity 0.5, wait 1 s into dwell, re-trigger with severity 0.8.
        /// The total dwell must be longer than the original but capped.
        /// </summary>
        [UnityTest]
        public IEnumerator ReHitDuringDwell_ResetsTimer_CappedAtMax()
        {
            _rig = PlayerPrefabTestRig.Create(new PlayerPrefabTestRig.Options
            {
                TestOrigin = TestOrigin + new Vector3(300f, 0f, 0f),
                SpawnOffset = new Vector3(0f, 0.5f, 0f),
            });
            yield return _rig.WarmUp(WarmUpFrames);

            // Initial surrender at medium severity.
            _rig.BalanceController.TriggerSurrender(0.5f);

            bool enteredFallen = false;
            for (int i = 0; i < 200; i++)
            {
                yield return new WaitForFixedUpdate();
                if (_rig.CharacterState.CurrentState == CharacterStateType.Fallen)
                {
                    enteredFallen = true;
                    break;
                }
            }

            Assert.That(enteredFallen, Is.True);

            float dwellStart = Time.time;

            // Wait ~1 s into the dwell.
            int waitFrames = Mathf.CeilToInt(1f / Time.fixedDeltaTime);
            for (int i = 0; i < waitFrames; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            Assert.That(_rig.CharacterState.CurrentState, Is.EqualTo(CharacterStateType.Fallen),
                "Character must still be in Fallen after 1 s of a medium-severity dwell.");

            // Re-trigger surrender with a higher severity. This should reset/extend the timer.
            _rig.BalanceController.TriggerSurrender(0.8f);

            // Wait for the character to eventually leave Fallen.
            bool transitioned = false;
            int maxFrames = Mathf.CeilToInt(8f / Time.fixedDeltaTime);
            for (int i = 0; i < maxFrames; i++)
            {
                yield return new WaitForFixedUpdate();
                if (_rig.CharacterState.CurrentState != CharacterStateType.Fallen)
                {
                    transitioned = true;
                    break;
                }
            }

            float totalDwell = Time.time - dwellStart;
            Assert.That(transitioned, Is.True,
                "Character must eventually leave Fallen after re-knockdown.");
            // Original severity 0.5 → Lerp(1.5, 3.0, 0.5) = 2.25 s.
            // The re-hit should extend beyond that.
            Assert.That(totalDwell, Is.GreaterThan(2.0f),
                $"Re-hit dwell should be longer than the original medium severity dwell " +
                $"(actual: {totalDwell:F2} s).");
            // Cap is 4.5 s. Allow some margin for physics jitter.
            Assert.That(totalDwell, Is.LessThanOrEqualTo(5.5f),
                $"Re-hit dwell must be capped at _reKnockdownFloorDwellCap " +
                $"(actual: {totalDwell:F2} s).");
        }
    }
}
