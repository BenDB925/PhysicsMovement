using PhysicsDrivenMovement.Core;
using PhysicsDrivenMovement.Environment;
using UnityEngine;

namespace PhysicsDrivenMovement.Editor
{
    /// <summary>
    /// Shared editor-only authoring helpers for Chapter 7 terrain scenarios.
    /// Builds reusable slope, step, uneven-ground, and obstacle lanes while stamping
    /// each scenario with a <see cref="TerrainScenarioMarker"/> so scenes expose a
    /// consistent runtime-query surface.
    /// </summary>
    internal static class TerrainScenarioBuilder
    {
        private const float SurfaceThickness = 0.12f;

        internal enum TerrainLaneAxis
        {
            X,
            Z,
        }

        internal static GameObject CreateScenarioContainer(Transform parent)
        {
            GameObject container = new GameObject("TerrainScenarios");
            container.transform.SetParent(parent, worldPositionStays: false);
            return container;
        }

        internal static Material CreateSurfaceMaterial(string materialName, Color color)
        {
            Material material = new Material(Shader.Find("Standard"))
            {
                name = materialName,
                color = color,
            };

            return material;
        }

        internal static TerrainScenarioMarker BuildSlopeLane(
            Transform parent,
            string scenarioId,
            Vector3 center,
            TerrainLaneAxis axis,
            float laneWidth,
            float totalLength,
            float rise,
            Material surfaceMaterial,
            PhysicsMaterial physicsMat)
        {
            // STEP 1: Create the scenario root and stamp its marker bounds before authoring geometry.
            TerrainScenarioMarker marker = CreateScenarioRoot(
                parent,
                scenarioId,
                TerrainScenarioType.SlopeLane,
                center + Vector3.up * (rise * 0.5f),
                GetBoundsSize(axis, totalLength, laneWidth, rise + 0.6f));

            float approachLength = totalLength * 0.28f;
            float rampLength = totalLength * 0.36f;
            float topLength = totalLength - approachLength - rampLength;
            float angleDegrees = Mathf.Atan2(rise, rampLength) * Mathf.Rad2Deg;

            // STEP 2: Build a flat approach, the sloped ramp, and a raised landing pad.
            CreateStaticBox(
                $"{scenarioId}_Approach",
                marker.transform,
                center + AxisOffset(axis, (-totalLength * 0.5f) + (approachLength * 0.5f)) + Vector3.up * (SurfaceThickness * 0.5f),
                GetBoundsSize(axis, approachLength, laneWidth, SurfaceThickness),
                surfaceMaterial,
                physicsMat);

            CreateStaticBox(
                $"{scenarioId}_Ramp",
                marker.transform,
                center + AxisOffset(axis, (-totalLength * 0.5f) + approachLength + (rampLength * 0.5f)) + Vector3.up * ((rise * 0.5f) + (SurfaceThickness * 0.5f)),
                GetBoundsSize(axis, rampLength, laneWidth, SurfaceThickness),
                surfaceMaterial,
                physicsMat,
                GetRampRotation(axis, angleDegrees));

            CreateStaticBox(
                $"{scenarioId}_Landing",
                marker.transform,
                center + AxisOffset(axis, (totalLength * 0.5f) - (topLength * 0.5f)) + Vector3.up * (rise * 0.5f),
                GetBoundsSize(axis, topLength, laneWidth, rise),
                surfaceMaterial,
                physicsMat);

            return marker;
        }

        internal static TerrainScenarioMarker BuildStepUpLane(
            Transform parent,
            string scenarioId,
            Vector3 center,
            TerrainLaneAxis axis,
            float laneWidth,
            float totalLength,
            float rise,
            Material surfaceMaterial,
            PhysicsMaterial physicsMat)
        {
            // STEP 1: Create the scenario root and bounds for a stepped ascent profile.
            TerrainScenarioMarker marker = CreateScenarioRoot(
                parent,
                scenarioId,
                TerrainScenarioType.StepUpLane,
                center + Vector3.up * (rise * 0.5f),
                GetBoundsSize(axis, totalLength, laneWidth, rise + 0.6f));

            float approachLength = totalLength * 0.24f;
            float stepLength = totalLength * 0.16f;
            float topLength = totalLength - approachLength - (stepLength * 2f);
            float stepHeight = rise * 0.5f;

            // STEP 2: Build a flat run-up, two discrete risers, and a raised landing block.
            CreateStaticBox(
                $"{scenarioId}_Approach",
                marker.transform,
                center + AxisOffset(axis, (-totalLength * 0.5f) + (approachLength * 0.5f)) + Vector3.up * (SurfaceThickness * 0.5f),
                GetBoundsSize(axis, approachLength, laneWidth, SurfaceThickness),
                surfaceMaterial,
                physicsMat);

            CreateStaticBox(
                $"{scenarioId}_StepA",
                marker.transform,
                center + AxisOffset(axis, (-totalLength * 0.5f) + approachLength + (stepLength * 0.5f)) + Vector3.up * (stepHeight * 0.5f),
                GetBoundsSize(axis, stepLength, laneWidth, stepHeight),
                surfaceMaterial,
                physicsMat);

            CreateStaticBox(
                $"{scenarioId}_StepB",
                marker.transform,
                center + AxisOffset(axis, (-totalLength * 0.5f) + approachLength + stepLength + (stepLength * 0.5f)) + Vector3.up * (rise * 0.5f),
                GetBoundsSize(axis, stepLength, laneWidth, rise),
                surfaceMaterial,
                physicsMat);

            CreateStaticBox(
                $"{scenarioId}_Landing",
                marker.transform,
                center + AxisOffset(axis, (totalLength * 0.5f) - (topLength * 0.5f)) + Vector3.up * (rise * 0.5f),
                GetBoundsSize(axis, topLength, laneWidth, rise),
                surfaceMaterial,
                physicsMat);

            return marker;
        }

