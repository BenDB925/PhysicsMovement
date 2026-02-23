using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Detects high-velocity collisions on the Head and triggers a knockout state by
    /// zeroing all joint SLERP drive springs for a configurable duration. The character
    /// goes fully limp (ragdoll), then recovers as drives are restored.
    ///
    /// Self-collisions (from the same ragdoll hierarchy) are excluded using
    /// <see cref="RagdollSetup.AllBodies"/>.
    ///
    /// The knockout naturally interacts with <see cref="CharacterState"/>: zeroed drives
    /// cause the character to fall, triggering the Fallen → GettingUp → Standing recovery
    /// cycle without any explicit coupling to the state machine.
    ///
    /// Attach to the Head GameObject of the ragdoll hierarchy.
    /// Lifecycle: Awake (cache self-bodies), Start (capture baseline drives),
    /// OnCollisionEnter (detect hits), FixedUpdate (tick knockout timer).
    /// Collaborators: <see cref="RagdollSetup"/>, <see cref="BalanceController"/>,
    /// <see cref="CharacterState"/>.
    /// </summary>
    public class HitReceiver : MonoBehaviour
    {
        // ── Serialized Fields ────────────────────────────────────────────────

        [SerializeField, Range(1f, 30f)]
        [Tooltip("Minimum relative collision velocity (m/s) required to trigger a knockout. " +
                 "Below this threshold, the hit is ignored. Default 8 m/s.")]
        private float _knockoutVelocityThreshold = 8f;

        [SerializeField, Range(0.5f, 10f)]
        [Tooltip("Duration (seconds) that all joint drives remain zeroed after a knockout hit. " +
                 "Character is fully limp during this period. Default 3s.")]
        private float _knockoutDuration = 3f;

        // ── Private Fields ───────────────────────────────────────────────────

        /// <summary>All Rigidbodies in this ragdoll, for self-collision exclusion.</summary>
        private System.Collections.Generic.HashSet<Rigidbody> _selfBodies;

        /// <summary>All ConfigurableJoints in this ragdoll.</summary>
        private ConfigurableJoint[] _allJoints;

        /// <summary>Baseline SLERP drives for every joint, captured in Start.</summary>
        private JointDrive[] _baselineDrives;

        /// <summary>Remaining knockout time. When >0, drives are zeroed.</summary>
        private float _knockoutTimer;

        private bool _isKnockedOut;

        // ── Public Properties ────────────────────────────────────────────────

        /// <summary>True while the character is in a knockout state (drives zeroed).</summary>
        public bool IsKnockedOut => _isKnockedOut;

        /// <summary>Remaining knockout time in seconds.</summary>
        public float KnockoutTimeRemaining => _knockoutTimer;

        // ── Test Seams ───────────────────────────────────────────────────────

        /// <summary>
        /// Test seam: directly trigger a knockout without requiring a physics collision.
        /// </summary>
        public void TriggerKnockoutForTest()
        {
            ApplyKnockout();
        }

        /// <summary>
        /// Injects the self-body set for test scenarios where RagdollSetup is not present.
        /// </summary>
        public void SetSelfBodiesForTest(System.Collections.Generic.HashSet<Rigidbody> selfBodies)
        {
            _selfBodies = selfBodies;
        }

        // ── Unity Lifecycle ──────────────────────────────────────────────────

        private void Awake()
        {
            // STEP 1: Cache self-ragdoll bodies for self-exclusion.
            RagdollSetup ragdollSetup = GetComponentInParent<RagdollSetup>();
            if (ragdollSetup != null && ragdollSetup.AllBodies != null)
            {
                _selfBodies = new System.Collections.Generic.HashSet<Rigidbody>(ragdollSetup.AllBodies);
            }
            else
            {
                Transform root = transform.root;
                Rigidbody[] bodies = root.GetComponentsInChildren<Rigidbody>(includeInactive: true);
                _selfBodies = new System.Collections.Generic.HashSet<Rigidbody>(bodies);
            }
        }

        private void Start()
        {
            // STEP 2: Cache all joints and their baseline drives.
            // Uses the root to find all joints in the ragdoll hierarchy.
            Transform root = transform.root;
            _allJoints = root.GetComponentsInChildren<ConfigurableJoint>(includeInactive: true);
            _baselineDrives = new JointDrive[_allJoints.Length];

            for (int i = 0; i < _allJoints.Length; i++)
            {
                _baselineDrives[i] = _allJoints[i].slerpDrive;
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            // Already knocked out — ignore stacking hits.
            if (_isKnockedOut)
                return;

            // Self-collision exclusion.
            if (collision.rigidbody != null && _selfBodies != null && _selfBodies.Contains(collision.rigidbody))
                return;

            // Check impact velocity.
            if (collision.relativeVelocity.magnitude < _knockoutVelocityThreshold)
                return;

            ApplyKnockout();
        }

        private void FixedUpdate()
        {
            if (!_isKnockedOut)
                return;

            _knockoutTimer -= Time.fixedDeltaTime;
            if (_knockoutTimer <= 0f)
            {
                RecoverFromKnockout();
            }
        }

        // ── Private Methods ──────────────────────────────────────────────────

        private void ApplyKnockout()
        {
            _isKnockedOut = true;
            _knockoutTimer = _knockoutDuration;

            // Zero all joint SLERP drive springs — character goes fully limp.
            if (_allJoints != null)
            {
                for (int i = 0; i < _allJoints.Length; i++)
                {
                    if (_allJoints[i] == null)
                        continue;

                    JointDrive zeroed = _allJoints[i].slerpDrive;
                    zeroed.positionSpring = 0f;
                    zeroed.positionDamper = 0f;
                    _allJoints[i].slerpDrive = zeroed;
                }
            }
        }

        private void RecoverFromKnockout()
        {
            _isKnockedOut = false;
            _knockoutTimer = 0f;

            // Restore all joint SLERP drives to their baseline values.
            if (_allJoints != null && _baselineDrives != null)
            {
                for (int i = 0; i < _allJoints.Length; i++)
                {
                    if (_allJoints[i] == null)
                        continue;

                    _allJoints[i].slerpDrive = _baselineDrives[i];
                }
            }
        }
    }
}
