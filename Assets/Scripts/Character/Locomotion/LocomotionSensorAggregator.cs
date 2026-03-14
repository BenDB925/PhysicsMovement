using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Shared sensor helper that gathers foot contacts, body motion, and support geometry into
    /// a single per-step snapshot so locomotion systems do not re-derive the same raw physics state ad hoc.
    /// </summary>
    internal sealed class LocomotionSensorAggregator
    {
        private const float MotionEpsilon = 0.0001f;
        private const float SingleSupportQuality = 0.5f;
        private const float DoubleSupportMinQuality = 0.75f;
        private const float DoubleSupportFullSpan = 0.45f;
        private const float DoubleSupportMinSpan = 0.12f;
        private const float PlantedSpeedThreshold = 0.05f;
        private const float SlipSpeedThreshold = 0.75f;

        private readonly Rigidbody _hipsBody;
        private readonly BalanceController _balanceController;
        private readonly Transform _leftFootTransform;
        private readonly Transform _rightFootTransform;
        private readonly GroundSensor _leftGroundSensor;
        private readonly GroundSensor _rightGroundSensor;

        private Vector3 _previousLeftFootPosition;
        private Vector3 _previousRightFootPosition;
        private bool _hasPreviousFootSample;

        public LocomotionSensorAggregator(
            Rigidbody hipsBody,
            BalanceController balanceController,
            Transform leftFootTransform,
            Transform rightFootTransform,
            GroundSensor leftGroundSensor,
            GroundSensor rightGroundSensor)
        {
            _hipsBody = hipsBody;
            _balanceController = balanceController;
            _leftFootTransform = leftFootTransform;
            _rightFootTransform = rightFootTransform;
            _leftGroundSensor = leftGroundSensor;
            _rightGroundSensor = rightGroundSensor;
            _previousLeftFootPosition = Vector3.zero;
            _previousRightFootPosition = Vector3.zero;
            _hasPreviousFootSample = false;
        }

        public bool TryCollect(out LocomotionSensorSnapshot snapshot)
        {
            // STEP 1: Validate the required runtime dependencies before sampling a new physics frame.
            if (_hipsBody == null ||
                _balanceController == null ||
                _leftFootTransform == null ||
                _rightFootTransform == null)
            {
                snapshot = default;
                return false;
            }

            // STEP 2: Sample the current contact points, foot motion, and support geometry.
            bool leftGrounded = _leftGroundSensor != null ? _leftGroundSensor.IsGrounded : _balanceController.IsGrounded;
            bool rightGrounded = _rightGroundSensor != null ? _rightGroundSensor.IsGrounded : _balanceController.IsGrounded;
            Vector3 leftFootPosition = GetSupportPoint(_leftFootTransform, _leftGroundSensor, leftGrounded);
            Vector3 rightFootPosition = GetSupportPoint(_rightFootTransform, _rightGroundSensor, rightGrounded);

            float leftFootPlanarSpeed = ComputeFootPlanarSpeed(leftFootPosition, _previousLeftFootPosition);
            float rightFootPlanarSpeed = ComputeFootPlanarSpeed(rightFootPosition, _previousRightFootPosition);

            FootContactObservation leftFoot = BuildFootContactObservation(
                LocomotionLeg.Left,
                leftGrounded,
                leftFootPlanarSpeed,
                _leftGroundSensor);
            FootContactObservation rightFoot = BuildFootContactObservation(
                LocomotionLeg.Right,
                rightGrounded,
                rightFootPlanarSpeed,
                _rightGroundSensor);

            SupportGeometry supportGeometry = new SupportGeometry(
                leftFootPosition,
                leftGrounded,
                rightFootPosition,
                rightGrounded);

            // STEP 3: Promote the raw sensor sample into the support-language snapshot used by locomotion systems.
            float contactConfidence = 0.5f * (leftFoot.ContactConfidence + rightFoot.ContactConfidence);
            float plantedFootConfidence = Mathf.Max(leftFoot.PlantedConfidence, rightFoot.PlantedConfidence);
            float slipEstimate = Mathf.Max(leftFoot.SlipEstimate, rightFoot.SlipEstimate);
            float supportQuality = ComputeSupportQuality(supportGeometry);
            bool isComOutsideSupport = supportGeometry.IsPointOutsideSupport(_hipsBody.worldCenterOfMass);
            SupportObservation support = new SupportObservation(
                leftFoot,
                rightFoot,
                supportQuality,
                contactConfidence,
                plantedFootConfidence,
                slipEstimate,
                isComOutsideSupport);

            snapshot = new LocomotionSensorSnapshot(
                _hipsBody.position,
                _hipsBody.worldCenterOfMass,
                _hipsBody.linearVelocity,
                _hipsBody.angularVelocity,
                supportGeometry,
                support);

            _previousLeftFootPosition = leftFootPosition;
            _previousRightFootPosition = rightFootPosition;
            _hasPreviousFootSample = true;
            return true;
        }

        private static Vector3 GetSupportPoint(Transform footTransform, GroundSensor sensor, bool isGrounded)
        {
            if (sensor != null && isGrounded)
            {
                return sensor.GroundPoint;
            }

            return footTransform.position;
        }

        private float ComputeFootPlanarSpeed(Vector3 currentPosition, Vector3 previousPosition)
        {
            if (!_hasPreviousFootSample || Time.fixedDeltaTime <= MotionEpsilon)
            {
                return 0f;
            }

            Vector3 planarDelta = Vector3.ProjectOnPlane(currentPosition - previousPosition, Vector3.up);
            return planarDelta.magnitude / Time.fixedDeltaTime;
        }

        private static FootContactObservation BuildFootContactObservation(
            LocomotionLeg leg,
            bool isGrounded,
            float planarSpeed,
            GroundSensor sensor)
        {
            float contactConfidence = isGrounded ? 1f : 0f;
            float slipEstimate = isGrounded
                ? Mathf.InverseLerp(PlantedSpeedThreshold, SlipSpeedThreshold, planarSpeed)
                : 0f;
            float plantedConfidence = isGrounded ? 1f - slipEstimate : 0f;
            bool hasForwardObstruction = sensor != null && sensor.HasForwardObstruction;
            float estimatedStepHeight = sensor != null ? sensor.EstimatedStepHeight : 0f;
            float forwardObstructionConfidence = sensor != null ? sensor.ForwardObstructionConfidence : 0f;

            return new FootContactObservation(
                leg,
                isGrounded,
                contactConfidence,
                plantedConfidence,
                slipEstimate,
                hasForwardObstruction,
                estimatedStepHeight,
                forwardObstructionConfidence);
        }

        private static float ComputeSupportQuality(SupportGeometry supportGeometry)
        {
            // STEP 1: Keep the existing single-support baseline while rewarding wider double-support stance.
            if (supportGeometry.GroundedFootCount <= 0)
            {
                return 0f;
            }

            if (supportGeometry.GroundedFootCount == 1)
            {
                return SingleSupportQuality;
            }

            float spanFactor = Mathf.InverseLerp(DoubleSupportMinSpan, DoubleSupportFullSpan, supportGeometry.SupportSpan);
            return Mathf.Lerp(DoubleSupportMinQuality, 1f, spanFactor);
        }
    }
}