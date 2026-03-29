using PhysicsDrivenMovement.Core;
using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Routes direct external collision impulses from selected ragdoll rigidbodies into
    /// <see cref="BalanceController.ReceiveCollisionImpact(float, Vector3)"/> so heavy hits
    /// can yield balance support immediately instead of waiting for a later angular-velocity spike.
    /// Attach to the main impact-bearing body parts on the player ragdoll prefab.
    /// Lifecycle: caches <see cref="BalanceController"/> in Awake, then filters collisions in
    /// OnCollisionEnter.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public class CollisionImpactReceiver : MonoBehaviour
    {
        private BalanceController _balance;

        private void Awake()
        {
            // STEP 1: Resolve the owning balance controller from this body part up the ragdoll hierarchy.
            _balance = GetComponentInParent<BalanceController>();
        }

        private void OnCollisionEnter(Collision collision)
        {
            // STEP 1: Ignore unwired setups and collisions without a usable other body.
            if (_balance == null || collision == null || collision.gameObject == null)
            {
                return;
            }

            // STEP 2: Filter out self-collisions and normal ground contacts so only external hits yield balance.
            int otherLayer = collision.gameObject.layer;
            if (otherLayer == GameSettings.LayerPlayer1Parts ||
                otherLayer == GameSettings.LayerEnvironment ||
                otherLayer == GameSettings.LayerLowerLegParts)
            {
                return;
            }

            // STEP 3: Forward the collision impulse so BalanceController can scale yield by hit severity.
            _balance.ReceiveCollisionImpact(collision.impulse.magnitude, collision.impulse);
        }
    }
}