        internal static TerrainScenarioMarker BuildStepDownLane(
            Transform parent,
            string scenarioId,
            Vector3 center,
            TerrainLaneAxis axis,
            float laneWidth,
            float totalLength,
            float rise,
            Material surfaceMaterial,
            PhysicsMaterial physicsMat)
        {
            // STEP 1: Create the scenario root and bounds for a stepped descent profile.
            TerrainScenarioMarker marker = CreateScenarioRoot(
                parent,
                scenarioId,
                TerrainScenarioType.StepDownLane,
                center + Vector3.up * (rise * 0.5f),
                GetBoundsSize(axis, totalLength, laneWidth, rise + 0.6f));

            float topLength = totalLength * 0.28f;
            float stepLength = totalLength * 0.16f;
            float runoutLength = totalLength - topLength - (stepLength * 2f);
            float stepHeight = rise * 0.5f;

            // STEP 2: Build a raised start block, two descending risers, and a flat runout.
            CreateStaticBox(
                $"{scenarioId}_Start",
                marker.transform,
                center + AxisOffset(axis, (-totalLength * 0.5f) + (topLength * 0.5f)) + Vector3.up * (rise * 0.5f),
                GetBoundsSize(axis, topLength, laneWidth, rise),
                surfaceMaterial,
                physicsMat);

            CreateStaticBox(
                $"{scenarioId}_StepA",
                marker.transform,
                center + AxisOffset(axis, (-totalLength * 0.5f) + topLength + (stepLength * 0.5f)) + Vector3.up * (stepHeight * 0.5f),
                GetBoundsSize(axis, stepLength, laneWidth, stepHeight),
                surfaceMaterial,
                physicsMat);

            CreateStaticBox(
                $"{scenarioId}_StepB",
                marker.transform,
                center + AxisOffset(axis, (-totalLength * 0.5f) + topLength + stepLength + (stepLength * 0.5f)) + Vector3.up * (SurfaceThickness * 0.5f),
                GetBoundsSize(axis, stepLength, laneWidth, SurfaceThickness),
                surfaceMaterial,
                physicsMat);

            CreateStaticBox(
                $"{scenarioId}_Runout",
                marker.transform,
                center + AxisOffset(axis, (totalLength * 0.5f) - (runoutLength * 0.5f)) + Vector3.up * (SurfaceThickness * 0.5f),
                GetBoundsSize(axis, runoutLength, laneWidth, SurfaceThickness),
                surfaceMaterial,
                physicsMat);

            return marker;
        }

        internal static TerrainScenarioMarker BuildUnevenPatch(
            Transform parent,
            string scenarioId,
            Vector3 center,
            Vector2 footprint,
            Material surfaceMaterial,
            PhysicsMaterial physicsMat)
        {
            // STEP 1: Create the root and publish the overall patch footprint as scenario bounds.
            TerrainScenarioMarker marker = CreateScenarioRoot(
                parent,
                scenarioId,
                TerrainScenarioType.UnevenPatch,
                center + Vector3.up * 0.2f,
                new Vector3(footprint.x, 0.8f, footprint.y));

            float[,] heights =
            {
                { 0.10f, 0.22f, 0.14f, 0.26f },
                { 0.18f, 0.08f, 0.24f, 0.12f },
                { 0.06f, 0.20f, 0.16f, 0.28f },
            };

            int rowCount = heights.GetLength(0);
            int columnCount = heights.GetLength(1);
            float tileWidth = footprint.x / columnCount;
            float tileDepth = footprint.y / rowCount;

            // STEP 2: Author a deterministic heightfield grid so the patch is uneven but reproducible.
            for (int row = 0; row < rowCount; row++)
            {
                for (int column = 0; column < columnCount; column++)
                {
                    float tileHeight = heights[row, column];
                    float x = center.x - (footprint.x * 0.5f) + (tileWidth * (column + 0.5f));
                    float z = center.z - (footprint.y * 0.5f) + (tileDepth * (row + 0.5f));

                    CreateStaticBox(
                        $"{scenarioId}_Tile_{row}_{column}",
                        marker.transform,
                        new Vector3(x, tileHeight * 0.5f, z),
                        new Vector3(tileWidth * 0.92f, tileHeight, tileDepth * 0.92f),
                        surfaceMaterial,
                        physicsMat);
                }
            }

            return marker;
        }

