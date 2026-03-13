using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Prevents recovery oscillation by requiring entry debounce, enforcing exit cooldown,
    /// and providing a ramp-in blend that smooths the onset of recovery strength.
    /// Pure state tracker with no MonoBehaviour dependency — the director advances it
    /// each FixedUpdate and queries it before entering or blending a recovery.
    /// Collaborators: <see cref="LocomotionDirector"/>, <see cref="RecoveryState"/>.
    /// </summary>
    internal sealed class RecoveryTransitionGuard
    {
        // ── Candidate tracking (entry debounce) ────────────────────────────

        private RecoverySituation _candidateSituation;
        private int _candidateFrameCount;

        // ── Cooldown tracking (exit anti-oscillation) ──────────────────────

        private RecoverySituation _cooldownSituation;
        private int _cooldownRemaining;

        // ── Ramp-in tracking ───────────────────────────────────────────────

        private int _rampInFramesElapsed;

        /// <summary>
        /// Evaluates whether a newly classified situation should be promoted into
        /// an active recovery entry. Returns true when the candidate has persisted
        /// long enough (debounce) and is not blocked by an exit cooldown.
        /// </summary>
        /// <param name="classified">The situation classified this frame (may be None).</param>
        /// <param name="debounceFrames">Minimum consecutive frames before entry.</param>
        /// <param name="cooldownFrames">Frames to block re-entry after recovery expires.</param>
        /// <returns>True when the director should call <see cref="RecoveryState.Enter"/>.</returns>
        public bool ShouldEnter(
            RecoverySituation classified,
            int debounceFrames,
            int cooldownFrames)
        {
            // STEP 1: Tick down exit cooldown regardless of candidate state.
            if (_cooldownRemaining > 0)
            {
                _cooldownRemaining--;
            }

            // STEP 2: No recovery candidate this frame — reset tracking.
            if (classified == RecoverySituation.None)
            {
                _candidateSituation = RecoverySituation.None;
                _candidateFrameCount = 0;
                return false;
            }

            // STEP 3: Check exit cooldown. Higher-priority situations bypass it.
            if (_cooldownRemaining > 0 && classified <= _cooldownSituation)
            {
                // Still in cooldown for same-or-lower priority — keep tracking but don't enter.
                _candidateSituation = classified;
                _candidateFrameCount = 1;
                return false;
            }

            // STEP 4: Advance candidate counter when the same situation persists.
            if (classified == _candidateSituation)
            {
                _candidateFrameCount++;
            }
            else
            {
                // New or different situation — restart debounce.
                _candidateSituation = classified;
                _candidateFrameCount = 1;
            }

            // STEP 5: High-priority situations (NearFall, Stumble) debounce faster.
            int effectiveDebounce = classified >= RecoverySituation.NearFall
                ? Mathf.Max(1, debounceFrames / 2)
                : debounceFrames;

            // STEP 6: Promote when debounce is met.
            if (_candidateFrameCount >= effectiveDebounce)
            {
                _candidateSituation = RecoverySituation.None;
                _candidateFrameCount = 0;
                _rampInFramesElapsed = 0;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Called by the director when an active recovery expires (FramesRemaining hits 0).
        /// Arms the exit cooldown so the same or lower-priority situation is blocked.
        /// </summary>
        public void OnRecoveryExpired(RecoverySituation expiredSituation, int cooldownFrames)
        {
            _cooldownSituation = expiredSituation;
            _cooldownRemaining = cooldownFrames;
            _rampInFramesElapsed = 0;
        }

        /// <summary>
        /// Advances the ramp-in counter. Call once per FixedUpdate while recovery is active.
        /// </summary>
        public void TickRampIn()
        {
            _rampInFramesElapsed++;
        }

        /// <summary>
        /// Returns a 0..1 multiplier for the recovery blend that ramps from 0 to 1
        /// over the first <paramref name="rampInFrames"/> frames of an active recovery.
        /// </summary>
        public float ComputeRampInBlend(int rampInFrames)
        {
            if (rampInFrames <= 0)
            {
                return 1f;
            }

            return Mathf.Clamp01((float)_rampInFramesElapsed / rampInFrames);
        }

        /// <summary>Resets all guard state. Used when the director clears recovery entirely.</summary>
        public void Reset()
        {
            _candidateSituation = RecoverySituation.None;
            _candidateFrameCount = 0;
            _cooldownSituation = RecoverySituation.None;
            _cooldownRemaining = 0;
            _rampInFramesElapsed = 0;
        }
    }
}
