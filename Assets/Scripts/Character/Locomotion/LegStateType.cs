namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Explicit per-leg locomotion role emitted by the Chapter 3 gait-state migration.
    /// These roles describe what the leg is doing conceptually even while the runtime
    /// remains on the current pass-through gait execution path.
    /// </summary>
    internal enum LegStateType
    {
        Stance = 0,
        Swing = 1,
        Plant = 2,
        RecoveryStep = 3,
        CatchStep = 4,
    }
}