        internal static TerrainScenarioMarker BuildLowObstacleLane(
            Transform parent,
            string scenarioId,
            Vector3 center,
            TerrainLaneAxis axis,
            float laneWidth,
            float totalLength,
            Material surfaceMaterial,
            PhysicsMaterial physicsMat)
        {
            // STEP 1: Create the root and stamp the lane footprint for runtime queries.
            TerrainScenarioMarker marker = CreateScenarioRoot(
                parent,
                scenarioId,
                TerrainScenarioType.LowObstacleLane,
                center + Vector3.up * 0.2f,
                GetBoundsSize(axis, totalLength, laneWidth, 0.8f));

            // STEP 2: Build a colored guide strip and a staggered obstacle pattern that leaves navigable gaps.
            CreateStaticBox(
                $"{scenarioId}_GuideStrip",
                marker.transform,
                center + Vector3.up * (SurfaceThickness * 0.5f),
                GetBoundsSize(axis, totalLength, laneWidth, SurfaceThickness),
                surfaceMaterial,
                physicsMat);

            float obstacleLength = totalLength / 8f;
            float[] offsets = { -0.28f, 0.24f, -0.22f, 0.30f };
            for (int obstacleIndex = 0; obstacleIndex < offsets.Length; obstacleIndex++)
            {
                float alongAxis = (-totalLength * 0.3f) + (obstacleIndex * obstacleLength * 1.6f);
                Vector3 lateralOffset = axis == TerrainLaneAxis.X
                    ? new Vector3(0f, 0f, laneWidth * offsets[obstacleIndex])
                    : new Vector3(laneWidth * offsets[obstacleIndex], 0f, 0f);

                CreateStaticBox(
                    $"{scenarioId}_Obstacle_{obstacleIndex}",
                    marker.transform,
                    center + AxisOffset(axis, alongAxis) + lateralOffset + Vector3.up * 0.16f,
                    GetBoundsSize(axis, obstacleLength, laneWidth * 0.28f, 0.32f),
                    surfaceMaterial,
                    physicsMat);
            }

            return marker;
        }

        private static TerrainScenarioMarker CreateScenarioRoot(
            Transform parent,
            string scenarioId,
            TerrainScenarioType scenarioType,
            Vector3 boundsCenter,
            Vector3 boundsSize)
        {
            GameObject scenarioRoot = new GameObject(scenarioId);
            scenarioRoot.transform.SetParent(parent, worldPositionStays: false);

            TerrainScenarioMarker marker = scenarioRoot.AddComponent<TerrainScenarioMarker>();
            marker.Initialise(scenarioId, scenarioType, new Bounds(boundsCenter, boundsSize));
            return marker;
        }

        private static GameObject CreateStaticBox(
            string name,
            Transform parent,
            Vector3 position,
            Vector3 scale,
            Material material,
            PhysicsMaterial physicsMat,
            Quaternion? rotation = null)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.layer = GameSettings.LayerEnvironment;
            go.transform.SetParent(parent, worldPositionStays: true);
            go.transform.SetPositionAndRotation(position, rotation ?? Quaternion.identity);
            go.transform.localScale = scale;

            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
            }

            BoxCollider collider = go.GetComponent<BoxCollider>();
            if (collider != null)
            {
                collider.material = physicsMat;
            }

            return go;
        }

        private static Vector3 AxisOffset(TerrainLaneAxis axis, float distance)
        {
            return axis == TerrainLaneAxis.X
                ? new Vector3(distance, 0f, 0f)
                : new Vector3(0f, 0f, distance);
        }

        private static Vector3 GetBoundsSize(TerrainLaneAxis axis, float length, float width, float height)
        {
            return axis == TerrainLaneAxis.X
                ? new Vector3(length, height, width)
                : new Vector3(width, height, length);
        }

        private static Quaternion GetRampRotation(TerrainLaneAxis axis, float angleDegrees)
        {
            return axis == TerrainLaneAxis.X
                ? Quaternion.Euler(0f, 0f, angleDegrees)
                : Quaternion.Euler(-angleDegrees, 0f, 0f);
        }
    }
}