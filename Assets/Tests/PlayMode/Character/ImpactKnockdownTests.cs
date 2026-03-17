using System.Collections;
using NUnit.Framework;
using PhysicsDrivenMovement.Character;
using PhysicsDrivenMovement.Core;
using UnityEngine;
using UnityEngine.TestTools;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// PlayMode tests for the Chapter 2 external impact knockdown system.
    /// Validates that high-velocity impacts knock the character down, medium impacts
    /// stagger without knockdown, self-collisions are ignored, impacts during GettingUp
    /// re-knock the character down, and rapid impacts within the cooldown are ignored.
    /// </summary>
    public class ImpactKnockdownTests
    {
        private const int WarmUpFrames = 150;
        private const int ProjectileLayer = 6; // UI layer repurposed — not player, not environment.

        private static readonly Vector3 TestOrigin = new Vector3(0f, 0f, 9500f);

        private PlayerPrefabTestRig _rig;

        [TearDown]
        public void TearDown()
        {
            _rig?.Dispose();
            _rig = null;
        }

        /// <summary>
        /// Test 1: High-velocity projectile → Fallen state.
        /// A heavy fast-moving projectile delivers enough delta-V to exceed the knockdown
        /// threshold (5 m/s effective), causing the character to surrender and enter Fallen.
        /// </summary>
        [UnityTest]
        public IEnumerator HighVelocityProjectile_KnocksCharacterDown()
        {
            _rig = PlayerPrefabTestRig.Create(new PlayerPrefabTestRig.Options
            {
                TestOrigin = TestOrigin,
                SpawnOffset = new Vector3(0f, 0.5f, 0f),
            });
            yield return _rig.WarmUp(WarmUpFrames);

            Assert.That(_rig.BalanceController.IsSurrendered, Is.False);
            Assert.That(_rig.CharacterState.CurrentState, Is.Not.EqualTo(CharacterStateType.Fallen));

            EnableProjectileCharacterCollisions();

            // Spawn projectile 1 m to the right of the hips, heading left at high speed.
            // LaunchProjectileAtHips phases through limbs so only the hips box is hit.
            LaunchProjectileAtHips(
                _rig.HipsBody.worldCenterOfMass + Vector3.right * 1f,
                Vector3.left,
                speed: 30f,
                mass: 5f);

            // Wait for the collision to register and character state to transition.
            bool fallen = false;
            for (int i = 0; i < 200; i++)
            {
                yield return new WaitForFixedUpdate();
                if (_rig.CharacterState.CurrentState == CharacterStateType.Fallen)
                {
                    fallen = true;
                    break;
                }
            }

            Assert.That(fallen, Is.True,
                "A high-velocity projectile (delta-V >> 5 m/s) must knock the character into Fallen.");
            Assert.That(_rig.BalanceController.IsSurrendered, Is.True,
                "The impact should have triggered surrender via ImpactKnockdownDetector.");
        }

        /// <summary>
        /// Test 2: Medium-velocity projectile → stagger, not Fallen.
        /// An impact that delivers delta-V between stagger threshold (2.5 m/s) and
        /// knockdown threshold (5 m/s) should stagger but not immediately knock down.
        /// </summary>
        [UnityTest]
        public IEnumerator MediumVelocityProjectile_StaggersButDoesNotKnockDown()
        {
            _rig = PlayerPrefabTestRig.Create(new PlayerPrefabTestRig.Options
            {
                TestOrigin = TestOrigin + new Vector3(100f, 0f, 0f),
                SpawnOffset = new Vector3(0f, 0.5f, 0f),
            });
            yield return _rig.WarmUp(WarmUpFrames);

            EnableProjectileCharacterCollisions();

            // Use a lighter, slower projectile so the effective delta-V lands between
            // stagger (2.5 m/s) and knockdown (5 m/s).
            LaunchProjectileAtHips(
                _rig.HipsBody.worldCenterOfMass + Vector3.right * 1f,
                Vector3.left,
                speed: 6f,
                mass: 2f);

            // Let physics resolve. The character should not immediately surrender.
            // We check over a short window — stagger should produce angular velocity
            // but not trigger the immediate ImpactKnockdownDetector knockdown.
            bool surrenderedImmediately = false;
            for (int i = 0; i < 10; i++)
            {
                yield return new WaitForFixedUpdate();
                if (_rig.BalanceController.IsSurrendered)
                {
                    surrenderedImmediately = true;
                    break;
                }
            }

            Assert.That(surrenderedImmediately, Is.False,
                "A medium-velocity projectile (delta-V between 2.5 and 5 m/s) should stagger " +
                "without triggering immediate impact knockdown.");
        }

        /// <summary>
        /// Test 3: Self-collision does not trigger knockdown.
        /// Even if character body parts generate collision impulses among themselves,
        /// the ImpactKnockdownDetector should filter them out.
        /// </summary>
        [UnityTest]
        public IEnumerator SelfCollision_DoesNotTriggerKnockdown()
        {
            _rig = PlayerPrefabTestRig.Create(new PlayerPrefabTestRig.Options
            {
                TestOrigin = TestOrigin + new Vector3(200f, 0f, 0f),
                SpawnOffset = new Vector3(0f, 0.5f, 0f),
            });
            yield return _rig.WarmUp(WarmUpFrames);

            // Flail the character by adding opposing angular velocities to arms.
            Rigidbody upperArmLRb = _rig.UpperArmL.GetComponent<Rigidbody>();
            Rigidbody upperArmRRb = _rig.UpperArmR.GetComponent<Rigidbody>();
            if (upperArmLRb != null)
            {
                upperArmLRb.AddTorque(Vector3.up * 50f, ForceMode.VelocityChange);
            }
            if (upperArmRRb != null)
            {
                upperArmRRb.AddTorque(Vector3.down * 50f, ForceMode.VelocityChange);
            }

            // Run physics and verify no surrender/knockdown from internal collisions.
            for (int i = 0; i < 50; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            Assert.That(_rig.BalanceController.IsSurrendered, Is.False,
                "Internal limb-on-limb collisions must not trigger impact knockdown.");
        }

        /// <summary>
        /// Test 4: Impact during GettingUp → re-enters Fallen.
        /// The GettingUp threshold multiplier (0.6×) lowers the bar, making it easier
        /// to re-knock the character down while they're trying to stand up.
        /// </summary>
        [UnityTest]
        public IEnumerator ImpactDuringGettingUp_ReEntersFallen()
        {
            _rig = PlayerPrefabTestRig.Create(new PlayerPrefabTestRig.Options
            {
                TestOrigin = TestOrigin + new Vector3(300f, 0f, 0f),
                SpawnOffset = new Vector3(0f, 0.5f, 0f),
            });
            yield return _rig.WarmUp(WarmUpFrames);

            EnableProjectileCharacterCollisions();

            // Force the character into Fallen → GettingUp by triggering surrender directly.
            _rig.BalanceController.TriggerSurrender(0.3f);

            // Wait for Fallen state and then for the floor dwell to expire into GettingUp.
            bool reachedGettingUp = false;
            for (int i = 0; i < 400; i++)
            {
                yield return new WaitForFixedUpdate();
                if (_rig.CharacterState.CurrentState == CharacterStateType.GettingUp)
                {
                    reachedGettingUp = true;
                    break;
                }
            }

            Assert.That(reachedGettingUp, Is.True,
                "Character must reach GettingUp state before we test the re-knockdown.");

            // Now hit the character during GettingUp with a moderate projectile.
            // The GettingUp threshold multiplier (0.6×) lowers the knockdown threshold
            // from 5 m/s to 3 m/s, so a moderate hit should work.
            var projectile = LaunchProjectileAtHips(
                _rig.HipsBody.worldCenterOfMass + Vector3.right * 1f,
                Vector3.left,
                speed: 20f,
                mass: 5f);

            bool reFallen = false;
            for (int i = 0; i < 200; i++)
            {
                yield return new WaitForFixedUpdate();
                if (_rig.CharacterState.CurrentState == CharacterStateType.Fallen)
                {
                    reFallen = true;
                    break;
                }
            }

            Assert.That(reFallen, Is.True,
                "An impact during GettingUp (with lowered threshold) must re-knock the character into Fallen.");
        }

        /// <summary>
        /// Test 5: Rapid impacts within cooldown → second impact ignored.
        /// The ImpactKnockdownDetector has a 1.0 s cooldown after a knockdown trigger.
        /// A second hard impact within that window should not raise OnKnockdown again.
        /// </summary>
        [UnityTest]
        public IEnumerator RapidImpacts_WithinCooldown_SecondIgnored()
        {
            _rig = PlayerPrefabTestRig.Create(new PlayerPrefabTestRig.Options
            {
                TestOrigin = TestOrigin + new Vector3(400f, 0f, 0f),
                SpawnOffset = new Vector3(0f, 0.5f, 0f),
            });
            yield return _rig.WarmUp(WarmUpFrames);

            EnableProjectileCharacterCollisions();

            ImpactKnockdownDetector detector =
                _rig.Instance.GetComponentInChildren<ImpactKnockdownDetector>();
            Assert.That(detector, Is.Not.Null,
                "The PlayerRagdoll prefab must have an ImpactKnockdownDetector.");

            int knockdownEventCount = 0;
            detector.OnKnockdown += _ => knockdownEventCount++;

            // First hard impact.
            LaunchProjectileAtHips(
                _rig.HipsBody.worldCenterOfMass + Vector3.right * 1f,
                Vector3.left,
                speed: 30f,
                mass: 5f);

            // Let the first impact register.
            for (int i = 0; i < 20; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            int countAfterFirst = knockdownEventCount;
            Assert.That(countAfterFirst, Is.GreaterThanOrEqualTo(1),
                "The first hard impact must trigger at least one OnKnockdown event.");

            // Second hard impact within cooldown (< 1.0 s).
            LaunchProjectileAtHips(
                _rig.HipsBody.worldCenterOfMass + Vector3.left * 1f,
                Vector3.right,
                speed: 30f,
                mass: 5f);

            for (int i = 0; i < 20; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            Assert.That(knockdownEventCount, Is.EqualTo(countAfterFirst),
                "A second hard impact within the 1.0 s cooldown must be ignored " +
                $"(events after first: {countAfterFirst}, current: {knockdownEventCount}).");
        }

        /// <summary>
        /// Enables physics collisions between the projectile layer and all character body layers.
        /// Must be called after PlayerPrefabTestRig creates the scene (which captures
        /// the layer collision matrix).
        /// </summary>
        private static void EnableProjectileCharacterCollisions()
        {
            Physics.IgnoreLayerCollision(ProjectileLayer, GameSettings.LayerPlayer1Parts, false);
            Physics.IgnoreLayerCollision(ProjectileLayer, GameSettings.LayerLowerLegParts, false);
            Physics.IgnoreLayerCollision(ProjectileLayer, ProjectileLayer, true);
            // Prevent the projectile from hitting the ground (needed when the character
            // is lying on the floor during GettingUp / Fallen).
            Physics.IgnoreLayerCollision(ProjectileLayer, GameSettings.LayerEnvironment, true);
        }

        /// <summary>
        /// Launches a projectile and configures it to ignore all character colliders
        /// EXCEPT those attached to the target <paramref name="targetRb"/>.
        /// This ensures <c>OnCollisionEnter</c> fires on the target body (the hips,
        /// where <see cref="ImpactKnockdownDetector"/> lives) rather than on an
        /// intervening limb or torso collider.
        /// </summary>
        private GameObject LaunchProjectileAtHips(
            Vector3 spawnPosition,
            Vector3 direction,
            float speed,
            float mass)
        {
            GameObject projectile = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            projectile.name = "TestProjectile";
            projectile.transform.position = spawnPosition;
            projectile.transform.localScale = Vector3.one * 0.3f;
            projectile.layer = ProjectileLayer;

            Rigidbody rb = projectile.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.useGravity = false;
            rb.linearVelocity = direction.normalized * speed;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            // Make the projectile phase through every character collider except the
            // hips — the hips Rigidbody hosts the ImpactKnockdownDetector with
            // OnCollisionEnter.
            Collider projectileCol = projectile.GetComponent<Collider>();
            foreach (Collider col in _rig.Instance.GetComponentsInChildren<Collider>())
            {
                if (col.attachedRigidbody == _rig.HipsBody)
                    continue;
                Physics.IgnoreCollision(projectileCol, col, true);
            }

            Object.Destroy(projectile, 5f);
            return projectile;
        }
    }
}
