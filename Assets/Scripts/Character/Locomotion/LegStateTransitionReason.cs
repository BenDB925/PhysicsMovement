namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Explains why a leg entered or currently remains in its explicit Chapter 3 role.
    /// These reasons let runtime logging distinguish default cadence from braking,
    /// turn-support, or stumble-recovery overrides without changing the executor surface.
    /// </summary>
    internal enum LegStateTransitionReason
    {
        None = 0,
        DefaultCadence = 1,
        SpeedUp = 2,
        Braking = 3,
        TurnSupport = 4,
        StumbleRecovery = 5,
        LowConfidenceFallback = 6,
    }
}