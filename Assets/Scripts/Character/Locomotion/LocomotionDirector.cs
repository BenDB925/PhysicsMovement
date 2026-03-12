using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Coordinates locomotion data flow on the Hips root by reading desired input and runtime
    /// observations, then publishing pass-through commands that future locomotion slices can
    /// hand to the execution systems without changing current behavior yet.
    /// </summary>
    [DefaultExecutionOrder(250)]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(PlayerMovement))]
    [RequireComponent(typeof(BalanceController))]
    [RequireComponent(typeof(CharacterState))]
    public class LocomotionDirector : MonoBehaviour
    {
        private const float CommandEpsilon = 0.0001f;

        [SerializeField]
        [Tooltip("Keeps the director on the legacy pass-through path while ownership is migrated.")]
        private bool _passThroughMode = true;

        private Rigidbody _hipsBody;
        private PlayerMovement _playerMovement;
        private BalanceController _balanceController;
        private CharacterState _characterState;
        private LocomotionCollapseDetector _collapseDetector;
        private LegAnimator _legAnimator;

        private DesiredInput _currentDesiredInput;
        private LocomotionObservation _currentObservation;
        private BodySupportCommand _currentBodySupportCommand;
        private LegCommandOutput _leftLegCommand;
        private LegCommandOutput _rightLegCommand;
        private Vector3 _previousMoveDirection;
        private int _recoveryFramesRemaining;

        public bool HasCommandFrame { get; private set; }

        public bool IsPassThroughMode => _passThroughMode;

        internal DesiredInput CurrentDesiredInput => _currentDesiredInput;

        internal LocomotionObservation CurrentObservation => _currentObservation;

        internal BodySupportCommand CurrentBodySupportCommand => _currentBodySupportCommand;

        internal LegCommandOutput LeftLegCommand => _leftLegCommand;

        internal LegCommandOutput RightLegCommand => _rightLegCommand;

        private void Awake()
        {
            // STEP 1: Cache the Hips-side locomotion collaborators required to build a single-frame snapshot.
            TryResolveDependencies();

            // STEP 2: Seed neutral pass-through commands so runtime consumers have a defined baseline before the first FixedUpdate.
            ResetOutputs();
        }

        private void FixedUpdate()
        {
            // STEP 1: Refresh the desired-input snapshot from PlayerMovement after the existing input loop has run.
            if (!TryResolveDependencies())
            {
                ResetOutputs();
                return;
            }

            _currentDesiredInput = _playerMovement.CurrentDesiredInput;

            // STEP 2: Refresh the locomotion observation snapshot from BalanceController, CharacterState, and the hips Rigidbody.
            _currentObservation = BuildObservation();

            // STEP 3: Emit pass-through support and legacy leg commands without changing current movement behavior.
            EmitPassThroughCommands();
            PushCommandsToExecutors();
            HasCommandFrame = true;
        }

        private void OnDisable()
        {
            if (_balanceController != null)
            {
                _balanceController.ClearBodySupportCommand();
            }

            if (_legAnimator != null)
            {
                _legAnimator.ClearCommandFrame();
            }

            _previousMoveDirection = Vector3.zero;
            _recoveryFramesRemaining = 0;
            ResetOutputs();
        }

        private LocomotionObservation BuildObservation()
        {
            bool isLocomotionCollapsed = _collapseDetector != null && _collapseDetector.IsCollapseConfirmed;

            return new LocomotionObservation(
                _characterState.CurrentState,
                _balanceController.IsGrounded,
                _balanceController.IsFallen,
                isLocomotionCollapsed,
                _balanceController.IsInSnapRecovery,
                _balanceController.UprightAngle,
                _hipsBody.linearVelocity,
                _hipsBody.angularVelocity,
                transform.forward,
                transform.up);
        }

        private void EmitPassThroughCommands()
        {
            Vector3 supportFacing = _currentDesiredInput.FacingDirection;
            if (supportFacing.sqrMagnitude < CommandEpsilon)
            {
                supportFacing = _currentObservation.BodyForward;
            }

            Vector3 moveDirection = _currentDesiredInput.MoveWorldDirection;
            if (_previousMoveDirection.sqrMagnitude > CommandEpsilon &&
                moveDirection.sqrMagnitude > CommandEpsilon &&
                Vector3.Dot(_previousMoveDirection, moveDirection) < 0.5f)
            {
                _recoveryFramesRemaining = _balanceController.SnapRecoveryDurationFrames;
            }

            int recoveryFramesThisStep = _recoveryFramesRemaining;
            float recoveryBlend = ComputeRecoveryBlend(recoveryFramesThisStep);

            int kdStartOffset = _balanceController.SnapRecoveryDurationFrames - _balanceController.SnapRecoveryKdDurationFrames;
            int recoveryFramesAfterSupport = recoveryFramesThisStep > 0 ? recoveryFramesThisStep - 1 : 0;
            int kdFrames = recoveryFramesAfterSupport - kdStartOffset;
            float recoveryKdBlend = ComputeRecoveryBlend(kdFrames);

            Vector3 supportTravel = moveDirection.sqrMagnitude > CommandEpsilon
                ? moveDirection
                : supportFacing;

            _currentBodySupportCommand = BodySupportCommand.PassThrough(
                supportFacing,
                supportTravel,
                recoveryBlend,
                recoveryKdBlend);

            if (_legAnimator != null && _passThroughMode)
            {
                _legAnimator.BuildPassThroughCommands(
                    _currentDesiredInput,
                    _currentObservation,
                    out _leftLegCommand,
                    out _rightLegCommand);
            }
            else
            {
                _leftLegCommand = LegCommandOutput.Disabled(LocomotionLeg.Left);
                _rightLegCommand = LegCommandOutput.Disabled(LocomotionLeg.Right);
            }

            if (moveDirection.sqrMagnitude > CommandEpsilon)
            {
                _previousMoveDirection = moveDirection;
            }

            if (recoveryFramesThisStep > 0)
            {
                _recoveryFramesRemaining = recoveryFramesThisStep - 1;
            }
        }

        private void PushCommandsToExecutors()
        {
            _balanceController.SetBodySupportCommand(_currentBodySupportCommand);

            if (_legAnimator == null)
            {
                return;
            }

            _legAnimator.SetCommandFrame(_currentDesiredInput, _currentObservation, _leftLegCommand, _rightLegCommand);
        }

        private bool TryResolveDependencies()
        {
            if (_hipsBody == null && !TryGetComponent(out _hipsBody))
            {
                return false;
            }

            if (_playerMovement == null && !TryGetComponent(out _playerMovement))
            {
                return false;
            }

            if (_balanceController == null && !TryGetComponent(out _balanceController))
            {
                return false;
            }

            if (_characterState == null && !TryGetComponent(out _characterState))
            {
                return false;
            }

            if (_legAnimator == null)
            {
                TryGetComponent(out _legAnimator);
            }

            if (_collapseDetector == null)
            {
                TryGetComponent(out _collapseDetector);
            }

            return true;
        }

        private void ResetOutputs()
        {
            Vector3 fallbackFacing = GetFallbackFacing();
            CharacterStateType fallbackState = _characterState != null
                ? _characterState.CurrentState
                : CharacterStateType.Standing;

            _currentDesiredInput = new DesiredInput(Vector2.zero, Vector3.zero, fallbackFacing, false);
            _currentObservation = new LocomotionObservation(
                fallbackState,
                false,
                false,
                false,
                false,
                0f,
                Vector3.zero,
                Vector3.zero,
                fallbackFacing,
                Vector3.up);
            _currentBodySupportCommand = BodySupportCommand.PassThrough(fallbackFacing);
            _leftLegCommand = LegCommandOutput.Disabled(LocomotionLeg.Left);
            _rightLegCommand = LegCommandOutput.Disabled(LocomotionLeg.Right);
            _previousMoveDirection = Vector3.zero;
            _recoveryFramesRemaining = 0;
            HasCommandFrame = false;
        }

        private static float ComputeRecoveryBlend(int framesRemaining)
        {
            const int RampFrames = 10;
            if (framesRemaining <= 0)
            {
                return 0f;
            }

            if (framesRemaining >= RampFrames)
            {
                return 1f;
            }

            return (float)framesRemaining / RampFrames;
        }

        private Vector3 GetFallbackFacing()
        {
            Vector3 planarForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (planarForward.sqrMagnitude > CommandEpsilon)
            {
                return planarForward.normalized;
            }

            return Vector3.forward;
        }
    }
}