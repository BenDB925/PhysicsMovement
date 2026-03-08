using System;
using PhysicsDrivenMovement.Character;
using UnityEngine;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    public class WaypointCourseRunner : MonoBehaviour
    {
        private GhostDriver _ghostDriver = new GhostDriver();

        private GameObject _player;
        private PlayerMovement _playerMovement;
        private Vector3[] _waypoints = Array.Empty<Vector3>();

        public bool IsComplete { get; private set; }
        public int FramesElapsed { get; private set; }
        public int CurrentWaypointIndex { get; private set; }

        private Transform _hipsTransform;

        public void Initialize(GameObject player, Vector3[] waypoints)
        {
            _ghostDriver = new GhostDriver();
            _player = player;
            _playerMovement = _player != null ? _player.GetComponentInChildren<PlayerMovement>() : null;
            if (_playerMovement == null)
            {
                Debug.LogError("[WaypointCourseRunner] PlayerMovement not found during Initialize.");
            }

            Rigidbody movementBody = _playerMovement != null ? _playerMovement.GetComponent<Rigidbody>() : null;
            if (_playerMovement != null && movementBody == null)
            {
                Debug.LogError("[WaypointCourseRunner] PlayerMovement object has no Rigidbody.");
            }

            _hipsTransform = movementBody != null
                ? movementBody.transform
                : (_playerMovement != null ? _playerMovement.transform : (_player != null ? _player.transform : null));
            _waypoints = waypoints ?? Array.Empty<Vector3>();

            FramesElapsed = 0;
            CurrentWaypointIndex = 0;
            IsComplete = _playerMovement == null || _waypoints.Length == 0;
        }

        private void FixedUpdate()
        {
            if (IsComplete)
            {
                return;
            }

            if (_player == null || _playerMovement == null)
            {
                IsComplete = true;
                return;
            }

            FramesElapsed++;

            Vector3 currentPosition = _hipsTransform != null ? _hipsTransform.position : _player.transform.position;
            Vector3 targetWaypoint = _waypoints[CurrentWaypointIndex];
            Vector2 input = _ghostDriver.Update(currentPosition, targetWaypoint, Time.fixedDeltaTime);
            _playerMovement.SetMoveInputForTest(input);

            Vector2 currentXZ = new Vector2(currentPosition.x, currentPosition.z);
            Vector2 targetXZ = new Vector2(targetWaypoint.x, targetWaypoint.z);
            float distance = Vector2.Distance(currentXZ, targetXZ);

            if (distance <= GhostDriver.WaypointThreshold)
            {
                CurrentWaypointIndex++;
                if (CurrentWaypointIndex >= _waypoints.Length)
                {
                    IsComplete = true;
                    _ghostDriver.Release();
                    _playerMovement.SetMoveInputForTest(Vector2.zero);
                }
            }
        }
    }
}
