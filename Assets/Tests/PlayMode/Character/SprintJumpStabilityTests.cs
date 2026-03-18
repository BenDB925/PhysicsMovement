using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using PhysicsDrivenMovement.Character;
using UnityEngine;
using UnityEngine.TestTools;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// Scaffold PlayMode coverage for the sprint-jump landing stability regression slice.
    /// </summary>
    public class SprintJumpStabilityTests
    {
        private const int SettleFrames = 80;
        private const int SprintRampFrames = 500;
        private const int PostJumpSettleFrames = 200;
        private const int SecondSprintFrames = 500;
        private const int FinalSettleFrames = 200;
        private const float FaceplantAngleThreshold = 45f;
        private const float StableUprightCeiling = 25f;
        private const int PostLandStabilityDeadline = 150;

        private PlayerPrefabTestRig _rig;

        [SetUp]
        public void SetUp()
        {
            _rig = PlayerPrefabTestRig.Create(new PlayerPrefabTestRig.Options
            {
                TestOrigin = new Vector3(200f, 0f, 200f),
                SpawnOffset = new Vector3(0f, 0.5f, 0f),
                GroundScale = new Vector3(400f, 1f, 400f),
            });
        }

        [TearDown]
        public void TearDown()
        {
            _rig?.Dispose();
            _rig = null;
        }

        private sealed class SprintJumpDiagnostics
        {
            public float MaxUprightAngleAfterJump1;
            public float MaxUprightAngleAfterJump2;
            public bool EnteredFallenAfterJump1;
            public bool EnteredFallenAfterJump2;
            public int FramesToRecoverAfterJump1 = -1;
            public int FramesToRecoverAfterJump2 = -1;
            public bool WasAirborneAfterJump1;
            public bool WasAirborneAfterJump2;

            public float PeakUprightAngleOverall;
            public float PeakSprintSpeed;
            public bool EverEnteredFallen;
            public CharacterStateType FinalState;
            public float FinalUprightAngle;

            public List<float> UprightAngleSamplesAfterJump1 = new List<float>();
            public List<float> UprightAngleSamplesAfterJump2 = new List<float>();
        }

        private IEnumerator RunSprintJumpSequence(SprintJumpDiagnostics diag)
        {
            yield return _rig.WarmUp(physicsFrames: SettleFrames, renderFrames: 0);

            Vector2 forwardInput = new Vector2(0f, 1f);
            if (ScenarioDefinitions.StartStop.Waypoints != null && ScenarioDefinitions.StartStop.Waypoints.Length > 1)
            {
                Vector3[] travelDirections = ScenarioPathUtility.GetTravelDirections(ScenarioDefinitions.StartStop);
                if (travelDirections.Length > 0)
                {
                    forwardInput = ScenarioPathUtility.ToMoveInput(travelDirections[0]);
                }
            }

            _rig.PlayerMovement.SetMoveInputForTest(forwardInput);
            _rig.PlayerMovement.SetSprintInputForTest(true);

            for (int frame = 0; frame < SprintRampFrames; frame++)
            {
                yield return new WaitForFixedUpdate();
                RecordContinuousState(diag);
            }

            _rig.PlayerMovement.SetJumpInputForTest(true);
            yield return new WaitForFixedUpdate();
            _rig.PlayerMovement.SetJumpInputForTest(false);
            RecordContinuousState(diag);
            diag.WasAirborneAfterJump1 = true;

            bool wasAirborne1 = false;
            bool landedAfter1 = false;
            for (int frame = 0; frame < PostJumpSettleFrames; frame++)
            {
                yield return new WaitForFixedUpdate();
                RecordContinuousState(diag);

                float angle = _rig.BalanceController.UprightAngle;
                diag.UprightAngleSamplesAfterJump1.Add(angle);
                diag.MaxUprightAngleAfterJump1 = Mathf.Max(diag.MaxUprightAngleAfterJump1, angle);

                CharacterStateType state = _rig.CharacterState.CurrentState;
                if (state == CharacterStateType.Airborne)
                {
                    wasAirborne1 = true;
                }

                if (wasAirborne1 && _rig.BalanceController.IsGrounded)
                {
                    landedAfter1 = true;
                }

                if (state == CharacterStateType.Fallen)
                {
                    diag.EnteredFallenAfterJump1 = true;
                }

                if (diag.FramesToRecoverAfterJump1 == -1
                    && landedAfter1
                    && angle < StableUprightCeiling
                    && state != CharacterStateType.Fallen)
                {
                    diag.FramesToRecoverAfterJump1 = frame;
                }
            }

            diag.WasAirborneAfterJump1 = wasAirborne1;

            for (int frame = 0; frame < SecondSprintFrames; frame++)
            {
                yield return new WaitForFixedUpdate();
                RecordContinuousState(diag);
            }

            _rig.PlayerMovement.SetJumpInputForTest(true);
            yield return new WaitForFixedUpdate();
            _rig.PlayerMovement.SetJumpInputForTest(false);
            RecordContinuousState(diag);
            diag.WasAirborneAfterJump2 = true;

            bool wasAirborne2 = false;
            bool landedAfter2 = false;
            for (int frame = 0; frame < FinalSettleFrames; frame++)
            {
                yield return new WaitForFixedUpdate();
                RecordContinuousState(diag);

                float angle = _rig.BalanceController.UprightAngle;
                diag.UprightAngleSamplesAfterJump2.Add(angle);
                diag.MaxUprightAngleAfterJump2 = Mathf.Max(diag.MaxUprightAngleAfterJump2, angle);

                CharacterStateType state = _rig.CharacterState.CurrentState;
                if (state == CharacterStateType.Airborne)
                {
                    wasAirborne2 = true;
                }

                if (wasAirborne2 && _rig.BalanceController.IsGrounded)
                {
                    landedAfter2 = true;
                }

                if (state == CharacterStateType.Fallen)
                {
                    diag.EnteredFallenAfterJump2 = true;
                }

                if (diag.FramesToRecoverAfterJump2 == -1
                    && landedAfter2
                    && angle < StableUprightCeiling
                    && state != CharacterStateType.Fallen)
                {
                    diag.FramesToRecoverAfterJump2 = frame;
                }
            }

            diag.WasAirborneAfterJump2 = wasAirborne2;
            diag.FinalState = _rig.CharacterState.CurrentState;
            diag.FinalUprightAngle = _rig.BalanceController.UprightAngle;

            _rig.PlayerMovement.SetMoveInputForTest(Vector2.zero);
            _rig.PlayerMovement.SetSprintInputForTest(false);
            _rig.PlayerMovement.SetJumpInputForTest(false);
        }

        private void RecordContinuousState(SprintJumpDiagnostics diag)
        {
            float angle = _rig.BalanceController.UprightAngle;
            diag.PeakUprightAngleOverall = Mathf.Max(diag.PeakUprightAngleOverall, angle);

            if (_rig.CharacterState.CurrentState == CharacterStateType.Fallen)
            {
                diag.EverEnteredFallen = true;
            }

            Vector3 velocity = _rig.HipsBody.linearVelocity;
            float planarSpeed = new Vector2(velocity.x, velocity.z).magnitude;
            diag.PeakSprintSpeed = Mathf.Max(diag.PeakSprintSpeed, planarSpeed);
        }

        [UnityTest]
        public IEnumerator RunSprintJumpSequence_WithFreshRig_CompletesWithoutErrors()
        {
            // Arrange
            SprintJumpDiagnostics diag = new SprintJumpDiagnostics();

            // Act
            yield return RunSprintJumpSequence(diag);

            // Assert
            TestContext.Out.WriteLine(
                "SprintJump smoke run completed. " +
                $"PeakTilt={diag.PeakUprightAngleOverall:F1} " +
                $"PeakSpeed={diag.PeakSprintSpeed:F2} " +
                $"FinalState={diag.FinalState} " +
                $"FinalTilt={diag.FinalUprightAngle:F1} " +
                $"Airborne1={diag.WasAirborneAfterJump1} " +
                $"Airborne2={diag.WasAirborneAfterJump2}");
        }

        [UnityTest]
        public IEnumerator SprintJump_SingleJump_DoesNotFaceplant()
        {
            // Arrange
            SprintJumpDiagnostics diag = new SprintJumpDiagnostics();

            // Act
            yield return RunSprintJumpSequence(diag);

            // Assert
            TestContext.Out.WriteLine(
                $"[METRIC] SprintJump_SingleJump PeakTiltAfterJump1={diag.MaxUprightAngleAfterJump1:F1}");
            TestContext.Out.WriteLine(
                $"[METRIC] SprintJump_SingleJump RecoveryFrames1={diag.FramesToRecoverAfterJump1}");
            TestContext.Out.WriteLine(
                $"[METRIC] SprintJump_SingleJump PeakSprintSpeed={diag.PeakSprintSpeed:F2}");

            Assert.That(diag.WasAirborneAfterJump1, Is.True,
                "Jump 1 should have entered Airborne state.");

            Assert.That(diag.MaxUprightAngleAfterJump1, Is.LessThan(FaceplantAngleThreshold),
                $"After sprint-jump landing #1, peak torso tilt was {diag.MaxUprightAngleAfterJump1:F1}° " +
                $"(threshold {FaceplantAngleThreshold}°). Character is faceplanting.");

            Assert.That(diag.EnteredFallenAfterJump1, Is.False,
                "Character should not enter Fallen state after sprint-jump landing #1.");
        }
    }
}