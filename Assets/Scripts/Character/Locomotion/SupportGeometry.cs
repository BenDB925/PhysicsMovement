using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Immutable support-geometry snapshot derived from the current foot contact positions.
    /// It approximates the active support patch as a capsule spanning the grounded feet so
    /// locomotion systems can reason about support-center placement without re-reading raw transforms.
    /// </summary>
    internal readonly struct SupportGeometry
    {
        private const float DirectionEpsilon = 0.0001f;
        private const float BaseSupportRadius = 0.12f;

        public SupportGeometry(
            Vector3 leftFootPosition,
            bool leftFootGrounded,
            Vector3 rightFootPosition,
            bool rightFootGrounded)
        {
            // STEP 1: Flatten the foot positions onto the locomotion plane and preserve grounded membership.
            LeftFootPosition = Flatten(leftFootPosition);
            RightFootPosition = Flatten(rightFootPosition);
            LeftFootGrounded = leftFootGrounded;
            RightFootGrounded = rightFootGrounded;

            // STEP 2: Choose the active support segment from the grounded feet when available.
            GroundedFootCount = (leftFootGrounded ? 1 : 0) + (rightFootGrounded ? 1 : 0);
            if (GroundedFootCount == 2)
            {
                SupportStart = LeftFootPosition;
                SupportEnd = RightFootPosition;
            }
            else if (leftFootGrounded)
            {
                SupportStart = LeftFootPosition;
                SupportEnd = LeftFootPosition;
            }
            else if (rightFootGrounded)
            {
                SupportStart = RightFootPosition;
                SupportEnd = RightFootPosition;
            }
            else
            {
                SupportStart = LeftFootPosition;
                SupportEnd = RightFootPosition;
            }

            // STEP 3: Publish aggregate support dimensions used by the observation layer.
            SupportCenter = 0.5f * (SupportStart + SupportEnd);
            SupportSpan = Vector3.Distance(SupportStart, SupportEnd);
            SupportRadius = BaseSupportRadius + (GroundedFootCount > 1 ? SupportSpan * 0.15f : 0f);
        }

        public Vector3 LeftFootPosition { get; }

        public bool LeftFootGrounded { get; }

        public Vector3 RightFootPosition { get; }

        public bool RightFootGrounded { get; }

        public int GroundedFootCount { get; }

        public Vector3 SupportStart { get; }

        public Vector3 SupportEnd { get; }

        public Vector3 SupportCenter { get; }

        public float SupportSpan { get; }

        public float SupportRadius { get; }

        public float GetSupportBehindDistance(Vector3 referencePosition, Vector3 direction)
        {
            // STEP 1: Ignore vertical input so support-behind distance stays in locomotion space.
            Vector3 planarDirection = Flatten(direction);
            if (planarDirection.sqrMagnitude <= DirectionEpsilon)
            {
                return 0f;
            }

            planarDirection.Normalize();

            // STEP 2: Measure how far the active support center trails behind the reference point.
            Vector3 supportOffset = SupportCenter - Flatten(referencePosition);
            float signedSupportOffset = Vector3.Dot(supportOffset, planarDirection);
            return Mathf.Max(0f, -signedSupportOffset);
        }

        public bool IsPointOutsideSupport(Vector3 point)
        {
            // STEP 1: Without grounded feet there is no trustworthy support patch to classify against.
            if (GroundedFootCount <= 0)
            {
                return false;
            }

            Vector3 planarPoint = Flatten(point);
            Vector3 supportVector = SupportEnd - SupportStart;

            // STEP 2: Approximate the support patch as a capsule around the active support segment.
            if (supportVector.sqrMagnitude <= DirectionEpsilon)
            {
                return Vector3.Distance(planarPoint, SupportCenter) > SupportRadius;
            }

            float projection = Vector3.Dot(planarPoint - SupportStart, supportVector) / supportVector.sqrMagnitude;
            Vector3 closestPoint = SupportStart + Mathf.Clamp01(projection) * supportVector;
            float distanceFromSupport = Vector3.Distance(planarPoint, closestPoint);
            return distanceFromSupport > SupportRadius;
        }

        private static Vector3 Flatten(Vector3 point)
        {
            return new Vector3(point.x, 0f, point.z);
        }
    }
}