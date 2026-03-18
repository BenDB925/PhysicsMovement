using System;
using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Drives a 4-phase physics-based stand-up sequence when the character enters
    /// <see cref="CharacterStateType.GettingUp"/> after a surrender knockdown.
    /// Phases: OrientProne → ArmPush → LegTuck → Stand — each phase has a physical
    /// success gate and a timeout-based failure path that re-enters Fallen.
    /// </summary>
    public class ProceduralStandUp : MonoBehaviour
    {
        // ─── Phase 0: Orient Prone ──────────────────────────────────────────
        [Header("Phase 0 — Orient Prone")]
        [SerializeField, Tooltip("Torque applied to roll the hips face-down.")]
        private float _orientTorque = 120f;

        [SerializeField, Tooltip("Maximum time (s) before advancing regardless of orientation.")]
        private float _orientTimeout = 0.6f;

        [SerializeField, Tooltip("Dot(hips.forward, down) above which the character counts as prone.")]
        private float _proneDotThreshold = 0.5f;

        // ─── Phase 1: Arm Push ──────────────────────────────────────────────
        [Header("Phase 1 — Arm Push")]
        [SerializeField, Tooltip("Upward force on chest during push-up.")]
        private float _armPushForce = 180f;

        [SerializeField, Tooltip("Arm spring multiplier during push.")]
        private float _armPushSpringMultiplier = 1.5f;

        [SerializeField, Tooltip("Chest height above ground that counts as success.")]
        private float _armPushTargetHeight = 0.25f;

        [SerializeField, Tooltip("Chest height below which arm push is considered failed at timeout.")]
        private float _armPushFailHeight = 0.1f;

        [SerializeField, Tooltip("Max time (s) before failure check.")]
        private float _armPushTimeout = 0.8f;

        [SerializeField, Range(0f, 1f), Tooltip("Partial height-maintenance scale restored during arm push so flat recoveries can lift before the final stand ramp.")]
        private float _armPushHeightSupportScale = 0.35f;

        [SerializeField, Range(0f, 1f), Tooltip("Partial upright-strength scale restored during arm push so lift does not lever the body farther onto its back.")]
        private float _armPushUprightSupportScale = 0.25f;

        [SerializeField, Range(0f, 1f), Tooltip("Partial COM stabilization restored during arm push to keep early lift centered over the support base.")]
        private float _armPushStabilizationSupportScale = 0.15f;

        // ─── Phase 2: Leg Tuck ──────────────────────────────────────────────
        [Header("Phase 2 — Leg Tuck")]
        [SerializeField, Tooltip("Leg spring multiplier during tuck.")]
        private float _legTuckSpringMultiplier = 1.2f;

        [SerializeField, Tooltip("Upward assist force on hips during tuck.")]
        private float _legTuckAssistForce = 80f;

        [SerializeField, Tooltip("Hips height above ground that counts as success.")]
        private float _legTuckTargetHeight = 0.2f;

        [SerializeField, Tooltip("Hips height below which tuck is considered failed at timeout.")]
        private float _legTuckFailHeight = 0.12f;

        [SerializeField, Tooltip("Max time (s) before failure check.")]
        private float _legTuckTimeout = 0.7f;

        [SerializeField, Range(0f, 1f), Tooltip("Height-maintenance scale restored during leg tuck while surrender is still active.")]
        private float _legTuckHeightSupportScale = 0.55f;

        [SerializeField, Range(0f, 1f), Tooltip("Upright-strength scale restored during leg tuck while surrender is still active.")]
        private float _legTuckUprightSupportScale = 0.5f;

        [SerializeField, Range(0f, 1f), Tooltip("COM stabilization restored during leg tuck while surrender is still active.")]
        private float _legTuckStabilizationSupportScale = 0.35f;

        // ─── Phase 3: Stand ─────────────────────────────────────────────────
        [Header("Phase 3 — Stand")]
        [SerializeField, Tooltip("Leg spring multiplier during final push to standing.")]
        private float _standSpringMultiplier = 2.0f;

        [SerializeField, Tooltip("Duration (s) for upright torque ramp-up.")]
        private float _standUprightRampDuration = 0.4f;

        [SerializeField, Tooltip("Duration (s) for height-maintenance ramp-up.")]
        private float _standHeightRampDuration = 0.3f;

        [SerializeField, Tooltip("Duration (s) for COM-stabilization ramp-up.")]
        private float _standStabilizationRampDuration = 0.3f;

        [SerializeField, Tooltip("Stabilization target scale during stand phase.")]
        private float _standStabilizationTarget = 0.8f;

        [SerializeField, Tooltip("Max time (s) before failure check.")]
        private float _standTimeout = 0.5f;

        [SerializeField, Tooltip("Fraction of standing hips height that counts as success.")]
        private float _standHeightFraction = 0.9f;

        // ─── General ────────────────────────────────────────────────────────
        [Header("General")]
        [SerializeField, Tooltip("Max stand-up attempts before forced stand safety net.")]
        private int _maxStandUpAttempts = 3;

        [SerializeField, Tooltip("Upward impulse applied as forced safety net after max failed attempts.")]
        private float _forcedStandImpulse = 350f;

        [SerializeField, Tooltip("Duration (s) for spring profile reset after stand completes.")]
        private float _completionSpringResetDuration = 0.2f;

        [SerializeField, Tooltip("Duration (s) for early-phase height support ramps before the final stand phase clears surrender.")]
        private float _phaseSupportRampDuration = 0.15f;

        // ─── References ─────────────────────────────────────────────────────
        [Header("References")]
        [SerializeField] private BalanceController _balanceController;
        [SerializeField] private RagdollSetup _ragdollSetup;

        // ─── Runtime State ──────────────────────────────────────────────────
        private Rigidbody _hipsRb;
        private Rigidbody _chestRb;
        private float _phaseTimer;
        private int _standUpAttempts;
        private float _severity;
        private float _groundY;

        // ─── Public API ─────────────────────────────────────────────────────

        /// <summary>Current stand-up phase. <see cref="StandUpPhase.Inactive"/> when idle.</summary>
        public StandUpPhase CurrentPhase { get; private set; } = StandUpPhase.Inactive;

        /// <summary>True while the stand-up sequence is running.</summary>
        public bool IsActive => CurrentPhase != StandUpPhase.Inactive;

        /// <summary>Raised when a phase completes successfully and the next phase begins.</summary>
        public event Action OnPhaseCompleted;

        /// <summary>Raised when a phase fails. The float parameter is the failure severity for re-knockdown.</summary>
        public event Action<float> OnFailed;

        /// <summary>Raised when the full stand-up sequence completes and the character is upright.</summary>
        public event Action OnCompleted;

        /// <summary>
        /// Begin the stand-up sequence. Called by <see cref="CharacterState"/> when
        /// entering GettingUp with a surrendered knockdown.
        /// </summary>
        /// <param name="severity">Knockdown severity (0–1) from the preceding fallen state.</param>
        public void Begin(float severity)
        {
            _severity = Mathf.Clamp01(severity);
            _standUpAttempts++;

            if (_standUpAttempts > _maxStandUpAttempts)
            {
                ApplyForcedStand();
                return;
            }

            CacheRigidbodies();
            CaptureGroundY();
            EnterPhase(StandUpPhase.OrientProne);
        }

        /// <summary>
        /// Abort the current stand-up sequence without firing failure or completion events.
        /// Used when external systems (e.g. ImpactKnockdownDetector) fully reset the knockdown.
        /// </summary>
        public void Abort()
        {
            if (!IsActive) return;
            CurrentPhase = StandUpPhase.Inactive;
            _phaseTimer = 0f;
            _standUpAttempts = 0;
        }

        // ─── Unity Lifecycle ────────────────────────────────────────────────

        private void Awake()
        {
            CacheRigidbodies();
        }

        private void FixedUpdate()
        {
            if (!IsActive) return;

            _phaseTimer += Time.fixedDeltaTime;

            switch (CurrentPhase)
            {
                case StandUpPhase.OrientProne:
                    TickOrientProne();
                    break;
                case StandUpPhase.ArmPush:
                    TickArmPush();
                    break;
                case StandUpPhase.LegTuck:
                    TickLegTuck();
                    break;
                case StandUpPhase.Stand:
                    TickStand();
                    break;
            }
        }

        // ─── Phase Transitions ──────────────────────────────────────────────

        private void EnterPhase(StandUpPhase phase)
        {
            CurrentPhase = phase;
            _phaseTimer = 0f;

            switch (phase)
            {
                case StandUpPhase.OrientProne:
                    // Keep limp springs — don't change profile yet.
                    break;

                case StandUpPhase.ArmPush:
                    _ragdollSetup.SetSpringProfile(
                        _armPushSpringMultiplier, 0.25f, 0.25f, 0.1f);
                    RestorePhaseSupport(
                        _armPushUprightSupportScale,
                        _armPushHeightSupportScale,
                        _armPushStabilizationSupportScale);
                    break;

                case StandUpPhase.LegTuck:
                    _ragdollSetup.SetSpringProfile(
                        _armPushSpringMultiplier * 0.5f,
                        _legTuckSpringMultiplier,
                        0.5f,
                        0.1f);
                    RestorePhaseSupport(
                        _legTuckUprightSupportScale,
                        _legTuckHeightSupportScale,
                        _legTuckStabilizationSupportScale);
                    break;

                case StandUpPhase.Stand:
                    _ragdollSetup.SetSpringProfile(
                        1f, _standSpringMultiplier, 1f, 0.15f);
                    _balanceController.ClearSurrender();
                    _balanceController.RampUprightStrength(1f, _standUprightRampDuration);
                    _balanceController.RampHeightMaintenance(1f, _standHeightRampDuration);
                    _balanceController.RampStabilization(
                        _standStabilizationTarget, _standStabilizationRampDuration);
                    break;
            }
        }

        private void AdvancePhase()
        {
            OnPhaseCompleted?.Invoke();

            switch (CurrentPhase)
            {
                case StandUpPhase.OrientProne:
                    EnterPhase(StandUpPhase.ArmPush);
                    break;

                case StandUpPhase.ArmPush:
                    EnterPhase(StandUpPhase.LegTuck);
                    break;

                case StandUpPhase.LegTuck:
                    EnterPhase(StandUpPhase.Stand);
                    break;

                case StandUpPhase.Stand:
                    CompleteStandUp();
                    break;
            }
        }

        private void Fail(float failureSeverity)
        {
            CurrentPhase = StandUpPhase.Inactive;
            _phaseTimer = 0f;
            OnFailed?.Invoke(failureSeverity);
        }

        private void CompleteStandUp()
        {
            CurrentPhase = StandUpPhase.Inactive;
            _phaseTimer = 0f;
            _standUpAttempts = 0;
            _ragdollSetup.ResetSpringProfile(_completionSpringResetDuration);
            OnCompleted?.Invoke();
        }

        // ─── Phase Ticks ────────────────────────────────────────────────────

        private void TickOrientProne()
        {
            if (_hipsRb == null) { AdvancePhase(); return; }

            float proneDot = Vector3.Dot(_hipsRb.transform.forward, Vector3.down);

            // Already face-down — skip immediately.
            if (proneDot >= _proneDotThreshold)
            {
                AdvancePhase();
                return;
            }

            // Apply rolling torque toward belly-down.
            Vector3 currentForward = _hipsRb.transform.forward;
            Vector3 desiredForward = Vector3.down;
            Vector3 rollAxis = Vector3.Cross(currentForward, desiredForward).normalized;
            if (rollAxis.sqrMagnitude > 0.001f)
            {
                _hipsRb.AddTorque(rollAxis * _orientTorque, ForceMode.Force);
            }

            // Timeout — advance regardless.
            if (_phaseTimer >= _orientTimeout)
            {
                AdvancePhase();
            }
        }

        private void TickArmPush()
        {
            Rigidbody pushTarget = _chestRb != null ? _chestRb : _hipsRb;
            if (pushTarget == null) { AdvancePhase(); return; }

            float chestHeight = pushTarget.position.y - _groundY;

            // Diminishing push force as we approach target.
            float progress = Mathf.Clamp01(chestHeight / _armPushTargetHeight);
            float force = _armPushForce * (1f - progress);
            pushTarget.AddForce(Vector3.up * force, ForceMode.Force);

            // Slight forward lean to prevent tipping backward.
            Vector3 leanDir = Vector3.ProjectOnPlane(_hipsRb.transform.forward, Vector3.up).normalized;
            if (leanDir.sqrMagnitude > 0.001f)
            {
                pushTarget.AddForce(leanDir * (force * 0.15f), ForceMode.Force);
            }

            // Success: chest is high enough.
            if (chestHeight >= _armPushTargetHeight)
            {
                AdvancePhase();
                return;
            }

            // Timeout failure check.
            if (_phaseTimer >= _armPushTimeout)
            {
                if (chestHeight < _armPushFailHeight)
                {
                    Fail(0.2f);
                }
                else
                {
                    // Close enough — proceed.
                    AdvancePhase();
                }
            }
        }

        private void TickLegTuck()
        {
            if (_hipsRb == null) { AdvancePhase(); return; }

            float hipsHeight = _hipsRb.position.y - _groundY;

            // Upward assist on hips.
            float progress = Mathf.Clamp01(hipsHeight / _legTuckTargetHeight);
            float assist = _legTuckAssistForce * (1f - progress);
            _hipsRb.AddForce(Vector3.up * assist, ForceMode.Force);

            // Keep upper body propped (reduced arm push force).
            if (_chestRb != null)
            {
                float chestHeight = _chestRb.position.y - _groundY;
                float chestAssist = _armPushForce * 0.5f * Mathf.Clamp01(1f - chestHeight / _armPushTargetHeight);
                _chestRb.AddForce(Vector3.up * chestAssist, ForceMode.Force);
            }

            // Success: hips high enough and grounded (foot contact).
            bool grounded = _balanceController != null && _balanceController.IsGrounded;
            if (hipsHeight >= _legTuckTargetHeight && grounded)
            {
                AdvancePhase();
                return;
            }

            // Timeout failure check.
            if (_phaseTimer >= _legTuckTimeout)
            {
                if (hipsHeight < _legTuckFailHeight)
                {
                    Fail(0.15f);
                }
                else
                {
                    AdvancePhase();
                }
            }
        }

        private void TickStand()
        {
            if (_hipsRb == null) { CompleteStandUp(); return; }

            float standingHeight = _balanceController != null
                ? _balanceController.StandingHipsHeight
                : 0.35f;
            float hipsHeight = _hipsRb.position.y - _groundY;
            bool isFallen = _balanceController != null && _balanceController.IsFallen;
            bool grounded = _balanceController != null && _balanceController.IsGrounded;

            // Success: near standing height, not fallen, grounded.
            if (hipsHeight >= standingHeight * _standHeightFraction && !isFallen && grounded)
            {
                CompleteStandUp();
                return;
            }

            // Timeout failure check.
            if (_phaseTimer >= _standTimeout)
            {
                if (isFallen)
                {
                    Fail(0.3f);
                }
                else
                {
                    // Close enough — complete.
                    CompleteStandUp();
                }
            }
        }

        // ─── Helpers ────────────────────────────────────────────────────────

        private void CacheRigidbodies()
        {
            if (_hipsRb != null) return;

            // RagdollSetup is attached to the Hips root.
            if (_ragdollSetup != null)
            {
                _hipsRb = _ragdollSetup.GetComponent<Rigidbody>();

                // The Torso is a direct child of Hips in the ragdoll hierarchy.
                Transform torso = _ragdollSetup.transform.Find("Torso");
                if (torso != null)
                {
                    _chestRb = torso.GetComponent<Rigidbody>();
                }
            }
        }

        private void CaptureGroundY()
        {
            if (_hipsRb != null)
            {
                // Use current lowest body position as ground estimate.
                _groundY = _hipsRb.position.y;

                if (_ragdollSetup != null)
                {
                    foreach (Rigidbody rb in _ragdollSetup.AllBodies)
                    {
                        if (rb.position.y < _groundY)
                        {
                            _groundY = rb.position.y;
                        }
                    }
                }
            }
        }

        private void RestorePhaseSupport(
            float uprightSupportScale,
            float heightSupportScale,
            float stabilizationSupportScale)
        {
            if (_balanceController == null)
            {
                return;
            }

            _balanceController.RampUprightStrength(uprightSupportScale, _phaseSupportRampDuration);
            _balanceController.RampHeightMaintenance(heightSupportScale, _phaseSupportRampDuration);
            _balanceController.RampStabilization(stabilizationSupportScale, _phaseSupportRampDuration);
        }

        private void ApplyForcedStand()
        {
            CacheRigidbodies();

            if (_hipsRb != null)
            {
                _hipsRb.AddForce(Vector3.up * _forcedStandImpulse, ForceMode.Impulse);
            }

            if (_balanceController != null)
            {
                _balanceController.ClearSurrender();
            }

            if (_ragdollSetup != null)
            {
                _ragdollSetup.ResetSpringProfile(0.1f);
            }

            _standUpAttempts = 0;
            CurrentPhase = StandUpPhase.Inactive;
            _phaseTimer = 0f;
            OnCompleted?.Invoke();
        }
    }

    /// <summary>
    /// Phases of the procedural stand-up sequence.
    /// </summary>
    public enum StandUpPhase
    {
        Inactive = 0,
        OrientProne = 1,
        ArmPush = 2,
        LegTuck = 3,
        Stand = 4,
    }
}
