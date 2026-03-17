using System;
using System.Reflection;
using PhysicsDrivenMovement.Character;
using UnityEngine;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// Immutable data for a canonical locomotion scenario used across tests,
    /// telemetry capture, and baseline comparison.
    /// </summary>
    public readonly struct ScenarioDefinition
    {
        /// <summary>
        /// Creates a named locomotion scenario definition.
        /// </summary>
        public ScenarioDefinition(
            string name,
            Vector3[] waypoints,
            float expectedDurationSeconds,
            string[] exercisedSubsystems)
        {
            // STEP 1: Reject incomplete definitions so the shared catalog cannot publish invalid data.
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Scenario name must be non-empty.", nameof(name));
            }

            if (waypoints == null || waypoints.Length == 0)
            {
                throw new ArgumentException("Scenario waypoints must be non-empty.", nameof(waypoints));
            }

            if (expectedDurationSeconds <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(expectedDurationSeconds),
                    "Scenario duration budget must be positive.");
            }

            if (exercisedSubsystems == null || exercisedSubsystems.Length == 0)
            {
                throw new ArgumentException("Scenario subsystems must be non-empty.", nameof(exercisedSubsystems));
            }

            // STEP 2: Persist the scenario payload exactly as authored so later slices can share one source of truth.
            Name = name;
            Waypoints = waypoints;
            ExpectedDurationSeconds = expectedDurationSeconds;
            ExercisedSubsystems = exercisedSubsystems;
        }

        /// <summary>
        /// Stable scenario name used by tests, baselines, and telemetry.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Canonical waypoint or path-anchor sequence for the scenario.
        /// </summary>
        public Vector3[] Waypoints { get; }

        /// <summary>
        /// Expected duration budget for the scenario in seconds.
        /// </summary>
        public float ExpectedDurationSeconds { get; }

        /// <summary>
        /// High-level subsystems that the scenario exercises.
        /// </summary>
        public string[] ExercisedSubsystems { get; }
    }

    /// <summary>
    /// Canonical locomotion scenarios shared by Chapter 9 validation work.
    /// </summary>
    public static class ScenarioDefinitions
    {
        private const float TerrainLaneProbeInset = 0.07f;
        private const float StartStopDurationSeconds = 4f;
        private const float ReversalDurationSeconds = 6f;
        private const float HardTurnDurationSeconds = 10f;
        private const float SlalomDurationSeconds = 30f;
        private const float StumbleRecoveryDurationSeconds = 5f;
        private const float TerrainStepUpDurationSeconds = 7f;
        private const float TerrainSlopeDurationSeconds = 5f;
        private const float LapCircuitDurationSeconds = 40f;

        private static readonly Vector3[] LapCircuitWaypoints = GetLapCircuitWaypoints();

        public static readonly ScenarioDefinition StartStop = new ScenarioDefinition(
            "StartStop",
            new[]
            {
                Vector3.zero,
                new Vector3(0f, 0f, 8f),
            },
            StartStopDurationSeconds,
            new[] { "gait", "balance" });

        public static readonly ScenarioDefinition Reversal = new ScenarioDefinition(
            "Reversal",
            new[]
            {
                new Vector3(0f, 0f, 10f),
                Vector3.zero,
                new Vector3(0f, 0f, 10f),
            },
            ReversalDurationSeconds,
            new[] { "gait", "recovery", "turning" });

        public static readonly ScenarioDefinition HardTurn90 = new ScenarioDefinition(
            "HardTurn90",
            new[]
            {
                new Vector3(0f, 0f, 10f),
                new Vector3(10f, 0f, 10f),
            },
            HardTurnDurationSeconds,
            new[] { "recovery", "turning", "balance" });

        public static readonly ScenarioDefinition Slalom5 = new ScenarioDefinition(
            "Slalom5",
            new[]
            {
                new Vector3(10f, 0f, 0f),
                new Vector3(10f, 0f, -10f),
                new Vector3(0f, 0f, -10f),
                Vector3.zero,
                new Vector3(10f, 0f, 0f),
            },
            SlalomDurationSeconds,
            new[] { "recovery", "turning", "gait" });

        public static readonly ScenarioDefinition StumbleRecovery = new ScenarioDefinition(
            "StumbleRecovery",
            new[]
            {
                new Vector3(0f, 0f, 8f),
                new Vector3(8f, 0f, 8f),
                new Vector3(8f, 0f, 14f),
            },
            StumbleRecoveryDurationSeconds,
            new[] { "recovery", "gait", "balance" });

        public static readonly ScenarioDefinition TerrainStepUp = new ScenarioDefinition(
            "TerrainStepUp",
            BuildArenaTerrainLaneWaypoints(
                center: new Vector3(0f, 0f, 18f),
                totalLength: 8.2f,
                spawnRunUp: 0.37f,
                endClearance: 0.08f),
            TerrainStepUpDurationSeconds,
            new[] { "terrain", "gait", "recovery" });

        public static readonly ScenarioDefinition TerrainSlope = new ScenarioDefinition(
            "TerrainSlope",
            BuildArenaTerrainLaneWaypoints(
                center: new Vector3(-22f, 0f, 18f),
                totalLength: 9f,
                spawnRunUp: 0.34f,
                endClearance: 0f),
            TerrainSlopeDurationSeconds,
            new[] { "terrain", "gait", "balance" });

        public static readonly ScenarioDefinition LapCircuit = new ScenarioDefinition(
            "LapCircuit",
            LapCircuitWaypoints,
            LapCircuitDurationSeconds,
            new[] { "gait", "recovery", "turning", "balance" });

        public static readonly ScenarioDefinition LongRunFatigue = new ScenarioDefinition(
            "LongRunFatigue",
            BuildRepeatedWaypoints(LapCircuitWaypoints, 3),
            LapCircuitDurationSeconds * 3f,
            new[] { "gait", "recovery", "endurance", "telemetry" });

        /// <summary>
        /// Ordered list of all canonical Chapter 9 scenarios.
        /// </summary>
        public static ScenarioDefinition[] All { get; } =
        {
            StartStop,
            Reversal,
            HardTurn90,
            Slalom5,
            StumbleRecovery,
            TerrainStepUp,
            TerrainSlope,
            LapCircuit,
            LongRunFatigue,
        };

        private static Vector3[] BuildArenaTerrainLaneWaypoints(
            Vector3 center,
            float totalLength,
            float spawnRunUp,
            float endClearance)
        {
            // STEP 1: Mirror the Chapter 7 arena lane sampling logic so terrain scenarios stay aligned with the authored gallery.
            float halfLength = totalLength * 0.5f;
            float lowSideX = center.x - (halfLength - TerrainLaneProbeInset) - spawnRunUp;
            float highSideX = center.x + (halfLength - TerrainLaneProbeInset) + endClearance;

            // STEP 2: Publish the direct traversal anchors used by the terrain outcome tests.
            return new[]
            {
                new Vector3(lowSideX, 0f, center.z),
                new Vector3(highSideX, 0f, center.z),
            };
        }

        private static Vector3[] BuildRepeatedWaypoints(Vector3[] sourceWaypoints, int repeatCount)
        {
            // STEP 1: Validate the source sequence before building the long-run fatigue path.
            if (sourceWaypoints == null || sourceWaypoints.Length == 0)
            {
                throw new ArgumentException("Source waypoint sequence must be non-empty.", nameof(sourceWaypoints));
            }

            if (repeatCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(repeatCount), "Repeat count must be positive.");
            }

            // STEP 2: Concatenate the single-lap path without altering the original lap-course reference.
            Vector3[] repeated = new Vector3[sourceWaypoints.Length * repeatCount];
            for (int repeatIndex = 0; repeatIndex < repeatCount; repeatIndex++)
            {
                Array.Copy(
                    sourceWaypoints,
                    0,
                    repeated,
                    repeatIndex * sourceWaypoints.Length,
                    sourceWaypoints.Length);
            }

            return repeated;
        }

        private static Vector3[] GetLapCircuitWaypoints()
        {
            // STEP 1: Read the runtime Top Gear circuit directly from LapDemoRunner so Chapter 9 keeps one authoritative source.
            FieldInfo courseWaypointsField = typeof(LapDemoRunner).GetField(
                "CourseWaypoints",
                BindingFlags.NonPublic | BindingFlags.Static);

            if (courseWaypointsField == null)
            {
                throw new MissingFieldException(typeof(LapDemoRunner).FullName, "CourseWaypoints");
            }

            // STEP 2: Return the runtime-authored waypoint array without duplicating the course geometry in test code.
            Vector3[] courseWaypoints = courseWaypointsField.GetValue(null) as Vector3[];
            if (courseWaypoints == null || courseWaypoints.Length == 0)
            {
                throw new InvalidOperationException("LapDemoRunner.CourseWaypoints must provide a non-empty waypoint sequence.");
            }

            return courseWaypoints;
        }
    }
}
