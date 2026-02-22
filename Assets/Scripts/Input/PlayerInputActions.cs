using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PhysicsDrivenMovement.Input
{
    /// <summary>
    /// Wrapper for project player input actions.
    /// Defines the Player action map with Move, Look, Jump, Grab, and Punch actions.
    /// </summary>
    public sealed class PlayerInputActions : IDisposable
    {
        private readonly InputActionAsset _asset;
        private readonly InputActionMap _player;
        private readonly InputAction _playerMove;
        private readonly InputAction _playerLook;
        private readonly InputAction _playerJump;
        private readonly InputAction _playerGrab;
        private readonly InputAction _playerPunch;
        private readonly InputAction _playerSprint;

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

            _playerGrab = _player.AddAction("Grab", InputActionType.Button);
            _playerGrab.expectedControlType = "Button";

            _playerPunch = _player.AddAction("Punch", InputActionType.Button);
            _playerPunch.expectedControlType = "Button";

            _playerSprint = _player.AddAction("Sprint", InputActionType.Button);
            _playerSprint.expectedControlType = "Button";

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

            _playerGrab.AddBinding("<Keyboard>/leftCtrl");
            _playerGrab.AddBinding("<Gamepad>/leftTrigger");

            _playerPunch.AddBinding("<Mouse>/leftButton");
            _playerPunch.AddBinding("<Gamepad>/rightTrigger");

            _playerSprint.AddBinding("<Keyboard>/leftShift");
            _playerSprint.AddBinding("<Gamepad>/leftShoulder");
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

            public InputAction Grab => _wrapper._playerGrab;

            public InputAction Punch => _wrapper._playerPunch;

            public InputAction Sprint => _wrapper._playerSprint;

            public InputActionMap Get() => _wrapper._player;

            public void Enable() => Get().Enable();

            public void Disable() => Get().Disable();

            public bool enabled => Get().enabled;
        }
    }
}
