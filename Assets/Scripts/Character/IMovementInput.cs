using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Common interface for movement input providers. Implemented by both
    /// <see cref="PlayerMovement"/> (human input) and AI locomotion systems
    /// so that <see cref="CharacterState"/> can drive the state machine
    /// without knowing the input source.
    /// </summary>
    public interface IMovementInput
    {
        /// <summary>Latest movement input as a 2D vector (X = strafe, Y = forward).</summary>
        Vector2 CurrentMoveInput { get; }
    }
}
