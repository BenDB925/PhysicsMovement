using System;

namespace PhysicsDrivenMovement.Environment
{
    /// <summary>
    /// Identifies the controlled terrain scenario authored into a scene for Chapter 7
    /// locomotion validation and future terrain-aware planning work.
    /// </summary>
    [Serializable]
    public enum TerrainScenarioType
    {
        SlopeLane,
        StepUpLane,
        StepDownLane,
        UnevenPatch,
        LowObstacleLane,
    }
}