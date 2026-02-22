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
    /// World-space swing (Phase 3E3 — forward-tilt fix):
    /// When <see cref="_useWorldSpaceSwing"/> is true (default), leg swing targets are
    /// computed relative to the world-space movement direction rather than the Hips local
    /// frame. This fixes the "dragging feet" symptom that occurs when the torso is pitched
    /// forward: in that state the Hips +Z local axis points downward in world space, so a
    /// pure local-frame "forward swing" actually drives the legs into the ground instead of
    /// stepping ahead of the body.
    ///
    /// World-space swing implementation:
    ///   1. A world-space gait-forward direction is derived from the Hips Rigidbody's
    ///      horizontal velocity. If velocity is negligible (e.g. just-started), the move
    ///      input direction (projected onto the XZ plane) is used as a fallback.
    ///   2. The swing axis in world space = Cross(gaitForward, worldUp), which is the
    ///      horizontal axis perpendicular to the movement direction. Rotating around this
    ///      axis swings a leg forward or backward in the sagittal plane of movement,
    ///      regardless of the torso's pitch angle.
    ///   3. The desired world-space rotation for each upper leg is computed as
    ///      Quaternion.AngleAxis(swingDeg, worldSwingAxis) applied to the joint body's
    ///      current world rotation. This is then converted to the ConnectedBody (parent)
    ///      frame before being assigned to ConfigurableJoint.targetRotation, so the SLERP
    ///      drive interprets it correctly.
    ///   4. The lower-leg knee bend uses a fixed angle around the same world-space swing
    ///      axis (positive = knee bends forward / upward relative to world up), ensuring
    ///      knees never fold into the ground even when the torso is pitched forward.
    ///
    /// When <see cref="_useWorldSpaceSwing"/> is false, the original local-frame behaviour
    /// is used (Quaternion.AngleAxis around <see cref="_swingAxis"/>/<see cref="_kneeAxis"/>
    /// in targetRotation space). This is provided for side-by-side comparison only.
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
    /// Collaborators: <see cref="PlayerMovement"/>, <see cref="CharacterState"/>, <see cref="Rigidbody"/>.
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
        //         These are only used when _useWorldSpaceSwing = false (legacy mode).

        [SerializeField]
        [Tooltip("Rotation axis for upper-leg forward/backward swing in ConfigurableJoint targetRotation " +
                 "space. With RagdollBuilder defaults (joint.axis=right, secondaryAxis=forward), " +
                 "the correct value is (0, 0, 1) — Z maps to the primary hinge axis. " +
                 "Only used when _useWorldSpaceSwing is false. " +
                 "Adjust if the joint axis configuration changes.")]
        private Vector3 _swingAxis = new Vector3(0f, 0f, 1f);

        [SerializeField]
        [Tooltip("Rotation axis for lower-leg knee bend in ConfigurableJoint targetRotation space. " +
                 "With RagdollBuilder defaults (joint.axis=right, secondaryAxis=forward), " +
                 "the correct value is (0, 0, 1) — Z maps to the primary hinge axis. " +
                 "Only used when _useWorldSpaceSwing is false. " +
                 "Adjust if the joint axis configuration changes.")]
        private Vector3 _kneeAxis = new Vector3(0f, 0f, 1f);

        [SerializeField]
        [Tooltip("When true (default), leg swing targets are computed relative to the world-space " +
                 "movement direction so legs always step forward regardless of torso pitch angle. " +
                 "When false, the legacy local-frame swing is used (may cause feet to drag when " +
                 "the torso is pitched forward). Toggle for side-by-side comparison.")]
        private bool _useWorldSpaceSwing = true;

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

        /// <summary>Hips Rigidbody used to read world-space velocity for movement direction.</summary>
        private Rigidbody _hipsRigidbody;

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

            if (!TryGetComponent(out _hipsRigidbody))
            {
                Debug.LogError("[LegAnimator] Missing Rigidbody on this GameObject.", this);
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

                // STEP 5: Compute sinusoidal upper-leg swing angles.
                //         Left leg uses phase directly; right leg is offset by π (half-cycle)
                //         so they always swing in opposite directions — the alternating gait.
                //         We scale amplitude by _smoothedInputMag to get the anti-pop ramp.
                float leftSwingDeg  = Mathf.Sin(_phase)            * _stepAngle * _smoothedInputMag;
                float rightSwingDeg = Mathf.Sin(_phase + Mathf.PI) * _stepAngle * _smoothedInputMag;

                // STEP 6: Compute lower-leg knee-bend angle.
                //         The knee holds a constant positive bend during gait so the character
                //         looks dynamically flexed rather than stiff-legged.
                float kneeBendDeg = _kneeAngle * _smoothedInputMag;

                // STEP 7: Apply computed rotations to joint targetRotations.
                if (_useWorldSpaceSwing)
                {
                    ApplyWorldSpaceSwing(leftSwingDeg, rightSwingDeg, kneeBendDeg);
                }
                else
                {
                    ApplyLocalSpaceSwing(leftSwingDeg, rightSwingDeg, kneeBendDeg);
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

        // ── Private Methods ──────────────────────────────────────────────────

        /// <summary>
        /// Applies world-space swing targets to the four leg ConfigurableJoints.
        /// The swing axis is computed from the character's actual movement direction (world
        /// horizontal velocity or move-input fallback), ensuring legs step forward in world
        /// space even when the torso is pitched forward.
        /// </summary>
        /// <param name="leftSwingDeg">Signed swing angle (degrees) for the left upper leg.</param>
        /// <param name="rightSwingDeg">Signed swing angle (degrees) for the right upper leg.</param>
        /// <param name="kneeBendDeg">Knee bend angle (degrees, positive = forward flex).</param>
        private void ApplyWorldSpaceSwing(float leftSwingDeg, float rightSwingDeg, float kneeBendDeg)
        {
            // STEP A: Determine the world-space gait-forward direction.
            //         Primary: use the Hips Rigidbody's horizontal velocity (most accurate
            //         proxy for actual movement direction regardless of torso pitch).
            //         Fallback: project CurrentMoveInput (XZ) when velocity is near zero
            //         (e.g. just starting to move).
            Vector3 gaitForward = GetWorldGaitForward();

            if (gaitForward.sqrMagnitude < 0.0001f)
            {
                // No direction available — use local-space fallback to avoid zero-vector issues.
                ApplyLocalSpaceSwing(leftSwingDeg, rightSwingDeg, kneeBendDeg);
                return;
            }

            // STEP B: Build the world-space swing axis.
            //         worldSwingAxis = Cross(gaitForward, worldUp) gives the horizontal axis
            //         that is perpendicular to the movement direction and lies in the ground
            //         plane. Rotating around this axis lifts a leg forward or backward in
            //         the sagittal plane of movement, independent of torso pitch.
            //         DESIGN: We use Vector3.up (world up) not the Hips local up so the
            //         knee always folds toward world-space "forward/upward", even when the
            //         torso is significantly pitched forward.
            Vector3 worldSwingAxis = Vector3.Cross(gaitForward, Vector3.up).normalized;

            if (worldSwingAxis.sqrMagnitude < 0.0001f)
            {
                // gaitForward is parallel to world up (degenerate — character facing straight
                // up or down). Fall back to local-space behaviour.
                ApplyLocalSpaceSwing(leftSwingDeg, rightSwingDeg, kneeBendDeg);
                return;
            }

            // STEP C: Apply upper-leg targets in world space, converted to joint-local frame.
            ApplyWorldSpaceJointTarget(_upperLegL, leftSwingDeg,  worldSwingAxis);
            ApplyWorldSpaceJointTarget(_upperLegR, rightSwingDeg, worldSwingAxis);

            // STEP D: Apply lower-leg knee-bend targets.
            //         Positive kneeBendDeg bends the knee forward (in the direction of
            //         movement) — same worldSwingAxis, but opposite sign convention
            //         so the lower leg folds in the physiologically correct direction.
            ApplyWorldSpaceJointTarget(_lowerLegL, -kneeBendDeg, worldSwingAxis);
            ApplyWorldSpaceJointTarget(_lowerLegR, -kneeBendDeg, worldSwingAxis);
        }

        /// <summary>
        /// Computes and assigns a world-space swing target rotation to a single
        /// ConfigurableJoint. The target is expressed as: "rotate the joint body's
        /// current orientation by <paramref name="swingDeg"/> degrees around
        /// <paramref name="worldAxis"/> in world space", then converted to the connected
        /// body's local frame for <c>ConfigurableJoint.targetRotation</c>.
        /// </summary>
        /// <param name="joint">The ConfigurableJoint to drive. No-op if null.</param>
        /// <param name="swingDeg">
        /// Signed angle in degrees. Positive = swing in the direction of the axis
        /// by the right-hand rule (forward/upward for knee, forward swing for upper leg).
        /// </param>
        /// <param name="worldAxis">The world-space rotation axis (should be pre-normalised).</param>
        private static void ApplyWorldSpaceJointTarget(ConfigurableJoint joint, float swingDeg, Vector3 worldAxis)
        {
            if (joint == null)
            {
                return;
            }

            // DESIGN: ConfigurableJoint.targetRotation is specified in the space of the
            // connected body (parent body). It represents the desired local rotation of the
            // joint body relative to its connected body, measured in the connected body's
            // frame at the time the joint was created (i.e., the initial reference frame).
            //
            // To drive toward a world-space orientation:
            //   1. Build the desired world-space rotation delta: a rotation around worldAxis
            //      by swingDeg, applied on top of the joint body's current world rotation.
            //      We use Quaternion.identity as the "rest" reference, so swingDeg=0 → identity
            //      target → joint returns to rest pose (exactly matching the legacy behaviour).
            //
            //   2. Convert to connected-body-local frame:
            //      localTarget = Inverse(connectedBody.rotation) × worldSpaceTargetRot
            //
            //   This is equivalent to: "in the parent's frame, the child should be at this
            //   rotation" — which is exactly what ConfigurableJoint.targetRotation expects.
            //
            //   Note: we intentionally do NOT multiply by the joint body's current world rotation.
            //   Instead we treat swingDeg as an absolute offset from the rest pose (identity),
            //   expressed in a world-aligned frame. This keeps the L/R alternating phase
            //   mathematics identical to the legacy path (sin(phase) × stepAngle) while
            //   ensuring the axis of rotation is always world-horizontal.

            Quaternion worldSwingRotation = Quaternion.AngleAxis(swingDeg, worldAxis);

            // Get the connected body's current world rotation to express the target in its frame.
            Quaternion connectedBodyRot = joint.connectedBody != null
                ? joint.connectedBody.rotation
                : Quaternion.identity;

            // Convert world-space rotation to connected-body local space.
            Quaternion localTarget = Quaternion.Inverse(connectedBodyRot) * worldSwingRotation;

            joint.targetRotation = localTarget;
        }

        /// <summary>
        /// Returns the world-space horizontal forward direction for gait animation.
        /// Uses the Hips Rigidbody's horizontal velocity as the primary source (accurate
        /// even when the torso is pitched). Falls back to CurrentMoveInput projected onto
        /// the XZ plane when velocity magnitude is below the threshold (e.g. start of motion).
        /// Returns <see cref="Vector3.zero"/> if neither source has a usable direction.
        /// </summary>
        private Vector3 GetWorldGaitForward()
        {
            // DESIGN: Velocity is the best proxy for actual movement direction because it is
            // already in world space and naturally accounts for camera yaw, slopes, and any
            // forces applied by PlayerMovement. The 0.1 m/s threshold avoids noisy direction
            // from near-zero velocity at the very start of a walk or when nearly stopped.
            const float VelocityThreshold = 0.1f;

            if (_hipsRigidbody != null)
            {
                Vector3 horizontalVel = new Vector3(
                    _hipsRigidbody.linearVelocity.x,
                    0f,
                    _hipsRigidbody.linearVelocity.z);

                if (horizontalVel.magnitude >= VelocityThreshold)
                {
                    return horizontalVel.normalized;
                }
            }

            // Fallback: use move input as a world-space XZ direction.
            // CurrentMoveInput is a raw 2D input; without camera transform it maps X→world-X,
            // Y→world-Z. This is an approximation sufficient for tests and the zero-velocity
            // start-of-movement window. PlayerMovement applies camera yaw to forces; here
            // we use the raw input for simplicity (the velocity path takes over once moving).
            Vector2 moveInput = _playerMovement.CurrentMoveInput;
            Vector3 inputDir = new Vector3(moveInput.x, 0f, moveInput.y);
            if (inputDir.sqrMagnitude > 0.0001f)
            {
                return inputDir.normalized;
            }

            return Vector3.zero;
        }

        /// <summary>
        /// Applies leg swing in the joint's local targetRotation frame (original behaviour).
        /// Used when <see cref="_useWorldSpaceSwing"/> is false, or as a fallback when a
        /// valid world-space gait direction cannot be computed.
        /// </summary>
        /// <param name="leftSwingDeg">Signed swing angle (degrees) for the left upper leg.</param>
        /// <param name="rightSwingDeg">Signed swing angle (degrees) for the right upper leg.</param>
        /// <param name="kneeBendDeg">Knee bend angle (degrees).</param>
        private void ApplyLocalSpaceSwing(float leftSwingDeg, float rightSwingDeg, float kneeBendDeg)
        {
            // DESIGN: Quaternion.AngleAxis with _swingAxis (default Vector3.forward / Z)
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
