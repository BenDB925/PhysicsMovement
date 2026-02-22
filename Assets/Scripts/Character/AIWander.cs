using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Simple wander brain for AI ragdolls. Picks random XZ targets within a radius
    /// of world origin, walks toward them via <see cref="PlayerMovement.SetMoveInputOverride"/>,
    /// and idles briefly between walks.
    /// Attach alongside <see cref="PlayerMovement"/> on a ragdoll Hips root.
    /// </summary>
    public class AIWander : MonoBehaviour
    {
        [SerializeField, Range(1f, 15f)]
        [Tooltip("Max distance from world origin for random wander targets.")]
        private float _wanderRadius = 8f;

        [SerializeField, Range(0.3f, 3f)]
        [Tooltip("Distance to target before picking a new one.")]
        private float _arrivalThreshold = 1f;

        [SerializeField, Range(0f, 5f)]
        [Tooltip("Minimum idle pause duration between walks.")]
        private float _idleDurationMin = 1f;

        [SerializeField, Range(0f, 5f)]
        [Tooltip("Maximum idle pause duration between walks.")]
        private float _idleDurationMax = 3f;

        private Vector3 _targetPosition;
        private float _idleTimer;
        private bool _isIdling;
        private PlayerMovement _playerMovement;

        private void Awake()
        {
            _playerMovement = GetComponent<PlayerMovement>();
            if (_playerMovement == null)
            {
                Debug.LogError("[AIWander] Missing PlayerMovement on this GameObject.", this);
            }
        }

        private void Start()
        {
            // Begin with a short idle so the ragdoll stabilises before walking.
            _isIdling = true;
            _idleTimer = Random.Range(_idleDurationMin, _idleDurationMax);
        }

        private void Update()
        {
            if (_playerMovement == null) return;

            if (_isIdling)
            {
                _playerMovement.SetMoveInputOverride(Vector2.zero);
                _idleTimer -= Time.deltaTime;
                if (_idleTimer <= 0f)
                {
                    PickNewTarget();
                    _isIdling = false;
                }
                return;
            }

            // Walking: compute XZ direction from hips to target.
            Vector3 hipsPos = transform.position;
            Vector3 toTarget = _targetPosition - hipsPos;
            toTarget.y = 0f;

            if (toTarget.magnitude < _arrivalThreshold)
            {
                // Arrived — transition to idle.
                _isIdling = true;
                _idleTimer = Random.Range(_idleDurationMin, _idleDurationMax);
                _playerMovement.SetMoveInputOverride(Vector2.zero);
                return;
            }

            Vector3 dir = toTarget.normalized;
            _playerMovement.SetMoveInputOverride(new Vector2(dir.x, dir.z));
        }

        private void PickNewTarget()
        {
            Vector2 randomCircle = Random.insideUnitCircle * _wanderRadius;
            _targetPosition = new Vector3(randomCircle.x, 0f, randomCircle.y);
        }
    }
}
