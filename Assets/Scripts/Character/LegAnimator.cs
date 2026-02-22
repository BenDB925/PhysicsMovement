using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Drives procedural gait animation on the ragdoll's four leg ConfigurableJoints
    /// by advancing a phase accumulator from movement input magnitude and applying
    /// sinusoidal target rotations each FixedUpdate.
    /// Left and right upper legs are offset by π (half-cycle), producing an alternating
    /// stepping pattern. Lower legs receive a constant knee-bend target during gait.
    /// When the character is in the <see cref="CharacterStateType.Fallen"/> or
    /// <see cref="CharacterStateType.GettingUp"/> state, all leg joints are returned
    /// to <see cref="Quaternion.identity"/> and gait is suspended.
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
        /// Advances proportionally to move input magnitude each FixedUpdate.
        /// </summary>
        private float _phase;

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
            //         leg joints to Quaternion.identity and exit early.
            //         This covers both Fallen (limp) and GettingUp (recovery) states.
            CharacterStateType state = _characterState.CurrentState;
            if (state == CharacterStateType.Fallen || state == CharacterStateType.GettingUp)
            {
                SetAllLegTargetsToIdentity();
                return;
            }

            // STEP 3: Read move input magnitude (0–1).
            //         Input magnitude drives both phase advancement speed and implicitly
            //         represents walking speed. No force is applied here — this script
            //         only sets ConfigurableJoint.targetRotation.
            float inputMagnitude = _playerMovement.CurrentMoveInput.magnitude;

            // STEP 4: Advance phase accumulator. Phase is in radians, cycling [0, 2π).
            //         Scale by 2π × frequency × deltaTime so one full cycle takes exactly
            //         1/_stepFrequency seconds at full input.
            _phase += inputMagnitude * 2f * Mathf.PI * _stepFrequency * Time.fixedDeltaTime;

            // Wrap phase to [0, 2π) to prevent float overflow over time.
            if (_phase >= 2f * Mathf.PI)
            {
                _phase -= 2f * Mathf.PI;
            }

            // STEP 5: Compute sinusoidal upper-leg targets.
            //         Left leg uses phase directly; right leg is offset by π (half-cycle)
            //         so they always swing in opposite directions — the alternating gait.
            //         The swing is a pure rotation around the X axis (forward/backward).
            //         We scale amplitude by inputMagnitude so standing still produces no swing.
            float leftSwingDeg  =  Mathf.Sin(_phase)              * _stepAngle * inputMagnitude;
            float rightSwingDeg =  Mathf.Sin(_phase + Mathf.PI)   * _stepAngle * inputMagnitude;

            // STEP 6: Compute lower-leg knee-bend target.
            //         The knee holds a constant positive bend during gait so the character
            //         looks dynamically flexed rather than stiff-legged.
            //         We also scale by input magnitude so idle legs relax to identity.
            float kneeBendDeg = _kneeAngle * inputMagnitude;

            // STEP 7: Apply computed rotations to joint targetRotations.
            //         ConfigurableJoint.targetRotation is specified in the joint's initial
            //         local drive frame. A rotation around X (Euler) drives forward/back swing.
            //         For lower legs, a negative X rotation produces a forward knee bend
            //         (the lower leg pulls forward/up relative to the upper leg).
            if (_upperLegL != null)
            {
                _upperLegL.targetRotation = Quaternion.Euler(leftSwingDeg, 0f, 0f);
            }

            if (_upperLegR != null)
            {
                _upperLegR.targetRotation = Quaternion.Euler(rightSwingDeg, 0f, 0f);
            }

            if (_lowerLegL != null)
            {
                _lowerLegL.targetRotation = Quaternion.Euler(-kneeBendDeg, 0f, 0f);
            }

            if (_lowerLegR != null)
            {
                _lowerLegR.targetRotation = Quaternion.Euler(-kneeBendDeg, 0f, 0f);
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Sets all four leg joint <c>targetRotation</c> values to <see cref="Quaternion.identity"/>,
        /// removing any active gait pose and letting the joint's natural drive return the
        /// limb to its resting orientation.
        /// Called when the character state is <see cref="CharacterStateType.Fallen"/> or
        /// <see cref="CharacterStateType.GettingUp"/>.
        /// </summary>
        private void SetAllLegTargetsToIdentity()
        {
            if (_upperLegL != null) _upperLegL.targetRotation = Quaternion.identity;
            if (_upperLegR != null) _upperLegR.targetRotation = Quaternion.identity;
            if (_lowerLegL != null) _lowerLegL.targetRotation = Quaternion.identity;
            if (_lowerLegR != null) _lowerLegR.targetRotation = Quaternion.identity;
        }
    }
}
