using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Immutable player locomotion intent captured at the input boundary before a coordinator
    /// chooses how the runtime should execute that intent.
    /// </summary>
    internal readonly struct DesiredInput
    {
        private const float IntentEpsilon = 0.0001f;

        public DesiredInput(Vector2 moveInput, Vector3 moveWorldDirection, Vector3 facingDirection, bool jumpRequested)
            : this(moveInput, moveWorldDirection, facingDirection, jumpRequested, 0f)
        {
        }

        public DesiredInput(
            Vector2 moveInput,
            Vector3 moveWorldDirection,
            Vector3 facingDirection,
            bool jumpRequested,
            float sprintNormalized)
        {
            // STEP 1: Clamp stick intent to a stable unit range at the contract boundary.
            MoveInput = Vector2.ClampMagnitude(moveInput, 1f);

            // STEP 2: Normalize world-space directions once so downstream systems can consume them directly.
            MoveWorldDirection = NormalizePlanarDirection(moveWorldDirection, Vector3.zero);
            FacingDirection = NormalizePlanarDirection(facingDirection, Vector3.forward);

            // STEP 3: Cache lightweight intent facts that future director logic will query repeatedly.
            JumpRequested = jumpRequested;
            MoveMagnitude = MoveInput.magnitude;
            HasMoveIntent = MoveMagnitude > IntentEpsilon;
            SprintNormalized = Mathf.Clamp01(sprintNormalized);
        }

        public Vector2 MoveInput { get; }

        public Vector3 MoveWorldDirection { get; }

        public Vector3 FacingDirection { get; }

        public bool JumpRequested { get; }

        public float MoveMagnitude { get; }

        public bool HasMoveIntent { get; }

        public float SprintNormalized { get; }

        private static Vector3 NormalizePlanarDirection(Vector3 rawDirection, Vector3 fallback)
        {
            Vector3 planarDirection = Vector3.ProjectOnPlane(rawDirection, Vector3.up);
            if (planarDirection.sqrMagnitude > IntentEpsilon)
            {
                return planarDirection.normalized;
            }

            if (fallback.sqrMagnitude > IntentEpsilon)
            {
                return fallback.normalized;
            }

            return Vector3.zero;
        }
    }
}