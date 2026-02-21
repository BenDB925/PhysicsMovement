using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Temporary debug helper for Phase 2D balance tuning.
    /// Applies a forward push to the local Rigidbody when keys are pressed:
    /// P = small push, O = large push.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class DebugPushForce : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Enable keyboard debug pushes (P/O) for balance tuning.")]
        private bool _enableDebugKeys = true;

        [SerializeField, Range(0f, 2000f)]
        [Tooltip("Force in Newtons applied when pressing P.")]
        private float _smallPushForce = 200f;

        [SerializeField, Range(0f, 2000f)]
        [Tooltip("Force in Newtons applied when pressing O.")]
        private float _largePushForce = 800f;

        private Rigidbody _rb;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
        }

        private void Update()
        {
            if (!_enableDebugKeys)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.P))
            {
                _rb.AddForce(transform.forward * _smallPushForce, ForceMode.Force);
            }

            if (Input.GetKeyDown(KeyCode.O))
            {
                _rb.AddForce(transform.forward * _largePushForce, ForceMode.Force);
            }
        }
    }
}