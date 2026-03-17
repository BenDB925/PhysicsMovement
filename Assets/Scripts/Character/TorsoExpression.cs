using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Drives a subtle counter-rotation twist on the Torso ConfigurableJoint that opposes
    /// the current gait phase, so the upper body twists slightly against the stride.
    ///
    /// Phase is read from <see cref="LegAnimator.Phase"/> and amplitude is blended by
    /// <see cref="LegAnimator.SmoothedInputMag"/>: the twist fades to zero at idle and
    /// ramps in when gait starts, matching leg and arm blend behaviour.
    ///
    /// The twist is applied around the joint's Y axis (yaw in joint-local space), producing
    /// horizontal counter-rotation: when the left leg swings forward the torso twists
    /// slightly to the right, and vice versa. This mirrors the natural human upper-body
    /// counter-rotation during walking.
    ///
    /// Attach to the same Hips GameObject as <see cref="LegAnimator"/> and <see cref="ArmAnimator"/>.
    /// Lifecycle: Awake (cache Torso joint + LegAnimator), FixedUpdate (apply twist).
    /// Collaborators: <see cref="LegAnimator"/> (reads Phase and SmoothedInputMag),
    /// Unity ConfigurableJoint (Torso).
    /// </summary>
    public class TorsoExpression : MonoBehaviour
    {
        // ── Serialized Fields ────────────────────────────────────────────────

        [SerializeField, Range(0f, 15f)]
        [Tooltip("Peak twist angle (degrees) applied to the Torso joint around its local Y axis. " +
                 "Scaled by SmoothedInputMag so it is zero at idle. Keep low (0.5\u20132\u00b0) because the " +
                 "Torso SLERP spring reaction torque can destabilize hips yaw control. Default 1\u00b0.")]
        private float _twistMaxDeg = 1f;

        [SerializeField, Range(1f, 30f)]
        [Tooltip("Smoothing speed for the twist signal. Higher = more responsive to phase changes, " +
                 "lower = more gradual and cinematic. Default 12.")]
        private float _twistSmoothing = 12f;

        // ── Private State ────────────────────────────────────────────────────

        /// <summary>Sibling LegAnimator for reading Phase and SmoothedInputMag.</summary>
        private LegAnimator _legAnimator;

        /// <summary>The Torso ConfigurableJoint found in child hierarchy.</summary>
        private ConfigurableJoint _torsoJoint;

        /// <summary>Smoothed twist angle in degrees applied this frame.</summary>
        private float _smoothedTwistDeg;

        /// <summary>Sibling CharacterState for Fallen/GettingUp kill-switch (C8.4b).</summary>
        private CharacterState _characterState;

        /// <summary>
        /// C8.4b: True while CharacterState is Fallen or GettingUp. Suppresses torso twist
        /// so it never fights recovery torques.
        /// </summary>
        private bool _suppressTwist;

        // ── Unity Lifecycle ──────────────────────────────────────────────────

        private void Awake()
        {
            // STEP 1: Cache the sibling LegAnimator reference.
            if (!TryGetComponent(out _legAnimator))
            {
                Debug.LogWarning("[TorsoExpression] No LegAnimator found on this GameObject. " +
                                 "Torso twist will remain at identity.", this);
            }

            // STEP 2: Locate the Torso ConfigurableJoint by name in children.
            ConfigurableJoint[] allJoints =
                GetComponentsInChildren<ConfigurableJoint>(includeInactive: true);
            foreach (ConfigurableJoint joint in allJoints)
            {
                if (joint.gameObject.name == "Torso")
                {
                    _torsoJoint = joint;
                    break;
                }
            }

            if (_torsoJoint == null)
            {
                Debug.LogWarning("[TorsoExpression] 'Torso' ConfigurableJoint not found in children. " +
                                 "Torso twist will have no effect.", this);
            }

            // STEP 3: Cache CharacterState for Fallen/GettingUp kill-switch (C8.4b).
            TryGetComponent(out _characterState);
        }

        private void OnEnable()
        {
            if (_characterState != null)
                _characterState.OnStateChanged += OnCharacterStateChanged;
        }

        private void OnDisable()
        {
            if (_characterState != null)
                _characterState.OnStateChanged -= OnCharacterStateChanged;
        }

        private void FixedUpdate()
        {
            // STEP 1: Gate on missing dependencies — reset to identity and bail.
            if (_legAnimator == null || _torsoJoint == null)
            {
                if (_torsoJoint != null)
                {
                    _torsoJoint.targetRotation = Quaternion.identity;
                }
                return;
            }

            // C8.4b: Kill-switch — decay twist to zero during Fallen/GettingUp.
            if (_suppressTwist)
            {
                _smoothedTwistDeg = Mathf.Lerp(_smoothedTwistDeg, 0f,
                    Time.fixedDeltaTime * _twistSmoothing);
                if (Mathf.Abs(_smoothedTwistDeg) < 0.01f)
                    _smoothedTwistDeg = 0f;
                _torsoJoint.targetRotation = Quaternion.Euler(0f, _smoothedTwistDeg, 0f);
                return;
            }

            float smoothedInputMag = _legAnimator.SmoothedInputMag;

            // STEP 2: When idle, decay twist to zero and set identity.
            if (smoothedInputMag < 0.01f)
            {
                _smoothedTwistDeg = Mathf.Lerp(_smoothedTwistDeg, 0f,
                    Time.fixedDeltaTime * _twistSmoothing);
                if (Mathf.Abs(_smoothedTwistDeg) < 0.01f)
                {
                    _smoothedTwistDeg = 0f;
                    _torsoJoint.targetRotation = Quaternion.identity;
                    return;
                }
                _torsoJoint.targetRotation = Quaternion.Euler(0f, _smoothedTwistDeg, 0f);
                return;
            }

            // STEP 3: Compute twist target from gait phase.
            // sin(phase) is positive when the left leg swings forward.
            // Counter-rotation means the torso twists in the OPPOSITE direction:
            // negate so the torso opposes the left-leg-forward direction.
            float twistTarget = -Mathf.Sin(_legAnimator.Phase) * _twistMaxDeg * smoothedInputMag;

            // STEP 4: Smooth the twist to avoid jitter from phase discontinuities.
            _smoothedTwistDeg = Mathf.Lerp(_smoothedTwistDeg, twistTarget,
                Time.fixedDeltaTime * _twistSmoothing);

            // STEP 4b: Clamp the smoothed output to enforce the amplitude cap.
            _smoothedTwistDeg = Mathf.Clamp(_smoothedTwistDeg, -_twistMaxDeg, _twistMaxDeg);

            // STEP 5: Apply as a Y-axis rotation in joint-local space.
            _torsoJoint.targetRotation = Quaternion.Euler(0f, _smoothedTwistDeg, 0f);
        }

        // ── Private Methods ──────────────────────────────────────────────────

        /// <summary>
        /// C8.4b: Reacts to CharacterState transitions. Suppresses torso twist during
        /// Fallen/GettingUp and re-enables on Standing/Moving.
        /// </summary>
        private void OnCharacterStateChanged(CharacterStateType previousState, CharacterStateType newState)
        {
            if (newState == CharacterStateType.Fallen || newState == CharacterStateType.GettingUp)
            {
                _suppressTwist = true;
            }
            else if (newState == CharacterStateType.Standing || newState == CharacterStateType.Moving)
            {
                _suppressTwist = false;
            }
        }
    }
}
