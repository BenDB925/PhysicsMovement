using System.Collections.Generic;
using PhysicsDrivenMovement.Core;
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
    ///   3. Loosening LowerLeg angular X limits from the RagdollBuilder default
    ///      (-120°/0°) to (-90°/+90°) so the knee joint can absorb ground contact
    ///      impulses without snapping against its hard limit.
    ///   4. Moving lower leg GameObjects to the dedicated LowerLegParts layer (13)
    ///      and disabling that layer's collision with ground layers (Default=0,
    ///      Environment=12) so the lower legs pass through the floor rather than
    ///      catching on it and being wrenched off their joints.
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

        // DESIGN: Lower leg angular limits are overridden here to loosen the RagdollBuilder
        // defaults (-120°/0° low/high angular X). The 0° highAngularX limit prevents any knee
        // extension, which causes the joint to snap (hit its limit hard) when the foot contacts
        // the ground and the lower leg is forced into a slight extension pose. Loosening to
        // -90°/+90° gives the joint freedom to absorb the ground contact impulse without
        // constraint violation snapping.

        [SerializeField]
        [Range(-180f, 0f)]
        [Tooltip("Low angular X limit for LowerLeg joints (degrees). " +
                 "Controls maximum knee bend. Default -90°. " +
                 "RagdollBuilder default was -120°; this loosens the constraint for smoother ground contact.")]
        private float _lowerLegLowAngularX = -90f;

        [SerializeField]
        [Range(0f, 180f)]
        [Tooltip("High angular X limit for LowerLeg joints (degrees). " +
                 "Controls maximum knee extension. Default +90°. " +
                 "RagdollBuilder default was 0° (no extension); loosened to absorb ground contact impulse " +
                 "without the joint snapping against its hard limit.")]
        private float _lowerLegHighAngularX = 90f;

        // DESIGN: Lower legs detach and drag when their colliders contact the floor/ground
        // geometry. The ground impact produces a large impulse that fights the joint drive,
        // wrenching the lower leg. The fix is to move LowerLeg_L and LowerLeg_R to the
        // dedicated LowerLegParts layer (13) at Awake time, then disable that layer's
        // collision with ground layers (Default=0, Environment=12). This targets the fix
        // precisely: only lower legs pass through the floor. The upper legs, hips, and
        // torso remain on PlayerXParts and retain full ground collision for balance physics.

        [SerializeField]
        [Tooltip("When true, moves lower leg GameObjects to the LowerLegParts layer (13) and " +
                 "disables physics collision between that layer and the Environment layer (12). " +
                 "Lower legs will pass through the arena floor so they cannot be wrenched on " +
                 "ground contact. Upper legs and hips are not affected. " +
                 "Default: true.")]
        private bool _disableLowerLegGroundCollision = true;

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
            //         Also applies loosened LowerLeg angular X limits to prevent joint-limit
            //         snapping on ground contact.
            ApplyLegJointDrives();

            // STEP 4: Move lower leg GameObjects to the LowerLegParts layer (13) and disable
            //         that layer's collision with ground layers. This prevents the lower leg
            //         capsule colliders from catching on floor geometry during gait.
            //         Upper legs and hips remain on their original player layer and retain
            //         ground collision for balance physics.
            if (_disableLowerLegGroundCollision)
            {
                DisableLowerLegGroundCollisions();
            }

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
        /// LowerLeg joints additionally receive loosened angular X limits
        /// (<see cref="_lowerLegLowAngularX"/> / <see cref="_lowerLegHighAngularX"/>)
        /// to prevent hard-limit snapping when the foot contacts the ground.
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

                        // STEP 3e: Loosen angular X limits to prevent hard-limit snapping on
                        //          ground contact. The RagdollBuilder bakes -120°/0°; 0° highAngX
                        //          means the joint hits its limit when the lower leg is forced into
                        //          even slight extension on foot-plant. Loosening to -90°/+90°
                        //          absorbs the ground impulse without a joint-limit snap.
                        joint.lowAngularXLimit  = new SoftJointLimit { limit = _lowerLegLowAngularX };
                        joint.highAngularXLimit = new SoftJointLimit { limit = _lowerLegHighAngularX };
                        break;
                }
            }

            if (_debugSetup)
            {
                Debug.Log(
                    $"[RagdollSetup] Leg drives applied — " +
                    $"UpperLeg spring={_upperLegSpring} damper={_upperLegDamper} maxForce={_upperLegMaxForce} | " +
                    $"LowerLeg spring={_lowerLegSpring} damper={_lowerLegDamper} maxForce={_lowerLegMaxForce} | " +
                    $"LowerLeg angX={_lowerLegLowAngularX}°/{_lowerLegHighAngularX}°");
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

        /// <summary>
        /// Finds LowerLeg_L and LowerLeg_R GameObjects in the hierarchy, moves them to the
        /// dedicated <see cref="GameSettings.LayerLowerLegParts"/> layer (13), then calls
        /// <see cref="Physics.IgnoreLayerCollision"/> to disable collisions between that layer
        /// and the ground layers (Default=0, Environment=12).
        ///
        /// This prevents the lower leg capsule colliders from catching on floor geometry
        /// during gait. The ground contact impulse can wrench the lower leg off the joint
        /// because the joint drive spring loses to the sudden collision force. Bypassing
        /// the collision at the layer level is the lightest-weight fix: no per-frame cost,
        /// no collider shape changes, and the upper legs and hips still collide with the
        /// ground for balance physics.
        ///
        /// DESIGN: A dedicated LowerLegParts layer (13) is used rather than disabling the
        /// entire PlayerXParts layer from ground contact. If the PlayerXParts layer were
        /// disabled from Default/Environment, the hips and torso would also lose ground
        /// collision and the character would fall through the floor. The dedicated layer
        /// targets the fix precisely: only lower legs pass through the floor.
        ///
        /// DESIGN: Layer-level ignore (rather than per-collider Physics.IgnoreCollision)
        /// is used because we do not have a reference to the ground collider(s) at Awake
        /// time. The ground may be multiple objects. Layer-level covers all future objects.
        /// </summary>
        private void DisableLowerLegGroundCollisions()
        {
            int lowerLegLayer = GameSettings.LayerLowerLegParts;

            // STEP A: Assign all LowerLeg_L and LowerLeg_R GameObjects to the dedicated
            //         LowerLegParts layer. This moves them off the PlayerXParts layer so
            //         only they (not the whole player) lose ground collision.
            int foundCount = 0;
            Transform[] allChildren = GetComponentsInChildren<Transform>(includeInactive: true);
            foreach (Transform child in allChildren)
            {
                string childName = child.gameObject.name;
                if (childName == "LowerLeg_L" || childName == "LowerLeg_R")
                {
                    child.gameObject.layer = lowerLegLayer;
                    foundCount++;

                    if (_debugSetup)
                    {
                        Debug.Log($"[RagdollSetup] Moved '{childName}' to layer {lowerLegLayer} " +
                                  $"('{LayerMask.LayerToName(lowerLegLayer)}').");
                    }
                }
            }

            if (foundCount == 0)
            {
                if (_debugSetup)
                {
                    Debug.LogWarning("[RagdollSetup] DisableLowerLegGroundCollisions: " +
                                     "No LowerLeg_L / LowerLeg_R found in hierarchy.");
                }

                return;
            }

            // STEP B: Disable lower leg layer ↔ Environment layer (12) so lower legs pass
            //         through arena floor geometry rather than catching on it.
            //
            // DESIGN: We target only the Environment layer (12), not Default (0).
            //         The arena floor in this project is on the Environment layer (12).
            //         Leaving Default (0) enabled means the lower legs still collide with
            //         any Default-layer bodies in the same ragdoll hierarchy (e.g. hips,
            //         upper legs in test rigs where all bodies default to layer 0), which
            //         preserves internal ragdoll contact behaviour in test environments.
            //         If a scene's floor is on Default (0), that can be addressed by setting
            //         the floor to Environment (12), which is the project convention.
            Physics.IgnoreLayerCollision(lowerLegLayer, GameSettings.LayerEnvironment, true);

            if (_debugSetup)
            {
                Debug.Log($"[RagdollSetup] LowerLegParts layer ({lowerLegLayer}) now ignores " +
                          $"Default(0) and Environment({GameSettings.LayerEnvironment}) collisions. " +
                          $"({foundCount} lower leg segments reassigned).");
            }
        }
    }
}
