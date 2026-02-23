using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Drives procedural arm-swing animation on the ragdoll's four arm ConfigurableJoints
    /// (UpperArm_L/R, LowerArm_L/R) during gait, counter-swinging opposite to the legs.
    ///
    /// The arm swing phase is read from <see cref="LegAnimator.Phase"/> and offset by π so
    /// that the left arm swings forward when the right leg swings forward (natural human gait).
    /// The swing amplitude is blended by <see cref="LegAnimator.SmoothedInputMag"/>: arms
    /// fade out smoothly as the character decelerates to a stop and fade back in when gait
    /// resumes, matching the leg blend behaviour exactly without duplicating the ramping logic.
    ///
    /// Axis convention:
    /// Arm joints are built by the same <c>RagdollBuilder</c> as leg joints. The joint's
    /// primary axis (joint.axis = Vector3.right) maps to the Z component of targetRotation
    /// space, so the correct swing axis is Vector3.forward (Z). This is exposed as
    /// <see cref="_armSwingAxis"/> for Inspector tuning in case the joint configuration changes.
    /// The elbow bend axis follows the same convention and is exposed as <see cref="_elbowAxis"/>.
    ///
    /// LowerArm and Hand joints are intentionally left WITHOUT SLERP drives (they remain
    /// floppy). Only <c>UpperArm</c> joints receive targetRotation commands here; LowerArm
    /// targetRotation is set to a constant slight elbow-bend so the arm hangs naturally.
    ///
    /// <see cref="RagdollSetup"/> must apply SLERP drives to UpperArm_L/R for this component's
    /// targetRotation commands to take physical effect.
    ///
    /// Attach to the same Hips GameObject as <see cref="LegAnimator"/>.
    /// Lifecycle: Awake (cache joints + LegAnimator reference), FixedUpdate (apply arm rotations).
    /// Collaborators: <see cref="LegAnimator"/> (reads Phase and SmoothedInputMag),
    /// Unity ConfigurableJoint.
    /// </summary>
    public class ArmAnimator : MonoBehaviour
    {
        // ── Serialized Fields ────────────────────────────────────────────────

        [SerializeField, Range(0f, 60f)]
        [Tooltip("Peak forward/backward swing angle (degrees) for upper arm joints during gait. " +
                 "Smaller than leg swing for natural proportions. Default 20°.")]
        private float _armSwingAngle = 20f;

        [SerializeField, Range(0f, 45f)]
        [Tooltip("Constant elbow bend angle (degrees) applied to lower arm joints throughout gait. " +
                 "Keeps the forearm slightly bent for a natural pose. Default 15°.")]
        private float _elbowBendAngle = 15f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Global scale multiplier for arm swing amplitude. " +
                 "0 = arms completely disabled (identity). " +
                 "1 = full swing (default). Use for blending or disabling arms at runtime.")]
        private float _armSwingScale = 1f;

        // DESIGN: _armSwingAxis and _elbowAxis follow the same convention as LegAnimator's
        // _swingAxis/_kneeAxis. With RagdollBuilder defaults (joint.axis = Vector3.right,
        // secondaryAxis = Vector3.forward), ConfigurableJoint.targetRotation maps the primary
        // hinge axis to the Z component (NOT X). Rotating around Vector3.forward (Z) swings
        // the arm forward/backward in the sagittal plane. Using Vector3.right (X) would
        // cause lateral abduction (sideways flap) instead.
        // These are serialized so they can be corrected in the Inspector without code changes.

        [SerializeField]
        [Tooltip("Rotation axis for upper-arm forward/backward swing in ConfigurableJoint " +
                 "targetRotation space. With RagdollBuilder defaults (joint.axis=right, " +
                 "secondaryAxis=forward), the correct value is (0, 0, 1) — Z maps to the " +
                 "primary hinge axis. Adjust if the joint axis configuration changes.")]
        private Vector3 _armSwingAxis = new Vector3(0f, 0f, 1f);

        [SerializeField]
        [Tooltip("Rotation axis for lower-arm elbow bend in ConfigurableJoint targetRotation " +
                 "space. With RagdollBuilder defaults (joint.axis=right, secondaryAxis=forward), " +
                 "the correct value is (0, 0, 1) — Z maps to the primary hinge axis. " +
                 "Adjust if the joint axis configuration changes.")]
        private Vector3 _elbowAxis = new Vector3(0f, 0f, 1f);

        // ── Private Fields ───────────────────────────────────────────────────

        /// <summary>Left upper arm ConfigurableJoint, found by name in Awake.</summary>
        private ConfigurableJoint _upperArmL;

        /// <summary>Right upper arm ConfigurableJoint, found by name in Awake.</summary>
        private ConfigurableJoint _upperArmR;

        /// <summary>Left lower arm ConfigurableJoint, found by name in Awake.</summary>
        private ConfigurableJoint _lowerArmL;

        /// <summary>Right lower arm ConfigurableJoint, found by name in Awake.</summary>
        private ConfigurableJoint _lowerArmR;

        /// <summary>
        /// Sibling LegAnimator, used to read <see cref="LegAnimator.Phase"/> and
        /// <see cref="LegAnimator.SmoothedInputMag"/> each FixedUpdate.
        /// </summary>
        private LegAnimator _legAnimator;

        /// <summary>
        /// Sibling GrabController, used to skip arm swing on grabbed arms.
        /// Null when no GrabController is present (Phase 1–3 prefabs).
        /// </summary>
        private GrabController _grabController;

        // ── Unity Lifecycle ──────────────────────────────────────────────────

        private void Awake()
        {
            // STEP 1: Cache the sibling LegAnimator reference.
            if (!TryGetComponent(out _legAnimator))
            {
                Debug.LogWarning("[ArmAnimator] No LegAnimator found on this GameObject. " +
                                 "Arm swing will remain at identity.", this);
            }

            // STEP 1b: Cache optional GrabController for grab cooperation.
            TryGetComponent(out _grabController);

            // STEP 2: Locate the four arm ConfigurableJoints by searching children by name.
            //         Pattern mirrors LegAnimator's Awake exactly — hierarchy-agnostic name lookup.
            ConfigurableJoint[] allJoints = GetComponentsInChildren<ConfigurableJoint>(includeInactive: true);
            foreach (ConfigurableJoint joint in allJoints)
            {
                switch (joint.gameObject.name)
                {
                    case "UpperArm_L":
                        _upperArmL = joint;
                        break;
                    case "UpperArm_R":
                        _upperArmR = joint;
                        break;
                    case "LowerArm_L":
                        _lowerArmL = joint;
                        break;
                    case "LowerArm_R":
                        _lowerArmR = joint;
                        break;
                }
            }

            // STEP 3: Warn about missing joints so misconfigured prefabs are obvious.
            if (_upperArmL == null)
            {
                Debug.LogWarning("[ArmAnimator] 'UpperArm_L' ConfigurableJoint not found in children.", this);
            }

            if (_upperArmR == null)
            {
                Debug.LogWarning("[ArmAnimator] 'UpperArm_R' ConfigurableJoint not found in children.", this);
            }

            if (_lowerArmL == null)
            {
                Debug.LogWarning("[ArmAnimator] 'LowerArm_L' ConfigurableJoint not found in children.", this);
            }

            if (_lowerArmR == null)
            {
                Debug.LogWarning("[ArmAnimator] 'LowerArm_R' ConfigurableJoint not found in children.", this);
            }
        }

        private void FixedUpdate()
        {
            // STEP 1: Gate on missing LegAnimator — reset to identity and bail out.
            if (_legAnimator == null)
            {
                SetAllArmTargetsToIdentity();
                return;
            }

            float smoothedInputMag = _legAnimator.SmoothedInputMag;

            // STEP 2: When SmoothedInputMag is near zero (character idle or decelerating to stop),
            //         set all arm targets directly to identity and return.
            //         This keeps arms relaxed at rest and avoids residual micro-sway.
            if (smoothedInputMag < 0.01f)
            {
                SetAllArmTargetsToIdentity();
                return;
            }

            // STEP 3: Compute effective amplitude with _armSwingScale multiplier.
            //         Both are in [0, 1] range; multiplying gives the final blend factor.
            float effectiveScale = smoothedInputMag * _armSwingScale;

            // STEP 4: Read the gait phase from LegAnimator and compute arm phases.
            //         Arms use the OPPOSITE phase from legs:
            //           Left arm uses  (phase + π) → opposite of left leg (phase)
            //           Right arm uses (phase)       → opposite of right leg (phase + π)
            //         This means left arm forward = right leg forward (natural human gait).
            //
            //         DESIGN: The phase relationship is:
            //           LegAnimator left leg  uses  phase       (sin positive = forward swing)
            //           LegAnimator right leg uses  phase + π   (sin positive = forward swing)
            //           ArmAnimator left arm  uses  phase + π   (counter to left leg)
            //           ArmAnimator right arm uses  phase       (counter to right leg)
            float legPhase = _legAnimator.Phase;

            float sinLeft  = Mathf.Sin(legPhase + Mathf.PI);   // opposite to left leg
            float sinRight = Mathf.Sin(legPhase);              // opposite to right leg

            float leftSwingDeg  = sinLeft  * _armSwingAngle * effectiveScale;
            float rightSwingDeg = sinRight * _armSwingAngle * effectiveScale;

            // STEP 5: Apply upper arm swing rotations using local-space targetRotation.
            //         Pattern is identical to LegAnimator.ApplyLocalSpaceSwing.
            ApplyArmSwing(leftSwingDeg, rightSwingDeg, effectiveScale);
        }

        // ── Private Methods ──────────────────────────────────────────────────

        /// <summary>
        /// Applies the computed arm swing angles to the four arm ConfigurableJoints.
        /// Upper arms receive a sinusoidal swing; lower arms receive a constant elbow bend.
        /// Rotation axis in targetRotation space is <see cref="_armSwingAxis"/> /
        /// <see cref="_elbowAxis"/> (default Vector3.forward / Z) for the same reason as
        /// LegAnimator — the primary joint hinge maps to Z in targetRotation space.
        /// </summary>
        /// <param name="leftSwingDeg">Signed swing angle (degrees) for the left upper arm.</param>
        /// <param name="rightSwingDeg">Signed swing angle (degrees) for the right upper arm.</param>
        /// <param name="effectiveScale">Combined amplitude scale [0,1] (smoothedInputMag × _armSwingScale).</param>
        private void ApplyArmSwing(float leftSwingDeg, float rightSwingDeg, float effectiveScale)
        {
            // Check grab state — skip swing on arms that are currently grabbing.
            bool leftGrabbing = _grabController != null && _grabController.IsGrabbingLeft;
            bool rightGrabbing = _grabController != null && _grabController.IsGrabbingRight;

            // Upper arms: sinusoidal counter-swing (skip grabbed side).
            if (_upperArmL != null && !leftGrabbing)
            {
                _upperArmL.targetRotation = Quaternion.AngleAxis(leftSwingDeg, _armSwingAxis);
            }

            if (_upperArmR != null && !rightGrabbing)
            {
                _upperArmR.targetRotation = Quaternion.AngleAxis(rightSwingDeg, _armSwingAxis);
            }

            // Lower arms: constant elbow bend, scaled by effective amplitude so elbows also
            // relax to identity when idle. Positive angle = elbow forward flex.
            float elbowDeg = _elbowBendAngle * effectiveScale;

            if (_lowerArmL != null && !leftGrabbing)
            {
                _lowerArmL.targetRotation = Quaternion.AngleAxis(elbowDeg, _elbowAxis);
            }

            if (_lowerArmR != null && !rightGrabbing)
            {
                _lowerArmR.targetRotation = Quaternion.AngleAxis(elbowDeg, _elbowAxis);
            }
        }

        /// <summary>
        /// Sets all four arm joint <c>targetRotation</c> values immediately to
        /// <see cref="Quaternion.identity"/>, removing any active arm swing pose and
        /// letting the joint's natural drive return the limb to its resting orientation.
        /// Called during idle (SmoothedInputMag ≈ 0) and when LegAnimator is missing.
        /// </summary>
        private void SetAllArmTargetsToIdentity()
        {
            bool leftGrabbing = _grabController != null && _grabController.IsGrabbingLeft;
            bool rightGrabbing = _grabController != null && _grabController.IsGrabbingRight;

            if (_upperArmL != null && !leftGrabbing) { _upperArmL.targetRotation = Quaternion.identity; }
            if (_upperArmR != null && !rightGrabbing) { _upperArmR.targetRotation = Quaternion.identity; }
            if (_lowerArmL != null && !leftGrabbing) { _lowerArmL.targetRotation = Quaternion.identity; }
            if (_lowerArmR != null && !rightGrabbing) { _lowerArmR.targetRotation = Quaternion.identity; }
        }
    }
}
