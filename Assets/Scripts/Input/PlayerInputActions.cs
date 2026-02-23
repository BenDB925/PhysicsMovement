using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PhysicsDrivenMovement.Input
{
    /// <summary>
    /// Wrapper for project player input actions.
    /// Defines the Player action map with Move, Look, Jump, LeftHand, and RightHand actions.
    /// LeftHand/RightHand are used by both grab (IsPressed) and punch (WasPressedThisFrame).
    /// </summary>
    public sealed class PlayerInputActions : IDisposable
    {
        private readonly InputActionAsset _asset;
        private readonly InputActionMap _player;
        private readonly InputAction _playerMove;
        private readonly InputAction _playerLook;
        private readonly InputAction _playerJump;
        private readonly InputAction _playerLeftHand;
        private readonly InputAction _playerRightHand;
        private readonly InputAction _playerRaiseHands;

        public PlayerInputActions()
        {
            _asset = ScriptableObject.CreateInstance<InputActionAsset>();
            _player = new InputActionMap("Player");

            _playerMove = _player.AddAction("Move", InputActionType.Value);
            _playerMove.expectedControlType = "Vector2";

            _playerLook = _player.AddAction("Look", InputActionType.Value);
            _playerLook.expectedControlType = "Vector2";

            _playerJump = _player.AddAction("Jump", InputActionType.Button);
            _playerJump.expectedControlType = "Button";

            _playerLeftHand = _player.AddAction("LeftHand", InputActionType.Button);
            _playerLeftHand.expectedControlType = "Button";

            _playerRightHand = _player.AddAction("RightHand", InputActionType.Button);
            _playerRightHand.expectedControlType = "Button";

            _playerRaiseHands = _player.AddAction("RaiseHands", InputActionType.Button);
            _playerRaiseHands.expectedControlType = "Button";

            AddMoveBindings();
            AddLookBindings();
            AddActionBindings();

            _asset.AddActionMap(_player);
        }

        public void Dispose()
        {
            UnityEngine.Object.Destroy(_asset);
        }

        public void Enable()
        {
            _asset.Enable();
        }

        public void Disable()
        {
            _asset.Disable();
        }

        public PlayerActions Player => new PlayerActions(this);

        private void AddMoveBindings()
        {
            _playerMove.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");

            _playerMove.AddBinding("<Gamepad>/leftStick");
        }

        private void AddLookBindings()
        {
            _playerLook.AddBinding("<Mouse>/delta");
            _playerLook.AddBinding("<Gamepad>/rightStick");
        }

        private void AddActionBindings()
        {
            _playerJump.AddBinding("<Keyboard>/space");
            _playerJump.AddBinding("<Gamepad>/buttonSouth");

            _playerLeftHand.AddBinding("<Mouse>/leftButton");
            _playerLeftHand.AddBinding("<Gamepad>/leftTrigger");

            _playerRightHand.AddBinding("<Mouse>/rightButton");
            _playerRightHand.AddBinding("<Gamepad>/rightTrigger");

            _playerRaiseHands.AddBinding("<Keyboard>/leftShift");
            _playerRaiseHands.AddBinding("<Gamepad>/buttonNorth");
        }

        public readonly struct PlayerActions
        {
            private readonly PlayerInputActions _wrapper;

            public PlayerActions(PlayerInputActions wrapper)
            {
                _wrapper = wrapper;
            }

            public InputAction Move => _wrapper._playerMove;

            public InputAction Look => _wrapper._playerLook;

            public InputAction Jump => _wrapper._playerJump;

            public InputAction LeftHand => _wrapper._playerLeftHand;

            public InputAction RightHand => _wrapper._playerRightHand;

            public InputAction RaiseHands => _wrapper._playerRaiseHands;

            public InputActionMap Get() => _wrapper._player;

            public void Enable() => Get().Enable();

            public void Disable() => Get().Disable();

            public bool enabled => Get().enabled;
        }
    }
}
