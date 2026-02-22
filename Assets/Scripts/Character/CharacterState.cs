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
    [RequireComponent(typeof(PlayerMovement))]
    public class CharacterState : MonoBehaviour
    {
        #pragma warning disable CS0414
        [SerializeField, Range(0f, 10f)]
        private float _getUpDelay = 0.5f;

        [SerializeField, Range(0f, 10f)]
        private float _knockoutDuration = 1.5f;

        [SerializeField, Range(0f, 2000f)]
        private float _getUpForce = 250f;
        #pragma warning restore CS0414

        private BalanceController _balanceController;
        private PlayerMovement _playerMovement;
        private Rigidbody _rb;

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
            // STEP 1: Cache BalanceController, PlayerMovement, and Rigidbody dependencies.
            TryGetComponent(out _balanceController);
            TryGetComponent(out _playerMovement);
            TryGetComponent(out _rb);

            // STEP 2: Validate required components and log errors when missing.
            if (_balanceController == null)
            {
                Debug.LogError("[CharacterState] Missing BalanceController.", this);
            }

            if (_playerMovement == null)
            {
                Debug.LogError("[CharacterState] Missing PlayerMovement.", this);
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
            // STEP 1: Read grounded/fallen/move-input signals from cached collaborators.
            // STEP 2: Apply deterministic state transition rules.
            // STEP 3: Route state mutations through ChangeState helper for centralized events.
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

            // STEP 3: Raise OnStateChanged event with previous/new values.
            OnStateChanged?.Invoke(previousState, newState);
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
