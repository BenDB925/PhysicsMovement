using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Drives procedural arm-swing animation on the ragdoll's four arm ConfigurableJoints
    /// (UpperArm_L/R, LowerArm_L/R) during gait, counter-swinging opposite to the legs.
    ///
    /// The arm swing phase is read from <see cref="LegAnimator.Phase"/> and offset by π so
    /// that the left arm swings forward when the right leg swings forward (natural human gait).
    /// The swing amplitude is blended by <see cref="LegAnimator.SmoothedInputMag"/> and the
    /// walk-to-sprint posture blend from <see cref="PlayerMovement.SprintNormalized"/>: arms
    /// fade out smoothly as the character decelerates to a stop, then widen their swing and
    /// tighten elbow bend as sprint ramps in.
    ///
    /// Axis convention:
    /// Arm joints use joint.axis = Vector3.forward and secondaryAxis = Vector3.up
    /// (see RagdollBuilder). The tertiary axis cross(forward, up) = right maps to
    /// the Z component of targetRotation space, so Quaternion.AngleAxis(angle,
    /// Vector3.forward) produces sagittal arm swing. This is exposed as
    /// <see cref="_armSwingAxis"/> for Inspector tuning in case the joint configuration changes.
    /// The elbow bend axis follows the same convention and is exposed as <see cref="_elbowAxis"/>.
    ///
    /// LowerArm joints receive a lighter SLERP drive (configured by <see cref="RagdollSetup"/>)
    /// so the constant elbow-bend targetRotation is honoured by PhysX while keeping a
    /// slightly loose feel. Hand joints remain floppy (no drive).
    ///
    /// <see cref="RagdollSetup"/> must apply SLERP drives to UpperArm_L/R and LowerArm_L/R
    /// for this component's targetRotation commands to take physical effect.
    ///
    /// Attach to the same Hips GameObject as <see cref="LegAnimator"/>.
    /// Lifecycle: Awake (cache joints + LegAnimator reference), FixedUpdate (apply arm rotations).
    /// Collaborators: <see cref="LegAnimator"/> (reads Phase and SmoothedInputMag),
    /// <see cref="PlayerMovement"/> (reads SprintNormalized), Unity ConfigurableJoint.
    /// </summary>
    public class ArmAnimator : MonoBehaviour
    {
        // ── Serialized Fields ────────────────────────────────────────────────

        [SerializeField, Range(0f, 60f)]
        [Tooltip("Peak forward/backward swing angle (degrees) for upper arm joints during gait. " +
                 "Smaller than leg swing for natural proportions. Default 20°.")]
        private float _armSwingAngle = 20f;

        [SerializeField, Range(20f, 60f)]
        [Tooltip("Peak forward/backward swing angle (degrees) for upper arm joints at full sprint. " +
             "Blended from _armSwingAngle by PlayerMovement.SprintNormalized. Default 45°.")]
        private float _sprintArmSwingAngle = 45f;

        [SerializeField, Range(0f, 45f)]
        [Tooltip("Constant elbow bend angle (degrees) applied to lower arm joints throughout gait. " +
                 "Keeps the forearm slightly bent for a natural pose. Default 15°.")]
        private float _elbowBendAngle = 15f;

        [SerializeField, Range(15f, 45f)]
        [Tooltip("Constant elbow bend angle (degrees) applied to lower arm joints at full sprint. " +
             "Blended from _elbowBendAngle by PlayerMovement.SprintNormalized. Default 35°.")]
        private float _sprintElbowBendAngle = 35f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Global scale multiplier for arm swing amplitude. " +
                 "0 = arms completely disabled (identity). " +
                 "1 = full swing (default). Use for blending or disabling arms at runtime.")]
        private float _armSwingScale = 1f;

        [SerializeField]
        [Tooltip("Non-linear response curve mapping SmoothedInputMag (0–1) to arm swing " +
                 "amplitude factor (0–1). Default ease-in-out keeps arms restrained at slow " +
                 "walk and assertive at higher speeds. X = input magnitude, Y = amplitude.")]
        private AnimationCurve _swingAmplitudeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [SerializeField, Range(0f, 45f)]
        [Tooltip("Rest abduction angle (degrees) that pushes each arm outward from the body. " +
                 "Applied at idle and during swing so arms never clip through the torso. " +
                 "UpperArm joints use axis=forward, so rotation around Vector3.right " +
                 "in targetRotation space produces frontal-plane abduction.")]
        private float _restAbductionAngle = 12f;

        [Header("Recovery Brace")]
        [SerializeField, Range(0f, 1f)]
        [Tooltip("Arm swing amplitude multiplier during active recovery (stumble, catch-step). " +
                 "0 = arms freeze, 1 = no dampening. Default 0.2 keeps arms tight while bracing.")]
        private float _braceSwingDampen = 0.2f;

        [SerializeField, Range(0f, 45f)]
        [Tooltip("Extra elbow bend angle (degrees) added during active recovery. " +
                 "Pulls forearms closer to the body for a visible bracing pose.")]
        private float _braceElbowBendBoost = 20f;

        [SerializeField, Range(1f, 30f)]
        [Tooltip("Rate at which the brace blend ramps in and out (units per second). " +
                 "Higher values produce a snappier transition into the brace pose.")]
        private float _braceBlendSpeed = 10f;

        [Header("Airborne Arm Raise")]
        [SerializeField, Range(0f, 60f)]
        [Tooltip("Extra abduction angle (degrees) added to each upper arm while airborne, " +
                 "pushing arms outward for an instinctive balance-seeking pose.")]
        private float _airborneAbductionBoost = 25f;

        [SerializeField, Range(-30f, 30f)]
        [Tooltip("Forward reach angle (degrees) applied via the arm swing axis while airborne. " +
                 "Positive = arms reach forward slightly.")]
        private float _airborneForwardReach = 10f;

        [SerializeField, Range(0f, 45f)]
        [Tooltip("Elbow bend angle (degrees) applied to lower arms while airborne. " +
                 "Slightly straighter than walk bend for an open-arm appearance.")]
        private float _airborneElbowBend = 8f;

        [SerializeField, Range(1f, 30f)]
        [Tooltip("Rate at which the airborne arm raise blends in on Airborne entry " +
                 "and blends out on landing (units per second).")]
        private float _airborneBlendSpeed = 6f;

        [Header("Expression Amplitude Caps")]
        [SerializeField, Range(0f, 90f)]
        [Tooltip("Hard cap (degrees) on the final upper-arm swing angle after all " +
                 "expression layers (amplitude curve, sprint blend, brace dampen) are composed. " +
                 "Prevents extreme inspector values from exceeding safe joint bounds.")]
        private float _maxSwingAngleDeg = 60f;

        [SerializeField, Range(0f, 90f)]
        [Tooltip("Hard cap (degrees) on the composed elbow bend angle after brace boost " +
                 "is added. Prevents over-bend that could clip through the torso.")]
        private float _maxElbowAngleDeg = 55f;

        [SerializeField, Range(0f, 60f)]
        [Tooltip("Hard cap (degrees) on the total upper-arm abduction angle during the " +
                 "airborne arm raise. Prevents arms from exceeding a natural range.")]
        private float _maxAbductionAngleDeg = 45f;

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
        /// Sibling PlayerMovement, used to read <see cref="PlayerMovement.SprintNormalized"/>
        /// for sprint-scaled arm posture.
        /// </summary>
        private PlayerMovement _playerMovement;

        /// <summary>
        /// Sibling LocomotionDirector, used to read <see cref="LocomotionDirector.IsRecoveryActive"/>
        /// for arm brace during recovery.
        /// </summary>
        private LocomotionDirector _locomotionDirector;

        /// <summary>Smoothed 0–1 blend toward the brace pose. Ramps in/out at <see cref="_braceBlendSpeed"/>.</summary>
        private float _currentBraceBlend;

        /// <summary>Sibling CharacterState, used for Airborne entry/exit detection.</summary>
        private CharacterState _characterState;

        /// <summary>True while CharacterState is Airborne. Set by OnCharacterStateChanged.</summary>
        private bool _isAirborne;

        /// <summary>
        /// C8.4b: True while CharacterState is Fallen or GettingUp. Suppresses all arm
        /// expression so it never fights recovery torques.
        /// </summary>
        private bool _suppressExpression;

        /// <summary>Smoothed 0–1 blend toward the airborne raised-arm pose.</summary>
        private float _currentAirborneBlend;

        /// <summary>Rest abduction rotation cached in Awake for the left upper arm (negative angle).</summary>
        private Quaternion _abductionL;

        /// <summary>Rest abduction rotation cached in Awake for the right upper arm (positive angle).</summary>
        private Quaternion _abductionR;

        // ── Unity Lifecycle ──────────────────────────────────────────────────

        private void Awake()
        {
            // STEP 1: Cache the sibling LegAnimator reference.
            if (!TryGetComponent(out _legAnimator))
            {
                Debug.LogWarning("[ArmAnimator] No LegAnimator found on this GameObject. " +
                                 "Arm swing will remain at identity.", this);
            }

            // STEP 1a: Cache PlayerMovement when present so sprint posture can use the same
            //          blend signal as the rest of locomotion. Missing PlayerMovement falls
            //          back to walk posture instead of warning.
            TryGetComponent(out _playerMovement);

            // STEP 1a2: Cache LocomotionDirector for recovery-brace reads. Missing director
            //           falls back to no brace (arms swing normally).
            TryGetComponent(out _locomotionDirector);

            // STEP 1a3: Cache CharacterState for airborne arm raise. Missing state means
            //           no airborne blend (arms use walk/idle pose only).
            TryGetComponent(out _characterState);

            // STEP 1b: Cache abduction quaternions.
            // In targetRotation space, Vector3.right (X) maps to rotation around the
            // joint's primary axis (forward in world). Positive rotation around +Z takes
            // the -Y arm direction toward +X — outward for the right arm, inward for the
            // left. So left gets a negative angle, right gets positive.
            _abductionL = Quaternion.AngleAxis( _restAbductionAngle, Vector3.right);
            _abductionR = Quaternion.AngleAxis(-_restAbductionAngle, Vector3.right);

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

        private void OnEnable()
        {
            if (_characterState != null)
            {
                _characterState.OnStateChanged += OnCharacterStateChanged;
            }
        }

        private void OnDisable()
        {
            if (_characterState != null)
            {
                _characterState.OnStateChanged -= OnCharacterStateChanged;
            }
        }

        private void FixedUpdate()
        {
            // C8.4b: Kill-switch — zero all arm expression during Fallen/GettingUp.
            if (_suppressExpression)
            {
                _currentAirborneBlend = 0f;
                _currentBraceBlend = 0f;
                SetAllArmTargetsToRest();
                return;
            }

            // STEP 0: Ramp airborne blend toward target.
            float airborneTarget = _isAirborne ? 1f : 0f;
            _currentAirborneBlend = Mathf.MoveTowards(_currentAirborneBlend, airborneTarget,
                _airborneBlendSpeed * Time.fixedDeltaTime);

            // STEP 1: Gate on missing LegAnimator — apply airborne pose if blending, else rest.
            if (_legAnimator == null)
            {
                if (_currentAirborneBlend > 0f)
                    ApplyAirbornePose(_currentAirborneBlend);
                else
                    SetAllArmTargetsToRest();
                return;
            }

            float smoothedInputMag = _legAnimator.SmoothedInputMag;

            // STEP 2: When SmoothedInputMag is near zero and not airborne,
            //         set arm targets to rest abduction pose (or airborne blend) and return.
            if (smoothedInputMag < 0.01f && _currentAirborneBlend <= 0f)
            {
                SetAllArmTargetsToRest();
                return;
            }

            // STEP 3: Compute effective amplitude with non-linear response curve and
            //         _armSwingScale multiplier. The curve remaps SmoothedInputMag so that low
            //         speeds produce restrained arm swing while higher speeds feel assertive.
            float curvedMag = _swingAmplitudeCurve != null
                ? Mathf.Clamp01(_swingAmplitudeCurve.Evaluate(smoothedInputMag))
                : smoothedInputMag;
            float effectiveScale = curvedMag * _armSwingScale;

            // STEP 3b: Read the locomotion sprint blend so upper-arm swing widens and
            //          elbow bend tightens as sprint ramps in.
            float sprintNormalized = GetCurrentSprintNormalized();
            float effectiveArmSwingAngle = GetEffectiveArmSwingAngle(sprintNormalized);
            float effectiveElbowBendAngle = GetEffectiveElbowBendAngle(sprintNormalized);

            // STEP 3c: Arm brace during recovery — dampen swing and tighten elbows when
            //          LocomotionDirector signals active recovery (stumble, catch-step, etc.).
            float braceTarget = (_locomotionDirector != null && _locomotionDirector.IsRecoveryActive) ? 1f : 0f;
            _currentBraceBlend = Mathf.MoveTowards(_currentBraceBlend, braceTarget,
                _braceBlendSpeed * Time.fixedDeltaTime);

            if (_currentBraceBlend > 0f)
            {
                float dampenFactor = Mathf.Lerp(1f, _braceSwingDampen, _currentBraceBlend);
                effectiveScale *= dampenFactor;
                effectiveElbowBendAngle += _braceElbowBendBoost * _currentBraceBlend;
            }

            // STEP 3d: Clamp composed elbow bend to the hard cap.
            effectiveElbowBendAngle = Mathf.Clamp(effectiveElbowBendAngle, 0f, _maxElbowAngleDeg);

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

            float leftSwingDeg  = sinLeft  * effectiveArmSwingAngle * effectiveScale;
            float rightSwingDeg = sinRight * effectiveArmSwingAngle * effectiveScale;

            // STEP 4b: Clamp swing angles to the hard cap.
            leftSwingDeg  = Mathf.Clamp(leftSwingDeg,  -_maxSwingAngleDeg, _maxSwingAngleDeg);
            rightSwingDeg = Mathf.Clamp(rightSwingDeg, -_maxSwingAngleDeg, _maxSwingAngleDeg);

            // STEP 5: Apply upper arm swing rotations using local-space targetRotation.
            //         When airborne blend > 0, interpolate between gait swing and the
            //         raised-outward airborne pose.
            if (_currentAirborneBlend > 0f)
            {
                ApplyArmSwingWithAirborneBlend(leftSwingDeg, rightSwingDeg,
                    effectiveElbowBendAngle, _currentAirborneBlend);
            }
            else
            {
                ApplyArmSwing(leftSwingDeg, rightSwingDeg, effectiveElbowBendAngle);
            }
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
        /// <param name="elbowDeg">Constant lower-arm bend angle (degrees) for the current locomotion blend.</param>
        private void ApplyArmSwing(float leftSwingDeg, float rightSwingDeg, float elbowDeg)
        {
            // Upper arms: rest abduction composed with sinusoidal counter-swing.
            // Abduction keeps arms clear of the torso; swing adds the gait motion on top.
            if (_upperArmL != null)
            {
                _upperArmL.targetRotation = _abductionL
                    * Quaternion.AngleAxis(leftSwingDeg, _armSwingAxis);
            }

            if (_upperArmR != null)
            {
                _upperArmR.targetRotation = _abductionR
                    * Quaternion.AngleAxis(rightSwingDeg, _armSwingAxis);
            }

            // Lower arms: constant elbow bend. The bend is NOT scaled by amplitude so the
            // elbows stay bent at rest — straight arms look unnatural even at idle.
            if (_lowerArmL != null)
            {
                _lowerArmL.targetRotation = Quaternion.AngleAxis(elbowDeg, _elbowAxis);
            }

            if (_lowerArmR != null)
            {
                _lowerArmR.targetRotation = Quaternion.AngleAxis(elbowDeg, _elbowAxis);
            }
        }

        /// <summary>
        /// Sets upper arm targets to the rest abduction pose (arms hanging beside the
        /// body with a slight outward angle) and lower arms to identity.
        /// Called during idle (SmoothedInputMag ≈ 0) and when LegAnimator is missing.
        /// </summary>
        private void SetAllArmTargetsToRest()
        {
            if (_upperArmL != null) { _upperArmL.targetRotation = _abductionL; }
            if (_upperArmR != null) { _upperArmR.targetRotation = _abductionR; }

            // Keep constant elbow bend even at idle — straight arms look unnatural.
            float sprintNormalized = GetCurrentSprintNormalized();
            float elbowBendDeg = GetEffectiveElbowBendAngle(sprintNormalized);
            Quaternion elbowRest = Quaternion.AngleAxis(elbowBendDeg, _elbowAxis);
            if (_lowerArmL != null) { _lowerArmL.targetRotation = elbowRest; }
            if (_lowerArmR != null) { _lowerArmR.targetRotation = elbowRest; }
        }

        private float GetCurrentSprintNormalized()
        {
            return _playerMovement != null ? Mathf.Clamp01(_playerMovement.SprintNormalized) : 0f;
        }

        private float GetEffectiveArmSwingAngle(float sprintNormalized)
        {
            return Mathf.Lerp(_armSwingAngle, _sprintArmSwingAngle, Mathf.Clamp01(sprintNormalized));
        }

        private float GetEffectiveElbowBendAngle(float sprintNormalized)
        {
            return Mathf.Lerp(_elbowBendAngle, _sprintElbowBendAngle, Mathf.Clamp01(sprintNormalized));
        }

        /// <summary>
        /// Applies the raised-outward airborne arm pose, blended by <paramref name="blend"/>.
        /// Called when airborne blend > 0 and there is no active gait swing to interpolate with.
        /// </summary>
        private void ApplyAirbornePose(float blend)
        {
            float boostedAbdL =  Mathf.Clamp(_restAbductionAngle + _airborneAbductionBoost * blend,
                                              -_maxAbductionAngleDeg, _maxAbductionAngleDeg);
            float boostedAbdR = -Mathf.Clamp(_restAbductionAngle + _airborneAbductionBoost * blend,
                                              -_maxAbductionAngleDeg, _maxAbductionAngleDeg);
            float forwardReach = _airborneForwardReach * blend;
            float elbowDeg = Mathf.Lerp(_elbowBendAngle, _airborneElbowBend, blend);

            if (_upperArmL != null)
            {
                _upperArmL.targetRotation = Quaternion.AngleAxis(boostedAbdL, Vector3.right)
                    * Quaternion.AngleAxis(forwardReach, _armSwingAxis);
            }
            if (_upperArmR != null)
            {
                _upperArmR.targetRotation = Quaternion.AngleAxis(boostedAbdR, Vector3.right)
                    * Quaternion.AngleAxis(forwardReach, _armSwingAxis);
            }
            if (_lowerArmL != null)
            {
                _lowerArmL.targetRotation = Quaternion.AngleAxis(elbowDeg, _elbowAxis);
            }
            if (_lowerArmR != null)
            {
                _lowerArmR.targetRotation = Quaternion.AngleAxis(elbowDeg, _elbowAxis);
            }
        }

        /// <summary>
        /// Applies gait arm swing interpolated toward the airborne raised-outward pose.
        /// At blend=0 this is identical to <see cref="ApplyArmSwing"/>; at blend=1 it
        /// produces the full airborne pose.
        /// </summary>
        private void ApplyArmSwingWithAirborneBlend(float leftSwingDeg, float rightSwingDeg,
            float elbowDeg, float blend)
        {
            // Compute airborne upper-arm targets.
            float boostedAbdL =  Mathf.Clamp(_restAbductionAngle + _airborneAbductionBoost * blend,
                                              -_maxAbductionAngleDeg, _maxAbductionAngleDeg);
            float boostedAbdR = -Mathf.Clamp(_restAbductionAngle + _airborneAbductionBoost * blend,
                                              -_maxAbductionAngleDeg, _maxAbductionAngleDeg);
            float forwardReach = _airborneForwardReach * blend;

            // Gait targets (same as ApplyArmSwing).
            Quaternion gaitL = _abductionL * Quaternion.AngleAxis(leftSwingDeg, _armSwingAxis);
            Quaternion gaitR = _abductionR * Quaternion.AngleAxis(rightSwingDeg, _armSwingAxis);

            // Airborne targets.
            Quaternion airL = Quaternion.AngleAxis(boostedAbdL, Vector3.right)
                * Quaternion.AngleAxis(forwardReach, _armSwingAxis);
            Quaternion airR = Quaternion.AngleAxis(boostedAbdR, Vector3.right)
                * Quaternion.AngleAxis(forwardReach, _armSwingAxis);

            if (_upperArmL != null)
                _upperArmL.targetRotation = Quaternion.Slerp(gaitL, airL, blend);
            if (_upperArmR != null)
                _upperArmR.targetRotation = Quaternion.Slerp(gaitR, airR, blend);

            // Elbow: blend between gait elbow bend and airborne elbow bend.
            float airElbow = Mathf.Lerp(elbowDeg, _airborneElbowBend, blend);
            Quaternion elbowRot = Quaternion.AngleAxis(airElbow, _elbowAxis);
            if (_lowerArmL != null)
                _lowerArmL.targetRotation = elbowRot;
            if (_lowerArmR != null)
                _lowerArmR.targetRotation = elbowRot;
        }

        /// <summary>
        /// Reacts to CharacterState transitions. Sets <see cref="_isAirborne"/> so
        /// FixedUpdate ramps the airborne arm blend accordingly.
        /// </summary>
        private void OnCharacterStateChanged(CharacterStateType previousState, CharacterStateType newState)
        {
            // C8.4b: Kill-switch — suppress arm expression during Fallen/GettingUp.
            if (newState == CharacterStateType.Fallen || newState == CharacterStateType.GettingUp)
            {
                _suppressExpression = true;
                _isAirborne = false;
            }
            else if (newState == CharacterStateType.Standing || newState == CharacterStateType.Moving)
            {
                _suppressExpression = false;
            }

            if (newState == CharacterStateType.Airborne)
            {
                _isAirborne = true;
            }
            else if (previousState == CharacterStateType.Airborne)
            {
                _isAirborne = false;
            }
        }
    }
}
