using System;
using UnityEngine.InputSystem;

namespace PhysicsDrivenMovement.Input
{
    /// <summary>
    /// Wrapper for project player input actions.
    /// </summary>
    public sealed class PlayerInputActions : IDisposable
    {
        private readonly InputActionAsset _asset;
        private readonly InputActionMap _player;
        private readonly InputAction _playerMove;
        private readonly InputAction _playerJump;
        private readonly InputAction _playerGrab;
        private readonly InputAction _playerPunch;

        public PlayerInputActions()
        {
            _asset = InputActionAsset.FromJson(@"{
    \""name\"": \""PlayerInputActions\"",
    \""maps\"": [
        {
            \""name\"": \""Player\"",
            \""id\"": \""0e8ed341-58d3-4a04-ad67-afb070bc6f68\"",
            \""actions\"": [
                {
                    \""name\"": \""Move\"",
                    \""type\"": \""Value\"",
                    \""id\"": \""89b75c31-6ddb-44f2-9f5d-f9d09f6b78d9\"",
                    \""expectedControlType\"": \""Vector2\"",
                    \""processors\"": \"\"",
                    \""interactions\"": \"\"",
                    \""initialStateCheck\"": true
                },
                {
                    \""name\"": \""Jump\"",
                    \""type\"": \""Button\"",
                    \""id\"": \""9cb17e58-efb8-47a8-8668-8eff0a4659d2\"",
                    \""expectedControlType\"": \""Button\"",
                    \""processors\"": \"\"",
                    \""interactions\"": \"\"",
                    \""initialStateCheck\"": false
                },
                {
                    \""name\"": \""Grab\"",
                    \""type\"": \""Button\"",
                    \""id\"": \""2ba99152-95f0-46e1-979d-5f8343f28a5e\"",
                    \""expectedControlType\"": \""Button\"",
                    \""processors\"": \"\"",
                    \""interactions\"": \"\"",
                    \""initialStateCheck\"": false
                },
                {
                    \""name\"": \""Punch\"",
                    \""type\"": \""Button\"",
                    \""id\"": \""6458fe0a-b933-4234-b885-5dc5955c65c4\"",
                    \""expectedControlType\"": \""Button\"",
                    \""processors\"": \"\"",
                    \""interactions\"": \"\"",
                    \""initialStateCheck\"": false
                }
            ],
            \""bindings\"": [
                {
                    \""name\"": \""WASD\"",
                    \""id\"": \""64ff10f8-3438-4767-b113-ecfcb31f0e1c\"",
                    \""path\"": \""2DVector\"",
                    \""interactions\"": \"\"",
                    \""processors\"": \"\"",
                    \""groups\"": \"\"",
                    \""action\"": \""Move\"",
                    \""isComposite\"": true,
                    \""isPartOfComposite\"": false
                },
                {
                    \""name\"": \""up\"",
                    \""id\"": \""bbf54937-c4fc-4fcd-812a-7ff12078e638\"",
                    \""path\"": \""<Keyboard>/w\"",
                    \""interactions\"": \"\"",
                    \""processors\"": \"\"",
                    \""groups\"": \"\"",
                    \""action\"": \""Move\"",
                    \""isComposite\"": false,
                    \""isPartOfComposite\"": true
                },
                {
                    \""name\"": \""down\"",
                    \""id\"": \""2fef6cb2-1a30-4885-a28e-d32011374eb2\"",
                    \""path\"": \""<Keyboard>/s\"",
                    \""interactions\"": \"\"",
                    \""processors\"": \"\"",
                    \""groups\"": \"\"",
                    \""action\"": \""Move\"",
                    \""isComposite\"": false,
                    \""isPartOfComposite\"": true
                },
                {
                    \""name\"": \""left\"",
                    \""id\"": \""20caeac4-1e59-48f6-ba4f-bdfd1af8e750\"",
                    \""path\"": \""<Keyboard>/a\"",
                    \""interactions\"": \"\"",
                    \""processors\"": \"\"",
                    \""groups\"": \"\"",
                    \""action\"": \""Move\"",
                    \""isComposite\"": false,
                    \""isPartOfComposite\"": true
                },
                {
                    \""name\"": \""right\"",
                    \""id\"": \""911f8195-3f7f-4b06-b6ec-e5a4dc6630f4\"",
                    \""path\"": \""<Keyboard>/d\"",
                    \""interactions\"": \"\"",
                    \""processors\"": \"\"",
                    \""groups\"": \"\"",
                    \""action\"": \""Move\"",
                    \""isComposite\"": false,
                    \""isPartOfComposite\"": true
                },
                {
                    \""name\"": \"\"",
                    \""id\"": \""d1df0ddf-fbe0-4af3-acfb-11f91ef8b5e8\"",
                    \""path\"": \""<Gamepad>/leftStick\"",
                    \""interactions\"": \"\"",
                    \""processors\"": \"\"",
                    \""groups\"": \"\"",
                    \""action\"": \""Move\"",
                    \""isComposite\"": false,
                    \""isPartOfComposite\"": false
                },
                {
                    \""name\"": \"\"",
                    \""id\"": \""6f5e103f-2100-48cc-a34f-1a7bec518071\"",
                    \""path\"": \""<Keyboard>/space\"",
                    \""interactions\"": \"\"",
                    \""processors\"": \"\"",
                    \""groups\"": \"\"",
                    \""action\"": \""Jump\"",
                    \""isComposite\"": false,
                    \""isPartOfComposite\"": false
                },
                {
                    \""name\"": \"\"",
                    \""id\"": \""bcff6f96-38f0-4deb-ab01-abba2e17f230\"",
                    \""path\"": \""<Gamepad>/buttonSouth\"",
                    \""interactions\"": \"\"",
                    \""processors\"": \"\"",
                    \""groups\"": \"\"",
                    \""action\"": \""Jump\"",
                    \""isComposite\"": false,
                    \""isPartOfComposite\"": false
                },
                {
                    \""name\"": \"\"",
                    \""id\"": \""b52f9557-54e9-4c6c-a7e2-5a0ca4439ec6\"",
                    \""path\"": \""<Keyboard>/leftShift\"",
                    \""interactions\"": \"\"",
                    \""processors\"": \"\"",
                    \""groups\"": \"\"",
                    \""action\"": \""Grab\"",
                    \""isComposite\"": false,
                    \""isPartOfComposite\"": false
                },
                {
                    \""name\"": \"\"",
                    \""id\"": \""addac268-12d6-4cc7-a47b-7f940f87cb64\"",
                    \""path\"": \""<Gamepad>/leftTrigger\"",
                    \""interactions\"": \"\"",
                    \""processors\"": \"\"",
                    \""groups\"": \"\"",
                    \""action\"": \""Grab\"",
                    \""isComposite\"": false,
                    \""isPartOfComposite\"": false
                },
                {
                    \""name\"": \"\"",
                    \""id\"": \""ac978b37-7c30-433f-b6a2-cfd6c8f27fac\"",
                    \""path\"": \""<Mouse>/leftButton\"",
                    \""interactions\"": \"\"",
                    \""processors\"": \"\"",
                    \""groups\"": \"\"",
                    \""action\"": \""Punch\"",
                    \""isComposite\"": false,
                    \""isPartOfComposite\"": false
                },
                {
                    \""name\"": \"\"",
                    \""id\"": \""ec6e9cf8-d52f-4ec9-8e4b-ab98f1cc2735\"",
                    \""path\"": \""<Gamepad>/rightTrigger\"",
                    \""interactions\"": \"\"",
                    \""processors\"": \"\"",
                    \""groups\"": \"\"",
                    \""action\"": \""Punch\"",
                    \""isComposite\"": false,
                    \""isPartOfComposite\"": false
                }
            ]
        }
    ],
    \""controlSchemes\"": []
}");

            _player = _asset.FindActionMap("Player", throwIfNotFound: true);
            _playerMove = _player.FindAction("Move", throwIfNotFound: true);
            _playerJump = _player.FindAction("Jump", throwIfNotFound: true);
            _playerGrab = _player.FindAction("Grab", throwIfNotFound: true);
            _playerPunch = _player.FindAction("Punch", throwIfNotFound: true);
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

        public struct PlayerActions
        {
            private readonly PlayerInputActions _wrapper;

            public PlayerActions(PlayerInputActions wrapper)
            {
                _wrapper = wrapper;
            }

            public InputAction Move => _wrapper._playerMove;

            public InputAction Jump => _wrapper._playerJump;

            public InputAction Grab => _wrapper._playerGrab;

            public InputAction Punch => _wrapper._playerPunch;

            public InputActionMap Get() => _wrapper._player;

            public void Enable() => Get().Enable();

            public void Disable() => Get().Disable();

            public bool enabled => Get().enabled;
        }
    }
}