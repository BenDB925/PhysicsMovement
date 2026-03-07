using UnityEngine;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    public class GhostDriver
    {
        public const float MaxTurnRate = 180f;
        public const float AccelRate = 2f;
        public const float DecelRate = 4f;
        public const float WaypointThreshold = 1.5f;

        private Vector2 _currentInputDir = Vector2.zero;
        private float _inputMagnitude;
        private bool _isReleasing;

        public Vector2 Update(Vector3 currentPos, Vector3 targetPos, float dt)
        {
            float stepDt = Mathf.Max(0f, dt);
            Vector2 toTarget = new Vector2(targetPos.x - currentPos.x, targetPos.z - currentPos.z);
            float distance = toTarget.magnitude;

            bool shouldSteer = !_isReleasing && distance > WaypointThreshold;
            if (shouldSteer)
            {
                Vector2 desiredDir = toTarget / distance;
                RotateToward(desiredDir, stepDt);
                _inputMagnitude = Mathf.MoveTowards(_inputMagnitude, 1f, AccelRate * stepDt);
            }
            else
            {
                _inputMagnitude = Mathf.MoveTowards(_inputMagnitude, 0f, DecelRate * stepDt);
            }

            return _currentInputDir * _inputMagnitude;
        }

        public void Release()
        {
            _isReleasing = true;
        }

        private void RotateToward(Vector2 desiredDir, float dt)
        {
            if (desiredDir.sqrMagnitude < 0.0001f)
            {
                return;
            }

            if (_currentInputDir.sqrMagnitude < 0.0001f)
            {
                _currentInputDir = desiredDir;
                return;
            }

            float currentAngle = Mathf.Atan2(_currentInputDir.y, _currentInputDir.x) * Mathf.Rad2Deg;
            float desiredAngle = Mathf.Atan2(desiredDir.y, desiredDir.x) * Mathf.Rad2Deg;
            float nextAngle = Mathf.MoveTowardsAngle(currentAngle, desiredAngle, MaxTurnRate * dt);

            float nextRadians = nextAngle * Mathf.Deg2Rad;
            _currentInputDir = new Vector2(Mathf.Cos(nextRadians), Mathf.Sin(nextRadians));
        }
    }
}
