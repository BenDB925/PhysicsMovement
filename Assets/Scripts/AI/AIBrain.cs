using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using PhysicsDrivenMovement.Character;

namespace PhysicsDrivenMovement.AI
{
    /// <summary>
    /// AI state machine that drives an AI ragdoll visitor through the museum.
    /// States: Idle, Walking, Observing, Fleeing, KnockedOut.
    /// Uses <see cref="AILocomotion"/> for movement, <see cref="MuseumNavigator"/>
    /// for pathfinding, and subscribes to <see cref="CharacterState.OnStateChanged"/>
    /// for reactive knockout/flee behavior.
    /// Attach to the Hips (root) GameObject of an AI ragdoll.
    /// </summary>
    [RequireComponent(typeof(AILocomotion))]
    public class AIBrain : MonoBehaviour
    {
        // ─── Serialized Fields ────────────────────────────────────────────────

        [Header("Idle")]
        [SerializeField, Range(0f, 5f)]
        [Tooltip("Minimum random pause before choosing a new art piece to visit.")]
        private float _idlePauseMin = 0.5f;

        [SerializeField, Range(0f, 5f)]
        [Tooltip("Maximum random pause before choosing a new art piece to visit.")]
        private float _idlePauseMax = 2f;

        [Header("Observing")]
        [SerializeField, Range(1f, 15f)]
        [Tooltip("Minimum time spent observing an art piece.")]
        private float _observeTimeMin = 3f;

        [SerializeField, Range(1f, 15f)]
        [Tooltip("Maximum time spent observing an art piece.")]
        private float _observeTimeMax = 8f;

        [Header("Fleeing")]
        [SerializeField, Range(1f, 10f)]
        [Tooltip("Duration of the flee behavior after recovering from knockout.")]
        private float _fleeDuration = 3f;

        [SerializeField, Range(1f, 10f)]
        [Tooltip("Distance to flee away from the threat source.")]
        private float _fleeDistance = 5f;

        // ─── State ────────────────────────────────────────────────────────────

        public enum AIState { Idle, Walking, Observing, Fleeing, KnockedOut, Grabbed }

        private AILocomotion _locomotion;
        private CharacterState _characterState;
        private HitReceiver _hitReceiver;
        private RagdollSetup _ragdollSetup;

        private AIState _currentState = AIState.Idle;
        private AIState _stateBeforeGrab;
        private float _stateTimer;

        // Walking state
        private List<Vector3> _waypoints;
        private int _waypointIndex;
        private MuseumInterestPoint _targetInterestPoint;

        // Fleeing state
        private Vector3 _lastHitDirection;

        // Grabbed detection
        private HashSet<Rigidbody> _selfBodies;

        // Cached interest points (found at Start)
        private MuseumInterestPoint[] _allInterestPoints;
        private MuseumInterestPoint _lastVisitedPoint;

        /// <summary>Current AI state, exposed for tests and debugging.</summary>
        public AIState CurrentState => _currentState;

        // ─── Unity Lifecycle ──────────────────────────────────────────────────

        private void Awake()
        {
            TryGetComponent(out _locomotion);
            TryGetComponent(out _characterState);
            TryGetComponent(out _ragdollSetup);

            // HitReceiver is on the Head child, not on Hips.
            _hitReceiver = GetComponentInChildren<HitReceiver>();

            // Cache own bodies for grab detection.
            _selfBodies = new HashSet<Rigidbody>();
            Rigidbody[] bodies = GetComponentsInChildren<Rigidbody>(includeInactive: true);
            foreach (Rigidbody rb in bodies)
            {
                _selfBodies.Add(rb);
            }
        }

        private void Start()
        {
            // Cache all interest points in the scene.
            _allInterestPoints = Object.FindObjectsByType<MuseumInterestPoint>(
                FindObjectsSortMode.None);

            if (_allInterestPoints == null || _allInterestPoints.Length == 0)
            {
                Debug.LogWarning("[AIBrain] No MuseumInterestPoints found in scene. " +
                                 "AI will remain idle.", this);
            }

            // Subscribe to state changes for knockout detection.
            if (_characterState != null)
            {
                _characterState.OnStateChanged += OnCharacterStateChanged;
            }

            // Start in Idle — will pick first target after a brief pause.
            EnterIdle();
        }

        private void OnDestroy()
        {
            if (_characterState != null)
            {
                _characterState.OnStateChanged -= OnCharacterStateChanged;
            }
        }

