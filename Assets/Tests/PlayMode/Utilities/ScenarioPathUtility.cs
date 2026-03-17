using System;
using System.Collections.Generic;
using UnityEngine;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// Helper methods for consuming shared scenario waypoint data in PlayMode tests.
    /// </summary>
    public static class ScenarioPathUtility
    {
        /// <summary>
        /// Returns one normalized planar direction per non-zero segment in the scenario path.
        /// </summary>
        public static Vector3[] GetTravelDirections(ScenarioDefinition scenario)
        {
            if (scenario.Waypoints == null || scenario.Waypoints.Length == 0)
            {
                throw new InvalidOperationException($"Scenario '{scenario.Name}' must expose waypoints before travel directions can be derived.");
            }

            List<Vector3> directions = new List<Vector3>(scenario.Waypoints.Length);
            Vector3 previousWaypoint = Vector3.zero;

            for (int index = 0; index < scenario.Waypoints.Length; index++)
            {
                Vector3 planarDelta = Vector3.ProjectOnPlane(scenario.Waypoints[index] - previousWaypoint, Vector3.up);
                if (planarDelta.sqrMagnitude > 0.0001f)
                {
                    directions.Add(planarDelta.normalized);
                }

                previousWaypoint = scenario.Waypoints[index];
            }

            if (directions.Count == 0)
            {
                throw new InvalidOperationException($"Scenario '{scenario.Name}' must contain at least one non-zero planar segment.");
            }

            return directions.ToArray();
        }

        /// <summary>
        /// Returns PlayerMovement test inputs that follow the scenario's planar travel directions.
        /// </summary>
        public static Vector2[] GetMoveInputs(ScenarioDefinition scenario)
        {
            Vector3[] directions = GetTravelDirections(scenario);
            Vector2[] inputs = new Vector2[directions.Length];

            for (int index = 0; index < directions.Length; index++)
            {
                inputs[index] = ToMoveInput(directions[index]);
            }

            return inputs;
        }

        /// <summary>
        /// Rotates a planar direction around the up axis and returns a normalized result.
        /// </summary>
        public static Vector3 RotatePlanarDirection(Vector3 direction, float degrees)
        {
            Vector3 planarDirection = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (planarDirection.sqrMagnitude <= 0.0001f)
            {
                throw new ArgumentOutOfRangeException(nameof(direction), "Direction must contain a non-zero planar component.");
            }

            return (Quaternion.Euler(0f, degrees, 0f) * planarDirection).normalized;
        }

        /// <summary>
        /// Converts a world-space planar direction into the PlayerMovement test seam input format.
        /// </summary>
        public static Vector2 ToMoveInput(Vector3 direction)
        {
            Vector3 planarDirection = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (planarDirection.sqrMagnitude <= 0.0001f)
            {
                throw new ArgumentOutOfRangeException(nameof(direction), "Direction must contain a non-zero planar component.");
            }

            planarDirection.Normalize();
            return new Vector2(planarDirection.x, planarDirection.z);
        }
    }
}