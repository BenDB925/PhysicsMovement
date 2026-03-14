using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Owns the leg-joint arrays and force/drive methods for the startup stand-assist
    /// system. Extracted from <see cref="BalanceController"/> to keep the assist
    /// implementation isolated from the core PD balance loop.
    /// </summary>
    internal class StartupStandAssist
    {
        private ConfigurableJoint[] _legJoints;
        private JointDrive[] _legBaseDrives;
        private Rigidbody[] _legBodies;

        /// <summary>
        /// Total mass of all Rigidbodies under the character root. Computed once
        /// during <see cref="Initialize"/> and used to scale the persistent seated
        /// recovery force so it overcomes gravity regardless of character weight.
        /// </summary>
        internal float TotalBodyMass { get; private set; }

        /// <summary>
        /// Finds all four leg ConfigurableJoints by name and caches their baseline
        /// SLERP drives and Rigidbodies. Also sums total body mass from all child
        /// Rigidbodies.
        /// </summary>
        internal void Initialize(Transform root)
        {
            ConfigurableJoint[] joints = root.GetComponentsInChildren<ConfigurableJoint>(includeInactive: true);
            int legCount = 0;
            foreach (ConfigurableJoint joint in joints)
            {
                if (IsLegJointName(joint.gameObject.name))
                {
                    legCount++;
                }
            }

            _legJoints = new ConfigurableJoint[legCount];
            _legBaseDrives = new JointDrive[legCount];
            _legBodies = new Rigidbody[legCount];

            int writeIndex = 0;
            foreach (ConfigurableJoint joint in joints)
            {
                if (!IsLegJointName(joint.gameObject.name))
                {
                    continue;
                }

                _legJoints[writeIndex] = joint;
                _legBaseDrives[writeIndex] = joint.slerpDrive;
                _legBodies[writeIndex] = joint.GetComponent<Rigidbody>();
                writeIndex++;
            }

            TotalBodyMass = 0f;
            Rigidbody[] bodies = root.GetComponentsInChildren<Rigidbody>(includeInactive: true);
            for (int i = 0; i < bodies.Length; i++)
            {
                TotalBodyMass += bodies[i].mass;
            }
        }

        /// <summary>
        /// Applies upward assist forces to the hips body and optionally to each cached
        /// leg body. When a <see cref="LegAnimator"/> is present, leg-body forces are
        /// skipped because the animator owns those joints exclusively.
        /// </summary>
        internal void ApplyForces(
            Rigidbody hipsBody,
            Vector3 assistDirection,
            float assistForce,
            float legForceFraction,
            bool hasLegAnimator)
        {
            float legForce = assistForce * legForceFraction;
            float hipsForce = assistForce - legForce;

            if (hipsForce > 0f)
            {
                hipsBody.AddForce(assistDirection * hipsForce, ForceMode.Force);
            }

            if (hasLegAnimator)
            {
                return;
            }

            if (_legBodies == null || _legBodies.Length == 0 || legForce <= 0f)
            {
                return;
            }

            float perLegBodyForce = legForce / _legBodies.Length;
            for (int i = 0; i < _legBodies.Length; i++)
            {
                Rigidbody legBody = _legBodies[i];
                if (legBody == null)
                {
                    continue;
                }

                legBody.AddForce(assistDirection * perLegBodyForce, ForceMode.Force);
            }
        }

        /// <summary>
        /// Adjusts leg joint SLERP drives toward boosted spring/damper values based on
        /// the current assist scale. When a <see cref="LegAnimator"/> is present, this
        /// method returns immediately because the animator owns the drives.
        /// </summary>
        internal void ApplyLegDrive(
            float assistScale,
            float springMultiplier,
            float damperMultiplier,
            bool hasLegAnimator)
        {
            if (hasLegAnimator)
            {
                return;
            }

            if (_legJoints == null || _legBaseDrives == null)
            {
                return;
            }

            for (int i = 0; i < _legJoints.Length; i++)
            {
                ConfigurableJoint joint = _legJoints[i];
                if (joint == null)
                {
                    continue;
                }

                JointDrive baseDrive = _legBaseDrives[i];
                JointDrive drive = joint.slerpDrive;
                drive.positionSpring = Mathf.Lerp(
                    baseDrive.positionSpring,
                    baseDrive.positionSpring * springMultiplier,
                    assistScale);
                drive.positionDamper = Mathf.Lerp(
                    baseDrive.positionDamper,
                    baseDrive.positionDamper * damperMultiplier,
                    assistScale);
                drive.maximumForce = baseDrive.maximumForce;
                joint.slerpDrive = drive;
            }
        }

        private static bool IsLegJointName(string segmentName)
        {
            return segmentName == "UpperLeg_L" ||
                   segmentName == "UpperLeg_R" ||
                   segmentName == "LowerLeg_L" ||
                   segmentName == "LowerLeg_R";
        }
    }
}
