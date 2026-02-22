using System.Collections.Generic;
using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Initialises ragdoll physics on the PlayerRagdoll prefab at startup.
    /// Responsible for calling Physics.IgnoreCollision on every pair of directly-connected
    /// body-part colliders (detected automatically from ConfigurableJoint.connectedBody links)
    /// to prevent jitter between adjacent segments.
    /// Attach to the Hips (root) GameObject of the ragdoll hierarchy.
    /// Lifecycle: Awake — scans joints and registers collision ignore pairs.
    /// Collaborators: <see cref="RagdollBone"/> (future), Unity ConfigurableJoint.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class RagdollSetup : MonoBehaviour
    {
        // ─── Serialised Fields ──────────────────────────────────────────────

        [SerializeField]
        [Tooltip("Log each collision-ignore pair to the console on startup. Useful during ragdoll tuning.")]
        private bool _debugSetup = false;

        // ─── Private Fields ──────────────────────────────────────────────────

        /// <summary>All Rigidbodies contained in this ragdoll, including the root.</summary>
        private Rigidbody[] _allBodies;

        // ─── Public Properties ────────────────────────────────────────────────

        /// <summary>
        /// All Rigidbodies that belong to this ragdoll hierarchy.
        /// Populated during Awake; returns null before then.
        /// </summary>
        public IReadOnlyList<Rigidbody> AllBodies => _allBodies;

        // ─── Unity Lifecycle ──────────────────────────────────────────────────

        private void Awake()
        {
            // STEP 1: Collect every Rigidbody in the hierarchy (including inactive parts)
            //         so other systems (balance controller, hit receiver) can reference them.
            _allBodies = GetComponentsInChildren<Rigidbody>(includeInactive: true);

            // STEP 2: Walk all ConfigurableJoints to discover direct neighbour pairs,
            //         then disable narrow-phase collision between them.
            DisableNeighboringCollisions();

            if (_debugSetup)
            {
                Debug.Log($"[RagdollSetup] '{name}' initialised with {_allBodies.Length} bodies.");
            }
        }

        private void Start()
        {
            // Ensure required gameplay components exist on this ragdoll so the
            // existing prefab/scene works without manual re-wiring.
            // Runs in Start (not Awake) so that tests and other code can add
            // components between Awake and Start without triggering errors from
            // downstream Awake() dependency checks.
            EnsureComponent<BalanceController>();
            EnsureComponent<PlayerMovement>();
            EnsureComponent<CharacterState>();
            EnsureComponent<LegAnimator>();

            // Wire CameraFollow onto the main camera if one exists.
            Camera mainCam = Camera.main;
            if (mainCam != null && !mainCam.TryGetComponent<CameraFollow>(out _))
            {
                mainCam.gameObject.AddComponent<CameraFollow>();
            }
        }

        // ─── Private Methods ──────────────────────────────────────────────────

        /// <summary>
        /// Adds <typeparamref name="T"/> to this GameObject if it is not already present.
        /// </summary>
        private T EnsureComponent<T>() where T : Component
        {
            if (!TryGetComponent<T>(out var existing))
            {
                existing = gameObject.AddComponent<T>();
            }
            return existing;
        }

        /// <summary>
        /// For every ConfigurableJoint in the hierarchy, retrieves the collider(s) on the
        /// joint owner and on its connected body, then calls Physics.IgnoreCollision for
        /// every cross-pairing. This eliminates jitter between segments that are physically
        /// joined but whose colliders would otherwise overlap.
        /// </summary>
        private void DisableNeighboringCollisions()
        {
            ConfigurableJoint[] allJoints =
                GetComponentsInChildren<ConfigurableJoint>(includeInactive: true);

            foreach (ConfigurableJoint joint in allJoints)
            {
                if (joint.connectedBody == null)
                {
                    continue;
                }

                Collider[] collidersOnJointBody  = joint.GetComponents<Collider>();
                Collider[] collidersOnParentBody = joint.connectedBody.GetComponents<Collider>();

                foreach (Collider colA in collidersOnJointBody)
                {
                    foreach (Collider colB in collidersOnParentBody)
                    {
                        Physics.IgnoreCollision(colA, colB, true);

                        if (_debugSetup)
                        {
                            Debug.Log($"[RagdollSetup] IgnoreCollision: " +
                                      $"{colA.gameObject.name} ↔ {colB.gameObject.name}");
                        }
                    }
                }
            }
        }
    }
}
