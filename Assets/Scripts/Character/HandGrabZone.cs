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

        /// <summary>Currently overlapping static colliders (no Rigidbody — walls, floors, etc.).</summary>
        private readonly List<Collider> _overlappingStatic = new List<Collider>();

        /// <summary>The trigger SphereCollider used for detection.</summary>
        private SphereCollider _triggerCollider;

        /// <summary>Active grab FixedJoint, if any.</summary>
        private FixedJoint _grabJoint;

        /// <summary>True when the grab is a world-anchor grab (connectedBody intentionally null).</summary>
        private bool _isWorldGrab;

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

        /// <summary>True when the active grab is a world-anchor grab (wall/static geometry).</summary>
        public bool IsWorldGrab => _isWorldGrab;

        /// <summary>The Rigidbody currently grabbed by this hand, or null.</summary>
        public Rigidbody GrabbedTarget => _grabJoint != null ? _grabJoint.connectedBody : null;

        /// <summary>
        /// The closest overlapping static collider (no Rigidbody), or null if none in range.
        /// </summary>
        public Collider NearestStaticCollider
        {
            get
            {
                PruneDestroyedStaticEntries();

                if (_overlappingStatic.Count == 0)
                    return null;

                Collider nearest = null;
                float nearestDistSq = float.MaxValue;
                Vector3 handPos = transform.position;

                for (int i = 0; i < _overlappingStatic.Count; i++)
                {
                    Collider col = _overlappingStatic[i];
                    Vector3 closest = col.ClosestPoint(handPos);
                    float distSq = (closest - handPos).sqrMagnitude;
                    if (distSq < nearestDistSq)
                    {
                        nearestDistSq = distSq;
                        nearest = col;
                    }
                }

                return nearest;
            }
        }

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
        /// Creates a FixedJoint from this hand anchored to a world-space point (for static
        /// geometry like walls). connectedBody is null so the joint locks to world space.
        /// Returns false if a joint already exists.
        /// </summary>
        public bool CreateWorldGrabJoint(Vector3 worldAnchor, float breakForce, float breakTorque)
        {
            if (_grabJoint != null)
                return false;

            Rigidbody handRb = GetComponent<Rigidbody>();
            if (handRb == null)
                return false;

            _grabJoint = gameObject.AddComponent<FixedJoint>();
            _grabJoint.connectedBody = null;
            _grabJoint.connectedAnchor = worldAnchor;
            _grabJoint.breakForce = breakForce;
            _grabJoint.breakTorque = breakTorque;
            _isWorldGrab = true;
            return true;
        }

        /// <summary>
        /// Destroys the active grab FixedJoint if one exists.
        /// Uses DestroyImmediate so the joint is removed before the current physics
        /// step, allowing throw impulses applied in the same FixedUpdate to take effect.
        /// </summary>
        public void DestroyGrabJoint()
        {
            if (_grabJoint != null)
            {
                DestroyImmediate(_grabJoint);
                _grabJoint = null;
                _isWorldGrab = false;
            }
        }

        /// <summary>
        /// Called by GrabController when a FixedJoint breaks (OnJointBreak).
        /// Clears the internal reference so IsGrabbing returns false.
        /// </summary>
        public void NotifyJointBroken()
        {
            _grabJoint = null;
            _isWorldGrab = false;
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
            Rigidbody rb = other.attachedRigidbody;

            if (rb == null)
            {
                // Static collider (wall, floor, etc.) — track separately.
                if (!other.isTrigger && !_overlappingStatic.Contains(other))
                {
                    _overlappingStatic.Add(other);
                }
                return;
            }

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
            Rigidbody rb = other.attachedRigidbody;

            if (rb == null)
            {
                _overlappingStatic.Remove(other);
                return;
            }

            _overlapping.Remove(rb);
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

        private void PruneDestroyedStaticEntries()
        {
            for (int i = _overlappingStatic.Count - 1; i >= 0; i--)
            {
                if (_overlappingStatic[i] == null)
                {
                    _overlappingStatic.RemoveAt(i);
                }
            }
        }

        private void FixedUpdate()
        {
            // Periodic cleanup of destroyed objects.
            PruneDestroyedEntries();
            PruneDestroyedStaticEntries();

            // Check if grab joint was destroyed externally (e.g. by physics break).
            // Skip this check for world grabs where connectedBody is intentionally null.
            if (_grabJoint != null && !_isWorldGrab && _grabJoint.connectedBody == null)
            {
                DestroyGrabJoint();
            }
        }
    }
}
