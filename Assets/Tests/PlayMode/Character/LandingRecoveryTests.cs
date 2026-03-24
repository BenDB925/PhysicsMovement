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
    /// Outcome-based PlayMode coverage for Plan 06 landing recovery slices 1 through 3.
    /// Verifies that touchdown spring restoration ramps after landing and that the
    /// landing recovery path limits tilt, bleeds excess speed, and recovers back into sprint.
    /// </summary>
    public class LandingRecoveryTests
    {
        private const int SettleFrames = 80;
        private const float FaceplantAngleThreshold = 50f;
        private const float PeakLandingTiltTarget = 35f;
        private const float CounterLeanPeakLandingTiltTarget = 25f;
        private const float MaxForwardPelvisTiltAfterLandingDeg = 2f;
        private const int SprintRampFrames = 500;
        private const int MeasurementWindowFrames = 150;
        private const int PelvisTiltMeasurementFrames = 20;
        private const int PostJumpObservationFrames = 260;
        private const int JumpReadyWindowFrames = 30;
        private const int JumpReadyStabilityFrames = 3;
        private const int RecoveryTimeTargetFrames = 30;
        private const int DampingDisabledMeasurementFrame = 3;
        private const int ReducedSpeedMeasurementFrame = 5;
        private const int SpeedRecoveryMeasurementFrame = 50;
        private const float RampAssertionDurationSeconds = 0.12f;
        private const float RecoveryTiltThresholdDeg = 5f;
        private const float Frame5VsFrame3MaxRatio = 0.95f;
        private const float PostLandingMinimumHorizontalSpeed = 0.25f;
        private const float RecoveryRetainMinRatio = 0.75f;
        private const float RecoveryMinimumHorizontalSpeed = 1f;
        private const float DisabledVsDampedMinRatio = 1.15f;
        private const float DisabledLandingDampingDurationSeconds = 0.08f;

        private static readonly Vector3 TestOrigin = new Vector3(2000f, 0f, 0f);
        private static readonly FieldInfo SmoothedPelvisTiltField = typeof(BalanceController).GetField(
            "_smoothedPelvisTiltDeg",
            BindingFlags.Instance | BindingFlags.NonPublic);
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
        private static readonly FieldInfo MaxSpeedField = typeof(PlayerMovement).GetField(
            "_maxSpeed",
            BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo SprintSpeedMultiplierField = typeof(PlayerMovement).GetField(
            "_sprintSpeedMultiplier",
            BindingFlags.Instance | BindingFlags.NonPublic);

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
            public float MaxForwardPelvisTiltAfterLanding;
            public float PeakTiltAfterLanding;
            public int FramesToRecoverUpright = -1;
            public float FirstGroundedSpringMultiplier = float.NaN;
            public float SpringMultiplierAfterRamp = float.NaN;
            public float PreJumpHorizontalSpeed = float.NaN;
            public float HorizontalSpeedAtGroundedFrame3 = float.NaN;
            public float HorizontalSpeedAtGroundedFrame5 = float.NaN;
            public float HorizontalSpeedAtRecoveryFrame = float.NaN;
            public float NominalSprintSpeed = float.NaN;
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

            CreateRig();
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

        [UnityTest]
        public IEnumerator LandingRecovery_NoPelvisTiltSpikeOnLanding()
        {
            // Arrange
            float landingSpringRampDurationSeconds = GetLandingSpringRampDuration(_rig.LegAnimator);
            LandingRecoveryMetrics metrics = new LandingRecoveryMetrics();

            // Act
            yield return RunSingleSprintJump(metrics, landingSpringRampDurationSeconds);

            // Assert
            TestContext.Out.WriteLine(
                $"[METRIC] LandingRecovery MaxForwardPelvisTilt={metrics.MaxForwardPelvisTiltAfterLanding:F2}");

            Assert.That(metrics.WasAirborne, Is.True,
                "Sprint jump should enter airborne before landing pelvis tilt is evaluated.");
            Assert.That(metrics.Landed, Is.True,
                "Sprint jump should land within the landing recovery observation window.");
            Assert.That(metrics.EnteredFallen, Is.False,
                "Pelvis tilt suppression should keep the character out of Fallen during landing recovery.");
            Assert.That(metrics.MaxForwardPelvisTiltAfterLanding, Is.LessThanOrEqualTo(MaxForwardPelvisTiltAfterLandingDeg),
                $"Landing absorb should prevent forward pelvis tilt spikes above {MaxForwardPelvisTiltAfterLandingDeg:F1}°. " +
                $"Observed {metrics.MaxForwardPelvisTiltAfterLanding:F2}° in the first {PelvisTiltMeasurementFrames} grounded frames.");
        }

        [UnityTest]
        public IEnumerator LandingRecovery_PeakTiltFurtherReducedWithCounterLean()
        {
            // Arrange
            float landingSpringRampDurationSeconds = GetLandingSpringRampDuration(_rig.LegAnimator);
            LandingRecoveryMetrics metrics = new LandingRecoveryMetrics();

            // Act
            yield return RunSingleSprintJump(metrics, landingSpringRampDurationSeconds);

            // Assert
            TestContext.Out.WriteLine(
                $"[METRIC] LandingRecovery CounterLeanPeakTilt={metrics.PeakTiltAfterLanding:F1}");

            Assert.That(metrics.WasAirborne, Is.True,
                "Sprint jump should enter airborne before the counter-lean tilt target is evaluated.");
            Assert.That(metrics.Landed, Is.True,
                "Sprint jump should land within the landing recovery observation window.");
            Assert.That(metrics.EnteredFallen, Is.False,
                "Counter-lean validation should stay in Standing or Moving, not Fallen.");
            Assert.That(metrics.PeakTiltAfterLanding, Is.LessThan(CounterLeanPeakLandingTiltTarget),
                $"Peak post-landing tilt was {metrics.PeakTiltAfterLanding:F1}°, above the Slice 2 target of {CounterLeanPeakLandingTiltTarget:F1}°.");
        }

        [UnityTest]
        public IEnumerator LandingRecovery_RecoveryTimeImproved()
        {
            // Arrange
            float landingSpringRampDurationSeconds = GetLandingSpringRampDuration(_rig.LegAnimator);
            LandingRecoveryMetrics metrics = new LandingRecoveryMetrics();

            // Act
            yield return RunSingleSprintJump(metrics, landingSpringRampDurationSeconds);

            // Assert
            TestContext.Out.WriteLine(
                $"[METRIC] LandingRecovery FramesToRecoverUpright={metrics.FramesToRecoverUpright}");

            Assert.That(metrics.WasAirborne, Is.True,
                "Sprint jump should enter airborne before recovery time is evaluated.");
            Assert.That(metrics.Landed, Is.True,
                "Sprint jump should land within the landing recovery observation window.");
            Assert.That(metrics.EnteredFallen, Is.False,
                "Recovery time validation should not enter Fallen.");
            Assert.That(metrics.FramesToRecoverUpright, Is.GreaterThanOrEqualTo(0),
                $"Upright angle never dropped below {RecoveryTiltThresholdDeg:F1}° within the measurement window.");
            Assert.That(metrics.FramesToRecoverUpright, Is.LessThan(RecoveryTimeTargetFrames),
                $"Recovery took {metrics.FramesToRecoverUpright} frames, above the Slice 2 target of {RecoveryTimeTargetFrames} frames.");
        }

        [UnityTest]
        public IEnumerator LandingRecovery_HorizontalSpeedReducedAfterLanding()
        {
            // Arrange
            LandingRecoveryMetrics dampedMetrics = new LandingRecoveryMetrics();

            // Act
            yield return RunLandingScenario(dampedMetrics);

            // Assert
            TestContext.Out.WriteLine(
                $"[METRIC] LandingRecovery DampedSpeedFrame3={dampedMetrics.HorizontalSpeedAtGroundedFrame3:F2}");
            TestContext.Out.WriteLine(
                $"[METRIC] LandingRecovery DampedSpeedFrame5={dampedMetrics.HorizontalSpeedAtGroundedFrame5:F2}");

            AssertLandingRunCompleted(dampedMetrics, "The damped sprint-jump run");
            Assert.That(float.IsNaN(dampedMetrics.HorizontalSpeedAtGroundedFrame3), Is.False,
                "The damped landing recovery harness did not capture horizontal speed three grounded frames after landing.");
            Assert.That(float.IsNaN(dampedMetrics.HorizontalSpeedAtGroundedFrame5), Is.False,
                "The damped landing recovery harness did not capture horizontal speed five grounded frames after landing.");
            Assert.That(dampedMetrics.HorizontalSpeedAtGroundedFrame5,
                Is.LessThan(dampedMetrics.HorizontalSpeedAtGroundedFrame3 * Frame5VsFrame3MaxRatio),
                $"With landing damping enabled, horizontal speed five grounded frames after landing should stay below {Frame5VsFrame3MaxRatio:P0} of the frame-3 landing speed. " +
                $"Observed frame-5 {dampedMetrics.HorizontalSpeedAtGroundedFrame5:F2} from frame-3 {dampedMetrics.HorizontalSpeedAtGroundedFrame3:F2}.");
            Assert.That(dampedMetrics.HorizontalSpeedAtGroundedFrame5,
                Is.GreaterThan(PostLandingMinimumHorizontalSpeed),
                $"With landing damping enabled, horizontal speed five grounded frames after landing should stay above {PostLandingMinimumHorizontalSpeed:F2} so the character does not dead-stop. " +
                $"Observed frame-5 {dampedMetrics.HorizontalSpeedAtGroundedFrame5:F2}.");
        }

        [UnityTest]
        public IEnumerator LandingRecovery_SpeedRecoveryAfterDamping()
        {
            // Arrange
            LandingRecoveryMetrics dampedMetrics = new LandingRecoveryMetrics();

            // Act
            yield return RunLandingScenario(dampedMetrics);

            // Assert
            TestContext.Out.WriteLine(
                $"[METRIC] LandingRecovery DampedSpeedFrame5={dampedMetrics.HorizontalSpeedAtGroundedFrame5:F2}");
            TestContext.Out.WriteLine(
                $"[METRIC] LandingRecovery DampedSpeedFrame50={dampedMetrics.HorizontalSpeedAtRecoveryFrame:F2}");

            AssertLandingRunCompleted(dampedMetrics, "The damped sprint-jump run");
            Assert.That(float.IsNaN(dampedMetrics.HorizontalSpeedAtGroundedFrame5), Is.False,
                "The damped landing recovery harness did not capture horizontal speed five grounded frames after landing.");
            Assert.That(float.IsNaN(dampedMetrics.HorizontalSpeedAtRecoveryFrame), Is.False,
                "The damped landing recovery harness did not capture horizontal speed fifty grounded frames after landing.");
            Assert.That(dampedMetrics.HorizontalSpeedAtRecoveryFrame,
                Is.GreaterThan(dampedMetrics.HorizontalSpeedAtGroundedFrame5 * RecoveryRetainMinRatio),
                $"Horizontal speed fifty grounded frames after landing should retain at least {RecoveryRetainMinRatio:P0} of the damped frame-5 speed once the damping window has expired. " +
                $"Observed {dampedMetrics.HorizontalSpeedAtRecoveryFrame:F2} from frame-5 {dampedMetrics.HorizontalSpeedAtGroundedFrame5:F2}.");
            Assert.That(dampedMetrics.HorizontalSpeedAtRecoveryFrame,
                Is.GreaterThan(RecoveryMinimumHorizontalSpeed),
                $"Horizontal speed fifty grounded frames after landing should recover into a clearly moving state above {RecoveryMinimumHorizontalSpeed:F2}. " +
                $"Observed {dampedMetrics.HorizontalSpeedAtRecoveryFrame:F2}.");
        }

        [UnityTest]
        public IEnumerator LandingRecovery_DampingDisabledWhenFactorIsOne()
        {
            // Arrange
            LandingRecoveryMetrics dampedMetrics = new LandingRecoveryMetrics();
            LandingRecoveryMetrics disabledMetrics = new LandingRecoveryMetrics();

            // Act
            yield return RunLandingScenario(dampedMetrics);
            RecreateRig();
            yield return RunLandingScenario(
                disabledMetrics,
                1f,
                DisabledLandingDampingDurationSeconds);

            // Assert
            TestContext.Out.WriteLine(
                $"[METRIC] LandingRecovery DampedSpeedFrame3={dampedMetrics.HorizontalSpeedAtGroundedFrame3:F2}");
            TestContext.Out.WriteLine(
                $"[METRIC] LandingRecovery DisabledSpeedFrame3={disabledMetrics.HorizontalSpeedAtGroundedFrame3:F2}");

            AssertLandingRunCompleted(dampedMetrics, "The damped sprint-jump run");
            AssertLandingRunCompleted(disabledMetrics, "The damping-disabled sprint-jump control run");
            Assert.That(float.IsNaN(dampedMetrics.HorizontalSpeedAtGroundedFrame3), Is.False,
                "The damped landing recovery harness did not capture horizontal speed three grounded frames after landing.");
            Assert.That(float.IsNaN(disabledMetrics.HorizontalSpeedAtGroundedFrame3), Is.False,
                "The damping-disabled landing recovery harness did not capture horizontal speed three grounded frames after landing.");
            float disabledMinimumFrame3Speed =
                dampedMetrics.HorizontalSpeedAtGroundedFrame3 * DisabledVsDampedMinRatio;
            Assert.That(disabledMetrics.HorizontalSpeedAtGroundedFrame3,
                Is.GreaterThan(disabledMinimumFrame3Speed),
                $"With damping disabled, horizontal speed three grounded frames after landing should stay materially above the damped run. " +
                $"Observed {disabledMetrics.HorizontalSpeedAtGroundedFrame3:F2} vs damped {dampedMetrics.HorizontalSpeedAtGroundedFrame3:F2}. " +
                $"Expected above {disabledMinimumFrame3Speed:F2}.");
        }

        private void CreateRig()
        {
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

        private void RecreateRig()
        {
            _rig?.Dispose();
            _rig = null;
            _upperLegLJoint = null;
            CreateRig();
        }

        private IEnumerator RunLandingScenario(
            LandingRecoveryMetrics metrics,
            float? landingDampingFactor = null,
            float? landingDampingDurationSeconds = null)
        {
            if (landingDampingFactor.HasValue)
            {
                _rig.PlayerMovement.SetLandingHorizontalDampingForTest(
                    landingDampingFactor.Value,
                    landingDampingDurationSeconds ?? DisabledLandingDampingDurationSeconds);
            }

            float landingSpringRampDurationSeconds = GetLandingSpringRampDuration(_rig.LegAnimator);
            yield return RunSingleSprintJump(metrics, landingSpringRampDurationSeconds);
        }

        private static void AssertLandingRunCompleted(LandingRecoveryMetrics metrics, string scenarioLabel)
        {
            Assert.That(metrics.WasAirborne, Is.True,
                $"{scenarioLabel} should enter airborne before landing recovery assertions are evaluated.");
            Assert.That(metrics.Landed, Is.True,
                $"{scenarioLabel} should land within the landing recovery observation window.");
            Assert.That(metrics.EnteredFallen, Is.False,
                $"{scenarioLabel} should stay out of Fallen during landing recovery.");
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

            metrics.PreJumpHorizontalSpeed = GetHorizontalSpeed(_rig.HipsBody.linearVelocity);
            metrics.NominalSprintSpeed = GetNominalSprintSpeed(_rig.PlayerMovement);

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
                float uprightAngle = _rig.BalanceController.UprightAngle;
                float horizontalSpeed = GetHorizontalSpeed(_rig.HipsBody.linearVelocity);
                metrics.PeakTiltAfterLanding = Mathf.Max(metrics.PeakTiltAfterLanding, uprightAngle);

                if (landingFrame == 0)
                {
                    metrics.FirstGroundedSpringMultiplier = currentMultiplier;
                }

                if (landingFrame == DampingDisabledMeasurementFrame)
                {
                    metrics.HorizontalSpeedAtGroundedFrame3 = horizontalSpeed;
                }

                if (landingFrame == ReducedSpeedMeasurementFrame)
                {
                    metrics.HorizontalSpeedAtGroundedFrame5 = horizontalSpeed;
                }

                if (landingFrame == SpeedRecoveryMeasurementFrame)
                {
                    metrics.HorizontalSpeedAtRecoveryFrame = horizontalSpeed;
                }

                if (landingFrame < PelvisTiltMeasurementFrames)
                {
                    float smoothedPelvisTilt = GetSmoothedPelvisTiltDeg(_rig.BalanceController);
                    metrics.MaxForwardPelvisTiltAfterLanding = Mathf.Max(
                        metrics.MaxForwardPelvisTiltAfterLanding,
                        smoothedPelvisTilt);
                }

                if (metrics.FramesToRecoverUpright < 0 && uprightAngle < RecoveryTiltThresholdDeg)
                {
                    metrics.FramesToRecoverUpright = landingFrame;
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

        private static float GetSmoothedPelvisTiltDeg(BalanceController balanceController)
        {
            Assert.That(SmoothedPelvisTiltField, Is.Not.Null,
                "BalanceController must expose the _smoothedPelvisTiltDeg field for landing recovery verification.");
            return (float)SmoothedPelvisTiltField.GetValue(balanceController);
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

        private static float GetNominalSprintSpeed(PlayerMovement playerMovement)
        {
            Assert.That(MaxSpeedField, Is.Not.Null,
                "PlayerMovement must expose the _maxSpeed field for landing damping verification.");
            Assert.That(SprintSpeedMultiplierField, Is.Not.Null,
                "PlayerMovement must expose the _sprintSpeedMultiplier field for landing damping verification.");

            float maxSpeed = (float)MaxSpeedField.GetValue(playerMovement);
            float sprintSpeedMultiplier = (float)SprintSpeedMultiplierField.GetValue(playerMovement);
            return maxSpeed * sprintSpeedMultiplier;
        }

        private static float GetHorizontalSpeed(Vector3 velocity)
        {
            return new Vector3(velocity.x, 0f, velocity.z).magnitude;
        }
    }
}
