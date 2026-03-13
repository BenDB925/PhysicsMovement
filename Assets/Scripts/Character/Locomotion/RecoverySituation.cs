namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Names the locomotion recovery situation that the director has classified from
    /// current observations. Each situation maps to a distinct recovery strategy with
    /// its own timeout window and strength profile. The director publishes the active
    /// situation so downstream executors (BalanceController, LegAnimator) can adapt
    /// their response without re-deriving the cause independently.
    /// </summary>
    internal enum RecoverySituation
    {
        /// <summary>No recovery needed — normal locomotion.</summary>
        None = 0,

        /// <summary>
        /// Sharp heading change (turn severity ≥ threshold) with degraded support.
        /// Recovery favours yaw suppression and outside-leg support.
        /// </summary>
        HardTurn = 1,

        /// <summary>
        /// Near-180° direction reversal. Recovery shortens stride, boosts height
        /// maintenance, and allows a braking pause before re-accelerating.
        /// </summary>
        Reversal = 2,

        /// <summary>
        /// Foot slip detected (high slip estimate) while support is compromised.
        /// Recovery widens catch-step placement and boosts stabilisation.
        /// </summary>
        Slip = 3,

        /// <summary>
        /// Support quality has dropped critically but the character is not yet fallen.
        /// Recovery prioritises an aggressive catch-step to reclaim support before
        /// the collapse detector fires.
        /// </summary>
        NearFall = 4,

        /// <summary>
        /// The collapse detector has confirmed a sustained locomotion failure.
        /// Recovery uses the strongest available response before yielding to the
        /// Fallen state transition.
        /// </summary>
        Stumble = 5,
    }
}
