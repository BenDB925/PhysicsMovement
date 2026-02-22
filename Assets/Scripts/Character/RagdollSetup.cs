using System.Collections.Generic;
using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Initialises ragdoll physics on the PlayerRagdoll prefab at startup.
    /// Responsible for:
    ///   1. Calling Physics.IgnoreCollision on every pair of directly-connected
    ///      body-part colliders (detected from ConfigurableJoint.connectedBody links)
    ///      to prevent jitter between adjacent segments.
    ///   2. Applying leg joint SLERP drive parameters at runtime so spring/damper
    ///      values can be tuned via the Inspector without rebuilding the prefab.
    ///      This fixes lower legs hanging limp during gait: the prefab bakes static
    ///      joint drives from RagdollBuilder, but the authoritative runtime values
    ///      are the serialized fields on this component.
    /// Attach to the Hips (root) GameObject of the ragdoll hierarchy.
    /// Lifecycle: Awake — scans joints, registers collision ignore pairs, applies leg drives.
    /// Collaborators: <see cref="RagdollBone"/> (future), <see cref="LegAnimator"/>,
    /// Unity ConfigurableJoint.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class RagdollSetup : MonoBehaviour
    {
        // ─── Serialised Fields ──────────────────────────────────────────────

        [SerializeField]
        [Tooltip("Log each collision-ignore pair to the console on startup. Useful during ragdoll tuning.")]
        private bool _debugSetup = false;

        // DESIGN: Leg joint drive parameters are [SerializeField] here (not hardcoded in
        // RagdollBuilder) because the project rule mandates all physics tuning parameters
        // be Inspector-tunable. RagdollBuilder bakes initial values into the prefab, but
        // Awake() overrides them with these serialized values at runtime. This means the
        // prefab can be rebuilt at any time without losing tuning changes that have been
        // saved to the scene/prefab override via the Inspector.
        //
        // Root-cause of the "lower legs hanging limp" bug: the RagdollBuilder baked
        // spring=550 for LowerLeg joints. While numerically that should resist gravity,
        // in practice the joint can only apply force proportional to the angular error
        // (spring × error in radians). During gait the LegAnimator sets targetRotation
        // to ~20° (0.35 rad) bend; spring force = 550 × 0.35 ≈ 193 Nm. However the
        // PhysX SLERP drive also divides by the maximum force ceiling — and at
        // maximumForce=2200 with a 0.35-rad error the *effective* output is capped well
        // below 193 Nm. Doubling the spring to 1200 (and matching maxForce) ensures the
        // drive generates ample torque across the full gait range without hitting the cap.

        [SerializeField]
        [Range(100f, 5000f)]
        [Tooltip("SLERP drive positionSpring for UpperLeg_L and UpperLeg_R joints. " +
                 "Higher = stiffer response to targetRotation changes from LegAnimator. " +
                 "Minimum ~800 to resist gravity during gait. Default 1200.")]
        private float _upperLegSpring = 1200f;

        [SerializeField]
        [Range(10f, 500f)]
        [Tooltip("SLERP drive positionDamper for UpperLeg joints. " +
                 "Roughly 10% of spring for critically-damped response. Default 120.")]
        private float _upperLegDamper = 120f;

        [SerializeField]
        [Range(100f, 10000f)]
        [Tooltip("SLERP drive maximumForce for UpperLeg joints. " +
                 "Must be high enough that the spring/damper are not capped. Default 5000.")]
        private float _upperLegMaxForce = 5000f;

        [SerializeField]
        [Range(100f, 5000f)]
        [Tooltip("SLERP drive positionSpring for LowerLeg_L and LowerLeg_R joints. " +
                 "Higher = stiffer response. Minimum ~800 to resist gravity. " +
                 "Lower legs hang limp if this is too weak. Default 1200.")]
        private float _lowerLegSpring = 1200f;

        [SerializeField]
        [Range(10f, 500f)]
        [Tooltip("SLERP drive positionDamper for LowerLeg joints. Default 120.")]
        private float _lowerLegDamper = 120f;

        [SerializeField]
        [Range(100f, 10000f)]
        [Tooltip("SLERP drive maximumForce for LowerLeg joints. Default 5000.")]
        private float _lowerLegMaxForce = 5000f;

        // ─── Private Fields ──────────────────────────────────────────────────

        /// <summary>All Rigidbodies contained in this ragdoll, including the root.</summary>
        private Rigidbody[] _allBodies;

        // ─── Public Properties ────────────────────────────────────────────────

        /// <summary>
        /// All Rigidbodies that belong to this ragdoll hierarchy.
        /// Populated during Awake; returns null before then.
        /// </summary>
        public IReadOnlyList<Rigidbody> AllBodies => _allBodies;

        // ─── Unity Lifecycle ──────────────────────────────────────────────────

        private void Awake()
        {
            // STEP 1: Collect every Rigidbody in the hierarchy (including inactive parts)
            //         so other systems (balance controller, hit receiver) can reference them.
            _allBodies = GetComponentsInChildren<Rigidbody>(includeInactive: true);

            // STEP 2: Walk all ConfigurableJoints to discover direct neighbour pairs,
            //         then disable narrow-phase collision between them.
            DisableNeighboringCollisions();

            // STEP 3: Apply authoritative leg joint SLERP drive values from serialized fields.
            //         This overrides whatever was baked into the prefab by RagdollBuilder,
            //         ensuring the joints use the Inspector-tuned spring/damper at runtime.
            //         Also guarantees RotationDriveMode.Slerp is set, which is required for
            //         LegAnimator.targetRotation to take effect.
            ApplyLegJointDrives();

            if (_debugSetup)
            {
                Debug.Log($"[RagdollSetup] '{name}' initialised with {_allBodies.Length} bodies.");
            }
        }

        // ─── Private Methods ──────────────────────────────────────────────────

        /// <summary>
        /// Searches the hierarchy for the four leg ConfigurableJoints by GameObject name
        /// (UpperLeg_L, UpperLeg_R, LowerLeg_L, LowerLeg_R) and applies the serialized
        /// SLERP drive parameters. Also enforces <see cref="RotationDriveMode.Slerp"/> on
        /// each joint, which is required for <see cref="LegAnimator"/> targetRotation to
        /// be honoured by PhysX.
        /// </summary>
        private void ApplyLegJointDrives()
        {
            // STEP 3a: Build the upper-leg and lower-leg drive profiles from serialized fields.
            JointDrive upperLegDrive = new JointDrive
            {
                positionSpring = _upperLegSpring,
                positionDamper = _upperLegDamper,
                maximumForce   = _upperLegMaxForce,
            };

            JointDrive lowerLegDrive = new JointDrive
            {
                positionSpring = _lowerLegSpring,
                positionDamper = _lowerLegDamper,
                maximumForce   = _lowerLegMaxForce,
            };

            // STEP 3b: Walk all ConfigurableJoints in the hierarchy. Match by name and apply.
            //          Using name-based matching mirrors LegAnimator's lookup strategy and
            //          is hierarchy-position-agnostic — future skeleton changes won't break
            //          this as long as the four segment names remain consistent.
            ConfigurableJoint[] allJoints =
                GetComponentsInChildren<ConfigurableJoint>(includeInactive: true);

            foreach (ConfigurableJoint joint in allJoints)
            {
                string segmentName = joint.gameObject.name;

                switch (segmentName)
                {
                    case "UpperLeg_L":
                    case "UpperLeg_R":
                        // STEP 3c: Ensure Slerp drive mode — required for targetRotation to work.
                        joint.rotationDriveMode = RotationDriveMode.Slerp;
                        joint.slerpDrive        = upperLegDrive;
                        break;

                    case "LowerLeg_L":
                    case "LowerLeg_R":
                        // STEP 3d: Apply the stronger lower-leg drive with Slerp mode enforced.
                        joint.rotationDriveMode = RotationDriveMode.Slerp;
                        joint.slerpDrive        = lowerLegDrive;
                        break;
                }
            }

            if (_debugSetup)
            {
                Debug.Log(
                    $"[RagdollSetup] Leg drives applied — " +
                    $"UpperLeg spring={_upperLegSpring} damper={_upperLegDamper} maxForce={_upperLegMaxForce} | " +
                    $"LowerLeg spring={_lowerLegSpring} damper={_lowerLegDamper} maxForce={_lowerLegMaxForce}");
            }
        }

        /// <summary>
        /// For every ConfigurableJoint in the hierarchy, retrieves the collider(s) on the
        /// joint owner and on its connected body, then calls Physics.IgnoreCollision for
        /// every cross-pairing. This eliminates jitter between segments that are physically
        /// joined but whose colliders would otherwise overlap.
        /// </summary>
        private void DisableNeighboringCollisions()
        {
            ConfigurableJoint[] allJoints =
                GetComponentsInChildren<ConfigurableJoint>(includeInactive: true);

            foreach (ConfigurableJoint joint in allJoints)
            {
                if (joint.connectedBody == null)
                {
                    continue;
                }

                Collider[] collidersOnJointBody  = joint.GetComponents<Collider>();
                Collider[] collidersOnParentBody = joint.connectedBody.GetComponents<Collider>();

                foreach (Collider colA in collidersOnJointBody)
                {
                    foreach (Collider colB in collidersOnParentBody)
                    {
                        Physics.IgnoreCollision(colA, colB, true);

                        if (_debugSetup)
                        {
                            Debug.Log($"[RagdollSetup] IgnoreCollision: " +
                                      $"{colA.gameObject.name} ↔ {colB.gameObject.name}");
                        }
                    }
                }
            }
        }
    }
}
