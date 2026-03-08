using System;
using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Tracks the high-level locomotion/posture state for a player ragdoll so other
    /// systems can react to deterministic gameplay state transitions.
    /// Part of the Character state machine system and driven by data from
    /// <see cref="BalanceController"/> and <see cref="PlayerMovement"/>.
    /// Lifecycle: caches dependencies in Awake and evaluates transitions in FixedUpdate.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(BalanceController))]
    public class CharacterState : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)]
        private float _moveEnterThreshold = 0.1f;

        [SerializeField, Range(0f, 1f)]
        private float _moveExitThreshold = 0.05f;

        [SerializeField, Range(0f, 10f)]
        private float _getUpDelay = 0.5f;

        [SerializeField, Range(0f, 10f)]
        private float _knockoutDuration = 1.5f;

        [SerializeField, Range(0f, 2000f)]
        private float _getUpForce = 250f;

        [SerializeField, Range(0.1f, 10f)]
        private float _getUpTimeout = 3f;

        private BalanceController _balanceController;
        private LocomotionCollapseDetector _collapseDetector;
        private PlayerMovement _playerMovement;
        private Rigidbody _rb;
        private float _fallenTimer;
        private float _gettingUpTimer;
        private int _getUpImpulseAppliedCount;
        private bool _enteredFallenFromCollapse;

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

        private void Awake()
        {
            // STEP 1: Cache BalanceController, collapse detection, PlayerMovement, and Rigidbody dependencies.
            TryGetComponent(out _balanceController);
            TryGetComponent(out _collapseDetector);
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

            // STEP 1: Track fallen timer only while grounded and in Fallen.
            if (CurrentState == CharacterStateType.Fallen && isGrounded)
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
                    if (isFallen || isLocomotionCollapsed)
                    {
                        nextState = CharacterStateType.Fallen;
                    }
                    else if (!isGrounded)
                    {
                        nextState = CharacterStateType.Airborne;
                    }
                    else if (wantsMove)
                    {
                        nextState = CharacterStateType.Moving;
                    }
                    break;

                case CharacterStateType.Moving:
                    if (isFallen || isLocomotionCollapsed)
                    {
                        nextState = CharacterStateType.Fallen;
                    }
                    else if (!isGrounded)
                    {
                        nextState = CharacterStateType.Airborne;
                    }
                    else if (stoppedMove)
                    {
                        nextState = CharacterStateType.Standing;
                    }
                    break;

                case CharacterStateType.Airborne:
                    if (isFallen || isLocomotionCollapsed)
                    {
                        nextState = CharacterStateType.Fallen;
                    }
                    else if (isGrounded)
                    {
                        nextState = wantsMove ? CharacterStateType.Moving : CharacterStateType.Standing;
                    }
                    break;

                case CharacterStateType.Fallen:
                    if (!isGrounded)
                    {
                        nextState = CharacterStateType.Airborne;
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
                    if (!isGrounded)
                    {
                        nextState = CharacterStateType.Airborne;
                    }
                    else if (_gettingUpTimer >= _getUpTimeout)
                    {
                        nextState = CharacterStateType.Standing;
                    }
                    else if (!isFallen)
                    {
                        nextState = wantsMove ? CharacterStateType.Moving : CharacterStateType.Standing;
                    }
                    break;
            }

            if (nextState != CurrentState && nextState == CharacterStateType.Fallen)
            {
                _enteredFallenFromCollapse = !isFallen && isLocomotionCollapsed;
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

            if (newState != CharacterStateType.Fallen)
            {
                _enteredFallenFromCollapse = false;
            }

            // STEP 3: Run state-entry behavior.
            if (newState == CharacterStateType.GettingUp)
            {
                ApplyGetUpImpulse();
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
