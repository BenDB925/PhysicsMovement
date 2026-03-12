namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// High-level execution mode for a single leg command.
    /// </summary>
    internal enum LegCommandMode
    {
        Disabled = 0,
        Cycle = 1,
        HoldPose = 2,
        Step = 3,
    }
}