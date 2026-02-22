using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Drives procedural gait animation on the ragdoll's four leg ConfigurableJoints
    /// by advancing a phase accumulator from movement input magnitude and applying
    /// sinusoidal target rotations each FixedUpdate.
    /// Left and right upper legs are offset by π (half-cycle), producing an alternating
    /// stepping pattern. Lower legs receive a constant knee-bend target during gait.
    ///
    /// Axis convention (Bug fix — Phase 3E1 axis correction):
    /// Unity's ConfigurableJoint.targetRotation maps the joint's primary axis (joint.axis)
    /// to the Z component of the target Euler rotation, NOT the X component as might be
    /// intuited. With RagdollBuilder defaults (joint.axis = Vector3.right, secondaryAxis =
    /// Vector3.forward), the correct swing axis in targetRotation space is Vector3.forward
    /// (Z), not Vector3.right (X). Using X caused lateral abduction (side-to-side wiggle)
    /// instead of sagittal swing (forward/backward lift). The axes are now serialized as
    /// <see cref="_swingAxis"/> and <see cref="_kneeAxis"/> for Inspector tuning.
    ///
    /// Idle settle behaviour (Phase 3E2):
    /// — When move input drops to zero, all four joint targetRotations are set directly
    ///   to <see cref="Quaternion.identity"/> each FixedUpdate, so legs immediately
    ///   relax to their rest pose.
    /// — The gait phase accumulator decays toward zero during idle at a rate controlled
    ///   by <see cref="_idleBlendSpeed"/>.  When input resumes the next step starts from
    ///   a near-neutral phase, keeping the first-frame rotation amplitude small.
    /// — A smoothed input scale (<see cref="_smoothedInputMag"/>) ramps from 0 to 1 as
    ///   input resumes, preventing an abrupt snap to full gait amplitude immediately
    ///   after a period of idle.  The ramp rate is also controlled by
    ///   <see cref="_idleBlendSpeed"/>.  When stopping, the smoothed scale is immediately
    ///   zeroed so legs snap cleanly to identity without residual sway.
    ///
    /// The combination of phase decay + smoothed re-entry eliminates abrupt visual pops
    /// on quick move/stop/move toggles while preserving the correct L/R alternating gait
    /// phase relationship at all times.
    ///
    /// When the character is in the <see cref="CharacterStateType.Fallen"/> or
    /// <see cref="CharacterStateType.GettingUp"/> state, all leg joints are returned
    /// immediately to <see cref="Quaternion.identity"/> and the phase / scale are reset
    /// to zero.
    /// Attach to the Hips (root) GameObject alongside <see cref="BalanceController"/>,
    /// <see cref="PlayerMovement"/>, and <see cref="CharacterState"/>.
    /// Lifecycle: Awake (cache joints + siblings), FixedUpdate (advance phase, apply rotations).
    /// Collaborators: <see cref="PlayerMovement"/>, <see cref="CharacterState"/>.
    /// </summary>
    public class LegAnimator : MonoBehaviour
    {
        // ── Serialized Gait Fields ──────────────────────────────────────────

        [SerializeField, Range(0f, 60f)]
        [Tooltip("Peak forward/backward swing angle (degrees) for upper leg joints during gait. " +
                 "Controls the visible stride amplitude. Typical range: 15–35°.")]
        private float _stepAngle = 25f;

        [SerializeField, Range(0f, 10f)]
        [Tooltip("Number of full step cycles per second. Higher = faster cadence. " +
                 "Match roughly to _maxSpeed / stride_length for realistic feel. Typical: 1.5–3 Hz.")]
        private float _stepFrequency = 2f;

        [SerializeField, Range(0f, 60f)]
        [Tooltip("Constant knee-bend angle (degrees) applied to lower leg joints during gait. " +
                 "Adds visible flexion to the lower leg. Typical range: 10–30°.")]
        private float _kneeAngle = 20f;

        [SerializeField, Range(0f, 20f)]
        [Tooltip("Controls three related smooth-transition behaviours:\n" +
                 "1. Phase decay rate: how quickly the gait phase accumulator decays toward zero\n" +
                 "   during idle (radians removed per fixedDeltaTime × π).\n" +
                 "2. Gait entry ramp: how quickly the smoothed input scale lerps from 0→1 when\n" +
                 "   movement resumes, preventing a snap to full gait amplitude.\n" +
                 "Higher = faster transition; 0 = no smoothing. Typical: 3–8.")]
        private float _idleBlendSpeed = 5f;

        // DESIGN: _swingAxis and _kneeAxis are specified in ConfigurableJoint targetRotation
        //         space, which maps joint.axis (the primary hinge) to the Z component of the
        //         Euler/quaternion rotation — NOT the X component as intuition might suggest.
        //         With joint.axis=Vector3.right and secondaryAxis=Vector3.forward (as set by
        //         RagdollBuilder for all leg joints), the targetRotation frame is:
        //           Z → joint.axis (Vector3.right) → sagittal forward/backward swing ✓
        //           X → secondaryAxis (Vector3.forward) → lateral abduction (unwanted for gait)
        //           Y → right×forward = Vector3.down → leg twist (unwanted for gait)
        //         Therefore the correct axis for leg swing and knee bend is Vector3.forward (Z).
        //         These fields are exposed for Inspector tuning so they can be adjusted without
        //         code changes if the ragdoll's joint axes are reconfigured in future.

        [SerializeField]
        [Tooltip("Rotation axis for upper-leg forward/backward swing in ConfigurableJoint targetRotation " +
                 "space. With RagdollBuilder defaults (joint.axis=right, secondaryAxis=forward), " +
                 "the correct value is (0, 0, 1) — Z maps to the primary hinge axis. " +
                 "Adjust if the joint axis configuration changes.")]
        private Vector3 _swingAxis = new Vector3(0f, 0f, 1f);

        [SerializeField]
        [Tooltip("Rotation axis for lower-leg knee bend in ConfigurableJoint targetRotation space. " +
                 "With RagdollBuilder defaults (joint.axis=right, secondaryAxis=forward), " +
                 "the correct value is (0, 0, 1) — Z maps to the primary hinge axis. " +
                 "Adjust if the joint axis configuration changes.")]
        private Vector3 _kneeAxis = new Vector3(0f, 0f, 1f);

        // ── Private Fields ──────────────────────────────────────────────────

        /// <summary>Left upper leg ConfigurableJoint, found by name in Awake.</summary>
        private ConfigurableJoint _upperLegL;

        /// <summary>Right upper leg ConfigurableJoint, found by name in Awake.</summary>
        private ConfigurableJoint _upperLegR;

        /// <summary>Left lower leg ConfigurableJoint, found by name in Awake.</summary>
        private ConfigurableJoint _lowerLegL;

        /// <summary>Right lower leg ConfigurableJoint, found by name in Awake.</summary>
        private ConfigurableJoint _lowerLegR;

        /// <summary>Sibling PlayerMovement component used to read current move input magnitude.</summary>
        private PlayerMovement _playerMovement;

        /// <summary>Sibling CharacterState component used to gate gait on posture state.</summary>
        private CharacterState _characterState;

        /// <summary>
        /// Current gait phase in radians, in the range [0, 2π).
        /// Advances proportionally to move input magnitude each FixedUpdate while moving.
        /// Decays linearly toward 0 during idle at a rate proportional to
        /// <see cref="_idleBlendSpeed"/>, so the next gait cycle resumes from a near-zero
        /// phase with a naturally small starting rotation amplitude.
        /// </summary>
        private float _phase;

        /// <summary>
        /// Smoothed version of the actual move input magnitude, in the range [0, 1].
        /// When input resumes after idle this value ramps up from 0 at a rate controlled
        /// by <see cref="_idleBlendSpeed"/>, preventing the gait amplitude from snapping
        /// to its full value on the very first frame of movement.
        /// When input stops this value is immediately reset to 0, ensuring legs snap
        /// cleanly to identity without any residual sway.
        /// </summary>
        private float _smoothedInputMag;

        // ── Unity Lifecycle ─────────────────────────────────────────────────

        private void Awake()
        {
            // STEP 1: Cache sibling components on the Hips GameObject.
            if (!TryGetComponent(out _playerMovement))
            {
                Debug.LogError("[LegAnimator] Missing PlayerMovement on this GameObject.", this);
            }

            if (!TryGetComponent(out _characterState))
            {
                Debug.LogError("[LegAnimator] Missing CharacterState on this GameObject.", this);
            }

            // STEP 2: Locate the four leg ConfigurableJoints by searching children by name.
            //         The hierarchy is: Hips → UpperLeg_L → LowerLeg_L
            //                           Hips → UpperLeg_R → LowerLeg_R
            //         We search all children (inclusive of nested) to be hierarchy-agnostic.
            ConfigurableJoint[] allJoints = GetComponentsInChildren<ConfigurableJoint>(includeInactive: true);
            foreach (ConfigurableJoint joint in allJoints)
            {
                string segmentName = joint.gameObject.name;
                switch (segmentName)
                {
                    case "UpperLeg_L":
                        _upperLegL = joint;
                        break;
                    case "UpperLeg_R":
                        _upperLegR = joint;
                        break;
                    case "LowerLeg_L":
                        _lowerLegL = joint;
                        break;
                    case "LowerLeg_R":
                        _lowerLegR = joint;
                        break;
                }
            }

            // STEP 3: Warn about missing joints so misconfigured prefabs are obvious.
            if (_upperLegL == null)
            {
                Debug.LogWarning("[LegAnimator] 'UpperLeg_L' ConfigurableJoint not found in children.", this);
            }

            if (_upperLegR == null)
            {
                Debug.LogWarning("[LegAnimator] 'UpperLeg_R' ConfigurableJoint not found in children.", this);
            }

            if (_lowerLegL == null)
            {
                Debug.LogWarning("[LegAnimator] 'LowerLeg_L' ConfigurableJoint not found in children.", this);
            }

            if (_lowerLegR == null)
            {
                Debug.LogWarning("[LegAnimator] 'LowerLeg_R' ConfigurableJoint not found in children.", this);
            }
        }

        private void FixedUpdate()
        {
            // STEP 1: Gate on missing dependencies — skip gracefully.
            if (_playerMovement == null || _characterState == null)
            {
                return;
            }

            // STEP 2: When the character is in a non-ambulatory state, reset all four
            //         leg joints to Quaternion.identity immediately and exit early.
            //         This covers both Fallen (limp) and GettingUp (recovery) states.
            //         Phase and smoothed input scale are also reset to zero so the next
            //         standing gait starts cleanly.
            CharacterStateType state = _characterState.CurrentState;
            if (state == CharacterStateType.Fallen || state == CharacterStateType.GettingUp)
            {
                _phase = 0f;
                _smoothedInputMag = 0f;
                SetAllLegTargetsToIdentity();
                return;
            }

            // STEP 3: Read move input magnitude (0–1).
            //         Input magnitude drives both phase advancement speed and implicitly
            //         represents walking speed. No force is applied here — this script
            //         only sets ConfigurableJoint.targetRotation.
            float inputMagnitude = _playerMovement.CurrentMoveInput.magnitude;

            if (inputMagnitude > 0.01f)
            {
                // STEP 4a: MOVING — advance phase accumulator.
                //          Phase is in radians, cycling [0, 2π).
                //          Scale by 2π × frequency × deltaTime so one full cycle takes exactly
                //          1/_stepFrequency seconds at full input.
                _phase += inputMagnitude * 2f * Mathf.PI * _stepFrequency * Time.fixedDeltaTime;

                // Wrap phase to [0, 2π) to prevent float overflow over time.
                if (_phase >= 2f * Mathf.PI)
                {
                    _phase -= 2f * Mathf.PI;
                }

                // STEP 4b: Ramp up the smoothed input magnitude toward the actual value.
                //          This is the anti-pop mechanism: instead of the gait amplitude
                //          snapping to full value on frame 1 of resumed movement, it ramps
                //          up smoothly.  At blendSpeed 5 and dt 0.01, t ≈ 0.05 per frame,
                //          reaching 95% amplitude in about 60 frames / 0.6 s.
                //          We lerp in float space (not quaternion space) to avoid touching
                //          the joint targetRotation directly, preserving the L/R phase
                //          relationship perfectly.
                float t = Mathf.Clamp01(_idleBlendSpeed * Time.fixedDeltaTime);
                _smoothedInputMag = Mathf.Lerp(_smoothedInputMag, inputMagnitude, t);

                // STEP 5: Compute sinusoidal upper-leg targets.
                //         Left leg uses phase directly; right leg is offset by π (half-cycle)
                //         so they always swing in opposite directions — the alternating gait.
                //         The swing is a pure rotation around _swingAxis (default Z / forward)
                //         in targetRotation space, which drives the primary hinge (joint.axis=right)
                //         for sagittal forward/backward swing.
                //         We scale amplitude by _smoothedInputMag to get the anti-pop ramp.
                float leftSwingDeg  = Mathf.Sin(_phase)            * _stepAngle * _smoothedInputMag;
                float rightSwingDeg = Mathf.Sin(_phase + Mathf.PI) * _stepAngle * _smoothedInputMag;

                // STEP 6: Compute lower-leg knee-bend target.
                //         The knee holds a constant positive bend during gait so the character
                //         looks dynamically flexed rather than stiff-legged.
                float kneeBendDeg = _kneeAngle * _smoothedInputMag;

                // STEP 7: Apply computed rotations to joint targetRotations directly.
                //         Using direct assignment (not slerp) preserves the exact L/R
                //         phase relationship — the smoothing is done via _smoothedInputMag.
                //         DESIGN: Quaternion.AngleAxis with _swingAxis (default Vector3.forward / Z)
                //         is used because ConfigurableJoint.targetRotation maps the primary joint
                //         hinge (joint.axis = Vector3.right) to the Z component of the rotation
                //         in targetRotation space. Using X-axis (Quaternion.Euler(angle, 0, 0))
                //         produced lateral abduction (side-to-side wiggle) instead of the intended
                //         sagittal forward/backward swing that lifts the leg off the ground.
                if (_upperLegL != null)
                {
                    _upperLegL.targetRotation = Quaternion.AngleAxis(leftSwingDeg, _swingAxis);
                }

                if (_upperLegR != null)
                {
                    _upperLegR.targetRotation = Quaternion.AngleAxis(rightSwingDeg, _swingAxis);
                }

                if (_lowerLegL != null)
                {
                    _lowerLegL.targetRotation = Quaternion.AngleAxis(-kneeBendDeg, _kneeAxis);
                }

                if (_lowerLegR != null)
                {
                    _lowerLegR.targetRotation = Quaternion.AngleAxis(-kneeBendDeg, _kneeAxis);
                }
            }
            else
            {
                // STEP 4b: IDLE — set joints to identity, decay phase, and snap smoothed scale.
                //
                //          JOINT RESET: Set all four joint targetRotations directly to
                //          Quaternion.identity every idle frame.  The joint's internal spring
                //          drive then smoothly returns the physical limb to its rest angle.
                //
                //          PHASE DECAY: Move the phase toward 0 by a fixed amount per frame.
                //          At speed 5 and dt 0.01 the phase shrinks by ~0.16 rad/frame,
                //          clearing a full cycle (2π ≈ 6.28 rad) in ~40 frames / 0.4 s.
                //          This ensures that when input next resumes, the phase is near 0 and
                //          the first computed rotation is small, reducing the visual pop even
                //          further on top of the smoothed-input ramp.
                //
                //          SMOOTH SCALE RESET: Reset _smoothedInputMag immediately to 0 so
                //          that when input next arrives the ramp starts from 0.
                float decayStep = _idleBlendSpeed * Mathf.PI * Time.fixedDeltaTime;
                _phase = Mathf.Max(0f, _phase - decayStep);
                _smoothedInputMag = 0f;

                SetAllLegTargetsToIdentity();
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Sets all four leg joint <c>targetRotation</c> values immediately to
        /// <see cref="Quaternion.identity"/>, removing any active gait pose and letting
        /// the joint's natural drive return the limb to its resting orientation.
        /// Called from both the idle branch and the Fallen/GettingUp early-exit path.
        /// </summary>
        private void SetAllLegTargetsToIdentity()
        {
            if (_upperLegL != null) { _upperLegL.targetRotation = Quaternion.identity; }
            if (_upperLegR != null) { _upperLegR.targetRotation = Quaternion.identity; }
            if (_lowerLegL != null) { _lowerLegL.targetRotation = Quaternion.identity; }
            if (_lowerLegR != null) { _lowerLegR.targetRotation = Quaternion.identity; }
        }
    }
}
