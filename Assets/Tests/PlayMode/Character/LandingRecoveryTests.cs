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
    /// Outcome-based PlayMode coverage for Plan 06 Slice 1 landing recovery.
    /// Verifies that spring restoration ramps after touchdown and keeps sprint-jump
    /// landing tilt within the locked quality thresholds.
    /// </summary>
    public class LandingRecoveryTests
    {
        private const int SettleFrames = 80;
        private const float FaceplantAngleThreshold = 50f;
        private const float PeakLandingTiltTarget = 35f;
        private const int SprintRampFrames = 500;
        private const int MeasurementWindowFrames = 150;
        private const int PostJumpObservationFrames = 260;
        private const int JumpReadyWindowFrames = 30;
        private const int JumpReadyStabilityFrames = 3;
        private const float RampAssertionDurationSeconds = 0.12f;

        private static readonly Vector3 TestOrigin = new Vector3(2000f, 0f, 0f);
        private static readonly MethodInfo SetLandingSpringRampDurationMethod = typeof(LegAnimator).GetMethod(
            "SetLandingSpringRampDurationForTest",
            BindingFlags.Instance | BindingFlags.Public);
        private static readonly FieldInfo LandingSpringRampDurationField = typeof(LegAnimator).GetField(
            "_landingSpringRampDuration",
            BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo LandingSpringRampTimerField = typeof(LegAnimator).GetField(
            "_landingSpringRampTimer",
            BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly PropertyInfo CurrentSpringMultiplierProperty = typeof(LegAnimator).GetProperty(
            "CurrentSpringMultiplier",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private PlayerPrefabTestRig _rig;
        private ConfigurableJoint _upperLegLJoint;
        private float _savedFixedDeltaTime;
        private int _savedSolverIterations;
        private int _savedSolverVelocityIterations;

        private sealed class LandingRecoveryMetrics
        {
            public bool WasAirborne;
            public bool Landed;
            public bool EnteredFallen;
            public float PeakTiltAfterLanding;
            public float FirstGroundedSpringMultiplier = float.NaN;
            public float SpringMultiplierAfterRamp = float.NaN;
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

            _upperLegLJoint = _rig.UpperLegL.GetComponent<ConfigurableJoint>();
            Assert.That(_upperLegLJoint, Is.Not.Null,
                "Landing recovery tests require the prefab-backed UpperLeg_L ConfigurableJoint.");
        }

        [TearDown]
        public void TearDown()
        {
            _rig?.Dispose();
            _rig = null;
            _upperLegLJoint = null;

            Time.fixedDeltaTime = _savedFixedDeltaTime;
            Physics.defaultSolverIterations = _savedSolverIterations;
            Physics.defaultSolverVelocityIterations = _savedSolverVelocityIterations;
        }

        [UnityTest]
        public IEnumerator LandingRecovery_SpringRampsGraduallyAfterLanding()
        {
            // Arrange
            SetLandingSpringRampDuration(_rig.LegAnimator, RampAssertionDurationSeconds);
            float landingSpringRampDurationSeconds = GetLandingSpringRampDuration(_rig.LegAnimator);
            LandingRecoveryMetrics metrics = new LandingRecoveryMetrics();

            // Act
            yield return RunSingleSprintJump(metrics, landingSpringRampDurationSeconds);

            // Assert
            TestContext.Out.WriteLine(
                $"[METRIC] LandingRecovery RampFirstGrounded={metrics.FirstGroundedSpringMultiplier:F3}");
            TestContext.Out.WriteLine(
                $"[METRIC] LandingRecovery RampAfterDuration={metrics.SpringMultiplierAfterRamp:F3}");
            TestContext.Out.WriteLine(
                $"[METRIC] LandingRecovery RampPeakTilt={metrics.PeakTiltAfterLanding:F1}");

            Assert.That(metrics.WasAirborne, Is.True,
                "Sprint jump should enter airborne before the landing recovery window is evaluated.");
            Assert.That(metrics.Landed, Is.True,
                "Sprint jump should land within the landing recovery observation window.");
            Assert.That(metrics.EnteredFallen, Is.False,
                "Landing spring ramp should not let the character enter Fallen during the sprint-jump recovery window.");
            Assert.That(metrics.FirstGroundedSpringMultiplier, Is.LessThan(1f),
                $"Expected the first grounded frame to stay below full stiffness while the landing ramp is active. " +
                $"Observed multiplier {metrics.FirstGroundedSpringMultiplier:F3}.");
            Assert.That(metrics.SpringMultiplierAfterRamp, Is.EqualTo(1f).Within(0.001f),
                $"Expected the landing spring ramp to finish at full stiffness after the configured duration. " +
                $"Observed multiplier {metrics.SpringMultiplierAfterRamp:F3}.");
        }

        [UnityTest]
        public IEnumerator LandingRecovery_PeakTiltReducedAfterSprintJump()
        {
            // Arrange
            float landingSpringRampDurationSeconds = GetLandingSpringRampDuration(_rig.LegAnimator);
            LandingRecoveryMetrics metrics = new LandingRecoveryMetrics();

            // Act
            yield return RunSingleSprintJump(metrics, landingSpringRampDurationSeconds);

            // Assert
            TestContext.Out.WriteLine(
                $"[METRIC] LandingRecovery PeakTilt={metrics.PeakTiltAfterLanding:F1}");

            Assert.That(metrics.WasAirborne, Is.True,
                "Sprint jump should enter airborne before peak landing tilt is evaluated.");
            Assert.That(metrics.Landed, Is.True,
                "Sprint jump should land within the landing recovery observation window.");
            Assert.That(metrics.EnteredFallen, Is.False,
                "Peak landing tilt measurement should stay in Standing or Moving, not Fallen.");
            Assert.That(metrics.PeakTiltAfterLanding, Is.LessThan(PeakLandingTiltTarget),
                $"Peak post-landing tilt was {metrics.PeakTiltAfterLanding:F1}°, above the slice target of {PeakLandingTiltTarget:F1}°.");
        }

        [UnityTest]
        public IEnumerator LandingRecovery_ExistingFaceplantTestStillPasses()
        {
            // Arrange
            float landingSpringRampDurationSeconds = GetLandingSpringRampDuration(_rig.LegAnimator);
            LandingRecoveryMetrics metrics = new LandingRecoveryMetrics();

            // Act
            yield return RunSingleSprintJump(metrics, landingSpringRampDurationSeconds);

            // Assert
            TestContext.Out.WriteLine(
                $"[METRIC] LandingRecovery FaceplantGuardPeakTilt={metrics.PeakTiltAfterLanding:F1}");

            Assert.That(metrics.WasAirborne, Is.True,
                "Sprint jump should enter airborne before the faceplant regression guard is evaluated.");
            Assert.That(metrics.Landed, Is.True,
                "Sprint jump should land within the landing recovery observation window.");
            Assert.That(metrics.EnteredFallen, Is.False,
                "Faceplant regression guard should stay clear of Fallen state.");
            Assert.That(metrics.PeakTiltAfterLanding, Is.LessThan(FaceplantAngleThreshold),
                $"Peak post-landing tilt was {metrics.PeakTiltAfterLanding:F1}°, above the faceplant threshold of {FaceplantAngleThreshold:F1}°.");
        }

        private IEnumerator RunSingleSprintJump(LandingRecoveryMetrics metrics, float landingSpringRampDurationSeconds)
        {
            yield return _rig.WarmUp(SettleFrames, renderFrames: 0);

            float baselineSpring = _upperLegLJoint.slerpDrive.positionSpring;
            Assert.That(baselineSpring, Is.GreaterThan(1f),
                $"Landing recovery tests require a non-trivial grounded spring baseline. Observed {baselineSpring:F2}.");

            _rig.PlayerMovement.SetMoveInputForTest(Vector2.up);
            _rig.PlayerMovement.SetSprintInputForTest(true);

            for (int frame = 0; frame < SprintRampFrames; frame++)
            {
                yield return new WaitForFixedUpdate();
            }

            yield return WaitForJumpReady();

            _rig.PlayerMovement.SetJumpInputForTest(true);
            yield return new WaitForFixedUpdate();
            _rig.PlayerMovement.SetJumpInputForTest(false);

            UpdateAirborneAndFallState(metrics);

            bool observedUngrounded = !_rig.BalanceController.IsGrounded;
            int rampCompletionFrames = Mathf.Max(
                1,
                Mathf.CeilToInt(landingSpringRampDurationSeconds / Mathf.Max(0.0001f, Time.fixedDeltaTime)) + 5);

            for (int frame = 0; frame < PostJumpObservationFrames; frame++)
            {
                yield return new WaitForFixedUpdate();

                UpdateAirborneAndFallState(metrics);
                if (!_rig.BalanceController.IsGrounded)
                {
                    observedUngrounded = true;
                }

                if (observedUngrounded && _rig.BalanceController.IsGrounded)
                {
                    metrics.Landed = true;
                    yield return MeasureLandingWindow(metrics, baselineSpring, rampCompletionFrames);
                    break;
                }
            }

            _rig.PlayerMovement.SetMoveInputForTest(Vector2.zero);
            _rig.PlayerMovement.SetSprintInputForTest(false);
            _rig.PlayerMovement.SetJumpInputForTest(false);
        }

        private IEnumerator MeasureLandingWindow(
            LandingRecoveryMetrics metrics,
            float baselineSpring,
            int rampCompletionFrames)
        {
            int measurementFrames = Mathf.Max(MeasurementWindowFrames, rampCompletionFrames);
            int framesSinceRampStart = -1;
            float previousRampTimer = 0f;
            for (int landingFrame = 0; landingFrame < measurementFrames; landingFrame++)
            {
                if (landingFrame > 0)
                {
                    yield return new WaitForFixedUpdate();
                }

                UpdateAirborneAndFallState(metrics);
                float currentMultiplier = GetCurrentSpringMultiplier(_rig.LegAnimator, baselineSpring);
                float currentRampTimer = GetLandingSpringRampTimer(_rig.LegAnimator);
                metrics.PeakTiltAfterLanding = Mathf.Max(metrics.PeakTiltAfterLanding, _rig.BalanceController.UprightAngle);

                if (landingFrame == 0)
                {
                    metrics.FirstGroundedSpringMultiplier = currentMultiplier;
                }

                bool rampRestarted = currentRampTimer > 0f &&
                                     (previousRampTimer <= 0f || currentRampTimer > previousRampTimer + 0.0005f);
                if (rampRestarted)
                {
                    framesSinceRampStart = 0;
                }
                else if (framesSinceRampStart >= 0)
                {
                    framesSinceRampStart++;
                }

                if (framesSinceRampStart == rampCompletionFrames - 1)
                {
                    metrics.SpringMultiplierAfterRamp = currentMultiplier;
                }

                previousRampTimer = currentRampTimer;
            }
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

            Assert.Fail("Sprint jump never reached a stable grounded jump-ready state before input was applied.");
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

        private void UpdateAirborneAndFallState(LandingRecoveryMetrics metrics)
        {
            if (_rig.CharacterState.CurrentState == CharacterStateType.Airborne || !_rig.BalanceController.IsGrounded)
            {
                metrics.WasAirborne = true;
            }

            if (_rig.CharacterState.CurrentState == CharacterStateType.Fallen)
            {
                metrics.EnteredFallen = true;
            }
        }

        private static float GetLandingSpringRampDuration(LegAnimator legAnimator)
        {
            Assert.That(LandingSpringRampDurationField, Is.Not.Null,
                "LegAnimator must expose the _landingSpringRampDuration field for landing recovery verification.");
            return (float)LandingSpringRampDurationField.GetValue(legAnimator);
        }

        private static void SetLandingSpringRampDuration(LegAnimator legAnimator, float duration)
        {
            if (SetLandingSpringRampDurationMethod != null)
            {
                SetLandingSpringRampDurationMethod.Invoke(legAnimator, new object[] { duration });
                return;
            }

            Assert.That(LandingSpringRampDurationField, Is.Not.Null,
                "LegAnimator must expose either SetLandingSpringRampDurationForTest or the _landingSpringRampDuration field.");
            LandingSpringRampDurationField.SetValue(legAnimator, duration);
        }

        private static float GetLandingSpringRampTimer(LegAnimator legAnimator)
        {
            Assert.That(LandingSpringRampTimerField, Is.Not.Null,
                "LegAnimator must expose the _landingSpringRampTimer field for landing recovery verification.");
            return (float)LandingSpringRampTimerField.GetValue(legAnimator);
        }

        private float GetCurrentSpringMultiplier(LegAnimator legAnimator, float baselineSpring)
        {
            if (CurrentSpringMultiplierProperty != null)
            {
                object value = CurrentSpringMultiplierProperty.GetValue(legAnimator);
                if (value is float multiplier)
                {
                    return multiplier;
                }
            }

            return _upperLegLJoint.slerpDrive.positionSpring / Mathf.Max(0.0001f, baselineSpring);
        }
    }
}