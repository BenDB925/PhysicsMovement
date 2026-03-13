using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Tracks the active recovery situation, its remaining duration, severity at entry,
    /// and elapsed time. Published by <see cref="LocomotionDirector"/> so downstream
    /// executors can read a typed recovery context instead of a bare frame counter.
    /// Collaborators: <see cref="LocomotionDirector"/>, <see cref="BodySupportCommand"/>,
    /// <see cref="LegCommandOutput"/>.
    /// </summary>
    internal readonly struct RecoveryState
    {
        /// <summary>Shared sentinel for no active recovery.</summary>
        public static readonly RecoveryState Inactive = new RecoveryState(
            RecoverySituation.None, 0, 0, 0f, 0f);

        public RecoveryState(
            RecoverySituation situation,
            int framesRemaining,
            int totalFrames,
            float entrySeverity,
            float entryTurnSeverity)
        {
            Situation = situation;
            FramesRemaining = Mathf.Max(0, framesRemaining);
            TotalFrames = Mathf.Max(0, totalFrames);
            EntrySeverity = Mathf.Clamp01(entrySeverity);
            EntryTurnSeverity = Mathf.Clamp01(entryTurnSeverity);
        }

        /// <summary>The classified recovery situation.</summary>
        public RecoverySituation Situation { get; }

        /// <summary>Remaining recovery frames (counts down to 0).</summary>
        public int FramesRemaining { get; }

        /// <summary>Total recovery duration assigned at entry (for blend computation).</summary>
        public int TotalFrames { get; }

        /// <summary>Support risk severity when the recovery was entered (0..1).</summary>
        public float EntrySeverity { get; }

        /// <summary>Turn severity when the recovery was entered (0..1).</summary>
        public float EntryTurnSeverity { get; }

        /// <summary>True when a non-None recovery situation is active with frames remaining.</summary>
        public bool IsActive => Situation != RecoverySituation.None && FramesRemaining > 0;

        /// <summary>0..1 blend that ramps from 1 at entry to 0 at expiry.</summary>
        public float Blend => TotalFrames > 0
            ? Mathf.Clamp01((float)FramesRemaining / TotalFrames)
            : 0f;

        /// <summary>Returns a new state with one fewer remaining frame, or Inactive if expired.</summary>
        public RecoveryState Tick()
        {
            int next = FramesRemaining - 1;
            if (next <= 0)
            {
                return Inactive;
            }

            return new RecoveryState(Situation, next, TotalFrames, EntrySeverity, EntryTurnSeverity);
        }

        /// <summary>
        /// Enters a new recovery situation, or extends the current one if the new situation
        /// is the same or higher priority with a longer window.
        /// </summary>
        public RecoveryState Enter(
            RecoverySituation situation,
            int durationFrames,
            float severity,
            float turnSeverity)
        {
            // Higher enum value = higher priority; always upgrade.
            if (situation > Situation)
            {
                return new RecoveryState(situation, durationFrames, durationFrames, severity, turnSeverity);
            }

            // Same situation: extend if the new window is longer than current remaining.
            if (situation == Situation && durationFrames > FramesRemaining)
            {
                return new RecoveryState(situation, durationFrames, durationFrames, severity, turnSeverity);
            }

            // Lower priority or shorter window: keep current.
            return this;
        }

        public override string ToString()
        {
            return IsActive
                ? $"{Situation} ({FramesRemaining}/{TotalFrames}, sev={EntrySeverity:F2}, turn={EntryTurnSeverity:F2})"
                : "Inactive";
        }
    }
}
