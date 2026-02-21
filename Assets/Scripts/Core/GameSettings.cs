using UnityEngine;

namespace PhysicsDrivenMovement.Core
{
    /// <summary>
    /// Singleton MonoBehaviour that applies authoritative global physics settings at startup.
    /// Ensures the project runs at 100 Hz fixed timestep with correct solver iterations regardless
    /// of what ProjectSettings UI shows, providing belt-and-suspenders guarantees.
    /// Also disables intra-player-layer collisions so ragdoll parts on the same player
    /// don't collide with each other at the physics-layer level.
    /// Lifecycle: Awake — applies all settings once.
    /// Collaborators: None (standalone setup).
    /// </summary>
    public class GameSettings : MonoBehaviour
    {
        // ─── Serialised Fields ──────────────────────────────────────────────

        [Header("Physics Timing")]
        [SerializeField, Range(0.005f, 0.05f)]
        [Tooltip("Fixed simulation step in seconds. 0.01 = 100 Hz. Required for joint stability.")]
        private float _fixedDeltaTime = 0.01f;

        [Header("Solver Quality")]
        [SerializeField, Range(1, 20)]
        [Tooltip("PhysX position solver iterations per step. 12 gives stable ConfigurableJoint stacks.")]
        private int _solverIterations = 12;

        [SerializeField, Range(1, 8)]
        [Tooltip("PhysX velocity solver iterations per step.")]
        private int _solverVelocityIterations = 4;

        // ─── Constants ──────────────────────────────────────────────────────

        /// <summary>Layer index assigned to Player 1 body parts.</summary>
        public const int LayerPlayer1Parts = 8;
        /// <summary>Layer index assigned to Player 2 body parts.</summary>
        public const int LayerPlayer2Parts = 9;
        /// <summary>Layer index assigned to Player 3 body parts.</summary>
        public const int LayerPlayer3Parts = 10;
        /// <summary>Layer index assigned to Player 4 body parts.</summary>
        public const int LayerPlayer4Parts = 11;
        /// <summary>Layer index for static environment geometry.</summary>
        public const int LayerEnvironment = 12;

        // ─── Unity Lifecycle ─────────────────────────────────────────────────

        private void Awake()
        {
            // STEP 1: Apply physics timing — must happen before the first FixedUpdate.
            Time.fixedDeltaTime = _fixedDeltaTime;

            // STEP 2: Apply solver quality settings.
            Physics.defaultSolverIterations = _solverIterations;
            Physics.defaultSolverVelocityIterations = _solverVelocityIterations;

            // STEP 3: Disable each player layer from self-colliding so ragdoll
            //         body parts belonging to the same player pass through each other
            //         at the broad-phase level (RagdollSetup handles the narrow-phase).
            SetupPlayerLayerCollisions();

            Debug.Log("[GameSettings] Physics configured: " +
                      $"fixedDeltaTime={Time.fixedDeltaTime}, " +
                      $"solverIterations={Physics.defaultSolverIterations}, " +
                      $"velocityIterations={Physics.defaultSolverVelocityIterations}");
        }

        // ─── Private Methods ─────────────────────────────────────────────────

        /// <summary>
        /// Disables intra-layer collisions for all four player body-part layers.
        /// Must be called before any Rigidbodies have been created in the scene.
        /// </summary>
        private static void SetupPlayerLayerCollisions()
        {
            Physics.IgnoreLayerCollision(LayerPlayer1Parts, LayerPlayer1Parts, true);
            Physics.IgnoreLayerCollision(LayerPlayer2Parts, LayerPlayer2Parts, true);
            Physics.IgnoreLayerCollision(LayerPlayer3Parts, LayerPlayer3Parts, true);
            Physics.IgnoreLayerCollision(LayerPlayer4Parts, LayerPlayer4Parts, true);
        }
    }
}