        private void FixedUpdate()
        {
            // Check for knockout override from HitReceiver.
            if (_hitReceiver != null && _hitReceiver.IsKnockedOut && _currentState != AIState.KnockedOut)
            {
                EnterKnockedOut();
                return;
            }

            // Check if being grabbed — stop all movement while held.
            bool isGrabbed = IsBeingGrabbed();
            if (isGrabbed && _currentState != AIState.Grabbed && _currentState != AIState.KnockedOut)
            {
                EnterGrabbed();
                return;
            }
            if (!isGrabbed && _currentState == AIState.Grabbed)
            {
                ExitGrabbed();
                return;
            }

            _stateTimer -= Time.fixedDeltaTime;

            switch (_currentState)
            {
                case AIState.Idle:
                    UpdateIdle();
                    break;
                case AIState.Walking:
                    UpdateWalking();
                    break;
                case AIState.Observing:
                    UpdateObserving();
                    break;
                case AIState.Fleeing:
                    UpdateFleeing();
                    break;
                case AIState.KnockedOut:
                    UpdateKnockedOut();
                    break;
                case AIState.Grabbed:
                    break; // Do nothing — just wait to be released.
            }
        }

        // ─── Grab Detection ───────────────────────────────────────────────────

        private bool IsBeingGrabbed()
        {
            // Check if any HandGrabZone in the scene is grabbing one of our bodies.
            HandGrabZone[] grabZones = Object.FindObjectsByType<HandGrabZone>(FindObjectsSortMode.None);
            foreach (HandGrabZone zone in grabZones)
            {
                if (!zone.IsGrabbing)
                    continue;

                Rigidbody grabbed = zone.GrabbedTarget;
                if (grabbed != null && _selfBodies.Contains(grabbed))
                    return true;
            }
            return false;
        }

        private void EnterGrabbed()
        {
            _stateBeforeGrab = _currentState;
            _currentState = AIState.Grabbed;
            _locomotion.ClearTarget();
        }

        private void ExitGrabbed()
        {
            // After being released, flee away.
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                Vector3 vel = rb.linearVelocity;
                vel.y = 0f;
                if (vel.sqrMagnitude > 0.1f)
                {
                    _lastHitDirection = vel.normalized;
                }
            }
            EnterFleeing();
        }

        // ─── State Entry ──────────────────────────────────────────────────────

        private void EnterIdle()
        {
            _currentState = AIState.Idle;
            _stateTimer = Random.Range(_idlePauseMin, _idlePauseMax);
            _locomotion.ClearTarget();
        }

        private void EnterWalking(MuseumInterestPoint target)
        {
            _currentState = AIState.Walking;
            _targetInterestPoint = target;

            Vector3 from = transform.position;
            Vector3 to = target.ViewPosition;

            // Use NavMesh pathfinding to route around obstacles.
            _waypoints = CalculateNavMeshPath(from, to);

            _waypointIndex = 0;

            if (_waypoints.Count > 0)
            {
                _locomotion.SetTarget(_waypoints[0]);
            }
            else
            {
                // No path found — go idle and try again later.
                EnterIdle();
            }
        }

        private List<Vector3> CalculateNavMeshPath(Vector3 from, Vector3 to)
        {
            NavMeshPath navPath = new NavMeshPath();
            var waypoints = new List<Vector3>();

            // Sample positions onto the NavMesh (snap to nearest walkable point).
            if (!NavMesh.SamplePosition(from, out NavMeshHit fromHit, 5f, NavMesh.AllAreas))
            {
                return waypoints;
            }

            if (!NavMesh.SamplePosition(to, out NavMeshHit toHit, 5f, NavMesh.AllAreas))
            {
                return waypoints;
            }

            if (NavMesh.CalculatePath(fromHit.position, toHit.position, NavMesh.AllAreas, navPath) &&
                navPath.status != NavMeshPathStatus.PathInvalid)
            {
                // Skip the first corner (current position) — start walking to the second.
                for (int i = 1; i < navPath.corners.Length; i++)
                {
                    waypoints.Add(navPath.corners[i]);
                }
            }

            return waypoints;
        }

        private void EnterObserving()
        {
            _currentState = AIState.Observing;
            _stateTimer = Random.Range(_observeTimeMin, _observeTimeMax);

            // Face the art piece.
            if (_targetInterestPoint != null)
            {
                _locomotion.SetFacingOnly(_targetInterestPoint.ViewDirection);
            }
            else
            {
                _locomotion.ClearTarget();
            }
        }

