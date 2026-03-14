using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// World-space step target describing where and when a foot should land,
    /// plus any explicit terrain-clearance request for step-up approaches.
    /// Part of the Chapter 4 step-planning layer. Produced by the step planner and
    /// consumed by <see cref="LegAnimator"/> for foothold-aware swing execution.
    /// Collaborators: StepPlanner (producer), <see cref="LegCommandOutput"/> (carrier),
    /// <see cref="LegAnimator"/> (consumer), <see cref="LocomotionDirector"/> (orchestrator).
    /// </summary>
    internal readonly struct StepTarget
    {
        // STEP 1: Core landing data — world-space position the foot should reach.
        public Vector3 LandingPosition { get; }

        // STEP 2: Timing — seconds until landing should occur (0 = land now).
        public float DesiredTiming { get; }

        // STEP 3: Step shape biases — lateral and longitudinal modifiers.
        //         WidthBias: -1 = narrower, 0 = default, +1 = wider.
        //         BrakingBias: -1 = aggressive brake (shortened step), 0 = normal, +1 = extending.
        public float WidthBias { get; }
        public float BrakingBias { get; }

        // STEP 4: Confidence — 0..1 indicating how reliable the target is.
        //         Low confidence means the executor should fall back to default swing.
        public float Confidence { get; }

        // STEP 4b: Terrain clearance intent — positive only when the planner has detected
        //          a real step-up ahead and wants the swing executor to lift higher.
        public float RequestedClearanceHeight { get; }

        public bool HasClearanceRequest { get; }

        // STEP 5: Validity flag — distinguishes computed targets from defaults.
        public bool IsValid { get; }

        public StepTarget(
            Vector3 landingPosition,
            float desiredTiming,
            float widthBias,
            float brakingBias,
            float confidence)
            : this(
                landingPosition,
                desiredTiming,
                widthBias,
                brakingBias,
                confidence,
                0f)
        {
        }

        public StepTarget(
            Vector3 landingPosition,
            float desiredTiming,
            float widthBias,
            float brakingBias,
            float confidence,
            float requestedClearanceHeight)
        {
            LandingPosition = landingPosition;
            DesiredTiming = Mathf.Max(0f, desiredTiming);
            WidthBias = Mathf.Clamp(widthBias, -1f, 1f);
            BrakingBias = Mathf.Clamp(brakingBias, -1f, 1f);
            Confidence = Mathf.Clamp01(confidence);
            RequestedClearanceHeight = Mathf.Max(0f, requestedClearanceHeight);
            HasClearanceRequest = RequestedClearanceHeight > 0f;
            IsValid = true;
        }

        /// <summary>
        /// Returns an invalid target with all fields at default. Used when no step plan
        /// has been computed or the planner has insufficient data.
        /// </summary>
        public static StepTarget Invalid => new StepTarget();
    }
}
