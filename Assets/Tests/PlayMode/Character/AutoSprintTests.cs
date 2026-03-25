using System.Collections;
using NUnit.Framework;
using PhysicsDrivenMovement.Character;
using UnityEngine;
using UnityEngine.TestTools;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// Outcome-based PlayMode coverage for the auto-sprint and land-into-run flow.
    /// Uses prefab-backed movement and horizontal speed measurements rather than internal sprint state.
    /// </summary>
    public class AutoSprintTests
    {
        private const int WarmUpFrames = 30;
        private const int PreSprintFrames = 80;
        private const int SprintLeadInFrames = 60;
        private const int SprintMeasurementFrames = 80;
        private const int SprintFrames = PreSprintFrames + SprintLeadInFrames + SprintMeasurementFrames;
        private const int StopFrames = 20;
        private const int WalkJumpFrames = 30;
        private const int PostSprintLandingFrames = 15;
        private const int PostWalkLandingFrames = 30;
        private const int JumpReadyWindowFrames = 30;
        private const int JumpReadyStabilityFrames = 3;
        private const int LandIntoRunObservationFrames = 300;
        private const int WalkJumpObservationFrames = 200;

        private static readonly Vector3 TestOrigin = new Vector3(2400f, 0f, 2400f);

        private PlayerPrefabTestRig _rig;
        private float _savedFixedDeltaTime;
        private int _savedSolverIterations;
        private int _savedSolverVelocityIterations;

        private sealed class JumpObservation
        {
            public bool ObservedAirborne;
            public bool ObservedLanding;
        }

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
                SpawnOffset = new Vector3(0f, 0.5f, 0f),
                GroundScale = new Vector3(400f, 1f, 400f),
            });
        }

        [TearDown]
        public void TearDown()
        {
            _rig?.Dispose();
            _rig = null;

            Time.fixedDeltaTime = _savedFixedDeltaTime;
            Physics.defaultSolverIterations = _savedSolverIterations;
            Physics.defaultSolverVelocityIterations = _savedSolverVelocityIterations;
        }

        [UnityTest]
        public IEnumerator AutoSprint_WalkRampsToSprintAfterDelay()
        {
            // Arrange
            float walkSpeedCap = _rig.PlayerMovement.MaxSpeed;
            float sprintSpeedCap = walkSpeedCap * _rig.PlayerMovement.SprintSpeedMultiplier;
            yield return WarmUpForAutoSprint();

            // Act
            _rig.PlayerMovement.SetMoveInputForTest(Vector2.up);

            Vector3 walkWindowStart = Flatten(_rig.HipsBody.position);
            yield return RunFixedFrames(PreSprintFrames);
            float speedAtWalkWindow = GetHorizontalSpeed(_rig.HipsBody.linearVelocity);
            float walkWindowDistance = GetHorizontalDistance(_rig.HipsBody.position, walkWindowStart);

            yield return RunFixedFrames(SprintLeadInFrames);

            Vector3 sprintWindowStart = Flatten(_rig.HipsBody.position);
            yield return RunFixedFrames(SprintMeasurementFrames);
            float speedAtSprintWindow = GetHorizontalSpeed(_rig.HipsBody.linearVelocity);
            float sprintWindowDistance = GetHorizontalDistance(_rig.HipsBody.position, sprintWindowStart);

            // Assert
            TestContext.Out.WriteLine($"[METRIC] AutoSprint WalkRamp walkCap={walkSpeedCap:F2} sprintCap={sprintSpeedCap:F2}");
            TestContext.Out.WriteLine($"[METRIC] AutoSprint WalkRamp speed80={speedAtWalkWindow:F2} speed220={speedAtSprintWindow:F2}");
            TestContext.Out.WriteLine($"[METRIC] AutoSprint WalkRamp dist80={walkWindowDistance:F2} dist140to220={sprintWindowDistance:F2}");

            Assert.That(speedAtWalkWindow, Is.LessThan(walkSpeedCap * 1.2f),
                $"After {PreSprintFrames} frames of sustained movement, horizontal speed should still be in walk territory. " +
                $"Observed {speedAtWalkWindow:F2} with walk cap {walkSpeedCap:F2}.");
            Assert.That(sprintWindowDistance, Is.GreaterThan(walkWindowDistance * 1.5f),
                $"The late movement window should cover materially more ground than the opening walk window once auto-sprint has had time to engage. " +
                $"Observed {sprintWindowDistance:F2}m in frames 140-220 versus {walkWindowDistance:F2}m in frames 0-80.");
        }

        [UnityTest]
        public IEnumerator AutoSprint_StoppingResetsTimer()
        {
            // Arrange
            float walkSpeedCap = _rig.PlayerMovement.MaxSpeed;
            yield return WarmUpForAutoSprint();

            // Act
            _rig.PlayerMovement.SetMoveInputForTest(Vector2.up);

            Vector3 initialWalkWindowStart = Flatten(_rig.HipsBody.position);
            yield return RunFixedFrames(PreSprintFrames);
            float initialWalkWindowDistance = GetHorizontalDistance(_rig.HipsBody.position, initialWalkWindowStart);

            yield return RunFixedFrames(SprintLeadInFrames);
            Vector3 sprintWindowStart = Flatten(_rig.HipsBody.position);
            yield return RunFixedFrames(SprintMeasurementFrames);
            float speedBeforeStop = GetHorizontalSpeed(_rig.HipsBody.linearVelocity);
            float sprintWindowDistance = GetHorizontalDistance(_rig.HipsBody.position, sprintWindowStart);

            _rig.PlayerMovement.SetMoveInputForTest(Vector2.zero);
            yield return RunFixedFrames(StopFrames);

            _rig.PlayerMovement.SetMoveInputForTest(Vector2.up);
            Vector3 restartWindowStart = Flatten(_rig.HipsBody.position);
            yield return RunFixedFrames(PreSprintFrames);
            float speedAfterRestart = GetHorizontalSpeed(_rig.HipsBody.linearVelocity);
            float restartWindowDistance = GetHorizontalDistance(_rig.HipsBody.position, restartWindowStart);

            // Assert
            TestContext.Out.WriteLine($"[METRIC] AutoSprint StopReset preStop={speedBeforeStop:F2} restart80={speedAfterRestart:F2}");
            TestContext.Out.WriteLine($"[METRIC] AutoSprint StopReset walk80={initialWalkWindowDistance:F2} sprint140to220={sprintWindowDistance:F2} restart80={restartWindowDistance:F2}");

            Assert.That(sprintWindowDistance, Is.GreaterThan(initialWalkWindowDistance * 1.5f),
                $"Before the stop/reset window, the late movement window should already cover materially more ground than the opening walk window. " +
                $"Observed {sprintWindowDistance:F2}m versus {initialWalkWindowDistance:F2}m.");
            Assert.That(speedAfterRestart, Is.LessThan(walkSpeedCap * 1.2f),
                $"After stopping long enough to clear the grace window, the first {PreSprintFrames} restart frames should return to walk pace. " +
                $"Observed {speedAfterRestart:F2} with walk cap {walkSpeedCap:F2}.");
        }

        [UnityTest]
        public IEnumerator AutoSprint_LandIntoRun_MaintainsSpeed()
        {
            // Arrange
            yield return WarmUpForAutoSprint();
            _rig.PlayerMovement.SetMoveInputForTest(Vector2.up);
            yield return RunFixedFrames(SprintFrames);
            yield return WaitForJumpReady();

            float preJumpSpeed = GetHorizontalSpeed(_rig.HipsBody.linearVelocity);
            JumpObservation observation = new JumpObservation();

            // Act
            _rig.PlayerMovement.SetJumpInputForTest(true);
            yield return new WaitForFixedUpdate();
            _rig.PlayerMovement.SetJumpInputForTest(false);

            yield return WaitForAirborneThenLanding(observation, LandIntoRunObservationFrames);
            yield return RunFixedFrames(PostSprintLandingFrames);
            float postLandingSpeed = GetHorizontalSpeed(_rig.HipsBody.linearVelocity);

            // Assert
            TestContext.Out.WriteLine($"[METRIC] AutoSprint LandIntoRun preJump={preJumpSpeed:F2} postLanding={postLandingSpeed:F2}");

            Assert.That(observation.ObservedAirborne, Is.True,
                "The sprint-jump run should go airborne before the land-into-run speed check is evaluated.");
            Assert.That(observation.ObservedLanding, Is.True,
                "The sprint-jump run should land within the observation window.");
            Assert.That(postLandingSpeed, Is.GreaterThan(preJumpSpeed * 0.75f),
                $"Landing from a sprint jump should preserve most of the pre-jump horizontal speed. " +
                $"Observed {postLandingSpeed:F2} after landing from {preJumpSpeed:F2}.");
        }

        [UnityTest]
        public IEnumerator AutoSprint_WalkJump_DoesNotLandAtSprintSpeed()
        {
            // Arrange
            float walkSpeedCap = _rig.PlayerMovement.MaxSpeed;
            yield return WarmUpForForcedWalk();

            // Act
            _rig.PlayerMovement.SetMoveInputForTest(Vector2.up);
            yield return RunFixedFrames(WalkJumpFrames);
            yield return WaitForJumpReady();

            JumpObservation observation = new JumpObservation();
            _rig.PlayerMovement.SetJumpInputForTest(true);
            yield return new WaitForFixedUpdate();
            _rig.PlayerMovement.SetJumpInputForTest(false);

            yield return WaitForAirborneThenLanding(observation, WalkJumpObservationFrames);
            yield return RunFixedFrames(PostWalkLandingFrames);
            float postLandingSpeed = GetHorizontalSpeed(_rig.HipsBody.linearVelocity);

            // Assert
            TestContext.Out.WriteLine($"[METRIC] AutoSprint WalkJump postLanding={postLandingSpeed:F2} walkCap={walkSpeedCap:F2}");

            Assert.That(observation.ObservedAirborne, Is.True,
                "The walk-jump control run should go airborne before the landing-speed check is evaluated.");
            Assert.That(observation.ObservedLanding, Is.True,
                "The walk-jump control run should land within the observation window.");
            Assert.That(postLandingSpeed, Is.LessThan(walkSpeedCap * 1.3f),
                $"A forced-walk jump should not land at sprint speed. Observed {postLandingSpeed:F2} with walk cap {walkSpeedCap:F2}.");
        }

        private IEnumerator WarmUpForAutoSprint()
        {
            _rig.PlayerMovement.SetMoveInputForTest(Vector2.zero);
            _rig.PlayerMovement.SetJumpInputForTest(false);
            _rig.PlayerMovement.ClearSprintInputOverrideForTest();

            yield return RunFixedFrames(WarmUpFrames);
        }

        private IEnumerator WarmUpForForcedWalk()
        {
            _rig.PlayerMovement.SetMoveInputForTest(Vector2.zero);
            _rig.PlayerMovement.SetJumpInputForTest(false);
            _rig.PlayerMovement.SetSprintInputForTest(false);

            yield return RunFixedFrames(WarmUpFrames);
        }

        private IEnumerator WaitForJumpReady()
        {
            int stableFrames = 0;
            for (int frame = 0; frame < JumpReadyWindowFrames; frame++)
            {
                if (IsJumpReady())
                {
                    stableFrames++;
                    if (stableFrames >= JumpReadyStabilityFrames)
                    {
                        yield break;
                    }
                }
                else
                {
                    stableFrames = 0;
                }

                yield return new WaitForFixedUpdate();
            }

            Assert.Fail("AutoSprintTests never reached a stable grounded jump-ready state before jump input was applied.");
        }

        private IEnumerator WaitForAirborneThenLanding(JumpObservation observation, int maxFrames)
        {
            bool observedUngrounded = !_rig.BalanceController.IsGrounded;
            if (observedUngrounded || _rig.CharacterState.CurrentState == CharacterStateType.Airborne)
            {
                observation.ObservedAirborne = true;
            }

            for (int frame = 0; frame < maxFrames; frame++)
            {
                yield return new WaitForFixedUpdate();

                if (!_rig.BalanceController.IsGrounded || _rig.CharacterState.CurrentState == CharacterStateType.Airborne)
                {
                    observation.ObservedAirborne = true;
                    observedUngrounded = true;
                }

                if (observedUngrounded && _rig.BalanceController.IsGrounded)
                {
                    observation.ObservedLanding = true;
                    yield break;
                }
            }
        }

        private bool IsJumpReady()
        {
            CharacterStateType state = _rig.CharacterState.CurrentState;
            bool canJumpFromState = state == CharacterStateType.Standing ||
                                    state == CharacterStateType.Moving;

            return _rig.BalanceController.IsGrounded &&
                   canJumpFromState &&
                   _rig.PlayerMovement.CurrentJumpPhase == JumpPhase.None;
        }

        private static float GetHorizontalSpeed(Vector3 velocity)
        {
            return new Vector2(velocity.x, velocity.z).magnitude;
        }

        private static Vector3 Flatten(Vector3 position)
        {
            return new Vector3(position.x, 0f, position.z);
        }

        private static float GetHorizontalDistance(Vector3 endPosition, Vector3 startPosition)
        {
            return Vector3.Distance(Flatten(endPosition), startPosition);
        }

        private static IEnumerator RunFixedFrames(int frames)
        {
            for (int frame = 0; frame < frames; frame++)
            {
                yield return new WaitForFixedUpdate();
            }
        }
    }
}