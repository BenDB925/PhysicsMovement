using System;
using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Tracks the high-level locomotion/posture state for a player ragdoll so other
    /// systems can react to deterministic gameplay state transitions.
    /// Part of the Character state machine system and driven by data from
    /// <see cref="BalanceController"/>, <see cref="PlayerMovement"/>,
    /// <see cref="LocomotionCollapseDetector"/>, and optionally
    /// <see cref="LocomotionDirector"/> (to defer collapse-triggered Fallen
    /// while an active recovery strategy is running).
    /// Lifecycle: caches dependencies in Awake and evaluates transitions in FixedUpdate.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(BalanceController))]
    public class CharacterState : MonoBehaviour
    {
        private const float SurrenderSeverityEpsilon = 0.0001f;

        [SerializeField, Range(0f, 1f)]
        private float _moveEnterThreshold = 0.1f;

        [SerializeField, Range(0f, 1f)]
        private float _moveExitThreshold = 0.05f;

        [SerializeField, Range(0f, 10f)]
        private float _getUpDelay = 0.5f;

        [SerializeField, Range(0.1f, 2f), Tooltip("Minimum time (s) the character must spend in GettingUp before it can exit to Standing/Moving. Prevents instant completion when the sequence gates pass immediately.")]
        private float _getUpMinDwellDuration = 0.4f;

        [SerializeField, Range(0f, 10f)]
        private float _knockoutDuration = 1.5f;

        [SerializeField, Range(0f, 10f)]
        private float _minFloorDwell = 1.5f;

        [SerializeField, Range(0f, 10f)]
        private float _maxFloorDwell = 3f;

        [SerializeField, Range(0f, 15f)]
        private float _reKnockdownFloorDwellCap = 4.5f;

        [SerializeField, Range(0f, 2000f)]
        private float _getUpForce = 250f;

        [SerializeField, Range(0.1f, 10f)]
        private float _getUpTimeout = 3f;

        [Header("Procedural Stand-Up")]
        [SerializeField, Tooltip("Optional ProceduralStandUp component. When assigned and the fall was surrender-driven, drives the physics stand-up sequence instead of the legacy impulse.")]
        private ProceduralStandUp _proceduralStandUp;

        [Header("Collapse Deferral")]
        [SerializeField, Range(0f, 3f), Tooltip("Maximum seconds collapse→Fallen can be deferred while director recovery is active.")]
        private float _collapseDeferralLimit = 1f;

        private BalanceController _balanceController;
        private LocomotionCollapseDetector _collapseDetector;
        private LocomotionDirector _locomotionDirector;
        private PlayerMovement _playerMovement;
        private Rigidbody _rb;
        private float _fallenTimer;
        private float _gettingUpTimer;
        private float _collapseDeferralTimer;
        private float _airborneLimboTimer;
        private Vector3 _limboPositionSample;
        private float _limboPositionSampleTimer;
        private float _limboForcedDwellTimer;
        private float _activeFloorDwellTargetTime;
        private int _getUpImpulseAppliedCount;
        private bool _enteredFallenFromCollapse;
        private bool _proceduralStandUpActive;
        private int _surrenderCountAtGettingUpEntry;

        /// <summary>
        /// Raised when <see cref="CurrentState"/> changes.
        /// First argument is previous state, second argument is new state.
        /// </summary>
#pragma warning disable CS0067
        public event Action<CharacterStateType, CharacterStateType> OnStateChanged;
        #pragma warning restore CS0067

        /// <summary>
        /// Current state of this character's locomotion/posture finite-state machine.
        /// </summary>
        public CharacterStateType CurrentState { get; private set; } = CharacterStateType.Standing;

        /// <summary>
        /// True when the most recent Fallen entry was caused by a surrender
        /// (as opposed to a simple angle-based or collapse-based fall).
        /// Reset on exit from Fallen.
        /// </summary>
        public bool WasSurrendered { get; private set; }

        /// <summary>
        /// Knockdown severity (0–1) captured at Fallen entry when <see cref="WasSurrendered"/> is true.
        /// Consumed by floor-dwell timing (Ch3) and stand-up difficulty (Ch4).
        /// </summary>
        public float KnockdownSeverityValue { get; private set; }

        private void Awake()
        {
            // STEP 1: Cache BalanceController, collapse detection, LocomotionDirector, PlayerMovement, and Rigidbody dependencies.
            TryGetComponent(out _balanceController);
            TryGetComponent(out _collapseDetector);
            TryGetComponent(out _locomotionDirector);
            TryGetComponent(out _playerMovement);
            TryGetComponent(out _rb);

            if (_collapseDetector == null)
            {
                _collapseDetector = gameObject.AddComponent<LocomotionCollapseDetector>();
            }

            // STEP 2: Validate required components and log errors when missing.
            if (_balanceController == null)
            {
                Debug.LogError("[CharacterState] Missing BalanceController.", this);
            }

            if (_rb == null)
            {
                Debug.LogError("[CharacterState] Missing Rigidbody.", this);
            }

            // STEP 3: Allow PlayerMovement to be absent during Awake and resolve it later.
            // Some test rigs and staged runtime setup attach CharacterState before PlayerMovement.

            // STEP 4: Initialize current state to Standing.
            CurrentState = CharacterStateType.Standing;
        }

        private void FixedUpdate()
        {
            if (_balanceController == null)
            {
                return;
            }

            if (_playerMovement == null)
            {
                TryGetComponent(out _playerMovement);
            }

            if (_collapseDetector == null)
            {
                TryGetComponent(out _collapseDetector);
            }

            if (_playerMovement == null)
            {
                return;
            }

            float moveMagnitude = _playerMovement.CurrentMoveInput.magnitude;
            bool isGrounded = _balanceController.IsGrounded;
            bool isFallen = _balanceController.IsFallen;
            bool isLocomotionCollapsed = _collapseDetector != null && _collapseDetector.IsCollapseConfirmed;
            bool wantsMove = moveMagnitude >= _moveEnterThreshold;
            bool stoppedMove = moveMagnitude <= _moveExitThreshold;
            bool shouldTreatJumpLaunchAsAirborne = _playerMovement.ShouldTreatJumpLaunchAsAirborne;
            bool shouldBeAirborne = !isGrounded || shouldTreatJumpLaunchAsAirborne;

            // STEP 0.5: Collapse deferral — when the collapse detector fires but
            // the director has an active recovery strategy, defer the Fallen
            // transition for up to _collapseDeferralLimit seconds so the recovery
            // has a chance to save the character. Angle-based isFallen is never
            // deferred because it means the body is already past the posture
            // threshold.
            bool isRecoveryDeferringCollapse = false;
            if (isLocomotionCollapsed && !isFallen)
            {
                bool directorRecoveryActive = _locomotionDirector != null &&
                                              _locomotionDirector.IsRecoveryActive;
                if (directorRecoveryActive && _collapseDeferralTimer < _collapseDeferralLimit)
                {
                    _collapseDeferralTimer += Time.fixedDeltaTime;
                    isRecoveryDeferringCollapse = true;
                }
            }

            if (!isLocomotionCollapsed)
            {
                _collapseDeferralTimer = 0f;
            }

            bool collapseTriggersfall = isLocomotionCollapsed && !isRecoveryDeferringCollapse;

            // STEP 1: Track fallen timer while in Fallen.
            // During surrender the character may lie flat on its back with feet off the
            // ground, so foot-sensor grounding is unreliable. Keep counting the floor
            // dwell timer regardless of isGrounded when WasSurrendered is true.
            if (CurrentState == CharacterStateType.Fallen && (isGrounded || WasSurrendered))
            {
                _fallenTimer += Time.fixedDeltaTime;
            }
            else
            {
                _fallenTimer = 0f;
            }

            // STEP 2: Track elapsed time while in GettingUp for timeout safety.
            if (CurrentState == CharacterStateType.GettingUp)
            {
                _gettingUpTimer += Time.fixedDeltaTime;
            }
            else
            {
                _gettingUpTimer = 0f;
            }

            CharacterStateType nextState = CurrentState;

            // STEP 3: Resolve deterministic transitions for all locomotion states.
            switch (CurrentState)
            {
                case CharacterStateType.Standing:
                    if (_limboForcedDwellTimer > 0f)
                        _limboForcedDwellTimer -= Time.fixedDeltaTime;

                    if (isFallen || collapseTriggersfall)
                    {
                        nextState = CharacterStateType.Fallen;
                    }
                    else if (shouldBeAirborne && _limboForcedDwellTimer <= 0f)
                    {
                        nextState = CharacterStateType.Airborne;
                    }
                    else if (wantsMove)
                    {
                        nextState = CharacterStateType.Moving;
                    }
                    break;

                case CharacterStateType.Moving:
                    if (isFallen || collapseTriggersfall)
                    {
                        nextState = CharacterStateType.Fallen;
                    }
                    else if (shouldBeAirborne)
                    {
                        nextState = CharacterStateType.Airborne;
                    }
                    else if (stoppedMove)
                    {
                        nextState = CharacterStateType.Standing;
                    }
                    break;

                case CharacterStateType.Airborne:
                    if (isFallen || collapseTriggersfall)
                    {
                        nextState = CharacterStateType.Fallen;
                    }
                    else if (!shouldBeAirborne && isGrounded)
                    {
                        _airborneLimboTimer = 0f;
                        nextState = wantsMove ? CharacterStateType.Moving : CharacterStateType.Standing;
                    }
                    else
                    {
                        // Limbo escape: stuck on geometry (e.g. knees on ledge, feet dangling).
                        // Checks displacement over a 0.5s window — catches cases where move input
                        // keeps velocity above zero but the character isn't actually going anywhere.
                        bool uprightEnough = _balanceController.UprightAngle < 45f;
                        if (_limboPositionSampleTimer == 0f && _rb != null)
                            _limboPositionSample = _rb.position; // seed on first frame
                        _limboPositionSampleTimer += Time.fixedDeltaTime;
                        if (_limboPositionSampleTimer >= 0.5f)
                        {
                            Vector3 currentPos = _rb != null ? _rb.position : transform.position;
                            float displaced = Vector3.Distance(currentPos, _limboPositionSample);
                            bool likelyStuck = displaced < 0.1f && uprightEnough; // <10cm in 0.5s = stuck
                            if (likelyStuck)
                                _airborneLimboTimer += 0.5f;
                            else
                                _airborneLimboTimer = Mathf.Max(0f, _airborneLimboTimer - 0.5f);
                            _limboPositionSample = currentPos;
                            _limboPositionSampleTimer = 0f;
                        }

                        if (_airborneLimboTimer >= 1.5f)
                        {
                            _airborneLimboTimer = 0f;
                            _limboPositionSampleTimer = 0f;
                            _limboForcedDwellTimer = 0.4f;
                            nextState = CharacterStateType.Standing;
                            ApplyLimboEscapeNudge();
                        }
                    }
                    break;

                case CharacterStateType.Fallen:
                    if (!isGrounded && !WasSurrendered)
                    {
                        nextState = CharacterStateType.Airborne;
                    }
                    else if (TryRefreshSurrenderFloorDwell())
                    {
                        nextState = CharacterStateType.Fallen;
                    }
                    else if (WasSurrendered)
                    {
                        if (_fallenTimer >= _activeFloorDwellTargetTime)
                        {
                            nextState = CharacterStateType.GettingUp;
                        }
                    }
                    else if (_enteredFallenFromCollapse && !isFallen && !isLocomotionCollapsed)
                    {
                        nextState = wantsMove ? CharacterStateType.Moving : CharacterStateType.Standing;
                    }
                    else if (_fallenTimer >= _getUpDelay && _fallenTimer >= _knockoutDuration)
                    {
                        nextState = CharacterStateType.GettingUp;
                    }
                    break;

                case CharacterStateType.GettingUp:
                    // Re-knockdown: an external impact called TriggerSurrender while
                    // already surrendered. The counter increment signals a new hit that
                    // should abort getting up and re-enter Fallen.
                    if (_balanceController != null &&
                        _balanceController.SurrenderTriggerCount > _surrenderCountAtGettingUpEntry)
                    {
                        CleanUpProceduralStandUp();
                        nextState = CharacterStateType.Fallen;
                    }
                    // During surrender recovery, suppress the Airborne transition so
                    // the balance ramp and get-up impulse have time to right the character.
                    // The _getUpTimeout safety net catches genuinely stuck cases.
                    else if (!isGrounded && !WasSurrendered)
                    {
                        nextState = CharacterStateType.Airborne;
                    }
                    else if (_gettingUpTimer >= _getUpTimeout)
                    {
                        CleanUpProceduralStandUp();
                        nextState = CharacterStateType.Standing;
                    }
                    else if (!_proceduralStandUpActive && !isFallen && _gettingUpTimer >= _getUpMinDwellDuration)
                    {
                        nextState = wantsMove ? CharacterStateType.Moving : CharacterStateType.Standing;
                    }
                    break;
            }

            if (nextState != CurrentState && nextState == CharacterStateType.Fallen)
            {
                CaptureFallenEntryState(isFallen, isLocomotionCollapsed);
            }

            ChangeState(nextState);
        }

        /// <summary>
        /// Test seam: directly force a state transition, bypassing FixedUpdate evaluation.
        /// Fires OnStateChanged so subscribers (e.g. LegAnimator spring scaling) react
        /// exactly as they would during a real transition.
        /// <br/>
        /// <b>Test use only.</b> Do not call from production code.
        /// </summary>
        public void SetStateForTest(CharacterStateType state)
        {
            ChangeState(state);
        }

        private void ChangeState(CharacterStateType newState)
        {
            // STEP 1: Exit early when state is unchanged.
            if (newState == CurrentState)
            {
                return;
            }

            // STEP 2: Capture previous state and assign CurrentState.
            CharacterStateType previousState = CurrentState;
            CurrentState = newState;

            // Clear Fallen-specific state when leaving Fallen for anything other than
            // GettingUp or a transient Airborne bounce. GettingUp inherits WasSurrendered
            // so it can choose the ProceduralStandUp path and gate its Airborne transition.
            // Airborne is also preserved because the ragdoll can briefly leave the ground
            // while still in a surrendered fall (grounding raycast flicker) — clearing
            // WasSurrendered on those bounces causes GettingUp to skip ProceduralStandUp.
            bool leavingFallen = previousState == CharacterStateType.Fallen;
            bool preserveSurrender = newState == CharacterStateType.GettingUp
                || (leavingFallen && newState == CharacterStateType.Airborne)
                || (newState == CharacterStateType.Fallen && WasSurrendered); // Airborne→Fallen bounce during crumple
            if (!preserveSurrender)
            {
                _enteredFallenFromCollapse = false;
                WasSurrendered = false;
                KnockdownSeverityValue = 0f;
                _activeFloorDwellTargetTime = 0f;
            }

            // STEP 3: Run state-entry behavior.
            if (newState == CharacterStateType.GettingUp)
            {
                _surrenderCountAtGettingUpEntry = _balanceController != null
                    ? _balanceController.SurrenderTriggerCount
                    : 0;

                if (WasSurrendered && _proceduralStandUp != null)
                {
                    BeginProceduralStandUp();
                }
                else if (WasSurrendered && _balanceController != null && _balanceController.IsSurrendered)
                {
                    // Surrender fallback: restore balance torques and joint springs but
                    // skip the legacy get-up impulse. At extreme surrender angles (>80°)
                    // a vertical impulse overshoots violently; letting the restored
                    // upright torque gradually right the character is more stable.
                    _balanceController.ClearSurrender();
                }
                else
                {
                    ApplyGetUpImpulse();
                }
            }

            if (previousState == CharacterStateType.GettingUp)
            {
                CleanUpProceduralStandUp();
            }

            // STEP 4: Raise OnStateChanged event with previous/new values.
            OnStateChanged?.Invoke(previousState, newState);
        }

        private void ApplyGetUpImpulse()
        {
            if (_rb == null)
            {
                return;
            }

            _rb.AddForce(Vector3.up * _getUpForce, ForceMode.Impulse);
            _getUpImpulseAppliedCount++;
        }

        private void CaptureFallenEntryState(bool isFallen, bool isLocomotionCollapsed)
        {
            _enteredFallenFromCollapse = !isFallen && isLocomotionCollapsed;
            _activeFloorDwellTargetTime = 0f;

            if (_balanceController != null && _balanceController.IsSurrendered)
            {
                WasSurrendered = true;
                KnockdownSeverityValue = _balanceController.SurrenderSeverity;
                _activeFloorDwellTargetTime = ComputeFloorDwellDuration(KnockdownSeverityValue);
                return;
            }

            WasSurrendered = false;
            KnockdownSeverityValue = 0f;
        }

        private bool TryRefreshSurrenderFloorDwell()
        {
            if (_balanceController == null || !_balanceController.IsSurrendered)
            {
                return false;
            }

            float observedSeverity = Mathf.Max(KnockdownSeverityValue, _balanceController.SurrenderSeverity);
            bool isNewSurrender = !WasSurrendered;
            bool severityIncreased = observedSeverity > KnockdownSeverityValue + SurrenderSeverityEpsilon;
            if (!isNewSurrender && !severityIncreased)
            {
                return false;
            }

            WasSurrendered = true;
            KnockdownSeverityValue = observedSeverity;
            float refreshedFloorDwell = _fallenTimer + ComputeFloorDwellDuration(observedSeverity);
            _activeFloorDwellTargetTime = Mathf.Min(refreshedFloorDwell, _reKnockdownFloorDwellCap);
            return true;
        }

        private float ComputeFloorDwellDuration(float severity)
        {
            return Mathf.Lerp(_minFloorDwell, _maxFloorDwell, Mathf.Clamp01(severity));
        }

        // ─── Procedural Stand-Up Wiring ─────────────────────────────────────

        private void BeginProceduralStandUp()
        {
            _proceduralStandUpActive = true;
            _proceduralStandUp.OnCompleted += HandleStandUpCompleted;
            _proceduralStandUp.OnFailed += HandleStandUpFailed;
            _proceduralStandUp.Begin(KnockdownSeverityValue);
        }

        private void CleanUpProceduralStandUp()
        {
            if (!_proceduralStandUpActive) return;
            _proceduralStandUpActive = false;
            _proceduralStandUp.OnCompleted -= HandleStandUpCompleted;
            _proceduralStandUp.OnFailed -= HandleStandUpFailed;

            if (_proceduralStandUp.IsActive)
            {
                _proceduralStandUp.Abort();
            }
        }

        private void HandleStandUpCompleted()
        {
            if (CurrentState != CharacterStateType.GettingUp) return;
            CleanUpProceduralStandUp();
            // Do not exit immediately — let the FixedUpdate dwell check handle the
            // transition so the minimum dwell gate (_getUpMinDwellDuration) is respected.
            // _proceduralStandUpActive is now false; FixedUpdate will exit on next frame
            // after the dwell has elapsed.
        }

        private void HandleStandUpFailed(float failureSeverity)
        {
            if (CurrentState != CharacterStateType.GettingUp) return;
            CleanUpProceduralStandUp();

            // Re-enter Fallen with the failure severity for a short re-dwell.
            WasSurrendered = true;
            KnockdownSeverityValue = failureSeverity;
            _activeFloorDwellTargetTime = ComputeFloorDwellDuration(failureSeverity);
            _fallenTimer = 0f;
            _enteredFallenFromCollapse = false;
            ChangeState(CharacterStateType.Fallen);
        }

    /// <summary>
    /// Applies a physics nudge to escape geometry when the character is stuck in Airborne limbo.
    /// Uses ComputePenetration to push away from overlapping colliders, plus a small upward impulse.
    /// </summary>
    private void ApplyLimboEscapeNudge()
    {        if (_rb == null) return;

        const float NudgeImpulse   = 3f;   // upward impulse to clear geometry
        const float SeparationImpulse = 4f; // per-collider separation impulse
        const float CheckRadius    = 0.7f;  // sphere around hips to find nearby colliders

        Collider[] nearby = Physics.OverlapSphere(_rb.position, CheckRadius,
            Physics.AllLayers, QueryTriggerInteraction.Ignore);

        Collider[] selfColliders = GetComponentsInChildren<Collider>(includeInactive: true);
        Collider hipsCollider = _rb.GetComponent<Collider>();

        Vector3 separationDir = Vector3.zero;
        int hitCount = 0;

        foreach (Collider other in nearby)
        {
            if (other == null || other.isTrigger) continue;

            bool isSelf = false;
            for (int i = 0; i < selfColliders.Length; i++)
            {
                if (selfColliders[i] == other) { isSelf = true; break; }
            }
            if (isSelf) continue;

            if (hipsCollider != null && Physics.ComputePenetration(
                hipsCollider, _rb.position, _rb.rotation,
                other, other.transform.position, other.transform.rotation,
                out Vector3 dir, out float dist))
            {
                separationDir += dir * dist;
                hitCount++;
            }
        }

        if (hitCount > 0)
            _rb.AddForce(separationDir.normalized * SeparationImpulse, ForceMode.Impulse);

        // Always nudge upward to help clear the geometry.
        _rb.AddForce(Vector3.up * NudgeImpulse, ForceMode.Impulse);
    }
}

/// <summary>
/// High-level locomotion/posture states for ragdoll gameplay logic.
/// </summary>
public enum CharacterStateType
{
        Standing = 0,
        Moving = 1,
        Airborne = 2,
        Fallen = 3,
        GettingUp = 4
    }
}
