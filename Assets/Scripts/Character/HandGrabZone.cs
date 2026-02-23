using System.Collections.Generic;
using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Trigger-based detection zone attached to each Hand (Hand_L, Hand_R) that tracks
    /// nearby external Rigidbodies eligible for grabbing. Self-ragdoll bodies are excluded
    /// using <see cref="RagdollSetup.AllBodies"/>.
    ///
    /// Exposes <see cref="NearestTarget"/> for <see cref="GrabController"/> to query when
    /// the player presses grab input. Also provides <see cref="CreateGrabJoint"/> and
    /// <see cref="DestroyGrabJoint"/> for the grab mechanic's FixedJoint lifecycle.
    ///
    /// Follows the same decentralised sensor pattern as <see cref="GroundSensor"/> on feet.
    ///
    /// Lifecycle: Awake (cache self-ragdoll bodies), OnTriggerEnter/Exit (track overlaps),
    /// FixedUpdate (prune destroyed objects).
    /// Collaborators: <see cref="GrabController"/>, <see cref="RagdollSetup"/>.
    /// </summary>
    [DefaultExecutionOrder(-15)]
    public class HandGrabZone : MonoBehaviour
    {
        // ── Serialized Fields ────────────────────────────────────────────────

        [SerializeField, Range(0.05f, 0.5f)]
        [Tooltip("Radius of the trigger sphere used to detect grabbable objects. " +
                 "Should be slightly larger than the hand collider. Default 0.15 m.")]
        private float _detectionRadius = 0.15f;

        // ── Private Fields ───────────────────────────────────────────────────

        /// <summary>All Rigidbodies belonging to this character's ragdoll, used for self-exclusion.</summary>
        private HashSet<Rigidbody> _selfBodies;

        /// <summary>Currently overlapping external Rigidbodies (not part of this ragdoll).</summary>
        private readonly List<Rigidbody> _overlapping = new List<Rigidbody>();

        /// <summary>The trigger SphereCollider used for detection.</summary>
        private SphereCollider _triggerCollider;

        /// <summary>Active grab FixedJoint, if any.</summary>
        private FixedJoint _grabJoint;

        // ── Public Properties ────────────────────────────────────────────────

        /// <summary>
        /// The closest overlapping external Rigidbody, or null if nothing is in range.
        /// Computed on demand from the current overlap list.
        /// </summary>
        public Rigidbody NearestTarget
        {
            get
            {
                PruneDestroyedEntries();

                if (_overlapping.Count == 0)
                    return null;

                Rigidbody nearest = null;
                float nearestDistSq = float.MaxValue;
                Vector3 handPos = transform.position;

                for (int i = 0; i < _overlapping.Count; i++)
                {
                    Rigidbody rb = _overlapping[i];
                    float distSq = (rb.position - handPos).sqrMagnitude;
                    if (distSq < nearestDistSq)
                    {
                        nearestDistSq = distSq;
                        nearest = rb;
                    }
                }

                return nearest;
            }
        }

        /// <summary>True when a grab FixedJoint is active on this hand.</summary>
        public bool IsGrabbing => _grabJoint != null;

        /// <summary>The Rigidbody currently grabbed by this hand, or null.</summary>
        public Rigidbody GrabbedTarget => _grabJoint != null ? _grabJoint.connectedBody : null;

        /// <summary>The trigger SphereCollider, exposed for tests.</summary>
        public SphereCollider TriggerCollider => _triggerCollider;

        // ── Public Methods ───────────────────────────────────────────────────

        /// <summary>
        /// Creates a FixedJoint from this hand to the given target Rigidbody.
        /// Returns false if a joint already exists or <paramref name="target"/> is null.
        /// </summary>
        public bool CreateGrabJoint(Rigidbody target, float breakForce, float breakTorque)
        {
            if (target == null || _grabJoint != null)
                return false;

            Rigidbody handRb = GetComponent<Rigidbody>();
            if (handRb == null)
                return false;

            _grabJoint = gameObject.AddComponent<FixedJoint>();
            _grabJoint.connectedBody = target;
            _grabJoint.breakForce = breakForce;
            _grabJoint.breakTorque = breakTorque;
            return true;
        }

        /// <summary>
        /// Destroys the active grab FixedJoint if one exists.
        /// </summary>
        public void DestroyGrabJoint()
        {
            if (_grabJoint != null)
            {
                Destroy(_grabJoint);
                _grabJoint = null;
            }
        }

        /// <summary>
        /// Called by GrabController when a FixedJoint breaks (OnJointBreak).
        /// Clears the internal reference so IsGrabbing returns false.
        /// </summary>
        public void NotifyJointBroken()
        {
            _grabJoint = null;
        }

        /// <summary>
        /// Injects the self-body set for test scenarios where RagdollSetup is not present.
        /// </summary>
        public void SetSelfBodiesForTest(HashSet<Rigidbody> selfBodies)
        {
            _selfBodies = selfBodies;
        }

        // ── Unity Lifecycle ──────────────────────────────────────────────────

        private void Awake()
        {
            // STEP 1: Cache self-ragdoll bodies from the root RagdollSetup.
            RagdollSetup ragdollSetup = GetComponentInParent<RagdollSetup>();
            if (ragdollSetup != null && ragdollSetup.AllBodies != null)
            {
                _selfBodies = new HashSet<Rigidbody>(ragdollSetup.AllBodies);
            }
            else
            {
                // Fallback: gather all Rigidbodies from the root's hierarchy.
                Transform root = transform.root;
                Rigidbody[] bodies = root.GetComponentsInChildren<Rigidbody>(includeInactive: true);
                _selfBodies = new HashSet<Rigidbody>(bodies);
            }

            // STEP 2: Ensure a trigger SphereCollider exists.
            _triggerCollider = GetTriggerSphere();
            if (_triggerCollider == null)
            {
                _triggerCollider = gameObject.AddComponent<SphereCollider>();
                _triggerCollider.isTrigger = true;
                _triggerCollider.radius = _detectionRadius;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.attachedRigidbody == null)
                return;

            Rigidbody rb = other.attachedRigidbody;

            // Exclude self-ragdoll bodies.
            if (_selfBodies != null && _selfBodies.Contains(rb))
                return;

            // Avoid duplicates.
            if (!_overlapping.Contains(rb))
            {
                _overlapping.Add(rb);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.attachedRigidbody == null)
                return;

            _overlapping.Remove(other.attachedRigidbody);
        }

        // ── Private Methods ──────────────────────────────────────────────────

        /// <summary>
        /// Finds an existing trigger SphereCollider on this GameObject, if any.
        /// </summary>
        private SphereCollider GetTriggerSphere()
        {
            SphereCollider[] spheres = GetComponents<SphereCollider>();
            for (int i = 0; i < spheres.Length; i++)
            {
                if (spheres[i].isTrigger)
                    return spheres[i];
            }
            return null;
        }

        /// <summary>
        /// Removes null (destroyed) entries from the overlap list.
        /// Called lazily when NearestTarget is accessed and from FixedUpdate.
        /// </summary>
        private void PruneDestroyedEntries()
        {
            for (int i = _overlapping.Count - 1; i >= 0; i--)
            {
                if (_overlapping[i] == null)
                {
                    _overlapping.RemoveAt(i);
                }
            }
        }

        private void FixedUpdate()
        {
            // Periodic cleanup of destroyed objects.
            PruneDestroyedEntries();

            // Check if grab joint was destroyed externally (e.g. by physics break).
            if (_grabJoint != null && _grabJoint.connectedBody == null)
            {
                DestroyGrabJoint();
            }
        }
    }
}
