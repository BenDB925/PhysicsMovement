using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
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
        private const int JumpReadyStabilityFrames = 3;
        private const int SecondJumpReadyWindowFrames = 30;
        private const float FaceplantAngleThreshold = 45f;
        private const float StableUprightCeiling = 25f;
        private const int PostLandStabilityDeadline = 150;
        private const float LandingTelemetryWindowSeconds = 0.5f;

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
            public List<LandingTelemetrySnapshot> LandingWindowSamplesAfterJump1 = new List<LandingTelemetrySnapshot>();
            public List<LandingTelemetrySnapshot> LandingWindowSamplesAfterJump2 = new List<LandingTelemetrySnapshot>();
        }

        private sealed class LandingTelemetrySnapshot
        {
            public int FrameNumber;
            public float Time;
            public float UprightAngle;
            public float DesiredLeanDegrees;
            public float LandingAbsorbBlend;
            public float TotalPelvisTilt;
            public float RecoveryBlend;
            public float RecoveryKdBlend;
            public bool IsGrounded;
            public bool IsSurrendered;
            public CharacterStateType CharacterState;
            public string NdjsonLine;
        }

        private sealed class JumpTelemetrySnapshot
        {
            public int AttemptId;
            public int FrameNumber;
            public float Time;
            public string EventType;
            public string Reason;
            public CharacterStateType CharacterState;
            public bool IsGrounded;
            public bool IsFallen;
            public string JumpPhase;
            public string NdjsonLine;
        }

        private IEnumerator RunSprintJumpSequence(SprintJumpDiagnostics diag)
        {
            int landingWindowFrames = Mathf.CeilToInt(
                LandingTelemetryWindowSeconds / Mathf.Max(0.0001f, Time.fixedDeltaTime));
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

            bool wasAirborne1 = _rig.CharacterState.CurrentState == CharacterStateType.Airborne
                || !_rig.BalanceController.IsGrounded;
            bool observedUngrounded1 = !_rig.BalanceController.IsGrounded;
            bool landedAfter1 = false;
            int landing1SampleCount = 0;
            for (int frame = 0; frame < PostJumpSettleFrames; frame++)
            {
                yield return new WaitForFixedUpdate();
                RecordContinuousState(diag);

                float angle = _rig.BalanceController.UprightAngle;
                diag.UprightAngleSamplesAfterJump1.Add(angle);
                diag.MaxUprightAngleAfterJump1 = Mathf.Max(diag.MaxUprightAngleAfterJump1, angle);

                CharacterStateType state = _rig.CharacterState.CurrentState;
                if (state == CharacterStateType.Airborne || !_rig.BalanceController.IsGrounded)
                {
                    wasAirborne1 = true;
                }

                if (!_rig.BalanceController.IsGrounded)
                {
                    observedUngrounded1 = true;
                }

                if (observedUngrounded1 && _rig.BalanceController.IsGrounded)
                {
                    landedAfter1 = true;
                }

                if (landedAfter1 && landing1SampleCount < landingWindowFrames)
                {
                    diag.LandingWindowSamplesAfterJump1.Add(CaptureLandingTelemetry(_rig.BalanceController));
                    landing1SampleCount++;
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

            int jumpReadyStableFrames = 0;
            for (int frame = 0; frame < SecondJumpReadyWindowFrames; frame++)
            {
                if (IsJumpReady())
                {
                    jumpReadyStableFrames++;
                    if (jumpReadyStableFrames >= JumpReadyStabilityFrames)
                    {
                        break;
                    }
                }
                else
                {
                    jumpReadyStableFrames = 0;
                }

                yield return new WaitForFixedUpdate();
                RecordContinuousState(diag);
            }

            _rig.PlayerMovement.SetJumpInputForTest(true);
            yield return new WaitForFixedUpdate();
            _rig.PlayerMovement.SetJumpInputForTest(false);
            RecordContinuousState(diag);

            bool wasAirborne2 = _rig.CharacterState.CurrentState == CharacterStateType.Airborne
                || !_rig.BalanceController.IsGrounded;
            bool observedUngrounded2 = !_rig.BalanceController.IsGrounded;
            bool landedAfter2 = false;
            int landing2SampleCount = 0;
            for (int frame = 0; frame < FinalSettleFrames; frame++)
            {
                yield return new WaitForFixedUpdate();
                RecordContinuousState(diag);

                float angle = _rig.BalanceController.UprightAngle;
                diag.UprightAngleSamplesAfterJump2.Add(angle);
                diag.MaxUprightAngleAfterJump2 = Mathf.Max(diag.MaxUprightAngleAfterJump2, angle);

                CharacterStateType state = _rig.CharacterState.CurrentState;
                if (state == CharacterStateType.Airborne || !_rig.BalanceController.IsGrounded)
                {
                    wasAirborne2 = true;
                }

                if (!_rig.BalanceController.IsGrounded)
                {
                    observedUngrounded2 = true;
                }

                if (observedUngrounded2 && _rig.BalanceController.IsGrounded)
                {
                    landedAfter2 = true;
                }

                if (landedAfter2 && landing2SampleCount < landingWindowFrames)
                {
                    diag.LandingWindowSamplesAfterJump2.Add(CaptureLandingTelemetry(_rig.BalanceController));
                    landing2SampleCount++;
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

        private bool IsJumpReady()
        {
            CharacterStateType state = _rig.CharacterState.CurrentState;
            bool canJumpFromState = state == CharacterStateType.Standing ||
                                    state == CharacterStateType.Moving;

            return _rig.BalanceController.IsGrounded &&
                   canJumpFromState &&
                   _rig.PlayerMovement.CurrentJumpPhase == JumpPhase.None;
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

        [UnityTest]
        public IEnumerator SprintJump_TwoConsecutiveJumps_DoesNotFaceplant()
        {
            // Arrange
            SprintJumpDiagnostics diag = new SprintJumpDiagnostics();

            // Act
            yield return RunSprintJumpSequence(diag);

            // Assert
            TestContext.Out.WriteLine(
                $"[METRIC] SprintJump_TwoJumps PeakTiltAfterJump1={diag.MaxUprightAngleAfterJump1:F1}");
            TestContext.Out.WriteLine(
                $"[METRIC] SprintJump_TwoJumps PeakTiltAfterJump2={diag.MaxUprightAngleAfterJump2:F1}");
            TestContext.Out.WriteLine(
                $"[METRIC] SprintJump_TwoJumps RecoveryFrames1={diag.FramesToRecoverAfterJump1}");
            TestContext.Out.WriteLine(
                $"[METRIC] SprintJump_TwoJumps RecoveryFrames2={diag.FramesToRecoverAfterJump2}");
            TestContext.Out.WriteLine(
                $"[METRIC] SprintJump_TwoJumps FinalTilt={diag.FinalUprightAngle:F1}");
            TestContext.Out.WriteLine(
                $"[METRIC] SprintJump_TwoJumps PeakSprintSpeed={diag.PeakSprintSpeed:F2}");
            TestContext.Out.WriteLine(
                $"[METRIC] SprintJump_TwoJumps EverFallen={diag.EverEnteredFallen}");

            Assert.That(diag.WasAirborneAfterJump1, Is.True,
                "Jump 1 should have entered Airborne.");
            Assert.That(diag.WasAirborneAfterJump2, Is.True,
                "Jump 2 should have entered Airborne.");

            Assert.That(diag.MaxUprightAngleAfterJump1, Is.LessThan(FaceplantAngleThreshold),
                $"Landing #1 peak tilt {diag.MaxUprightAngleAfterJump1:F1}° exceeds {FaceplantAngleThreshold}°.");
            Assert.That(diag.MaxUprightAngleAfterJump2, Is.LessThan(FaceplantAngleThreshold),
                $"Landing #2 peak tilt {diag.MaxUprightAngleAfterJump2:F1}° exceeds {FaceplantAngleThreshold}°.");

            Assert.That(diag.EnteredFallenAfterJump1, Is.False,
                "No Fallen state after landing #1.");
            Assert.That(diag.EnteredFallenAfterJump2, Is.False,
                "No Fallen state after landing #2.");

            Assert.That(diag.FinalUprightAngle, Is.LessThan(StableUprightCeiling),
                $"After the full sequence, final tilt is {diag.FinalUprightAngle:F1}° " +
                $"(ceiling {StableUprightCeiling}°).");
            Assert.That(diag.FinalState, Is.Not.EqualTo(CharacterStateType.Fallen),
                "Character should not be in Fallen state at the end of the sequence.");
        }

        [UnityTest]
        public IEnumerator SprintJump_LandingRecovery_RegainsUprightWithinDeadline()
        {
            // Arrange
            SprintJumpDiagnostics diag = new SprintJumpDiagnostics();

            // Act
            yield return RunSprintJumpSequence(diag);

            // Assert
            TestContext.Out.WriteLine(
                $"[METRIC] SprintJump_Recovery RecoveryFrames1={diag.FramesToRecoverAfterJump1}");
            TestContext.Out.WriteLine(
                $"[METRIC] SprintJump_Recovery RecoveryFrames2={diag.FramesToRecoverAfterJump2}");

            Assert.That(diag.WasAirborneAfterJump1, Is.True,
                "Jump 1 must produce Airborne state.");

            Assert.That(diag.FramesToRecoverAfterJump1, Is.GreaterThanOrEqualTo(0),
                $"After landing #1, character never recovered below {StableUprightCeiling}° " +
                $"within {PostJumpSettleFrames} frames. Peak tilt was {diag.MaxUprightAngleAfterJump1:F1}°.");

            Assert.That(diag.FramesToRecoverAfterJump1, Is.LessThanOrEqualTo(PostLandStabilityDeadline),
                $"Recovery after landing #1 took {diag.FramesToRecoverAfterJump1} frames " +
                $"(deadline: {PostLandStabilityDeadline} frames = {PostLandStabilityDeadline * 0.01f:F1} s).");

            if (diag.WasAirborneAfterJump2)
            {
                Assert.That(diag.FramesToRecoverAfterJump2, Is.GreaterThanOrEqualTo(0),
                    $"After landing #2, character never recovered below {StableUprightCeiling}°.");

                Assert.That(diag.FramesToRecoverAfterJump2, Is.LessThanOrEqualTo(PostLandStabilityDeadline),
                    $"Recovery after landing #2 took {diag.FramesToRecoverAfterJump2} frames " +
                    $"(deadline: {PostLandStabilityDeadline}).");
            }
        }

        [UnityTest]
        public IEnumerator SprintJump_TelemetryCapture_LogsRecoveryEventsAroundLanding()
        {
            // Arrange
            LocomotionDirector director = _rig.Instance.GetComponentInChildren<LocomotionDirector>();
            Assert.That(director, Is.Not.Null,
                "LocomotionDirector must be present on the player prefab.");

            System.Reflection.FieldInfo telemetryField = typeof(LocomotionDirector).GetField(
                "_enableRecoveryTelemetry",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.That(telemetryField, Is.Not.Null,
                "_enableRecoveryTelemetry field must exist.");

            System.Reflection.PropertyInfo telemetryLogProperty = typeof(LocomotionDirector).GetProperty(
                "RecoveryTelemetryLog",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.That(telemetryLogProperty, Is.Not.Null,
                "RecoveryTelemetryLog should be accessible through LocomotionDirector.");

            telemetryField.SetValue(director, true);

            SprintJumpDiagnostics diag = new SprintJumpDiagnostics();

            // Act
            yield return RunSprintJumpSequence(diag);

            IList telemetryLog = telemetryLogProperty.GetValue(director) as IList;
            List<JumpTelemetrySnapshot> jumpTelemetry = ReadJumpTelemetry(_rig.PlayerMovement);

            // Assert
            Assert.That(telemetryLog, Is.Not.Null,
                "RecoveryTelemetryLog should return the in-memory telemetry ring buffer.");
            Assert.That(jumpTelemetry, Is.Not.Null,
                "Jump telemetry should return the in-memory jump-attempt log.");
            Assert.That(jumpTelemetry.Count, Is.GreaterThanOrEqualTo(2),
                "The two scripted jump requests should emit jump telemetry events.");

            TestContext.Out.WriteLine($"[METRIC] SprintJump_Telemetry EventCount={telemetryLog.Count}");
            for (int index = 0; index < telemetryLog.Count; index++)
            {
                object telemetryEvent = telemetryLog[index];
                Assert.That(telemetryEvent, Is.Not.Null,
                    $"Telemetry event {index} should not be null.");

                System.Reflection.MethodInfo toNdjsonLineMethod = telemetryEvent.GetType().GetMethod(
                    "ToNdjsonLine",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                Assert.That(toNdjsonLineMethod, Is.Not.Null,
                    "Recovery telemetry entries should expose ToNdjsonLine() for structured log output.");

                string ndjsonLine = toNdjsonLineMethod.Invoke(telemetryEvent, null) as string;
                TestContext.Out.WriteLine($"[METRIC] SprintJump_Telemetry Event[{index}]={ndjsonLine}");
            }

            TestContext.Out.WriteLine($"[METRIC] SprintJump_JumpTelemetry EventCount={jumpTelemetry.Count}");
            for (int index = 0; index < jumpTelemetry.Count; index++)
            {
                TestContext.Out.WriteLine($"[METRIC] SprintJump_JumpTelemetry Event[{index}]={jumpTelemetry[index].NdjsonLine}");
            }

            LogLandingWindowMetrics("Landing1", diag.LandingWindowSamplesAfterJump1);
            LogLandingWindowMetrics("Landing2", diag.LandingWindowSamplesAfterJump2);
            LogJumpAttemptMetrics("Jump1", jumpTelemetry, 1);
            LogJumpAttemptMetrics("Jump2", jumpTelemetry, 2);

            if (diag.EverEnteredFallen || diag.PeakUprightAngleOverall > 30f)
            {
                Assert.That(telemetryLog.Count, Is.GreaterThan(0),
                    "Recovery telemetry should log at least one event when the character " +
                    $"wobbles (peak tilt {diag.PeakUprightAngleOverall:F1}°) or enters Fallen.");
            }
        }

        private static LandingTelemetrySnapshot CaptureLandingTelemetry(BalanceController balanceController)
        {
            object sample = GetPropertyValue<object>(balanceController, "CurrentLandingWindowTelemetrySample");

            return new LandingTelemetrySnapshot
            {
                FrameNumber = GetPropertyValue<int>(sample, "FrameNumber"),
                Time = GetPropertyValue<float>(sample, "Time"),
                UprightAngle = GetPropertyValue<float>(sample, "UprightAngle"),
                DesiredLeanDegrees = GetPropertyValue<float>(sample, "DesiredLeanDegrees"),
                LandingAbsorbBlend = GetPropertyValue<float>(sample, "LandingAbsorbBlend"),
                TotalPelvisTilt = GetPropertyValue<float>(sample, "TotalPelvisTilt"),
                RecoveryBlend = GetPropertyValue<float>(sample, "RecoveryBlend"),
                RecoveryKdBlend = GetPropertyValue<float>(sample, "RecoveryKdBlend"),
                IsGrounded = GetPropertyValue<bool>(sample, "IsGrounded"),
                IsSurrendered = GetPropertyValue<bool>(sample, "IsSurrendered"),
                CharacterState = GetPropertyValue<CharacterStateType>(sample, "CharacterState"),
                NdjsonLine = InvokeToNdjsonLine(sample),
            };
        }

        private static List<JumpTelemetrySnapshot> ReadJumpTelemetry(PlayerMovement playerMovement)
        {
            IList telemetryLog = GetPropertyValue<object>(playerMovement, "JumpTelemetryLog") as IList;
            Assert.That(telemetryLog, Is.Not.Null,
                "JumpTelemetryLog should expose the in-memory jump attempt ring buffer.");

            List<JumpTelemetrySnapshot> snapshots = new List<JumpTelemetrySnapshot>(telemetryLog.Count);
            for (int index = 0; index < telemetryLog.Count; index++)
            {
                object telemetryEvent = telemetryLog[index];
                Assert.That(telemetryEvent, Is.Not.Null,
                    $"Jump telemetry event {index} should not be null.");

                snapshots.Add(new JumpTelemetrySnapshot
                {
                    AttemptId = GetPropertyValue<int>(telemetryEvent, "AttemptId"),
                    FrameNumber = GetPropertyValue<int>(telemetryEvent, "FrameNumber"),
                    Time = GetPropertyValue<float>(telemetryEvent, "Time"),
                    EventType = GetPropertyValue<object>(telemetryEvent, "EventType").ToString(),
                    Reason = GetPropertyValue<string>(telemetryEvent, "Reason"),
                    CharacterState = GetPropertyValue<CharacterStateType>(telemetryEvent, "CharacterState"),
                    IsGrounded = GetPropertyValue<bool>(telemetryEvent, "IsGrounded"),
                    IsFallen = GetPropertyValue<bool>(telemetryEvent, "IsFallen"),
                    JumpPhase = GetPropertyValue<object>(telemetryEvent, "JumpPhase").ToString(),
                    NdjsonLine = InvokeToNdjsonLine(telemetryEvent),
                });
            }

            return snapshots;
        }

        private static void LogLandingWindowMetrics(string label, List<LandingTelemetrySnapshot> samples)
        {
            TestContext.Out.WriteLine($"[METRIC] SprintJump_{label} WindowSampleCount={samples.Count}");
            if (samples.Count == 0)
            {
                return;
            }

            LandingTelemetrySnapshot landingSample = samples[0];
            LandingTelemetrySnapshot peakSample = landingSample;
            int peakSampleIndex = 0;
            for (int index = 1; index < samples.Count; index++)
            {
                if (samples[index].UprightAngle > peakSample.UprightAngle)
                {
                    peakSample = samples[index];
                    peakSampleIndex = index;
                }
            }

            int firstRecoverySampleIndex = FindFirstSampleIndex(samples, sample => sample.RecoveryBlend > 0.0001f);
            int firstRecoveryKdSampleIndex = FindFirstSampleIndex(samples, sample => sample.RecoveryKdBlend > 0.0001f);
            int firstSurrenderSampleIndex = FindFirstSampleIndex(samples, sample => sample.IsSurrendered);

            TestContext.Out.WriteLine($"[METRIC] SprintJump_{label} LandingFrame={landingSample.FrameNumber}");
            TestContext.Out.WriteLine($"[METRIC] SprintJump_{label} PeakTiltFrame={peakSample.FrameNumber}");
            TestContext.Out.WriteLine($"[METRIC] SprintJump_{label} PeakTiltSampleIndex={peakSampleIndex}");
            TestContext.Out.WriteLine($"[METRIC] SprintJump_{label} FirstRecoverySampleIndex={firstRecoverySampleIndex}");
            TestContext.Out.WriteLine($"[METRIC] SprintJump_{label} FirstRecoveryKdSampleIndex={firstRecoveryKdSampleIndex}");
            TestContext.Out.WriteLine($"[METRIC] SprintJump_{label} FirstSurrenderSampleIndex={firstSurrenderSampleIndex}");
            TestContext.Out.WriteLine(
                $"[METRIC] SprintJump_{label} PeakDuringRecovery={peakSample.RecoveryBlend > 0.0001f}");
            TestContext.Out.WriteLine(
                $"[METRIC] SprintJump_{label} PeakDuringReducedDamping={peakSample.RecoveryKdBlend > 0.0001f}");
            TestContext.Out.WriteLine(
                $"[METRIC] SprintJump_{label} PeakAfterSurrender={peakSample.IsSurrendered}");
            TestContext.Out.WriteLine($"[METRIC] SprintJump_{label} LandingSample={landingSample.NdjsonLine}");
            TestContext.Out.WriteLine($"[METRIC] SprintJump_{label} PeakSample={peakSample.NdjsonLine}");
        }

        private static void LogJumpAttemptMetrics(string label, List<JumpTelemetrySnapshot> telemetry, int attemptId)
        {
            List<JumpTelemetrySnapshot> attemptEvents = new List<JumpTelemetrySnapshot>();
            for (int index = 0; index < telemetry.Count; index++)
            {
                if (telemetry[index].AttemptId == attemptId)
                {
                    attemptEvents.Add(telemetry[index]);
                }
            }

            bool jumpAccepted = false;
            bool windUpEntered = false;
            bool launchFired = false;
            string windUpAbortReason = "none";

            for (int index = 0; index < attemptEvents.Count; index++)
            {
                JumpTelemetrySnapshot attemptEvent = attemptEvents[index];
                if (attemptEvent.EventType == "JumpAccepted")
                {
                    jumpAccepted = true;
                }
                else if (attemptEvent.EventType == "WindUpEntered")
                {
                    windUpEntered = true;
                }
                else if (attemptEvent.EventType == "WindUpAborted")
                {
                    windUpAbortReason = attemptEvent.Reason;
                }
                else if (attemptEvent.EventType == "LaunchFired")
                {
                    launchFired = true;
                }
            }

            TestContext.Out.WriteLine($"[METRIC] SprintJump_{label} AttemptEventCount={attemptEvents.Count}");
            TestContext.Out.WriteLine($"[METRIC] SprintJump_{label} JumpAccepted={jumpAccepted}");
            TestContext.Out.WriteLine($"[METRIC] SprintJump_{label} WindUpEntered={windUpEntered}");
            TestContext.Out.WriteLine($"[METRIC] SprintJump_{label} WindUpAbortedReason={windUpAbortReason}");
            TestContext.Out.WriteLine($"[METRIC] SprintJump_{label} LaunchFired={launchFired}");
            for (int index = 0; index < attemptEvents.Count; index++)
            {
                TestContext.Out.WriteLine(
                    $"[METRIC] SprintJump_{label} Event[{index}]={attemptEvents[index].NdjsonLine}");
            }
        }

        private static int FindFirstSampleIndex(
            List<LandingTelemetrySnapshot> samples,
            Predicate<LandingTelemetrySnapshot> predicate)
        {
            for (int index = 0; index < samples.Count; index++)
            {
                if (predicate(samples[index]))
                {
                    return index;
                }
            }

            return -1;
        }

        private static string InvokeToNdjsonLine(object instance)
        {
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            MethodInfo method = instance.GetType().GetMethod("ToNdjsonLine", flags);

            Assert.That(method, Is.Not.Null,
                $"Expected type '{instance.GetType().FullName}' to expose ToNdjsonLine().");

            return method.Invoke(instance, null) as string;
        }

        private static T GetPropertyValue<T>(object instance, string propertyName)
        {
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            PropertyInfo property = instance.GetType().GetProperty(propertyName, flags);

            Assert.That(property, Is.Not.Null,
                $"Expected type '{instance.GetType().FullName}' to expose property '{propertyName}'.");

            object value = property.GetValue(instance);
            Assert.That(value, Is.Not.Null, $"Property '{propertyName}' should not be null.");
            return (T)value;
        }
    }
}