        private void EnterFleeing()
        {
            _currentState = AIState.Fleeing;
            _stateTimer = _fleeDuration;

            // Flee away from the last hit direction. If no hit direction, pick random.
            Vector3 fleeDir;
            if (_lastHitDirection.sqrMagnitude > 0.001f)
            {
                fleeDir = -_lastHitDirection;
            }
            else
            {
                float angle = Random.Range(0f, 360f);
                fleeDir = new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad), 0f, Mathf.Sin(angle * Mathf.Deg2Rad));
            }

            fleeDir.y = 0f;
            fleeDir.Normalize();

            // Find a valid flee target on the NavMesh.
            Vector3 fleeTarget = transform.position + fleeDir * _fleeDistance;
            if (NavMesh.SamplePosition(fleeTarget, out NavMeshHit hit, _fleeDistance, NavMesh.AllAreas))
            {
                fleeTarget = hit.position;
            }

            _locomotion.SetTarget(fleeTarget);
        }

        private void EnterKnockedOut()
        {
            _currentState = AIState.KnockedOut;
            _locomotion.ClearTarget();
        }

        // ─── State Updates ────────────────────────────────────────────────────

        private void UpdateIdle()
        {
            if (_stateTimer <= 0f)
            {
                PickNewTarget();
            }
        }

        private void UpdateWalking()
        {
            if (_locomotion.HasArrived && _waypoints != null)
            {
                _waypointIndex++;
                if (_waypointIndex >= _waypoints.Count)
                {
                    // Arrived at art piece.
                    _lastVisitedPoint = _targetInterestPoint;
                    EnterObserving();
                }
                else
                {
                    // Advance to next waypoint.
                    _locomotion.SetTarget(_waypoints[_waypointIndex]);
                }
            }
        }

        private void UpdateObserving()
        {
            if (_stateTimer <= 0f)
            {
                EnterIdle();
            }
        }

        private void UpdateFleeing()
        {
            if (_stateTimer <= 0f || _locomotion.HasArrived)
            {
                EnterIdle();
            }
        }

        private void UpdateKnockedOut()
        {
            // Wait for HitReceiver to finish knockout.
            if (_hitReceiver == null || !_hitReceiver.IsKnockedOut)
            {
                EnterFleeing();
            }
        }

        // ─── Target Selection ─────────────────────────────────────────────────

        private void PickNewTarget()
        {
            if (_allInterestPoints == null || _allInterestPoints.Length == 0)
            {
                EnterIdle(); // No targets available, wait and try again.
                return;
            }

            // Pick a random interest point, avoiding the one just visited.
            MuseumInterestPoint chosen = null;
            int attempts = 0;
            while (attempts < 10)
            {
                int index = Random.Range(0, _allInterestPoints.Length);
                MuseumInterestPoint candidate = _allInterestPoints[index];

                if (candidate != null && candidate != _lastVisitedPoint)
                {
                    chosen = candidate;
                    break;
                }
                attempts++;
            }

            // Fallback: pick any valid point.
            if (chosen == null)
            {
                for (int i = 0; i < _allInterestPoints.Length; i++)
                {
                    if (_allInterestPoints[i] != null)
                    {
                        chosen = _allInterestPoints[i];
                        break;
                    }
                }
            }

            if (chosen == null)
            {
                EnterIdle();
                return;
            }

            EnterWalking(chosen);
        }

        // ─── Reactive Behavior ────────────────────────────────────────────────

        private void OnCharacterStateChanged(CharacterStateType previous, CharacterStateType next)
        {
            // Track hit direction from collisions on the hips for fleeing.
            if (next == CharacterStateType.Fallen)
            {
                // Record the last collision direction as the threat source.
                // We use the hips velocity as an approximation of where force came from.
                if (_locomotion != null)
                {
                    Rigidbody rb = GetComponent<Rigidbody>();
                    if (rb != null)
                    {
                        Vector3 vel = rb.linearVelocity;
                        vel.y = 0f;
                        if (vel.sqrMagnitude > 0.1f)
                        {
                            _lastHitDirection = vel.normalized;
                        }
                    }
                }
            }
        }

        // ─── Test Seams ───────────────────────────────────────────────────────

        /// <summary>
        /// Test seam: force the AI into a specific state.
        /// </summary>
        public void SetStateForTest(AIState state)
        {
            _currentState = state;
        }

        /// <summary>
        /// Test seam: inject interest points without relying on FindObjectsByType.
        /// </summary>
        public void SetInterestPointsForTest(MuseumInterestPoint[] points)
        {
            _allInterestPoints = points;
        }
    }
}
