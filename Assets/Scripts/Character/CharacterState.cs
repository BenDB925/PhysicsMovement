using System;
using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Tracks the high-level locomotion/posture state for a ragdoll so other
    /// systems can react to deterministic gameplay state transitions.
    /// Part of the Character state machine system and driven by data from
    /// <see cref="BalanceController"/> and any <see cref="IMovementInput"/> provider.
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
        private IMovementInput _movementInput;
        private Rigidbody _rb;
        private float _fallenTimer;
        private float _gettingUpTimer;
        private int _getUpImpulseAppliedCount;

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
            // STEP 1: Cache BalanceController, IMovementInput, and Rigidbody dependencies.
            TryGetComponent(out _balanceController);
            _movementInput = GetComponent<IMovementInput>();
            TryGetComponent(out _rb);

            // STEP 2: Validate required components and log errors when missing.
            if (_balanceController == null)
            {
                Debug.LogError("[CharacterState] Missing BalanceController.", this);
            }

            if (_movementInput == null)
            {
                Debug.LogError("[CharacterState] Missing IMovementInput (PlayerMovement or AILocomotion).", this);
            }

            if (_rb == null)
            {
                Debug.LogError("[CharacterState] Missing Rigidbody.", this);
            }

            // STEP 3: Initialize current state to Standing.
            CurrentState = CharacterStateType.Standing;
        }

        private void FixedUpdate()
        {
            if (_balanceController == null || _movementInput == null)
            {
                return;
            }

            float moveMagnitude = _movementInput.CurrentMoveInput.magnitude;
            bool isGrounded = _balanceController.IsGrounded;
            bool isFallen = _balanceController.IsFallen;
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
                    if (isFallen)
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
                    if (isFallen)
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
                    if (isFallen)
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

            ChangeState(nextState);
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
