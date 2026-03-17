using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Payload describing an external impact that caused a knockdown.
    /// Produced by <see cref="ImpactKnockdownDetector"/> for downstream gameplay systems.
    /// </summary>
    public struct KnockdownEvent
    {
        /// <summary>
        /// Knockdown severity in the 0–1 range.
        /// </summary>
        public float Severity;

        /// <summary>
        /// World-space direction the impact pushed the character.
        /// </summary>
        public Vector3 ImpactDirection;

        /// <summary>
        /// World-space collision contact point.
        /// </summary>
        public Vector3 ImpactPoint;

        /// <summary>
        /// Effective impact delta-V after direction weighting, in meters per second.
        /// </summary>
        public float EffectiveDeltaV;

        /// <summary>
        /// GameObject responsible for the impact, when known.
        /// </summary>
        public GameObject Source;
    }
}