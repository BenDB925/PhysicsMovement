using System;
using UnityEngine;
using PhysicsDrivenMovement.Core;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Detects external collision impacts that should stagger or knock the character down.
    /// Attach to the Hips Rigidbody or another central body part and wire it to the
    /// character's <see cref="BalanceController"/> and <see cref="CharacterState"/>.
    /// Lifecycle: caches collaborators in Awake, counts down cooldown in FixedUpdate,
    /// and evaluates collision impulses in OnCollisionEnter.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public class ImpactKnockdownDetector : MonoBehaviour
    {
        private const float MinimumVectorSqrMagnitude = 0.0001f;
        private const float MinimumMass = 0.0001f;
        private const float FallenKnockdownThresholdMultiplier = 0.4f;

        [Header("Impact Thresholds")]
        [SerializeField, Range(0f, 20f)]
        [Tooltip("Minimum effective delta-v (m/s) required to trigger an immediate knockdown.")]
        private float _impactKnockdownDeltaV = 5f;

        [SerializeField, Range(0f, 20f)]
        [Tooltip("Minimum effective delta-v (m/s) that triggers a stagger but not an instant knockdown.")]
        private float _impactStaggerDeltaV = 2.5f;

        [SerializeField, Range(0f, 5f)]
        [Tooltip("Seconds to ignore new impacts after a knockdown has already been triggered.")]
        private float _impactCooldown = 1f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Blend between raw impulse magnitude and lateral-only weighting. Higher values make side hits count more.")]
        private float _impactDirectionWeight = 0.7f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Multiplier applied to the knockdown threshold while the character is in GettingUp.")]
        private float _gettingUpThresholdMultiplier = 0.6f;

        [Header("Filtering")]
        [SerializeField]
        [Tooltip("Layers that count as ground/static contact and should never trigger an impact knockdown.")]
        private LayerMask _groundLayers = 1 << GameSettings.LayerEnvironment;

        [Header("References")]
        [SerializeField]
        [Tooltip("Central Rigidbody used to compute effective delta-v and receive stagger torque. Defaults to the local Rigidbody.")]
        private Rigidbody _hipsRb;

        [SerializeField]
        [Tooltip("Balance controller that receives the Chapter 1 surrender trigger.")]
        private BalanceController _balanceController;

        [SerializeField]
        [Tooltip("Character state used for getting-up vulnerability checks.")]
        private CharacterState _characterState;

        private float _cooldownRemaining;
        private Transform _selfRoot;

        /// <summary>
        /// Raised when an impact exceeds the knockdown threshold and triggers surrender.
        /// </summary>
        public event Action<KnockdownEvent> OnKnockdown;

        private void Awake()
        {
            // STEP 1: Cache the central Rigidbody and local root used by impact math and self-collision filtering.
            if (_hipsRb == null)
            {
                TryGetComponent(out _hipsRb);
            }

            _selfRoot = transform.root;

            // STEP 2: Resolve the character-side collaborators from this body or the ragdoll root.
            if (_balanceController == null)
            {
                _balanceController = GetComponentInParent<BalanceController>();
            }

            if (_characterState == null)
            {
                _characterState = GetComponentInParent<CharacterState>();
            }

            // STEP 3: Surface missing wiring early so prefab setup problems are obvious.
            if (_balanceController == null)
            {
                Debug.LogWarning("[ImpactKnockdownDetector] Missing BalanceController reference.", this);
            }

            if (_characterState == null)
            {
                Debug.LogWarning("[ImpactKnockdownDetector] Missing CharacterState reference.", this);
            }
        }

        private void FixedUpdate()
        {
            // STEP 1: Count down the post-knockdown cooldown in fixed time so repeated tumbles do not re-trigger instantly.
            if (_cooldownRemaining > 0f)
            {
                _cooldownRemaining = Mathf.Max(0f, _cooldownRemaining - Time.fixedDeltaTime);
            }
        }

        private void OnValidate()
        {
            _impactKnockdownDeltaV = Mathf.Max(0f, _impactKnockdownDeltaV);
            _impactStaggerDeltaV = Mathf.Clamp(_impactStaggerDeltaV, 0f, _impactKnockdownDeltaV);
            _impactCooldown = Mathf.Max(0f, _impactCooldown);
            _impactDirectionWeight = Mathf.Clamp01(_impactDirectionWeight);
            _gettingUpThresholdMultiplier = Mathf.Clamp01(_gettingUpThresholdMultiplier);
        }

        private void OnCollisionEnter(Collision collision)
        {
            // STEP 1: Ignore rapid re-triggers, unwired setups, self-collisions, and ground contacts.
            if (_cooldownRemaining > 0f ||
                _hipsRb == null ||
                _balanceController == null ||
                _characterState == null ||
                ShouldIgnoreCollision(collision))
            {
                return;
            }

            // STEP 2: Convert the collision impulse into an effective delta-v weighted toward lateral hits.
            float effectiveDeltaV = ComputeEffectiveDeltaV(
                collision,
                out Vector3 impactDirection,
                out Vector3 impactPoint,
                out GameObject source);

            if (effectiveDeltaV <= 0f)
            {
                return;
            }

            // STEP 3: Knock down hard hits immediately; otherwise apply a stagger and let existing recovery decide the outcome.
            float knockdownThreshold = GetCurrentKnockdownThreshold();
            if (effectiveDeltaV >= knockdownThreshold)
            {
                TriggerImpactKnockdown(effectiveDeltaV, knockdownThreshold, impactDirection, impactPoint, source);
                return;
            }

            if (effectiveDeltaV >= _impactStaggerDeltaV)
            {
                ApplyStaggerImpulse(impactDirection, effectiveDeltaV);
            }
        }

        private bool ShouldIgnoreCollision(Collision collision)
        {
            if (collision == null || collision.collider == null)
            {
                return true;
            }

            Collider otherCollider = collision.collider;
            return IsGroundCollision(otherCollider) || IsCharacterCollision(otherCollider);
        }

        private float ComputeEffectiveDeltaV(
            Collision collision,
            out Vector3 impactDirection,
            out Vector3 impactPoint,
            out GameObject source)
        {
            source = GetImpactSource(collision);
            impactPoint = _hipsRb != null ? _hipsRb.worldCenterOfMass : transform.position;
            impactDirection = Vector3.zero;

            if (_hipsRb == null)
            {
                return 0f;
            }

            float safeMass = Mathf.Max(MinimumMass, _hipsRb.mass);
            Vector3 impactDeltaV = collision.impulse / safeMass;
            float rawDeltaV = impactDeltaV.magnitude;
            if (rawDeltaV <= Mathf.Epsilon)
            {
                return 0f;
            }

            float lateralComponent = Vector3.ProjectOnPlane(impactDeltaV, Vector3.up).magnitude;
            float lateralRatio = Mathf.Clamp01(lateralComponent / rawDeltaV);
            float directionFactor = Mathf.Lerp(1f, lateralRatio, _impactDirectionWeight);

            impactDirection = impactDeltaV / rawDeltaV;
            if (collision.contactCount > 0)
            {
                impactPoint = collision.GetContact(0).point;
            }

            return rawDeltaV * directionFactor;
        }

        private float GetCurrentKnockdownThreshold()
        {
            float threshold = _impactKnockdownDeltaV;
            if (_characterState == null)
            {
                return threshold;
            }

            if (_characterState.CurrentState == CharacterStateType.Fallen)
            {
                return threshold * FallenKnockdownThresholdMultiplier;
            }

            if (_characterState.CurrentState == CharacterStateType.GettingUp)
            {
                threshold *= _gettingUpThresholdMultiplier;
            }

            return threshold;
        }

        private void TriggerImpactKnockdown(
            float effectiveDeltaV,
            float knockdownThreshold,
            Vector3 impactDirection,
            Vector3 impactPoint,
            GameObject source)
        {
            float severity = KnockdownSeverity.ComputeFromImpact(effectiveDeltaV, knockdownThreshold);
            _balanceController.TriggerSurrender(severity);
            _cooldownRemaining = _impactCooldown;

            KnockdownEvent knockdownEvent = new KnockdownEvent
            {
                Severity = severity,
                ImpactDirection = impactDirection,
                ImpactPoint = impactPoint,
                EffectiveDeltaV = effectiveDeltaV,
                Source = source,
            };

            OnKnockdown?.Invoke(knockdownEvent);
        }

        private void ApplyStaggerImpulse(Vector3 impactDirection, float effectiveDeltaV)
        {
            if (_hipsRb == null || impactDirection.sqrMagnitude < MinimumVectorSqrMagnitude)
            {
                return;
            }

            Vector3 staggerAxis = Vector3.Cross(Vector3.up, impactDirection);
            if (staggerAxis.sqrMagnitude < MinimumVectorSqrMagnitude)
            {
                Vector3 fallbackForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
                if (fallbackForward.sqrMagnitude < MinimumVectorSqrMagnitude)
                {
                    fallbackForward = Vector3.forward;
                }

                staggerAxis = Vector3.Cross(fallbackForward.normalized, impactDirection);
                if (staggerAxis.sqrMagnitude < MinimumVectorSqrMagnitude)
                {
                    return;
                }
            }

            _hipsRb.AddTorque(staggerAxis.normalized * effectiveDeltaV, ForceMode.VelocityChange);
        }

        private bool IsGroundCollision(Collider otherCollider)
        {
            return otherCollider != null && IsInLayerMask(_groundLayers, otherCollider.gameObject.layer);
        }

        private bool IsCharacterCollision(Collider otherCollider)
        {
            if (otherCollider == null)
            {
                return true;
            }

            Transform otherRoot = otherCollider.transform.root;
            if (_selfRoot != null && otherRoot == _selfRoot)
            {
                return true;
            }

            return IsCharacterLayer(otherCollider.gameObject.layer);
        }

        private static GameObject GetImpactSource(Collision collision)
        {
            if (collision.rigidbody != null)
            {
                return collision.rigidbody.gameObject;
            }

            return collision.collider != null ? collision.collider.gameObject : null;
        }

        private static bool IsInLayerMask(LayerMask mask, int layer)
        {
            return (mask.value & (1 << layer)) != 0;
        }

        private static bool IsCharacterLayer(int layer)
        {
            return layer == GameSettings.LayerPlayer1Parts ||
                   layer == GameSettings.LayerPlayer2Parts ||
                   layer == GameSettings.LayerPlayer3Parts ||
                   layer == GameSettings.LayerPlayer4Parts ||
                   layer == GameSettings.LayerLowerLegParts;
        }
    }
}