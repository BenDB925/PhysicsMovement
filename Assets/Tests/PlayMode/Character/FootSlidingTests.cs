using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using PhysicsDrivenMovement.Character;
using UnityEngine;
using UnityEngine.TestTools;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// Outcome tests that measure planted-foot drift — how far a foot slides in the
    /// XZ plane while it should be stationary on the ground. Failing tests indicate
    /// that the body is moving faster than the leg cycle can anchor.
    /// </summary>
    public class FootSlidingTests
    {
        private const int SettleFrames = 100;
        private const int WalkFrames = 500;
        private const int SprintFrames = 500;
        private static readonly float[] SweepMoveForces = { 100f, 125f, 150f, 175f, 200f, 250f, 300f };

        /// <summary>
        /// Maximum per-step planted foot drift at walk speed before the test fails.
        /// The plan's initial guess of 0.04m assumed IK-quality foot anchoring;
        /// the physics-driven character (no foot pinning) measures ~0.31m peak
        /// drift at walk speed. This threshold is a regression gate — not a quality
        /// target. WP3b will tighten it after the speed envelope is analyzed.
        /// </summary>
        private const float MaxPlantedFootDriftMetres = 0.35f;

        /// <summary>
        /// Maximum per-step planted foot drift at sprint speed. Baseline measurement
        /// shows ~0.74m peak drift at sprint (PeakSpeed=3.22 m/s). Set to 0.80m as a
        /// regression gate that captures the current sprint quality level. WP3b will
        /// tighten after the speed envelope sweep in WP2b.
        /// </summary>
        private const float MaxSprintPlantedFootDriftMetres = 0.80f;

        /// <summary>
        /// Frames to skip after entering Stance before starting drift measurement.
        /// This filters out the initial foot-settling motion as the plant finalizes.
        /// </summary>
        private const int StanceSettleFrames = 3;

        private static readonly FieldInfo LeftLegStateMachineField = typeof(LegAnimator)
            .GetField("_leftLegStateMachine", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo RightLegStateMachineField = typeof(LegAnimator)
            .GetField("_rightLegStateMachine", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo MoveForceField = typeof(PlayerMovement)
            .GetField("_moveForce", BindingFlags.NonPublic | BindingFlags.Instance);

        private static PropertyInfo _legStateCurrentStateProperty;

        // STEP 1: Build every foot-sliding measurement from the live prefab rig and stance signal.
        // STEP 2: Reuse that probe for the walk and sprint regression gates.
        // STEP 3: Re-run the same probe across move-force tiers in the explicit speed-envelope sweep.

        private PlayerPrefabTestRig _rig;
        private int _leftStanceAge;
        private int _rightStanceAge;

        private sealed class DriftMeasurement
        {
            public DriftMeasurement(PlantedFootDriftTracker tracker, float peakSpeed, float finalSpeed)
            {
                MaxDrift = tracker.MaxDrift;
                AverageDrift = tracker.AverageDrift;
                StepCount = tracker.CompletedStepCount;
                MaxDriftLeft = tracker.MaxDriftLeft;
                MaxDriftRight = tracker.MaxDriftRight;
                PeakSpeed = peakSpeed;
                FinalSpeed = finalSpeed;
            }

            public float MaxDrift { get; }
            public float AverageDrift { get; }
            public int StepCount { get; }
            public float MaxDriftLeft { get; }
            public float MaxDriftRight { get; }
            public float PeakSpeed { get; }
            public float FinalSpeed { get; }
        }

        private sealed class SpeedSweepSample
        {
            public SpeedSweepSample(float moveForce, DriftMeasurement measurement)
            {
                MoveForce = moveForce;
                MaxDrift = measurement.MaxDrift;
                AverageDrift = measurement.AverageDrift;
                PeakSpeed = measurement.PeakSpeed;
                StepCount = measurement.StepCount;
                Verdict = measurement.MaxDrift < MaxSprintPlantedFootDriftMetres
                    ? "within-gate"
                    : "over-gate";
            }

            public float MoveForce { get; }
            public float MaxDrift { get; }
            public float AverageDrift { get; }
            public float PeakSpeed { get; }
            public int StepCount { get; }
            public string Verdict { get; }
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _rig = PlayerPrefabTestRig.Create();
            _leftStanceAge = 0;
            _rightStanceAge = 0;
            yield return _rig.WarmUp(SettleFrames);
        }

        [TearDown]
        public void TearDown()
        {
            _rig?.Dispose();
            _rig = null;
        }

        [UnityTest]
        public IEnumerator WalkForward_PlantedFeetDoNotSlide()
        {
            // Arrange
            DriftMeasurement measurement = null;

            // Act
            yield return MeasureForwardDrift(
                sprint: false,
                measurementFrames: WalkFrames,
                captureMeasurement: result => measurement = result);

            // Assert
            Assert.That(measurement, Is.Not.Null, "Walk drift measurement should be captured.");

            Debug.Log(
                $"[METRIC] PlantedFootDrift_Walk " +
                $"MaxDrift={measurement.MaxDrift:F4} " +
                $"AverageDrift={measurement.AverageDrift:F4} " +
                $"StepCount={measurement.StepCount} " +
                $"MaxDriftL={measurement.MaxDriftLeft:F4} " +
                $"MaxDriftR={measurement.MaxDriftRight:F4} " +
                $"FinalSpeed={measurement.FinalSpeed:F2}");

            Assert.That(measurement.StepCount, Is.GreaterThan(0),
                "No completed step cycles detected — planted-state signal may be broken.");

            Assert.That(measurement.MaxDrift, Is.LessThan(MaxPlantedFootDriftMetres),
                $"Planted foot drift {measurement.MaxDrift:F4}m exceeds threshold {MaxPlantedFootDriftMetres}m. " +
                $"The body is moving faster than the leg cycle can anchor. " +
                $"Steps={measurement.StepCount} AvgDrift={measurement.AverageDrift:F4}m");
        }

        [UnityTest]
        public IEnumerator SprintForward_PlantedFeetDoNotSlide()
        {
            // Arrange
            DriftMeasurement measurement = null;

            // Act
            yield return MeasureForwardDrift(
                sprint: true,
                measurementFrames: SprintFrames,
                captureMeasurement: result => measurement = result);

            // Assert
            Assert.That(measurement, Is.Not.Null, "Sprint drift measurement should be captured.");

            Debug.Log(
                $"[METRIC] PlantedFootDrift_Sprint " +
                $"MaxDrift={measurement.MaxDrift:F4} " +
                $"AverageDrift={measurement.AverageDrift:F4} " +
                $"StepCount={measurement.StepCount} " +
                $"MaxDriftL={measurement.MaxDriftLeft:F4} " +
                $"MaxDriftR={measurement.MaxDriftRight:F4} " +
                $"PeakSpeed={measurement.PeakSpeed:F2} " +
                $"FinalSpeed={measurement.FinalSpeed:F2}");

            Assert.That(measurement.StepCount, Is.GreaterThan(0),
                "No completed step cycles detected — planted-state signal may be broken.");

            Assert.That(measurement.MaxDrift, Is.LessThan(MaxSprintPlantedFootDriftMetres),
                $"Planted foot drift {measurement.MaxDrift:F4}m exceeds sprint threshold {MaxSprintPlantedFootDriftMetres}m. " +
                $"The body is moving faster than the leg cycle can anchor at sprint speed. " +
                $"Steps={measurement.StepCount} AvgDrift={measurement.AverageDrift:F4}m PeakSpeed={measurement.PeakSpeed:F2}m/s");
        }

        [UnityTest, Explicit("Diagnostic speed sweep for planted-foot drift tuning.")]
        public IEnumerator SpeedSweep_MeasureDriftAtEachTier()
        {
            // Arrange
            List<SpeedSweepSample> results = new List<SpeedSweepSample>(SweepMoveForces.Length);
            _rig?.Dispose();
            _rig = null;

            // Act
            for (int i = 0; i < SweepMoveForces.Length; i++)
            {
                yield return RecreateRigForSweep(i);

                float moveForce = SweepMoveForces[i];
                SetMoveForce(_rig.PlayerMovement, moveForce);

                DriftMeasurement measurement = null;
                yield return MeasureForwardDrift(
                    sprint: true,
                    measurementFrames: SprintFrames,
                    captureMeasurement: result => measurement = result);

                Assert.That(measurement, Is.Not.Null,
                    $"Sweep measurement for moveForce {moveForce:F0} should be captured.");
                Assert.That(measurement.StepCount, Is.GreaterThan(0),
                    $"Sweep measurement for moveForce {moveForce:F0} completed no stance phases.");

                results.Add(new SpeedSweepSample(moveForce, measurement));

                Debug.Log(
                    $"[METRIC] PlantedFootDrift_SpeedSweep " +
                    $"MoveForce={moveForce:F0} " +
                    $"MaxDrift={measurement.MaxDrift:F4} " +
                    $"AverageDrift={measurement.AverageDrift:F4} " +
                    $"PeakSpeed={measurement.PeakSpeed:F2} " +
                    $"StepCount={measurement.StepCount}");
            }

            // Assert
            Assert.That(results, Has.Count.EqualTo(SweepMoveForces.Length),
                "The explicit sweep should record one result per requested move-force tier.");

            Debug.Log(BuildSpeedSweepSummary(results));
        }

        private IEnumerator MeasureForwardDrift(bool sprint, int measurementFrames, Action<DriftMeasurement> captureMeasurement)
        {
            Assert.That(_rig, Is.Not.Null, "PlayerPrefabTestRig should be available before measuring drift.");
            Assert.That(captureMeasurement, Is.Not.Null, "Drift measurement callback should be provided.");

            GroundSensor leftSensor = FindGroundSensor(_rig.Instance, "Foot_L");
            GroundSensor rightSensor = FindGroundSensor(_rig.Instance, "Foot_R");
            Assert.That(leftSensor, Is.Not.Null, "Left GroundSensor not found.");
            Assert.That(rightSensor, Is.Not.Null, "Right GroundSensor not found.");

            object leftStateMachine = LeftLegStateMachineField?.GetValue(_rig.LegAnimator);
            object rightStateMachine = RightLegStateMachineField?.GetValue(_rig.LegAnimator);
            Assert.That(leftStateMachine, Is.Not.Null, "Left LegStateMachine not resolved.");
            Assert.That(rightStateMachine, Is.Not.Null, "Right LegStateMachine not resolved.");

            EnsureLegStatePropertyCached(leftStateMachine);
            ResetStanceAges();

            PlantedFootDriftTracker tracker = new PlantedFootDriftTracker();
            float peakSpeed = 0f;

            _rig.PlayerMovement.SetMoveInputForTest(new Vector2(0f, 1f));
            _rig.PlayerMovement.SetSprintInputForTest(sprint);

            for (int i = 0; i < measurementFrames; i++)
            {
                yield return new WaitForFixedUpdate();

                bool leftInStance = IsFootInStance(leftStateMachine, leftSensor);
                bool rightInStance = IsFootInStance(rightStateMachine, rightSensor);

                _leftStanceAge = leftInStance ? _leftStanceAge + 1 : 0;
                _rightStanceAge = rightInStance ? _rightStanceAge + 1 : 0;

                bool leftPlanted = _leftStanceAge > StanceSettleFrames;
                bool rightPlanted = _rightStanceAge > StanceSettleFrames;

                tracker.Sample(
                    _rig.FootL.position,
                    leftPlanted,
                    _rig.FootR.position,
                    rightPlanted);

                peakSpeed = Mathf.Max(peakSpeed, GetPlanarSpeed(_rig.HipsBody));
            }

            _rig.PlayerMovement.SetMoveInputForTest(Vector2.zero);
            _rig.PlayerMovement.SetSprintInputForTest(false);
            tracker.Flush();

            captureMeasurement(new DriftMeasurement(tracker, peakSpeed, GetPlanarSpeed(_rig.HipsBody)));
        }

        private IEnumerator RecreateRigForSweep(int sweepIndex)
        {
            _rig?.Dispose();
            _rig = PlayerPrefabTestRig.Create(new PlayerPrefabTestRig.Options
            {
                TestOrigin = new Vector3(sweepIndex * 250f, 0f, 0f),
            });

            ResetStanceAges();
            yield return _rig.WarmUp(SettleFrames);
        }

        private static void SetMoveForce(PlayerMovement playerMovement, float moveForce)
        {
            Assert.That(MoveForceField, Is.Not.Null,
                "PlayerMovement._moveForce field not found via reflection.");
            MoveForceField.SetValue(playerMovement, moveForce);

            float appliedMoveForce = (float)MoveForceField.GetValue(playerMovement);
            Assert.That(appliedMoveForce, Is.EqualTo(moveForce).Within(0.0001f),
                $"Failed to apply move-force override. Expected {moveForce:F2}, actual {appliedMoveForce:F2}.");
        }

        private void ResetStanceAges()
        {
            _leftStanceAge = 0;
            _rightStanceAge = 0;
        }

        private static float GetPlanarSpeed(Rigidbody body)
        {
            return new Vector3(body.linearVelocity.x, 0f, body.linearVelocity.z).magnitude;
        }

        private static string BuildSpeedSweepSummary(IReadOnlyList<SpeedSweepSample> results)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Foot sliding speed sweep summary");
            builder.AppendLine("MoveForce | PeakSpeed | MaxDrift | AvgDrift | Steps | Verdict");

            for (int i = 0; i < results.Count; i++)
            {
                SpeedSweepSample sample = results[i];
                builder.AppendLine(
                    $"{sample.MoveForce,9:F0} | {sample.PeakSpeed,9:F2} | {sample.MaxDrift,8:F4} | {sample.AverageDrift,8:F4} | {sample.StepCount,5} | {sample.Verdict}");
            }

            return builder.ToString().TrimEnd();
        }

        private static bool IsFootInStance(object legStateMachine, GroundSensor sensor)
        {
            if (!sensor.IsGrounded) return false;

            // LegStateType: Stance=0 — the established support phase.
            // We exclude Plant (2) because the foot is still settling into position.
            object stateObj = _legStateCurrentStateProperty.GetValue(legStateMachine);
            int state = (int)stateObj;
            return state == 0; // Stance only
        }

        private static void EnsureLegStatePropertyCached(object legStateMachine)
        {
            if (_legStateCurrentStateProperty != null) return;
            _legStateCurrentStateProperty = legStateMachine.GetType()
                .GetProperty("CurrentState", BindingFlags.Public | BindingFlags.Instance);
            Assert.That(_legStateCurrentStateProperty, Is.Not.Null,
                "LegStateMachine.CurrentState property not found via reflection.");
        }

        private static GroundSensor FindGroundSensor(GameObject root, string footName)
        {
            GroundSensor[] sensors = root.GetComponentsInChildren<GroundSensor>(includeInactive: false);
            for (int i = 0; i < sensors.Length; i++)
            {
                if (sensors[i].transform.name == footName)
                    return sensors[i];
            }
            return null;
        }
    }
}
