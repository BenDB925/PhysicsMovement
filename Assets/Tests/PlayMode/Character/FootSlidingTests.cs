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
        private static readonly float[] SweepStepFrequencyScales = { 0.10f, 0.12f, 0.15f, 0.18f, 0.20f };
        private static readonly float[] SweepMaxStrideLengths = { 0.25f, 0.30f, 0.35f, 0.40f };

        private const float StepFrequencySweepMoveForce = 150f;
        private const float StepFrequencySweepSprintMultiplier = 1.8f;
        private const float MaxStrideSweepMoveForce = 150f;
        private const float MaxStrideSweepSprintMultiplier = 1.8f;
        private const float MaxStrideSweepStepFrequencyScale = 0.15f;
        private const float MaxStrideSweepBaselineLength = 0.30f;
        private const int ComparisonTrialCount = 3;

        private static readonly EnvelopeTuningProfile LockedBaselineEnvelope =
            new EnvelopeTuningProfile("baseline", 150f, 1.8f, 0.10f, 0.30f);

        private static readonly EnvelopeTuningProfile CandidateEnvelope =
            new EnvelopeTuningProfile("candidate", 150f, 1.8f, 0.15f, 0.35f);

        /// <summary>
        /// Confirmed walk-speed regression gate from WP3a for the honest envelope.
        /// The user-preferred walk baseline measures 0.3076m peak drift at
        /// _moveForce = 150, so 0.35m keeps a small tolerance for physics variance
        /// without widening the accepted envelope.
        /// </summary>
        private const float MaxWalkPlantedFootDriftMetres = 0.35f;

        /// <summary>
        /// Confirmed sprint-speed regression gate from WP3a for the locked
        /// 150 / 1.8 / 0.10 / 0.30 envelope. The user-preferred sprint baseline stays
        /// within this threshold, while the speed sweep crosses the drift knee at
        /// _moveForce = 200 and above.
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

        private static readonly FieldInfo SprintSpeedMultiplierField = typeof(PlayerMovement)
            .GetField("_sprintSpeedMultiplier", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo StepFrequencyScaleField = typeof(LegAnimator)
            .GetField("_stepFrequencyScale", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo StepPlannerField = typeof(LegAnimator)
            .GetField("_stepPlanner", BindingFlags.NonPublic | BindingFlags.Instance);

        private static PropertyInfo _legStateCurrentStateProperty;

        // STEP 1: Build every foot-sliding measurement from the live prefab rig and stance signal.
        // STEP 2: Reuse that probe for the locked walk and sprint regression gates.
        // STEP 3: Re-run the same probe across move-force, cadence, and stride-length tiers in explicit tuning sweeps.
        // STEP 4: Compare the locked baseline against candidate tuning in repeated walk and sprint trials before promoting defaults.

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

        private sealed class StepFrequencySweepSample
        {
            public StepFrequencySweepSample(float stepFrequencyScale, DriftMeasurement measurement)
            {
                StepFrequencyScale = stepFrequencyScale;
                MaxDrift = measurement.MaxDrift;
                AverageDrift = measurement.AverageDrift;
                PeakSpeed = measurement.PeakSpeed;
                StepCount = measurement.StepCount;
                Verdict = measurement.MaxDrift < MaxSprintPlantedFootDriftMetres
                    ? "within-gate"
                    : "over-gate";
            }

            public float StepFrequencyScale { get; }
            public float MaxDrift { get; }
            public float AverageDrift { get; }
            public float PeakSpeed { get; }
            public int StepCount { get; }
            public string Verdict { get; }
        }

        private sealed class MaxStrideSweepSample
        {
            public MaxStrideSweepSample(float maxStrideLength, DriftMeasurement measurement)
            {
                MaxStrideLength = maxStrideLength;
                MaxDrift = measurement.MaxDrift;
                AverageDrift = measurement.AverageDrift;
                PeakSpeed = measurement.PeakSpeed;
                StepCount = measurement.StepCount;
                Verdict = measurement.MaxDrift < MaxSprintPlantedFootDriftMetres
                    ? "within-gate"
                    : "over-gate";
            }

            public float MaxStrideLength { get; }
            public float MaxDrift { get; }
            public float AverageDrift { get; }
            public float PeakSpeed { get; }
            public int StepCount { get; }
            public string Verdict { get; }
        }

        private sealed class EnvelopeTuningProfile
        {
            public EnvelopeTuningProfile(
                string name,
                float moveForce,
                float sprintSpeedMultiplier,
                float stepFrequencyScale,
                float maxStrideLength)
            {
                Name = name;
                MoveForce = moveForce;
                SprintSpeedMultiplier = sprintSpeedMultiplier;
                StepFrequencyScale = stepFrequencyScale;
                MaxStrideLength = maxStrideLength;
            }

            public string Name { get; }
            public float MoveForce { get; }
            public float SprintSpeedMultiplier { get; }
            public float StepFrequencyScale { get; }
            public float MaxStrideLength { get; }

            public string Label =>
                $"{MoveForce:F0}/{SprintSpeedMultiplier:F1}/{StepFrequencyScale:F2}/{MaxStrideLength:F2}";
        }

        private sealed class EnvelopeComparisonSummary
        {
            public EnvelopeComparisonSummary(
                EnvelopeTuningProfile profile,
                bool sprint,
                IReadOnlyList<DriftMeasurement> measurements)
            {
                Assert.That(profile, Is.Not.Null, "Envelope profile should be provided.");
                Assert.That(measurements, Is.Not.Null, "Envelope measurements should be provided.");
                Assert.That(measurements.Count, Is.GreaterThan(0), "Envelope comparison needs at least one measurement.");

                Profile = profile;
                Sprint = sprint;
                TrialCount = measurements.Count;
                GateThreshold = sprint ? MaxSprintPlantedFootDriftMetres : MaxWalkPlantedFootDriftMetres;

                float totalMaxDrift = 0f;
                float totalAverageDrift = 0f;
                float totalPeakSpeed = 0f;
                float totalFinalSpeed = 0f;
                float totalStepCount = 0f;
                float minMaxDrift = float.MaxValue;
                float maxMaxDrift = float.MinValue;
                float minPeakSpeed = float.MaxValue;
                float maxPeakSpeed = float.MinValue;
                bool allTrialsWithinGate = true;

                for (int i = 0; i < measurements.Count; i++)
                {
                    DriftMeasurement measurement = measurements[i];
                    totalMaxDrift += measurement.MaxDrift;
                    totalAverageDrift += measurement.AverageDrift;
                    totalPeakSpeed += measurement.PeakSpeed;
                    totalFinalSpeed += measurement.FinalSpeed;
                    totalStepCount += measurement.StepCount;
                    minMaxDrift = Mathf.Min(minMaxDrift, measurement.MaxDrift);
                    maxMaxDrift = Mathf.Max(maxMaxDrift, measurement.MaxDrift);
                    minPeakSpeed = Mathf.Min(minPeakSpeed, measurement.PeakSpeed);
                    maxPeakSpeed = Mathf.Max(maxPeakSpeed, measurement.PeakSpeed);
                    allTrialsWithinGate &= measurement.MaxDrift < GateThreshold;
                }

                AverageMaxDrift = totalMaxDrift / measurements.Count;
                AverageAverageDrift = totalAverageDrift / measurements.Count;
                AveragePeakSpeed = totalPeakSpeed / measurements.Count;
                AverageFinalSpeed = totalFinalSpeed / measurements.Count;
                AverageStepCount = totalStepCount / measurements.Count;
                MinMaxDrift = minMaxDrift;
                MaxMaxDrift = maxMaxDrift;
                DriftRange = maxMaxDrift - minMaxDrift;
                MinPeakSpeed = minPeakSpeed;
                MaxPeakSpeed = maxPeakSpeed;
                PeakSpeedRange = maxPeakSpeed - minPeakSpeed;
                AllTrialsWithinGate = allTrialsWithinGate;
                ConsistencyTag = BuildConsistencyTag(allTrialsWithinGate, DriftRange, PeakSpeedRange);
            }

            public EnvelopeTuningProfile Profile { get; }
            public bool Sprint { get; }
            public int TrialCount { get; }
            public float GateThreshold { get; }
            public float AverageMaxDrift { get; }
            public float AverageAverageDrift { get; }
            public float AveragePeakSpeed { get; }
            public float AverageFinalSpeed { get; }
            public float AverageStepCount { get; }
            public float MinMaxDrift { get; }
            public float MaxMaxDrift { get; }
            public float DriftRange { get; }
            public float MinPeakSpeed { get; }
            public float MaxPeakSpeed { get; }
            public float PeakSpeedRange { get; }
            public bool AllTrialsWithinGate { get; }
            public string ConsistencyTag { get; }

            public string ModeLabel => Sprint ? "Sprint" : "Walk";

            private static string BuildConsistencyTag(bool allTrialsWithinGate, float driftRange, float peakSpeedRange)
            {
                if (!allTrialsWithinGate)
                {
                    return "gate-breach";
                }

                if (driftRange <= 0.05f && peakSpeedRange <= 0.20f)
                {
                    return "stable";
                }

                if (driftRange <= 0.12f && peakSpeedRange <= 0.35f)
                {
                    return "moderate-variance";
                }

                return "high-variance";
            }
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

            Assert.That(measurement.MaxDrift, Is.LessThan(MaxWalkPlantedFootDriftMetres),
                $"Planted foot drift {measurement.MaxDrift:F4}m exceeds walk threshold {MaxWalkPlantedFootDriftMetres}m. " +
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
                yield return RecreateRigForSweep();

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

        [UnityTest, Explicit("Diagnostic step-frequency sweep for planted-foot drift tuning.")]
        public IEnumerator StepFrequencySweep_MeasureDriftAtEachTier()
        {
            // Arrange
            List<StepFrequencySweepSample> results = new List<StepFrequencySweepSample>(SweepStepFrequencyScales.Length);
            _rig?.Dispose();
            _rig = null;

            // Act
            for (int i = 0; i < SweepStepFrequencyScales.Length; i++)
            {
                yield return RecreateRigForSweep();

                float stepFrequencyScale = SweepStepFrequencyScales[i];
                SetMoveForce(_rig.PlayerMovement, StepFrequencySweepMoveForce);
                SetSprintSpeedMultiplier(_rig.PlayerMovement, StepFrequencySweepSprintMultiplier);
                SetStepFrequencyScale(_rig.LegAnimator, stepFrequencyScale);

                DriftMeasurement measurement = null;
                yield return MeasureForwardDrift(
                    sprint: true,
                    measurementFrames: SprintFrames,
                    captureMeasurement: result => measurement = result);

                Assert.That(measurement, Is.Not.Null,
                    $"Sweep measurement for stepFrequencyScale {stepFrequencyScale:F2} should be captured.");
                Assert.That(measurement.StepCount, Is.GreaterThan(0),
                    $"Sweep measurement for stepFrequencyScale {stepFrequencyScale:F2} completed no stance phases.");

                results.Add(new StepFrequencySweepSample(stepFrequencyScale, measurement));

                Debug.Log(
                    $"[METRIC] PlantedFootDrift_StepFrequencySweep " +
                    $"StepFrequencyScale={stepFrequencyScale:F2} " +
                    $"MaxDrift={measurement.MaxDrift:F4} " +
                    $"AverageDrift={measurement.AverageDrift:F4} " +
                    $"PeakSpeed={measurement.PeakSpeed:F2} " +
                    $"StepCount={measurement.StepCount}");
            }

            // Assert
            Assert.That(results, Has.Count.EqualTo(SweepStepFrequencyScales.Length),
                "The explicit sweep should record one result per requested step-frequency tier.");

            Debug.Log(BuildStepFrequencySweepSummary(results));
        }

        [UnityTest, Explicit("Diagnostic max-stride sweep for planted-foot drift tuning.")]
        public IEnumerator MaxStrideLengthSweep_MeasureDriftAtEachTier()
        {
            // Arrange
            List<MaxStrideSweepSample> results = new List<MaxStrideSweepSample>(SweepMaxStrideLengths.Length);
            _rig?.Dispose();
            _rig = null;

            // Act
            for (int i = 0; i < SweepMaxStrideLengths.Length; i++)
            {
                yield return RecreateRigForSweep();

                float maxStrideLength = SweepMaxStrideLengths[i];
                SetMoveForce(_rig.PlayerMovement, MaxStrideSweepMoveForce);
                SetSprintSpeedMultiplier(_rig.PlayerMovement, MaxStrideSweepSprintMultiplier);
                SetStepFrequencyScale(_rig.LegAnimator, MaxStrideSweepStepFrequencyScale);
                SetMaxStrideLength(_rig.LegAnimator, maxStrideLength);

                DriftMeasurement measurement = null;
                yield return MeasureForwardDrift(
                    sprint: true,
                    measurementFrames: SprintFrames,
                    captureMeasurement: result => measurement = result);

                Assert.That(measurement, Is.Not.Null,
                    $"Sweep measurement for maxStrideLength {maxStrideLength:F2} should be captured.");
                Assert.That(measurement.StepCount, Is.GreaterThan(0),
                    $"Sweep measurement for maxStrideLength {maxStrideLength:F2} completed no stance phases.");

                results.Add(new MaxStrideSweepSample(maxStrideLength, measurement));

                Debug.Log(
                    $"[METRIC] PlantedFootDrift_MaxStrideSweep " +
                    $"StepFrequencyScale={MaxStrideSweepStepFrequencyScale:F2} " +
                    $"MaxStrideLength={maxStrideLength:F2} " +
                    $"MaxDrift={measurement.MaxDrift:F4} " +
                    $"AverageDrift={measurement.AverageDrift:F4} " +
                    $"PeakSpeed={measurement.PeakSpeed:F2} " +
                    $"StepCount={measurement.StepCount}");
            }

            // Assert
            Assert.That(results, Has.Count.EqualTo(SweepMaxStrideLengths.Length),
                "The explicit sweep should record one result per requested max-stride tier.");

            Debug.Log(BuildMaxStrideSweepSummary(results));
        }

        [UnityTest, Explicit("Diagnostic repeated comparison for the locked baseline versus the WP4 candidate envelope.")]
        public IEnumerator EnvelopeComparison_BaselineAndCandidate_RepeatWalkAndSprintTrials()
        {
            // Arrange
            List<EnvelopeComparisonSummary> summaries = new List<EnvelopeComparisonSummary>(4);
            _rig?.Dispose();
            _rig = null;

            // Act
            yield return RunEnvelopeTrials(LockedBaselineEnvelope, sprint: false, summary => summaries.Add(summary));
            yield return RunEnvelopeTrials(LockedBaselineEnvelope, sprint: true, summary => summaries.Add(summary));
            yield return RunEnvelopeTrials(CandidateEnvelope, sprint: false, summary => summaries.Add(summary));
            yield return RunEnvelopeTrials(CandidateEnvelope, sprint: true, summary => summaries.Add(summary));

            // Assert
            Assert.That(summaries, Has.Count.EqualTo(4),
                "The envelope comparison should capture baseline and candidate summaries for both walk and sprint.");

            Debug.Log(BuildEnvelopeComparisonSummary(summaries));
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

        private IEnumerator RunEnvelopeTrials(
            EnvelopeTuningProfile profile,
            bool sprint,
            Action<EnvelopeComparisonSummary> captureSummary)
        {
            Assert.That(profile, Is.Not.Null, "Envelope tuning profile should be provided.");
            Assert.That(captureSummary, Is.Not.Null, "Envelope comparison summary callback should be provided.");

            List<DriftMeasurement> measurements = new List<DriftMeasurement>(ComparisonTrialCount);
            int measurementFrames = sprint ? SprintFrames : WalkFrames;

            for (int trialIndex = 0; trialIndex < ComparisonTrialCount; trialIndex++)
            {
                yield return RecreateRigForSweep();
                ApplyEnvelopeProfile(_rig, profile);

                DriftMeasurement measurement = null;
                yield return MeasureForwardDrift(
                    sprint,
                    measurementFrames,
                    captureMeasurement: result => measurement = result);

                Assert.That(measurement, Is.Not.Null,
                    $"Envelope comparison measurement should be captured for profile {profile.Label}, " +
                    $"mode {(sprint ? "Sprint" : "Walk")}, trial {trialIndex + 1}.");
                Assert.That(measurement.StepCount, Is.GreaterThan(0),
                    $"Envelope comparison completed no stance phases for profile {profile.Label}, " +
                    $"mode {(sprint ? "Sprint" : "Walk")}, trial {trialIndex + 1}.");

                measurements.Add(measurement);

                Debug.Log(
                    $"[METRIC] PlantedFootDrift_EnvelopeTrial " +
                    $"Profile={profile.Name} " +
                    $"Mode={(sprint ? "Sprint" : "Walk")} " +
                    $"Trial={trialIndex + 1} " +
                    $"MoveForce={profile.MoveForce:F0} " +
                    $"SprintMultiplier={profile.SprintSpeedMultiplier:F2} " +
                    $"StepFrequencyScale={profile.StepFrequencyScale:F2} " +
                    $"MaxStrideLength={profile.MaxStrideLength:F2} " +
                    $"MaxDrift={measurement.MaxDrift:F4} " +
                    $"AverageDrift={measurement.AverageDrift:F4} " +
                    $"PeakSpeed={measurement.PeakSpeed:F2} " +
                    $"FinalSpeed={measurement.FinalSpeed:F2} " +
                    $"StepCount={measurement.StepCount}");
            }

            EnvelopeComparisonSummary summary = new EnvelopeComparisonSummary(profile, sprint, measurements);

            Debug.Log(
                $"[METRIC] PlantedFootDrift_EnvelopeSummary " +
                $"Profile={profile.Name} " +
                $"Mode={summary.ModeLabel} " +
                $"Trials={summary.TrialCount} " +
                $"AvgMaxDrift={summary.AverageMaxDrift:F4} " +
                $"DriftRange={summary.DriftRange:F4} " +
                $"AvgPeakSpeed={summary.AveragePeakSpeed:F2} " +
                $"PeakSpeedRange={summary.PeakSpeedRange:F2} " +
                $"AvgFinalSpeed={summary.AverageFinalSpeed:F2} " +
                $"AvgStepCount={summary.AverageStepCount:F1} " +
                $"GateVerdict={(summary.AllTrialsWithinGate ? "within-gate" : "gate-breach")} " +
                $"Consistency={summary.ConsistencyTag}");

            captureSummary(summary);
        }

        private IEnumerator RecreateRigForSweep()
        {
            _rig?.Dispose();
            _rig = PlayerPrefabTestRig.Create(new PlayerPrefabTestRig.Options
            {
                // Scene isolation already gives each measurement a fresh empty scene, so
                // keep every tuning trial at the same origin and avoid position-driven drift variance.
                TestOrigin = Vector3.zero,
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

        private static void SetSprintSpeedMultiplier(PlayerMovement playerMovement, float sprintSpeedMultiplier)
        {
            Assert.That(SprintSpeedMultiplierField, Is.Not.Null,
                "PlayerMovement._sprintSpeedMultiplier field not found via reflection.");
            SprintSpeedMultiplierField.SetValue(playerMovement, sprintSpeedMultiplier);

            float appliedSprintSpeedMultiplier = (float)SprintSpeedMultiplierField.GetValue(playerMovement);
            Assert.That(appliedSprintSpeedMultiplier, Is.EqualTo(sprintSpeedMultiplier).Within(0.0001f),
                $"Failed to apply sprint-speed multiplier override. Expected {sprintSpeedMultiplier:F2}, actual {appliedSprintSpeedMultiplier:F2}.");
        }

        private static void SetStepFrequencyScale(LegAnimator legAnimator, float stepFrequencyScale)
        {
            Assert.That(StepFrequencyScaleField, Is.Not.Null,
                "LegAnimator._stepFrequencyScale field not found via reflection.");
            StepFrequencyScaleField.SetValue(legAnimator, stepFrequencyScale);

            float appliedStepFrequencyScale = (float)StepFrequencyScaleField.GetValue(legAnimator);
            Assert.That(appliedStepFrequencyScale, Is.EqualTo(stepFrequencyScale).Within(0.0001f),
                $"Failed to apply step-frequency scale override. Expected {stepFrequencyScale:F2}, actual {appliedStepFrequencyScale:F2}.");
        }

        private static void SetMaxStrideLength(LegAnimator legAnimator, float maxStrideLength)
        {
            Assert.That(StepPlannerField, Is.Not.Null,
                "LegAnimator._stepPlanner field not found via reflection.");

            object stepPlanner = StepPlannerField.GetValue(legAnimator);
            Assert.That(stepPlanner, Is.Not.Null,
                "LegAnimator._stepPlanner instance not found via reflection.");

            MethodInfo setter = stepPlanner.GetType().GetMethod(
                "SetMaxStrideLengthForTesting",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            Assert.That(setter, Is.Not.Null,
                "StepPlanner.SetMaxStrideLengthForTesting not found via reflection.");

            setter.Invoke(stepPlanner, new object[] { maxStrideLength });

            FieldInfo maxStrideLengthField = stepPlanner.GetType().GetField(
                "_maxStrideLength",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(maxStrideLengthField, Is.Not.Null,
                "StepPlanner._maxStrideLength field not found via reflection.");

            float appliedMaxStrideLength = (float)maxStrideLengthField.GetValue(stepPlanner);
            Assert.That(appliedMaxStrideLength, Is.EqualTo(maxStrideLength).Within(0.0001f),
                $"Failed to apply max-stride override. Expected {maxStrideLength:F2}, actual {appliedMaxStrideLength:F2}.");
        }

        private static void ApplyEnvelopeProfile(PlayerPrefabTestRig rig, EnvelopeTuningProfile profile)
        {
            Assert.That(rig, Is.Not.Null, "PlayerPrefabTestRig should exist before applying an envelope profile.");
            Assert.That(profile, Is.Not.Null, "Envelope tuning profile should be provided.");

            SetMoveForce(rig.PlayerMovement, profile.MoveForce);
            SetSprintSpeedMultiplier(rig.PlayerMovement, profile.SprintSpeedMultiplier);
            SetStepFrequencyScale(rig.LegAnimator, profile.StepFrequencyScale);
            SetMaxStrideLength(rig.LegAnimator, profile.MaxStrideLength);
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

        private static string BuildStepFrequencySweepSummary(IReadOnlyList<StepFrequencySweepSample> results)
        {
            StringBuilder builder = new StringBuilder();
            StepFrequencySweepSample baseline = results[0];
            builder.AppendLine("Foot sliding step-frequency sweep summary");
            builder.AppendLine("StepFreq | PeakSpeed | MaxDrift | AvgDrift | Steps | Verdict | InferredVisualNote");

            for (int i = 0; i < results.Count; i++)
            {
                StepFrequencySweepSample sample = results[i];
                string inferredVisualNote = InferVisualNote(sample, baseline);
                builder.AppendLine(
                    $"{sample.StepFrequencyScale,8:F2} | {sample.PeakSpeed,9:F2} | {sample.MaxDrift,8:F4} | {sample.AverageDrift,8:F4} | {sample.StepCount,5} | {sample.Verdict,-11} | {inferredVisualNote}");
            }

            return builder.ToString().TrimEnd();
        }

        private static string BuildMaxStrideSweepSummary(IReadOnlyList<MaxStrideSweepSample> results)
        {
            StringBuilder builder = new StringBuilder();
            MaxStrideSweepSample baseline = FindStrideSweepBaseline(results);
            builder.AppendLine("Foot sliding max-stride sweep summary");
            builder.AppendLine("MaxStride | PeakSpeed | MaxDrift | AvgDrift | Steps | Verdict | InferredVisualNote");

            for (int i = 0; i < results.Count; i++)
            {
                MaxStrideSweepSample sample = results[i];
                string inferredVisualNote = InferStrideVisualNote(sample, baseline);
                builder.AppendLine(
                    $"{sample.MaxStrideLength,9:F2} | {sample.PeakSpeed,9:F2} | {sample.MaxDrift,8:F4} | {sample.AverageDrift,8:F4} | {sample.StepCount,5} | {sample.Verdict,-11} | {inferredVisualNote}");
            }

            return builder.ToString().TrimEnd();
        }

        private static string BuildEnvelopeComparisonSummary(IReadOnlyList<EnvelopeComparisonSummary> summaries)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Foot sliding envelope comparison summary");
            builder.AppendLine("Mode | Profile | AvgMaxDrift | DriftRange | AvgPeakSpeed | PeakSpeedRange | AvgFinalSpeed | AvgSteps | GateVerdict | Consistency | DeltaVsBaseline");

            for (int i = 0; i < summaries.Count; i++)
            {
                EnvelopeComparisonSummary summary = summaries[i];
                EnvelopeComparisonSummary baseline = FindEnvelopeComparisonBaseline(summaries, summary.Sprint);
                string deltaSummary = BuildEnvelopeDeltaSummary(summary, baseline);

                builder.AppendLine(
                    $"{summary.ModeLabel,5} | {summary.Profile.Label,18} | {summary.AverageMaxDrift,11:F4} | {summary.DriftRange,10:F4} | {summary.AveragePeakSpeed,12:F2} | {summary.PeakSpeedRange,14:F2} | {summary.AverageFinalSpeed,13:F2} | {summary.AverageStepCount,8:F1} | {(summary.AllTrialsWithinGate ? "within-gate" : "gate-breach"),11} | {summary.ConsistencyTag,17} | {deltaSummary}");
            }

            return builder.ToString().TrimEnd();
        }

        private static EnvelopeComparisonSummary FindEnvelopeComparisonBaseline(
            IReadOnlyList<EnvelopeComparisonSummary> summaries,
            bool sprint)
        {
            for (int i = 0; i < summaries.Count; i++)
            {
                if (summaries[i].Sprint == sprint && summaries[i].Profile.Name == LockedBaselineEnvelope.Name)
                {
                    return summaries[i];
                }
            }

            return null;
        }

        private static string BuildEnvelopeDeltaSummary(
            EnvelopeComparisonSummary summary,
            EnvelopeComparisonSummary baseline)
        {
            if (baseline == null || ReferenceEquals(summary, baseline))
            {
                return "baseline reference";
            }

            float driftDelta = summary.AverageMaxDrift - baseline.AverageMaxDrift;
            float peakSpeedDelta = summary.AveragePeakSpeed - baseline.AveragePeakSpeed;
            return $"dDrift={driftDelta:+0.0000;-0.0000;0.0000} dSpeed={peakSpeedDelta:+0.00;-0.00;0.00}";
        }

        private static string InferVisualNote(StepFrequencySweepSample sample, StepFrequencySweepSample baseline)
        {
            if (Mathf.Abs(sample.StepFrequencyScale - baseline.StepFrequencyScale) < 0.0001f)
            {
                return "baseline cadence reference";
            }

            float driftDelta = sample.MaxDrift - baseline.MaxDrift;
            float stepCountDeltaRatio = baseline.StepCount > 0
                ? (sample.StepCount - baseline.StepCount) / (float)baseline.StepCount
                : 0f;

            if (driftDelta <= -0.05f && stepCountDeltaRatio <= 0.20f)
            {
                return "inferred: lower drift, modest cadence rise";
            }

            if (driftDelta <= -0.05f)
            {
                return "inferred: lower drift, faster cadence";
            }

            if (driftDelta >= 0.05f)
            {
                return "inferred: worse drift than baseline";
            }

            if (stepCountDeltaRatio >= 0.35f)
            {
                return "inferred: cadence jump, no clear drift win";
            }

            return "inferred: near-baseline; manual review later";
        }

        private static MaxStrideSweepSample FindStrideSweepBaseline(IReadOnlyList<MaxStrideSweepSample> results)
        {
            for (int i = 0; i < results.Count; i++)
            {
                if (Mathf.Abs(results[i].MaxStrideLength - MaxStrideSweepBaselineLength) < 0.0001f)
                {
                    return results[i];
                }
            }

            return results[0];
        }

        private static string InferStrideVisualNote(MaxStrideSweepSample sample, MaxStrideSweepSample baseline)
        {
            if (Mathf.Abs(sample.MaxStrideLength - baseline.MaxStrideLength) < 0.0001f)
            {
                return "baseline stride reference";
            }

            float strideDelta = sample.MaxStrideLength - baseline.MaxStrideLength;
            float driftDelta = sample.MaxDrift - baseline.MaxDrift;
            float speedDelta = sample.PeakSpeed - baseline.PeakSpeed;

            if (driftDelta <= -0.05f && speedDelta >= -0.05f)
            {
                return strideDelta >= 0f
                    ? "inferred: longer reach, cleaner support"
                    : "inferred: tighter stride, cleaner support";
            }

            if (driftDelta <= -0.05f)
            {
                return strideDelta >= 0f
                    ? "inferred: longer reach, slower envelope"
                    : "inferred: tighter stride, slower envelope";
            }

            if (driftDelta >= 0.05f && speedDelta >= 0.10f)
            {
                return strideDelta >= 0f
                    ? "inferred: longer reach, more slip"
                    : "inferred: tighter stride, more slip";
            }

            if (driftDelta >= 0.05f)
            {
                return strideDelta >= 0f
                    ? "inferred: worse drift than baseline"
                    : "inferred: tighter stride, worse drift";
            }

            if (speedDelta >= 0.10f)
            {
                return strideDelta >= 0f
                    ? "inferred: longer reach, modest speed gain"
                    : "inferred: tighter stride, modest speed gain";
            }

            return "inferred: near-baseline; manual review later";
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
