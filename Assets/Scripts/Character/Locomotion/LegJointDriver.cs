using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Applies computed swing and knee targets to the four leg ConfigurableJoints.
    /// Supports both world-space swing (targets computed relative to movement
    /// direction) and local-space swing (targets in joint targetRotation space).
    /// Also manages baseline joint drives for airborne spring scaling.
    ///
    /// Lifecycle: constructed once by LegAnimator after joint references are resolved,
    /// then called each FixedUpdate to push the resolved execution targets onto joints.
    /// </summary>
    internal class LegJointDriver
    {
        private readonly ConfigurableJoint _upperLegL;
        private readonly ConfigurableJoint _upperLegR;
        private readonly ConfigurableJoint _lowerLegL;
        private readonly ConfigurableJoint _lowerLegR;
        private readonly Vector3 _swingAxis;
        private readonly Vector3 _kneeAxis;

        private JointDrive _baselineUpperLegLDrive;
        private JointDrive _baselineUpperLegRDrive;
        private JointDrive _baselineLowerLegLDrive;
        private JointDrive _baselineLowerLegRDrive;

        private Vector3 _worldSwingAxis;
        private Vector3 _upperLegLTargetEuler;
        private Vector3 _lowerLegLTargetEuler;

        internal Vector3 WorldSwingAxis => _worldSwingAxis;
        internal Vector3 UpperLegLTargetEuler => _upperLegLTargetEuler;
        internal Vector3 LowerLegLTargetEuler => _lowerLegLTargetEuler;

        internal LegJointDriver(
            ConfigurableJoint upperLegL,
            ConfigurableJoint upperLegR,
            ConfigurableJoint lowerLegL,
            ConfigurableJoint lowerLegR,
            Vector3 swingAxis,
            Vector3 kneeAxis)
        {
            _upperLegL = upperLegL;
            _upperLegR = upperLegR;
            _lowerLegL = lowerLegL;
            _lowerLegR = lowerLegR;
            _swingAxis = swingAxis;
            _kneeAxis = kneeAxis;
        }

        internal void CaptureBaselineDrives()
        {
            if (_upperLegL != null) { _baselineUpperLegLDrive = _upperLegL.slerpDrive; }
            if (_upperLegR != null) { _baselineUpperLegRDrive = _upperLegR.slerpDrive; }
            if (_lowerLegL != null) { _baselineLowerLegLDrive = _lowerLegL.slerpDrive; }
            if (_lowerLegR != null) { _baselineLowerLegRDrive = _lowerLegR.slerpDrive; }
        }

        internal void ResetFrameState()
        {
            _worldSwingAxis = Vector3.zero;
        }

        /// <summary>
        /// Applies the resolved swing and knee targets to the four leg joints using
        /// either world-space or local-space swing depending on <paramref name="useWorldSpace"/>.
        /// </summary>
        internal void ApplySwingTargets(
            float leftSwingDeg,
            float rightSwingDeg,
            float leftKneeBendDeg,
            float rightKneeBendDeg,
            bool useWorldSpace,
            DesiredInput desiredInput,
            LocomotionObservation observation)
        {
            if (useWorldSpace)
            {
                ApplyWorldSpaceSwing(leftSwingDeg, rightSwingDeg, leftKneeBendDeg, rightKneeBendDeg, desiredInput, observation);
                return;
            }

            ApplyLocalSpaceSwing(leftSwingDeg, rightSwingDeg, leftKneeBendDeg, rightKneeBendDeg);
        }

        internal void SetAllTargetsToIdentity()
        {
            if (_upperLegL != null) { _upperLegL.targetRotation = Quaternion.identity; }
            if (_upperLegR != null) { _upperLegR.targetRotation = Quaternion.identity; }
            if (_lowerLegL != null) { _lowerLegL.targetRotation = Quaternion.identity; }
            if (_lowerLegR != null) { _lowerLegR.targetRotation = Quaternion.identity; }
        }

        /// <summary>
        /// Multiplies the baseline spring by <paramref name="multiplier"/> on all four leg joints.
        /// 0 = fully limp; 1 = full stiffness. Damper stays at baseline.
        /// </summary>
        internal void SetSpringMultiplier(float multiplier)
        {
            if (_baselineUpperLegLDrive.positionSpring <= 0f)
            {
                return;
            }

            if (_upperLegL != null)
            {
                JointDrive drive = _baselineUpperLegLDrive;
                drive.positionSpring = _baselineUpperLegLDrive.positionSpring * multiplier;
                _upperLegL.slerpDrive = drive;
            }

            if (_upperLegR != null)
            {
                JointDrive drive = _baselineUpperLegRDrive;
                drive.positionSpring = _baselineUpperLegRDrive.positionSpring * multiplier;
                _upperLegR.slerpDrive = drive;
            }

            if (_lowerLegL != null)
            {
                JointDrive drive = _baselineLowerLegLDrive;
                drive.positionSpring = _baselineLowerLegLDrive.positionSpring * multiplier;
                _lowerLegL.slerpDrive = drive;
            }

            if (_lowerLegR != null)
            {
                JointDrive drive = _baselineLowerLegRDrive;
                drive.positionSpring = _baselineLowerLegRDrive.positionSpring * multiplier;
                _lowerLegR.slerpDrive = drive;
            }
        }

        /// <summary>
        /// Returns the world-space horizontal forward direction for gait animation.
        /// Uses planar velocity as primary source, falling back to desired input direction.
        /// </summary>
        internal static Vector3 GetWorldGaitForward(DesiredInput desiredInput, LocomotionObservation observation)
        {
            const float VelocityThreshold = 0.05f;

            Vector3 horizontalVel = observation.PlanarVelocity;

            if (horizontalVel.magnitude >= VelocityThreshold)
            {
                return horizontalVel.normalized;
            }

            if (desiredInput.HasMoveIntent)
            {
                return desiredInput.MoveWorldDirection;
            }

            return Vector3.zero;
        }

        // ── Private Methods ─────────────────────────────────────────────────

        private void ApplyWorldSpaceSwing(
            float leftSwingDeg,
            float rightSwingDeg,
            float leftKneeBendDeg,
            float rightKneeBendDeg,
            DesiredInput desiredInput,
            LocomotionObservation observation)
        {
            Vector3 gaitForward = GetWorldGaitForward(desiredInput, observation);

            Vector3 worldSwingAxis;
            if (gaitForward.sqrMagnitude > 0.0001f)
            {
                worldSwingAxis = Vector3.Cross(Vector3.up, gaitForward).normalized;
            }
            else if (_worldSwingAxis.sqrMagnitude > 0.0001f)
            {
                worldSwingAxis = _worldSwingAxis;
            }
            else
            {
                ApplyLocalSpaceSwing(leftSwingDeg, rightSwingDeg, leftKneeBendDeg, rightKneeBendDeg);
                return;
            }

            _worldSwingAxis = worldSwingAxis;

            ApplyWorldSpaceJointTarget(_upperLegL, leftSwingDeg, worldSwingAxis);
            if (_upperLegL != null) { _upperLegLTargetEuler = _upperLegL.targetRotation.eulerAngles; }
            ApplyWorldSpaceJointTarget(_upperLegR, rightSwingDeg, worldSwingAxis);

            ApplyWorldSpaceJointTarget(_lowerLegL, -leftKneeBendDeg, worldSwingAxis);
            if (_lowerLegL != null) { _lowerLegLTargetEuler = _lowerLegL.targetRotation.eulerAngles; }
            ApplyWorldSpaceJointTarget(_lowerLegR, -rightKneeBendDeg, worldSwingAxis);
        }

        private static void ApplyWorldSpaceJointTarget(ConfigurableJoint joint, float swingDeg, Vector3 worldAxis)
        {
            if (joint == null)
            {
                Debug.LogError("[LegJointDriver] ApplyWorldSpaceJointTarget: joint reference is null.");
                return;
            }

            Quaternion worldSwingRotation = Quaternion.AngleAxis(swingDeg, worldAxis);

            Quaternion connectedBodyRot = joint.connectedBody != null
                ? joint.connectedBody.rotation
                : Quaternion.identity;

            Quaternion localTarget = Quaternion.Inverse(connectedBodyRot) * worldSwingRotation;

            joint.targetRotation = localTarget;
        }

        private void ApplyLocalSpaceSwing(
            float leftSwingDeg,
            float rightSwingDeg,
            float leftKneeBendDeg,
            float rightKneeBendDeg)
        {
            if (_upperLegL != null)
            {
                _upperLegL.targetRotation = Quaternion.AngleAxis(leftSwingDeg, _swingAxis);
                _upperLegLTargetEuler = _upperLegL.targetRotation.eulerAngles;
            }
            else
            {
                Debug.LogError("[LegJointDriver] ApplyLocalSpaceSwing: _upperLegL is null.");
            }

            if (_upperLegR != null)
            {
                _upperLegR.targetRotation = Quaternion.AngleAxis(rightSwingDeg, _swingAxis);
            }
            else
            {
                Debug.LogError("[LegJointDriver] ApplyLocalSpaceSwing: _upperLegR is null.");
            }

            if (_lowerLegL != null)
            {
                _lowerLegL.targetRotation = Quaternion.AngleAxis(-leftKneeBendDeg, _kneeAxis);
                _lowerLegLTargetEuler = _lowerLegL.targetRotation.eulerAngles;
            }
            else
            {
                Debug.LogError("[LegJointDriver] ApplyLocalSpaceSwing: _lowerLegL is null.");
            }

            if (_lowerLegR != null)
            {
                _lowerLegR.targetRotation = Quaternion.AngleAxis(-rightKneeBendDeg, _kneeAxis);
            }
            else
            {
                Debug.LogError("[LegJointDriver] ApplyLocalSpaceSwing: _lowerLegR is null.");
            }
        }
    }
}
