using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Static debug-visualization helpers extracted from <see cref="LocomotionDirector"/>.
    /// All methods are editor-only diagnostics (Debug.DrawLine / Debug.Log) and have
    /// no runtime side effects beyond drawing gizmos.
    /// </summary>
    internal static class LocomotionDebugDraw
    {
        private const float Epsilon = 0.0001f;

        internal static string BuildObservationTelemetryLine(
            string ownerName,
            LocomotionSensorSnapshot sensorSnapshot,
            LocomotionObservation observation,
            Vector3 predictedDriftDirection)
        {
            SupportGeometry supportGeometry = sensorSnapshot.SupportGeometry;

            string supportStart = FormatPlanarVector(supportGeometry.SupportStart);
            string supportEnd = FormatPlanarVector(supportGeometry.SupportEnd);
            string supportCenter = FormatPlanarVector(supportGeometry.SupportCenter);
            string driftDirection = FormatPlanarVector(predictedDriftDirection);

            return
                $"[LocomotionDirector] '{ownerName}' observation: supportStart={supportStart}, supportEnd={supportEnd}, " +
                $"supportCenter={supportCenter}, supportSpan={supportGeometry.SupportSpan:F2}, supportRadius={supportGeometry.SupportRadius:F2}, " +
                $"groundedFeet={supportGeometry.GroundedFootCount}, supportQuality={observation.SupportQuality:F2}, " +
                $"contactConfidence={observation.ContactConfidence:F2}, plantedConfidence={observation.PlantedFootConfidence:F2}, " +
                $"slip={observation.SlipEstimate:F2}, turnSeverity={observation.TurnSeverity:F2}, " +
                $"comOutsideSupport={observation.IsComOutsideSupport}, leftPlanted={observation.LeftFoot.IsPlanted}, " +
                $"rightPlanted={observation.RightFoot.IsPlanted}, driftDir={driftDirection}";
        }

        internal static void DrawObservationDebug(
            LocomotionSensorSnapshot sensorSnapshot,
            LocomotionObservation observation,
            Vector3 predictedDriftDirection,
            float drawHeight)
        {
            SupportGeometry supportGeometry = sensorSnapshot.SupportGeometry;
            Vector3 lift = Vector3.up * drawHeight;
            Color supportColor = observation.IsComOutsideSupport
                ? Color.red
                : Color.Lerp(Color.red, Color.green, observation.SupportQuality);
            float duration = Time.fixedDeltaTime;

            DrawSupportCapsule(supportGeometry, lift, supportColor, duration);

            Vector3 supportCenter = supportGeometry.SupportCenter + lift;
            Vector3 centerOfMass = Vector3.ProjectOnPlane(sensorSnapshot.CenterOfMassPosition, Vector3.up) + lift;
            Debug.DrawLine(supportCenter, centerOfMass, Color.yellow, duration, false);
            DrawCross(centerOfMass, 0.05f, observation.IsComOutsideSupport ? Color.red : Color.cyan, duration);

            if (predictedDriftDirection.sqrMagnitude > Epsilon)
            {
                float driftLength = Mathf.Max(0.25f, supportGeometry.SupportRadius * 2f + observation.TurnSeverity * 0.2f);
                DrawArrow(centerOfMass, predictedDriftDirection.normalized, driftLength, Color.magenta, duration);
            }
        }

        internal static void DrawStepTargetDebug(
            LegCommandOutput leftCommand,
            LegCommandOutput rightCommand,
            float duration)
        {
            DrawStepTarget(leftCommand, new Color(0.3f, 0.5f, 1f), duration);
            DrawStepTarget(rightCommand, new Color(0.8f, 0.9f, 0.2f), duration);
        }

        private static void DrawStepTarget(
            LegCommandOutput command,
            Color baseColor,
            float duration)
        {
            StepTarget target = command.StepTarget;
            if (!target.IsValid)
            {
                return;
            }

            Color color = Color.Lerp(Color.red, baseColor, target.Confidence);

            Vector3 landingPosition = target.LandingPosition;
            DrawCross(landingPosition, 0.08f, color, duration);

            float radius = Mathf.Lerp(0.04f, 0.12f, target.Confidence);
            DrawCircle(landingPosition, radius, color, duration);

            float pillarHeight = Mathf.Clamp(target.DesiredTiming, 0.05f, 0.5f);
            Debug.DrawLine(landingPosition, landingPosition + Vector3.up * pillarHeight, color, duration, false);
        }

        private static void DrawSupportCapsule(
            SupportGeometry supportGeometry,
            Vector3 lift,
            Color color,
            float duration)
        {
            Vector3 supportStart = supportGeometry.SupportStart + lift;
            Vector3 supportEnd = supportGeometry.SupportEnd + lift;
            float radius = supportGeometry.SupportRadius;

            Vector3 supportAxis = supportEnd - supportStart;
            Vector3 lateral = supportAxis.sqrMagnitude > Epsilon
                ? Vector3.Cross(Vector3.up, supportAxis.normalized)
                : Vector3.right;
            lateral.Normalize();
            Vector3 lateralOffset = lateral * radius;

            Debug.DrawLine(supportStart + lateralOffset, supportEnd + lateralOffset, color, duration, false);
            Debug.DrawLine(supportStart - lateralOffset, supportEnd - lateralOffset, color, duration, false);

            DrawCircle(supportStart, radius, color, duration);
            DrawCircle(supportEnd, radius, color, duration);
        }

        private static void DrawArrow(
            Vector3 origin,
            Vector3 direction,
            float length,
            Color color,
            float duration)
        {
            Vector3 arrowDirection = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (arrowDirection.sqrMagnitude <= Epsilon)
            {
                return;
            }

            arrowDirection.Normalize();
            Vector3 tip = origin + arrowDirection * length;
            Debug.DrawLine(origin, tip, color, duration, false);

            Vector3 wing = Quaternion.AngleAxis(150f, Vector3.up) * arrowDirection;
            Debug.DrawLine(tip, tip + wing * (length * 0.25f), color, duration, false);

            wing = Quaternion.AngleAxis(-150f, Vector3.up) * arrowDirection;
            Debug.DrawLine(tip, tip + wing * (length * 0.25f), color, duration, false);
        }

        private static void DrawCross(Vector3 center, float halfSize, Color color, float duration)
        {
            Debug.DrawLine(center + Vector3.right * halfSize, center - Vector3.right * halfSize, color, duration, false);
            Debug.DrawLine(center + Vector3.forward * halfSize, center - Vector3.forward * halfSize, color, duration, false);
        }

        private static void DrawCircle(Vector3 center, float radius, Color color, float duration)
        {
            const int SegmentCount = 12;
            if (radius <= 0f)
            {
                return;
            }

            Vector3 previousPoint = center + Vector3.forward * radius;
            for (int segmentIndex = 1; segmentIndex <= SegmentCount; segmentIndex++)
            {
                float angleRadians = (segmentIndex / (float)SegmentCount) * Mathf.PI * 2f;
                Vector3 nextPoint = center + new Vector3(
                    Mathf.Sin(angleRadians) * radius,
                    0f,
                    Mathf.Cos(angleRadians) * radius);
                Debug.DrawLine(previousPoint, nextPoint, color, duration, false);
                previousPoint = nextPoint;
            }
        }

        internal static string FormatPlanarVector(Vector3 value)
        {
            return $"({value.x:F2}, {value.z:F2})";
        }
    }
}
